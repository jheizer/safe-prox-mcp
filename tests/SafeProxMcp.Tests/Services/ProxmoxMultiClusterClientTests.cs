using System.Net;
using FluentAssertions;
using Moq;
using SafeProxMcp.Models;
using SafeProxMcp.Services;
using Xunit;

namespace SafeProxMcp.Tests.Services;

public class ProxmoxMultiClusterClientTests
{
    private static ProxmoxClusterConfig Config(string id)
        => new() { Id = id, Url = $"https://{id}:8006", Token = "user@pam!token=uuid" };

    private static (ProxmoxMultiClusterClient Client, List<Mock<IProxmoxClient>> Clients) Create(
        params string[] ids)
    {
        var mocks = new List<Mock<IProxmoxClient>>();
        var client = new ProxmoxMultiClusterClient(
            ids.Select(Config),
            _ =>
            {
                var mock = new Mock<IProxmoxClient>();
                mocks.Add(mock);
                return mock.Object;
            });
        return (client, mocks);
    }

    [Fact]
    public void GetClusterInfo_ReturnsEveryClusterWithoutTokens()
    {
        var (client, _) = Create("home", "office");

        var info = client.GetClusterInfo();

        info.Should().HaveCount(2);
        info.Select(c => c.Id).Should().BeEquivalentTo(new[] { "home", "office" });
        info.Should().NotContain(c => c.Url.Contains("token"));
    }

    [Fact]
    public void GetClient_ReturnsTheMatchingCluster()
    {
        var (client, mocks) = Create("alpha", "beta");

        client.GetClient("alpha").Should().BeSameAs(mocks[0].Object);
        client.GetClient("beta").Should().BeSameAs(mocks[1].Object);
    }

    [Fact]
    public void GetClient_IsCaseInsensitive()
    {
        var (client, mocks) = Create("Home");

        client.GetClient("HOME").Should().BeSameAs(mocks[0].Object);
    }

