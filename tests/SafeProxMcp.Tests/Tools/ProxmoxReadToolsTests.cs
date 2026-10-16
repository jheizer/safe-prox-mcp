using System.Text.Json;
using FluentAssertions;
using Moq;
using SafeProxMcp.Models;
using SafeProxMcp.Services;
using SafeProxMcp.Tools;
using Xunit;

namespace SafeProxMcp.Tests.Tools;

public class ProxmoxReadToolsTests
{
    private static readonly JsonSerializerOptions ModelOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Mock<IProxmoxClient> _client = new();
    private readonly Mock<IMultiClusterProxmoxClient> _multi = new();
    private readonly ProxmoxReadTools _tools;

    public ProxmoxReadToolsTests()
    {
        _multi.Setup(m => m.GetClient(It.IsAny<string>())).Returns((string cluster) =>
            cluster == "home"
                ? _client.Object
                : throw new ArgumentException($"Unknown cluster '{cluster}'. Configured clusters: home"));
        _multi.Setup(m => m.GetClusterInfo()).Returns(new List<ProxmoxClusterInfo>
        {
            new("home", "https://pve:8006")
        }.AsReadOnly());

        _tools = new ProxmoxReadTools(_multi.Object);
    }

    private static T Model<T>(string json) => JsonSerializer.Deserialize<T>(json, ModelOptions)!;

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    // -- Cluster selection ----------------------------------------------

    [Fact]
    public async Task GetClustersAsync_ReturnsIdsAndUrlsWithoutTokens()
    {
        var clusters = Json(await _tools.GetClustersAsync()).EnumerateArray().ToList();

        clusters.Should().HaveCount(1);
        clusters[0].GetProperty("id").GetString().Should().Be("home");
        clusters[0].GetProperty("url").GetString().Should().Be("https://pve:8006");
        clusters[0].ToString().Should().NotContain("token");
    }

    [Fact]
    public async Task UnknownCluster_SurfacesConfiguredClusterIds()
    {
        var act = () => _tools.GetNodesAsync("nope");

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("Configured clusters: home");
    }

