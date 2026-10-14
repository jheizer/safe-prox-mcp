using FluentAssertions;
using SafeProxMcp.Models;
using SafeProxMcp.Services;
using Xunit;

namespace SafeProxMcp.Tests.Services;

/// <summary>
/// Read-only integration tests against a real Proxmox cluster.
///
/// The config is the same clusters.json file the server requires; the search walks
/// up from the test output directory to the repository root. Without a readable
/// clusters.json these tests are no-ops.
/// </summary>
public class ProxmoxLiveIntegrationTests
{
    private static readonly ProxmoxClusterConfig? Cluster = FindClusterConfig();

    private static ProxmoxClusterConfig? FindClusterConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ClusterConfigLoader.DefaultConfigFile);
            if (!File.Exists(path))
                continue;

            try
            {
                return ClusterConfigLoader.Load(path).FirstOrDefault();
            }
            catch (InvalidOperationException)
            {
                // A malformed config in the repository must not fail the unit suite.
                return null;
            }
        }

        return null;
    }

    private static ProxmoxApiClient CreateClient() => new(Cluster!);

    private static bool Skipped => Cluster is null;

    private static async Task<string> FirstNodeAsync(ProxmoxApiClient client)
        => (await client.GetNodesAsync()).Data.First().Name;

    [Fact]
    public async Task GetNodesAsync_ReturnsNodes()
    {
        if (Skipped) return;
        using var client = CreateClient();

        var result = await client.GetNodesAsync();

        result.Data.Should().NotBeEmpty();
        result.Data.Should().Contain(n => !string.IsNullOrWhiteSpace(n.Name));
        result.Data.Should().OnlyContain(n => n.Type == "node");
    }

    [Fact]
    public async Task GetNodeStatusAsync_ReturnsCapacityAndVersion()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetNodeStatusAsync(node);

        result.PveVersion.Should().NotBeNullOrWhiteSpace();
        result.Uptime.Should().BeGreaterThan(0);
        result.Memory!.Total.Should().BeGreaterThan(0);
        result.CpuUsage.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetClusterStatusAsync_ReturnsMembers()
    {
        if (Skipped) return;
        using var client = CreateClient();

        var result = await client.GetClusterStatusAsync();

        result.Data.Should().NotBeEmpty();
        result.Data.Should().Contain(c => c.Online == 1);
    }

    [Fact]
    public async Task GetClusterResourcesAsync_HonoursTypeFilter()
    {
        if (Skipped) return;
        using var client = CreateClient();

        var all = await client.GetClusterResourcesAsync();
        var vms = await client.GetClusterResourcesAsync("vm");

        all.Data.Should().NotBeEmpty();
        // Proxmox expands type=vm to qemu and lxc entries; there is no resource
        // literally typed "vm".
        vms.Data.Should().NotBeEmpty();
        vms.Data.Should().OnlyContain(r => r.Type == "qemu" || r.Type == "lxc");
        vms.Data.Should().OnlyContain(r => r.VmId.HasValue);
        vms.Data.Count.Should().BeLessThanOrEqualTo(all.Data.Count);
    }

    [Fact]
    public async Task GetStorageAsync_ClusterAndPerNode()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var clusterStorage = await client.GetStorageAsync();
        var nodeStorage = await client.GetStorageAsync(node);

        clusterStorage.Data.Should().NotBeEmpty();
        clusterStorage.Data.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Storage));

        nodeStorage.Data.Should().NotBeEmpty();
        nodeStorage.Data.Should().Contain(s => s.Total > 0);
    }

    [Fact]
    public async Task GetNetworksAsync_ReturnsInterfaces()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetNetworksAsync(node);

        result.Data.Should().NotBeEmpty();
        result.Data.Should().Contain(n => n.Iface == "vmbr0");
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsUsers()
    {
        if (Skipped) return;
        using var client = CreateClient();

        var result = await client.GetUsersAsync();

        result.Data.Should().NotBeEmpty();
        result.Data.Should().Contain(u => u.UserId.EndsWith("@pam"));
    }

    [Fact]
    public async Task GetGroupsAsync_ReturnsWithoutError()
    {
        if (Skipped) return;
        using var client = CreateClient();

        var result = await client.GetGroupsAsync();

        // A cluster may legitimately have no groups.
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDisksAsync_ReturnsDisks()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetDisksAsync(node);

        result.Data.Should().NotBeNull();
        result.Data.Should().OnlyContain(d => d.DevPath != null);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsVersion()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetVersionAsync(node);

        result.Data.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetVmsAsync_ReturnsWithoutError()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetVmsAsync(node);

        // A node may legitimately host no VMs.
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetContainersAsync_ReturnsWithoutError()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetContainersAsync(node);

        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetNodeSyslogAsync_ReturnsFirstPage()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);

        var result = await client.GetNodeSyslogAsync(node, start: 0, limit: 5);

        result.Data.Should().NotBeEmpty();
        result.Data.Should().HaveCountLessThanOrEqualTo(5);
        result.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetVmSnapshotsAsync_ReturnsWithoutError()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);
        var vm = (await client.GetVmsAsync(node)).Data.FirstOrDefault();
        if (vm is null) return;

        var result = await client.GetVmSnapshotsAsync(node, vm.VmId);

        result.Data.Should().NotBeNull();
        // PVE always reports the implicit "current" snapshot; a VM with no real
        // snapshots returns just that entry.
        result.Data.Should().NotBeEmpty();
        result.Data.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Name));
    }

    [Fact]
    public async Task GetContainerSnapshotsAsync_ReturnsWithoutError()
    {
        if (Skipped) return;
        using var client = CreateClient();
        var node = await FirstNodeAsync(client);
        var container = (await client.GetContainersAsync(node)).Data.FirstOrDefault();
        if (container is null) return;

        var result = await client.GetContainerSnapshotsAsync(node, container.VmId);

        result.Data.Should().NotBeNull();
    }
}