    [Fact]
    public void GetClient_UnknownCluster_ListsConfiguredClusters()
    {
        var (client, _) = Create("home", "office");

        var act = () => client.GetClient("nope");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown cluster 'nope'*home, office*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetClient_MissingClusterId_Throws(string? clusterId)
    {
        var (client, _) = Create("home");

        var act = () => client.GetClient(clusterId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsDuplicateIds()
    {
        var act = () => new ProxmoxMultiClusterClient(
            new[] { Config("home"), Config("home") },
            _ => new Mock<IProxmoxClient>().Object);

        act.Should().Throw<ArgumentException>().WithMessage("*Duplicate cluster id 'home'*");
    }

    [Fact]
    public void Constructor_RejectsEmptyConfigList()
    {
        var act = () => new ProxmoxMultiClusterClient(
            Array.Empty<ProxmoxClusterConfig>(),
            _ => new Mock<IProxmoxClient>().Object);

        act.Should().Throw<ArgumentException>().WithMessage("*At least one cluster*");
    }

    [Fact]
    public void Constructor_RejectsConfigWithoutId()
    {
        var act = () => new ProxmoxMultiClusterClient(
            new[] { new ProxmoxClusterConfig { Url = "https://x:8006", Token = "u@pam!t=uuid" } },
            _ => new Mock<IProxmoxClient>().Object);

        act.Should().Throw<ArgumentException>().WithMessage("*non-empty id*");
    }

    [Fact]
    public void Dispose_DisposesEveryClient()
    {
        var (client, mocks) = Create("home", "office");

        client.Dispose();

        foreach (var mock in mocks)
            mock.Verify(c => c.Dispose());
    }

    // -- Inventory lookup -----------------------------------------------

    private static ProxmoxClusterConfig HttpConfig(string id)
        => new() { Id = id, Url = $"https://{id}:8006/api2/json", Token = "user@pam!token=uuid" };

    private static (ProxmoxMultiClusterClient Client, Dictionary<string, TestHttpHandler> Handlers) CreateWithTransport(
        params string[] ids)
    {
        var handlers = new Dictionary<string, TestHttpHandler>(StringComparer.OrdinalIgnoreCase);
        var client = new ProxmoxMultiClusterClient(
            ids.Select(HttpConfig),
            config =>
            {
                var handler = new TestHttpHandler();
                handlers[config.Id] = handler;
                return new ProxmoxApiClient(handler, config);
            });

        return (client, handlers);
    }

    [Fact]
    public async Task GetInventoryAsync_ReturnsEveryVmAndContainerOnEveryNodeOfEveryCluster()
    {
        var (client, handlers) = CreateWithTransport("home", "router");

        handlers["home"]
            .Add(@"^/api2/json/nodes$", """{"data":[{"node":"wtrpro","type":"node","status":"online"},{"node":"wtrpro2","type":"node","status":"online"}]}""")
            .Add(@"^/api2/json/nodes/wtrpro/qemu$", """{"data":[{"vmid":100,"name":"web-vm","status":"running"}]}""")
            .Add(@"^/api2/json/nodes/wtrpro/lxc$", """{"data":[{"vmid":200,"name":"dns-ct","status":"stopped"}]}""")
            .Add(@"^/api2/json/nodes/wtrpro2/qemu$", """{"data":[]}""")
            .Add(@"^/api2/json/nodes/wtrpro2/lxc$", """{"data":[]}""");

        handlers["router"]
            .Add(@"^/api2/json/nodes$", """{"data":[{"node":"i5quotom","type":"node","status":"online"}]}""")
            .Add(@"^/api2/json/nodes/i5quotom/qemu$", """{"data":[{"vmid":100,"name":"opnsense","status":"running"}]}""")
            .Add(@"^/api2/json/nodes/i5quotom/lxc$", """{"data":[{"vmid":101,"name":"pi-hole","status":"running"},{"vmid":103,"name":"iot-pi-hole","status":"running"}]}""");

        var inventory = await client.GetInventoryAsync();

        inventory.Errors.Should().BeEmpty();
        inventory.Entries.Should().Equal(
            new ProxmoxInventoryEntry("home", "wtrpro", 100, "web-vm", "vm", "running"),
            new ProxmoxInventoryEntry("home", "wtrpro", 200, "dns-ct", "container", "stopped"),
            new ProxmoxInventoryEntry("router", "i5quotom", 100, "opnsense", "vm", "running"),
            new ProxmoxInventoryEntry("router", "i5quotom", 101, "pi-hole", "container", "running"),
            new ProxmoxInventoryEntry("router", "i5quotom", 103, "iot-pi-hole", "container", "running"));
    }

    [Fact]
    public async Task GetInventoryAsync_UnreadableCluster_ReportsItAndKeepsTheOtherClusters()
    {
        var (client, handlers) = CreateWithTransport("home", "router");

        handlers["home"].Add(@"^/api2/json/nodes$",
            """{"data":null,"message":"Permission denied"}""",
            (int)HttpStatusCode.Forbidden);
        handlers["router"]
            .Add(@"^/api2/json/nodes$", """{"data":[{"node":"i5quotom","type":"node","status":"online"}]}""")
            .Add(@"^/api2/json/nodes/i5quotom/qemu$", """{"data":[{"vmid":100,"name":"opnsense","status":"running"}]}""")
            .Add(@"^/api2/json/nodes/i5quotom/lxc$", """{"data":[]}""");

        var inventory = await client.GetInventoryAsync();

        inventory.Entries.Should().ContainSingle()
            .Which.Should().Be(new ProxmoxInventoryEntry("router", "i5quotom", 100, "opnsense", "vm", "running"));
        inventory.Errors.Should().ContainSingle()
            .Which.Should().Contain("cluster 'home'").And.Contain("Permission denied");
    }

    [Fact]
    public async Task GetInventoryAsync_FailingVmList_ReportsTheNodeAndStillReturnsItsContainers()
    {
        var (client, handlers) = CreateWithTransport("home");

        handlers["home"]
            .Add(@"^/api2/json/nodes$", """{"data":[{"node":"wtrpro","type":"node","status":"online"}]}""")
            .Add(@"^/api2/json/nodes/wtrpro/qemu$",
                """{"data":null,"message":"no privileges"}""",
                (int)HttpStatusCode.Forbidden)
            .Add(@"^/api2/json/nodes/wtrpro/lxc$", """{"data":[{"vmid":200,"name":"dns-ct","status":"running"}]}""");

        var inventory = await client.GetInventoryAsync();

        inventory.Entries.Should().ContainSingle()
            .Which.Should().Be(new ProxmoxInventoryEntry("home", "wtrpro", 200, "dns-ct", "container", "running"));
        inventory.Errors.Should().ContainSingle()
            .Which.Should().Contain("node 'wtrpro' VMs").And.Contain("no privileges");
    }
}