    [Fact]
    public void Constructor_RejectsNullMultiClient()
    {
        var act = () => new ProxmoxReadTools(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -- Identifier lookup ----------------------------------------------

    [Fact]
    public async Task LookupAllVmsAndContainersAsync_ProjectsClusterNodeVmIdNameTypeAndStatus()
    {
        _multi.Setup(m => m.GetInventoryAsync()).ReturnsAsync(new ProxmoxInventoryResult(
            new List<ProxmoxInventoryEntry>
            {
                new("home", "wtrpro", 100, "web-vm", "vm", "running"),
                new("home", "wtrpro", 200, "dns-ct", "container", "stopped"),
                new("router", "i5quotom", 100, "opnsense", "vm", "running")
            },
            new List<string> { "cluster 'office' node 'nas' VMs: Permission denied" }));

        var result = Json(await _tools.LookupAllVmsAndContainersAsync());

        var machines = result.GetProperty("machines").EnumerateArray().ToList();
        machines.Should().HaveCount(3);
        machines[0].GetProperty("cluster").GetString().Should().Be("home");
        machines[0].GetProperty("node").GetString().Should().Be("wtrpro");
        machines[0].GetProperty("vmid").GetInt32().Should().Be(100);
        machines[0].GetProperty("name").GetString().Should().Be("web-vm");
        machines[0].GetProperty("type").GetString().Should().Be("vm");
        machines[0].GetProperty("status").GetString().Should().Be("running");
        machines[1].GetProperty("type").GetString().Should().Be("container");
        machines[2].GetProperty("cluster").GetString().Should().Be("router");
        machines[2].GetProperty("vmid").GetInt32().Should().Be(100);

        result.GetProperty("errors").EnumerateArray()
            .Select(entry => entry.GetString())
            .Should().ContainSingle().Which.Should().Contain("cluster 'office'");
    }

    // -- Nodes -----------------------------------------------------------

    [Fact]
    public async Task GetNodesAsync_ProjectsNodeFields()
    {
        _client.Setup(c => c.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxNodesResponse>("""
                {"data":[{"node":"pve1","type":"node","status":"online","maxcpu":8,
                 "maxmem":34359738368,"maxdisk":100000000000,"cpu":0.45,
                 "mem":16000000000,"disk":50000000000,"uptime":123456}]}
                """));

        var nodes = Json(await _tools.GetNodesAsync("home")).EnumerateArray().ToList();

        nodes.Should().HaveCount(1);
        nodes[0].GetProperty("name").GetString().Should().Be("pve1");
        nodes[0].GetProperty("status").GetString().Should().Be("online");
        nodes[0].GetProperty("maxcpu").GetInt32().Should().Be(8);
        nodes[0].GetProperty("uptimeSeconds").GetInt64().Should().Be(123456);
    }

    [Fact]
    public async Task GetNodeStatusAsync_ProjectsNestedUsage()
    {
        _client.Setup(c => c.GetNodeStatusAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxNodeStatusResponse>("""
                {"pveversion":"pve-manager/8.2.4","kversion":"6.8.8-2-pve","uptime":123456,
                 "cpu":0.45,"wait":0.01,"idle":0.54,
                 "memory":{"total":34359738368,"used":16000000000,"free":18359738368,"available":18000000000},
                 "swap":{"total":4000000000,"used":0,"free":4000000000},
                 "rootfs":{"total":100000000000,"used":50000000000,"avail":45000000000},
                 "cpuinfo":{"model":"Intel Xeon","cpus":8,"sockets":1,"cores":8,"mhz":"2400"},
                 "loadavg":[0.11,0.12,0.13]}
                """));

        var status = Json(await _tools.GetNodeStatusAsync("home", "pve1"));

        status.GetProperty("pveVersion").GetString().Should().Be("pve-manager/8.2.4");
        status.GetProperty("memory").GetProperty("total").GetInt64().Should().Be(34359738368);
        status.GetProperty("cpu").GetProperty("model").GetString().Should().Be("Intel Xeon");
        status.GetProperty("rootFs").GetProperty("avail").GetInt64().Should().Be(45000000000);
    }

    [Fact]
    public async Task GetNodeStatusAsync_MissingOptionalSections_ReturnNulls()
    {
        _client.Setup(c => c.GetNodeStatusAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxNodeStatusResponse>("""{"pveversion":"pve-manager/8.2.4"}"""));

        var status = Json(await _tools.GetNodeStatusAsync("home", "pve1"));

        status.GetProperty("memory").ValueKind.Should().Be(JsonValueKind.Null);
        status.GetProperty("cpu").ValueKind.Should().Be(JsonValueKind.Null);
        status.GetProperty("swap").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyNodeName_IsRejected(string node)
    {
        var act = () => _tools.GetNodeStatusAsync("home", node);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("node is required*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    public async Task NonPositiveVmId_IsRejected(int vmId)
    {
        var act = () => _tools.GetVmStatusAsync("home", "pve1", vmId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*positive Proxmox VMID*");
        _client.Verify(c => c.GetVmStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -- Cluster level ---------------------------------------------------

    [Fact]
    public async Task GetClusterStatusAsync_ProjectsMembers()
    {
        _client.Setup(c => c.GetClusterStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxClusterStatusResponse>("""
                {"data":[{"name":"pve1","type":"node","level":"","status":"online","online":1,
                 "ip":"10.0.0.69","nodeid":1}]}
                """));

        var members = Json(await _tools.GetClusterStatusAsync("home")).EnumerateArray().ToList();

        members[0].GetProperty("name").GetString().Should().Be("pve1");
        members[0].GetProperty("online").GetInt32().Should().Be(1);
        members[0].GetProperty("ip").GetString().Should().Be("10.0.0.69");
    }

    [Fact]
    public async Task GetClusterResourcesAsync_PassesTypeFilter()
    {
        _client.Setup(c => c.GetClusterResourcesAsync("vm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxResourcesResponse>("""
                {"data":[{"type":"vm","id":"pve1/100","node":"pve1","name":"web-vm","vmid":100,
                 "status":"running","uptime":5000,"cpu":0.1,"cpus":2,"maxcpu":2,
                 "mem":4000000000,"maxmem":8000000000,"disk":10000000000,"maxdisk":20000000000,
                 "netin":0,"netout":0,"diskread":0,"diskwrite":0,"template":0,"tags":"web;prod"}]}
                """));

        var resources = Json(await _tools.GetClusterResourcesAsync("home", "vm")).EnumerateArray().ToList();

        resources.Should().HaveCount(1);
        resources[0].GetProperty("vmid").GetInt32().Should().Be(100);
        resources[0].GetProperty("tags").GetString().Should().Be("web;prod");
    }

    // -- Virtual machines ------------------------------------------------

    [Fact]
    public async Task GetVmsAsync_ProjectsVmList()
    {
        _client.Setup(c => c.GetVmsAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxVmListResponse>("""
                {"data":[{"vmid":100,"name":"web-vm","status":"running","uptime":5000,"cpu":0.1,
                 "cpus":2,"maxcpu":2,"mem":4000000000,"maxmem":8000000000,
                 "disk":10000000000,"maxdisk":20000000000,"netin":0,"netout":0,
                 "diskread":0,"diskwrite":0,"template":0}]}
                """));

        var vms = Json(await _tools.GetVmsAsync("home", "pve1")).EnumerateArray().ToList();

        vms.Should().HaveCount(1);
        vms[0].GetProperty("vmid").GetInt32().Should().Be(100);
        vms[0].GetProperty("status").GetString().Should().Be("running");
        vms[0].GetProperty("uptimeSeconds").GetInt64().Should().Be(5000);
    }

    [Fact]
    public async Task GetVmStatusAsync_ProjectsLiveStatus()
    {
        _client.Setup(c => c.GetVmStatusAsync("pve1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxVmStatusResponse>("""
                {"vmid":100,"name":"web-vm","type":"qemu","status":"running","qmpstatus":"running",
                 "uptime":5000,"pid":12345,"cpus":2,"maxcpu":2,"cpu":0.3,
                 "mem":4000000000,"maxmem":8000000000,"disk":10000000000,"maxdisk":20000000000,
                 "netin":0,"netout":0,"diskread":0,"diskwrite":0,"template":0,"digest":"abc"}
                """));

        var status = Json(await _tools.GetVmStatusAsync("home", "pve1", 100));

        status.GetProperty("vmid").GetInt32().Should().Be(100);
        status.GetProperty("qmpstatus").GetString().Should().Be("running");
        status.GetProperty("pid").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task GetVmConfigAsync_ExposesDeviceLines()
    {
        _client.Setup(c => c.GetVmConfigAsync("pve1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxVmConfigResponse>("""
                {"name":"web-vm","description":"Web server","ostype":"l26","memory":8192,"cores":4,
                 "sockets":1,"cpu":"host","balloon":1024,"numa":0,"onboot":1,"boot":"order=scsi0",
                 "bootdisk":"scsi0","kvm":1,"bios":"seabios","tablet":1,"autostart":0,"protect":0,
                 "template":0,"tags":"web;prod","digest":"d1","vmgenid":"uuid-1",
                 "scsi0":"local:100/vm-100-disk-0.raw,size=50G",
                 "ipconfig0":"ip=10.0.0.10/24"}
                """));

        var config = Json(await _tools.GetVmConfigAsync("home", "pve1", 100));

        config.GetProperty("name").GetString().Should().Be("web-vm");
        config.GetProperty("memoryMB").GetInt64().Should().Be(8192);
        config.GetProperty("onboot").GetBoolean().Should().BeTrue();
        config.GetProperty("devices").GetProperty("scsi0").GetString()
            .Should().Be("local:100/vm-100-disk-0.raw,size=50G");
    }

    // -- Containers ------------------------------------------------------

    [Fact]
    public async Task GetContainersAsync_ProjectsContainerList()
    {
        _client.Setup(c => c.GetContainersAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxLxcListResponse>("""
                {"data":[{"vmid":200,"name":"web-lxc","status":"running","uptime":5000,"pid":67890,
                 "type":"lxc","cpu":0.2,"cpus":2,"maxcpu":2,"mem":1000000000,"maxmem":2000000000,
                 "disk":3000000000,"maxdisk":10000000000,"swap":0,"maxswap":0,
                 "netin":0,"netout":0,"diskread":0,"diskwrite":0,"template":0}]}
                """));

        var containers = Json(await _tools.GetContainersAsync("home", "pve1")).EnumerateArray().ToList();

        containers.Should().HaveCount(1);
        containers[0].GetProperty("vmid").GetInt32().Should().Be(200);
        containers[0].GetProperty("pid").GetInt32().Should().Be(67890);
    }

    [Fact]
    public async Task GetContainerConfigAsync_ProjectsContainerConfig()
    {
        _client.Setup(c => c.GetContainerConfigAsync("pve1", 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxLxcConfigResponse>("""
                {"hostname":"web-lxc","arch":"amd64","ostype":"debian","memory":2048,"swap":512,
                 "cores":2,"cpuunits":1024,"rootfs":"local:200/sub-vz.conf","onboot":1,
                 "unprivileged":1,"features":"keyctl=1","tty":2,"tags":"web;prod","protection":0,
                 "mp0":"/mnt/data"}
                """));

        var config = Json(await _tools.GetContainerConfigAsync("home", "pve1", 200));

        config.GetProperty("hostname").GetString().Should().Be("web-lxc");
        config.GetProperty("unprivileged").GetBoolean().Should().BeTrue();
        config.GetProperty("rootfs").GetString().Should().Be("local:200/sub-vz.conf");
        config.GetProperty("devices").GetProperty("mp0").GetString().Should().Be("/mnt/data");
    }

    // -- Infrastructure --------------------------------------------------

    [Fact]
    public async Task GetStorageAsync_WithoutNode_UsesClusterEndpoint()
    {
        _client.Setup(c => c.GetStorageAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxStorageListResponse>("""
                {"data":[{"storage":"local","type":"dir","content":"backup,iso","nodes":"pve1",
                 "digest":"d1","shared":0}]}
                """));

        var storage = Json(await _tools.GetStorageAsync("home")).EnumerateArray().ToList();

        storage[0].GetProperty("storage").GetString().Should().Be("local");
        storage[0].GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetStorageAsync_WithNode_IncludesUsage()
    {
        _client.Setup(c => c.GetStorageAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxStorageListResponse>("""
                {"data":[{"storage":"local","type":"dir","content":"backup,iso","nodes":"pve1",
                 "enabled":1,"active":1,"shared":0,"total":100000000000,"used":50000000000,
                 "avail":50000000000,"used_fraction":0.5}]}
                """));

        var storage = Json(await _tools.GetStorageAsync("home", "pve1")).EnumerateArray().ToList();

        storage[0].GetProperty("total").GetInt64().Should().Be(100000000000);
        storage[0].GetProperty("usedFraction").GetDouble().Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public async Task GetNetworksAsync_ProjectsInterfaces()
    {
        _client.Setup(c => c.GetNetworksAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxNetworkListResponse>("""
                {"data":[{"iface":"vmbr0","type":"bridge","method":"manual","active":1,"exists":1,
                 "autostart":1,"vlan_aware":0,"ip":"10.0.0.69/24","gateway":"10.0.0.1",
                 "bridge":"vmbr0","ports":"eth0","priority":0}]}
                """));

        var networks = Json(await _tools.GetNetworksAsync("home", "pve1")).EnumerateArray().ToList();

        networks[0].GetProperty("iface").GetString().Should().Be("vmbr0");
        networks[0].GetProperty("active").GetBoolean().Should().BeTrue();
        networks[0].GetProperty("gateway").GetString().Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task GetUsersAsync_ProjectsUsers()
    {
        _client.Setup(c => c.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxUserListResponse>("""
                {"data":[{"userid":"mcp@pve","enable":1,"expire":0,"email":"mcp@example.com",
                 "comment":"read-only MCP","groups":"monitoring","realm-type":"pve"}]}
                """));

        var users = Json(await _tools.GetUsersAsync("home")).EnumerateArray().ToList();

        users[0].GetProperty("userid").GetString().Should().Be("mcp@pve");
        users[0].GetProperty("enable").GetBoolean().Should().BeTrue();
        users[0].GetProperty("groups").GetString().Should().Be("monitoring");
    }

    [Fact]
    public async Task GetGroupsAsync_ProjectsGroups()
    {
        _client.Setup(c => c.GetGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxGroupListResponse>("""
                {"data":[{"groupid":"monitoring","comment":"Read-only","users":"mcp@pve","realm-type":"pve"}]}
                """));

        var groups = Json(await _tools.GetGroupsAsync("home")).EnumerateArray().ToList();

        groups[0].GetProperty("groupid").GetString().Should().Be("monitoring");
    }

    [Fact]
    public async Task GetDisksAsync_ProjectsDisks()
    {
        _client.Setup(c => c.GetDisksAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxDiskListResponse>("""
                {"data":[{"devname":"/dev/sda","devpath":"/dev/sda","type":"ata","size":250059350016,
                 "model":"Samsung SSD 870","vendor":"Samsung","serial":"S123","health":"PASSED",
                 "rpm":7200,"rotational":0,"scheduler":"none","smart":1,"filesystem":"ext4",
                 "mounts":"/var/lib/vz"}]}
                """));

        var disks = Json(await _tools.GetDisksAsync("home", "pve1")).EnumerateArray().ToList();

        disks[0].GetProperty("devname").GetString().Should().Be("/dev/sda");
        disks[0].GetProperty("health").GetString().Should().Be("PASSED");
        disks[0].GetProperty("size").GetInt64().Should().Be(250059350016);
    }

    [Fact]
    public async Task GetVersionAsync_ProjectsVersion()
    {
        _client.Setup(c => c.GetVersionAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxVersionResponse>(
                """{"data":{"version":"8.2.4","release":"pve-manager/8.2.4","repoid":"enterprise"}}"""));

        var version = Json(await _tools.GetVersionAsync("home", "pve1"));

        version.GetProperty("version").GetString().Should().Be("8.2.4");
        version.GetProperty("repoid").GetString().Should().Be("enterprise");
    }

    // -- Syslog -----------------------------------------------------------

    [Fact]
    public async Task GetNodeSyslogAsync_PagesAndReportsTotal()
    {
        _client.Setup(c => c.GetNodeSyslogAsync("pve1", 10, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxNodeSyslogResponse>("""
                {"data":[{"n":10,"t":"line ten"},{"n":11,"t":"line eleven"}],"total":1234}
                """));

        var syslog = Json(await _tools.GetNodeSyslogAsync("home", "pve1", start: 10, limit: 2));

        syslog.GetProperty("start").GetInt64().Should().Be(10);
        syslog.GetProperty("total").GetInt64().Should().Be(1234);
        syslog.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("text").GetString())
            .Should().Equal("line ten", "line eleven");
    }

    [Theory]
    [InlineData(-1, 200)]
    [InlineData(0, 0)]
    [InlineData(0, 1001)]
    public async Task GetNodeSyslogAsync_RejectsOutOfRangePaging(long start, long limit)
    {
        var act = () => _tools.GetNodeSyslogAsync("home", "pve1", start, limit);

        await act.Should().ThrowAsync<ArgumentException>();
        _client.Verify(c => c.GetNodeSyslogAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -- Snapshots ---------------------------------------------------------

    [Fact]
    public async Task GetVmSnapshotsAsync_FallsBackToSnapTimeWhenTimestampMissing()
    {
        _client.Setup(c => c.GetVmSnapshotsAsync("pve1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Model<ProxmoxSnapshotListResponse>("""
                {"data":[{"name":"before-update","description":"Before system update",
                 "snaptime":1694500000,"parent":"root","vmstate":1}]}
                """));

        var snapshots = Json(await _tools.GetVmSnapshotsAsync("home", "pve1", 100)).EnumerateArray().ToList();

        snapshots[0].GetProperty("timestamp").GetInt64().Should().Be(1694500000);
        snapshots[0].GetProperty("parent").GetString().Should().Be("root");
    }

    // -- Error propagation -------------------------------------------------

    [Fact]
    public async Task ClusterApiError_PropagatesToCaller()
    {
        _client.Setup(c => c.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProxmoxApiException("Proxmox cluster 'home' returned 401 for GET nodes",
                System.Net.HttpStatusCode.Unauthorized));

        var act = () => _tools.GetNodesAsync("home");

        await act.Should().ThrowAsync<ProxmoxApiException>().WithMessage("*401*");
    }
}
