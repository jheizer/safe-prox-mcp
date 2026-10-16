using System.Net;
using FluentAssertions;
using SafeProxMcp.Models;
using SafeProxMcp.Services;
using Xunit;

namespace SafeProxMcp.Tests.Services;

public class ProxmoxApiClientTests
{
    private const string BasePath = "/api2/json";

    private static ProxmoxApiClient Client(
        TestHttpHandler handler,
        string url = "https://test-proxmox:8006",
        string token = "root@pam!test=test-uuid")
        => new(handler, new ProxmoxClusterConfig { Id = "test", Url = url, Token = token });

    private static ProxmoxClusterConfig Config(string url)
        => new() { Id = "test", Url = url, Token = "root@pam!test=test-uuid" };

    // -- Request construction ------------------------------------------

    [Fact]
    public async Task Request_UsesApi2JsonRoot_WhenUrlIsClusterRoot()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes$", "{\"data\":[]}");
        using var client = Client(handler, "https://pve:8006");

        await client.GetNodesAsync();

        handler.LastRequestPath.Should().Be("/api2/json/nodes");
    }

    [Fact]
    public async Task Request_UsesApi2JsonRoot_WhenUrlAlreadyEndsWithApi2Json()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes$", "{\"data\":[]}");
        using var client = Client(handler, "https://pve:8006/api2/json");

        await client.GetNodesAsync();

        handler.LastRequestPath.Should().Be("/api2/json/nodes");
    }

    [Fact]
    public async Task Request_SendsPveApiTokenHeader()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes$", "{\"data\":[]}");
        using var client = Client(handler, token: "mcp@pve!read=abc-123");

        await client.GetNodesAsync();

        handler.LastRequest!.Headers.Authorization!.ToString()
            .Should().Be("PVEAPIToken mcp@pve!read=abc-123");
    }

    [Fact]
    public void Constructor_RejectsMalformedToken()
    {
        var act = () => Client(new TestHttpHandler(), token: "just-a-uuid");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*user@realm!name=uuid*");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://pve:8006")]
    [InlineData("pve:8006")]
    public void Constructor_RejectsNonHttpUrl(string url)
    {
        var act = () => new ProxmoxApiClient(new TestHttpHandler(), Config(url));

        act.Should().Throw<ArgumentException>().WithMessage("*invalid url*");
    }

    [Fact]
    public void Constructor_UsesTheFixedSixtySecondRequestTimeout()
    {
        using var client = new ProxmoxApiClient(new TestHttpHandler(), Config("https://pve:8006"));

        client.RequestTimeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    // -- Read endpoints -------------------------------------------------

    [Fact]
    public async Task GetNodesAsync_ReturnsNodes()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes$",
            """
            {"data":[{"node":"pve1","type":"node","status":"online","maxcpu":8,"maxmem":34359738368,"uptime":123456},
                     {"node":"pve2","type":"node","status":"offline"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetNodesAsync();

        result.Data.Should().HaveCount(2);
        result.Data[0].Name.Should().Be("pve1");
        result.Data[0].MaxCpu.Should().Be(8);
        result.Data[0].Uptime.Should().Be(123456);
        result.Data[1].Status.Should().Be("offline");
    }

    [Fact]
    public async Task GetNodeStatusAsync_ReturnsNestedUsage()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/status$",
            """
            {"data":{"pveversion":"pve-manager/8.2.4/a5f88e33","kversion":"6.8.8-2-pve","uptime":123456,
                     "cpu":0.45,"wait":0.01,"idle":0.54,
                     "memory":{"total":34359738368,"used":16000000000,"free":18359738368},
                     "swap":{"total":4000000000,"used":0},
                     "rootfs":{"total":100000000000,"used":50000000000,"avail":45000000000},
                     "cpuinfo":{"model":"Intel Xeon","cpus":8,"sockets":1,"cores":8,"mhz":"2400"}}}
            """);
        using var client = Client(handler);

        var result = await client.GetNodeStatusAsync("pve1");

        result.PveVersion.Should().StartWith("pve-manager/");
        result.CpuUsage.Should().BeApproximately(0.45, 0.001);
        result.Memory!.Total.Should().Be(34359738368);
        result.RootFs!.Avail.Should().Be(45000000000);
        result.CpuInfo!.Model.Should().Be("Intel Xeon");
    }

    [Fact]
    public async Task GetClusterStatusAsync_ReturnsClusterAndNodeEntries()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/cluster/status$",
            """
            {"data":[{"name":"pvecluster","type":"cluster","level":"","clusterid":{"name":"pvecluster"}},
                     {"name":"pve1","type":"node","status":"online","level":"","id":null,"online":1},
                     {"name":"corosync","type":"corosync","level":"","id":"corosync","online":1}]}
            """);
        using var client = Client(handler);

        var result = await client.GetClusterStatusAsync();

        result.Data.Should().HaveCount(3);
        result.Data[1].Name.Should().Be("pve1");
        result.Data[1].Online.Should().Be(1);
        result.Data[0].Status.Should().BeNull();
    }

    [Fact]
    public async Task GetClusterResourcesAsync_AddsTypeFilter()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/cluster/resources\?type=vm$",
            """
            {"data":[{"type":"vm","id":"pve1/100","node":"pve1","vmid":100,"name":"web-vm","status":"running",
                      "cpus":2,"cpu":0.1,"maxmem":8000000000,"mem":4000000000,"template":0,"tags":"web;prod"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetClusterResourcesAsync("vm");

        handler.LastRequestQuery.Should().Be("?type=vm");
        result.Data.Should().HaveCount(1);
        result.Data[0].VmId.Should().Be(100);
        result.Data[0].Tags.Should().Be("web;prod");
    }

    [Fact]
    public async Task GetClusterResourcesAsync_WithoutFilter_ReturnsAll()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/cluster/resources$",
            """
            {"data":[{"type":"node","id":"pve1","node":"pve1","name":"pve1"},
                     {"type":"storage","id":"pve1/local","node":"pve1","storage":"local","plugintype":"dir"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetClusterResourcesAsync();

        handler.LastRequestQuery.Should().BeEmpty();
        result.Data.Should().HaveCount(2);
        result.Data[1].PluginType.Should().Be("dir");
    }

    [Fact]
    public async Task GetVmsAsync_ReturnsVmList()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu$",
            """
            {"data":[{"vmid":100,"name":"web-vm","status":"running","uptime":5000,"cpu":0.1,"cpus":2,
                      "maxmem":8000000000,"mem":4000000000,"maxdisk":20000000000,"disk":10000000000,"template":0},
                     {"vmid":101,"name":"db-vm","status":"stopped","maxmem":16000000000}]}
            """);
        using var client = Client(handler);

        var result = await client.GetVmsAsync("pve1");

        result.Data.Should().HaveCount(2);
        result.Data[0].VmId.Should().Be(100);
        result.Data[0].Status.Should().Be("running");
        result.Data[1].Status.Should().Be("stopped");
    }

    [Fact]
    public async Task GetVmStatusAsync_ReturnsStatus()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu/100/status/current$",
            """
            {"data":{"vmid":100,"name":"web-vm","status":"running","qmpstatus":"running","pid":12345,
                     "cpus":2,"cpu":0.3,"mem":4000000000,"maxmem":8000000000,"uptime":5000,"template":0}}
            """);
        using var client = Client(handler);

        var result = await client.GetVmStatusAsync("pve1", 100);

        result.VmId.Should().Be(100);
        result.QmpStatus.Should().Be("running");
        result.Pid.Should().Be(12345);
        result.CpuUsage.Should().BeApproximately(0.3, 0.001);
    }

    [Fact]
    public async Task GetVmConfigAsync_ParsesPveBooleansAndKeepsUnknownKeys()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu/100/config$",
            """
            {"data":{"name":"web-vm","description":"Web server","memory":"8192","cores":4,"sockets":1,
                     "cpu":"host","onboot":"1","kvm":1,"numa":0,"template":0,"protect":"0",
                     "agent":"1","tags":"web;prod","digest":"abc123","vmgenid":"uuid-1",
                     "scsi0":"local:100/vm-100-disk-0.raw,size=50G","ipconfig0":"ip=dhcp"}}
            """);
        using var client = Client(handler);

        var result = await client.GetVmConfigAsync("pve1", 100);

        result.Name.Should().Be("web-vm");
        result.Memory.Should().Be(8192);
        result.OnBoot.Should().BeTrue();
        result.Kvm.Should().BeTrue();
        result.NumaEnabled.Should().BeFalse();
        result.Template.Should().BeFalse();
        result.ExtensionData.Should().ContainKey("scsi0");
        result.ExtensionData!["ipconfig0"].GetString().Should().Be("ip=dhcp");
    }

    [Fact]
    public async Task GetContainersAsync_ReturnsContainerList()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/lxc$",
            """
            {"data":[{"vmid":200,"name":"web-lxc","status":"running","type":"lxc","cpus":2,
                      "mem":2000000000,"maxmem":4000000000,"template":0}]}
            """);
        using var client = Client(handler);

        var result = await client.GetContainersAsync("pve1");

        result.Data.Should().HaveCount(1);
        result.Data[0].VmId.Should().Be(200);
        result.Data[0].Type.Should().Be("lxc");
    }

    [Fact]
    public async Task GetContainerStatusAsync_ReturnsStatus()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/lxc/200/status/current$",
            """
            {"data":{"vmid":200,"name":"web-lxc","status":"running","pid":67890,"cpus":2,
                     "mem":1000000000,"maxmem":2000000000}}
            """);
        using var client = Client(handler);

        var result = await client.GetContainerStatusAsync("pve1", 200);

        result.Name.Should().Be("web-lxc");
        result.Status.Should().Be("running");
        result.Pid.Should().Be(67890);
    }

    [Fact]
    public async Task GetContainerConfigAsync_ParsesContainerConfig()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/lxc/200/config$",
            """
            {"data":{"hostname":"web-lxc","arch":"amd64","ostype":"debian","memory":"2048","swap":"512",
                     "cores":2,"onboot":1,"unprivileged":"1","features":"keyctl=1,nesting=1","tty":2,
                     "tags":"web;prod","protection":"0","rootfs":"local:200/sub-vz.conf","digest":"abc"}}
            """);
        using var client = Client(handler);

        var result = await client.GetContainerConfigAsync("pve1", 200);

        result.Hostname.Should().Be("web-lxc");
        result.Memory.Should().Be(2048);
        result.Unprivileged.Should().BeTrue();
        result.OnBoot.Should().BeTrue();
        result.RootFs.Should().Be("local:200/sub-vz.conf");
    }

    [Fact]
    public async Task GetStorageAsync_ReturnsStorageList()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/storage$",
            """
            {"data":[{"storage":"local","type":"dir","content":"backup,iso,vztmpl","nodes":"pve1",
                      "shared":0,"digest":"abc123","path":"/var/lib/vz"},
                     {"storage":"nas","type":"nfs","content":"backup,images","nodes":"pve1,pve2",
                      "shared":1,"server":"10.0.0.67","path":"/volume1/pvebackup","prune-backups":"keep-7",
                      "total":1000000000000,"used":500000000000,"avail":500000000000,
                      "used_fraction":0.5,"active":1,"enabled":1}]}
            """);
        using var client = Client(handler);

        var result = await client.GetStorageAsync();

        result.Data.Should().HaveCount(2);
        result.Data[0].Storage.Should().Be("local");
        result.Data[0].Shared.Should().Be(0);
        result.Data[0].Path.Should().Be("/var/lib/vz");
        result.Data[1].Server.Should().Be("10.0.0.67");
        result.Data[1].Active.Should().BeTrue();
        result.Data[1].UsedFraction.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public async Task GetStorageAsync_WithNode_ReturnsPerNodeUsage()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/storage$",
            """
            {"data":[{"storage":"local","type":"dir","total":100000000000,"used":50000000000,
                      "avail":50000000000,"used_fraction":0.5,"enabled":1,"active":1,"shared":0}]}
            """);
        using var client = Client(handler);

        var result = await client.GetStorageAsync("pve1");

        result.Data.Should().HaveCount(1);
        result.Data[0].Total.Should().Be(100000000000);
        result.Data[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetNetworksAsync_ReturnsInterfaces()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/network$",
            """
            {"data":[{"iface":"vmbr0","type":"bridge","method":"static","active":1,"exists":1,
                      "autostart":1,"address":"10.0.0.69","netmask":"24","cidr":"10.0.0.69/24",
                      "gateway":"10.0.0.1","bridge_ports":"enp5s0","bridge_fd":"0",
                      "bridge_stp":"off","families":["inet"],"priority":4},
                     {"iface":"vmbr1","type":"bridge","method":"manual","method6":"manual",
                      "bridge_vlan_aware":"1"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetNetworksAsync("pve1");

        result.Data.Should().HaveCount(2);
        result.Data[0].Iface.Should().Be("vmbr0");
        result.Data[0].Active.Should().BeTrue();
        result.Data[0].Address.Should().Be("10.0.0.69");
        result.Data[0].Cidr.Should().Be("10.0.0.69/24");
        result.Data[0].BridgePorts.Should().Be("enp5s0");
        result.Data[0].BridgeStp.Should().Be("off");
        result.Data[0].Families.Should().Contain("inet");
        result.Data[1].VlanAware.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsUsers()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/access/users$",
            """
            {"data":[{"userid":"root@pam","enable":1,"email":"admin@example.com","expire":0,"realm-type":"pam"},
                     {"userid":"mcp@pve","enable":1,"comment":"read-only MCP user","groups":"monitoring"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetUsersAsync();

        result.Data.Should().HaveCount(2);
        result.Data[0].UserId.Should().Be("root@pam");
        result.Data[0].Enable.Should().BeTrue();
        result.Data[1].Groups.Should().Be("monitoring");
    }

    [Fact]
    public async Task GetGroupsAsync_ReturnsGroups()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/access/groups$",
            """
            {"data":[{"groupid":"monitoring","comment":"Read-only dashboards","users":"mcp@pve"}]}
            """);
        using var client = Client(handler);

        var result = await client.GetGroupsAsync();

        result.Data.Should().HaveCount(1);
        result.Data[0].GroupId.Should().Be("monitoring");
        result.Data[0].Users.Should().Be("mcp@pve");
    }

    [Fact]
    public async Task GetDisksAsync_ReturnsDisks()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/disks/list$",
            """
            {"data":[{"devpath":"/dev/sda","type":"hdd","size":18000207937536,"used":"ZFS",
                      "model":"ST18000NM003D","vendor":"ATA","serial":"ZVT881MQ","health":"PASSED",
                      "rpm":"7200","wearout":"N/A","gpt":1,"osdid":-1},
                     {"devpath":"/dev/nvme0n1","type":"nvme","size":29260513280,"used":"BIOS boot",
                      "model":"INTEL MEMPEK1J032GAD","health":"PASSED","rpm":0,"wearout":98}]}
            """);
        using var client = Client(handler);

        var result = await client.GetDisksAsync("pve1");

        result.Data.Should().HaveCount(2);
        result.Data[0].DevPath.Should().Be("/dev/sda");
        result.Data[0].Used.Should().Be("ZFS");
        result.Data[0].Rpm.Should().Be(7200);
        result.Data[0].Wearout.Should().BeNull();
        result.Data[1].Rpm.Should().Be(0);
        result.Data[1].Wearout.Should().Be(98);
        result.Data[1].Size.Should().Be(29260513280);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsVersion()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/version$",
            """
            {"data":{"version":"8.2.4","release":"pve-manager/8.2.4/a5f88e33","repoid":"enterprise"}}
            """);
        using var client = Client(handler);

        var result = await client.GetVersionAsync("pve1");

        result.Data.Version.Should().Be("8.2.4");
        result.Data.Release.Should().StartWith("pve-manager/");
        result.Data.RepoId.Should().Be("enterprise");
    }

    [Fact]
    public async Task GetNodeSyslogAsync_SendsPagingQuery()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/syslog\?start=10&limit=2$",
            """
            {"data":[{"n":10,"t":"Sep 11 10:00:00 pve1 systemd[1]: Started Proxmox VE."},
                     {"n":11,"t":"Sep 11 10:00:01 pve1 pve-cluster[1]: starting cluster services"}],
             "total":1234}
            """);
        using var client = Client(handler);

        var result = await client.GetNodeSyslogAsync("pve1", start: 10, limit: 2);

        result.Data.Should().HaveCount(2);
        result.Data[0].Number.Should().Be(10);
        result.Data[1].Text.Should().Contain("cluster services");
        result.Total.Should().Be(1234);
    }

    [Fact]
    public async Task GetVmSnapshotsAsync_ReturnsSnapshots()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu/100/snapshot$",
            """
            {"data":[{"name":"root","description":"","snaptime":1694400000},
                     {"name":"before-update","description":"Before system update","snaptime":1694500000,
                      "parent":"root","vmstate":1}]}
            """);
        using var client = Client(handler);

        var result = await client.GetVmSnapshotsAsync("pve1", 100);

        result.Data.Should().HaveCount(2);
        result.Data[1].Name.Should().Be("before-update");
        result.Data[1].Parent.Should().Be("root");
        result.Data[1].SnapTime.Should().Be(1694500000);
    }

    [Fact]
    public async Task GetContainerSnapshotsAsync_ReturnsSnapshots()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/lxc/200/snapshot$",
            """
            {"data":[{"name":"ct-backup","description":"Daily backup","snaptime":1694400000}]}
            """);
        using var client = Client(handler);

        var result = await client.GetContainerSnapshotsAsync("pve1", 200);

        result.Data.Should().HaveCount(1);
        result.Data[0].Name.Should().Be("ct-backup");
    }

    // -- Snapshot creation ----------------------------------------------

    [Fact]
    public async Task CreateVmSnapshotAsync_PostsFormEncodedSnapshotName()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu/100/snapshot$",
            """
            {"data":"UPID:pve1:00001234:00ABCD:65012345:createSnapshot:100:root@pam:"}
            """);
        using var client = Client(handler);

        var result = await client.CreateVmSnapshotAsync("pve1", 100, "before-update", "Before system update");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.Content!.Headers.ContentType!.MediaType
            .Should().Be("application/x-www-form-urlencoded");
        handler.LastRequestBody.Should().Contain("snapname=before-update");
        handler.LastRequestBody.Should().Contain("description=Before+system+update");
        result.Data.Should().StartWith("UPID:");
    }

    [Fact]
    public async Task CreateContainerSnapshotAsync_PostsFormEncodedSnapshotName()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/lxc/200/snapshot$",
            """
            {"data":"UPID:pve1:00001234:00ABCD:65012345:createSnapshot:200:root@pam:"}
            """);
        using var client = Client(handler);

        await client.CreateContainerSnapshotAsync("pve1", 200, "ct-snap");

        handler.LastRequestBody.Should().Contain("snapname=ct-snap");
        handler.LastRequestBody.Should().Contain("description=");
    }

    // -- Backup creation -------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_PostsFormEncodedVzdumpParameters()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/vzdump$",
            """
            {"data":"UPID:pve1:00001234:00ABCD:65012345:vzdump:100:root@pam:"}
            """);
        using var client = Client(handler);

        var result = await client.CreateBackupAsync("pve1", 100, "nas", "snapshot", "zstd", "pre upgrade backup");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.Content!.Headers.ContentType!.MediaType
            .Should().Be("application/x-www-form-urlencoded");
        handler.LastRequestBody.Should().Contain("vmid=100");
        handler.LastRequestBody.Should().Contain("storage=nas");
        handler.LastRequestBody.Should().Contain("mode=snapshot");
        handler.LastRequestBody.Should().Contain("compress=zstd");
        handler.LastRequestBody.Should().Contain("notes=pre+upgrade+backup");
        result.Data.Should().StartWith("UPID:");
    }

    [Fact]
    public async Task CreateBackupAsync_OmitsOptionalParametersWhenNull()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/vzdump$",
            """
            {"data":"UPID:pve1:00001234:00ABCD:65012345:vzdump:200:root@pam:"}
            """);
        using var client = Client(handler);

        var result = await client.CreateBackupAsync("pve1", 200, "local");

        handler.LastRequestBody.Should().Contain("vmid=200");
        handler.LastRequestBody.Should().Contain("storage=local");
        handler.LastRequestBody.Should().NotContain("mode=");
        handler.LastRequestBody.Should().NotContain("compress=");
        handler.LastRequestBody.Should().NotContain("notes=");
        result.Data.Should().StartWith("UPID:");
    }

    // -- Error handling --------------------------------------------------

    [Fact]
    public async Task GetVmStatusAsync_Unauthorized_ThrowsApiExceptionWithStatus()
    {
        var handler = new TestHttpHandler().Add(
            @"^/api2/json/nodes/pve1/qemu/100/status/current$",
            "<html><body>Permission denied</body></html>",
            (int)HttpStatusCode.Unauthorized);
        using var client = Client(handler);

        var act = () => client.GetVmStatusAsync("pve1", 100);

        var apiException = (await act.Should().ThrowAsync<ProxmoxApiException>())
            .Which as ProxmoxApiException;
        apiException!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        apiException.Message.Should().Contain("GET");
        apiException.Message.Should().Contain("nodes/pve1/qemu/100/status/current");
        apiException.Message.Should().Contain("401");
    }

    [Fact]
    public async Task CreateVmSnapshotAsync_ParameterVerificationFailure_ReportsTheRejectedParameter()
    {
        var handler = new TestHttpHandler().Add(
            @"^/api2/json/nodes/pve1/qemu/100/snapshot$",
            """
            {"data":null,"errors":{"snapname":"invalid format - invalid configuration ID 'bad name with spaces'"},"message":"Parameter verification failed.\n"}
            """,
            (int)HttpStatusCode.BadRequest);
        using var client = Client(handler);

        var act = () => client.CreateVmSnapshotAsync("pve1", 100, "bad name with spaces");

        var apiException = (await act.Should().ThrowAsync<ProxmoxApiException>())
            .Which as ProxmoxApiException;
        apiException!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        apiException.Message.Should().Contain("Parameter verification failed");
        apiException.Message.Should().Contain("snapname");
        apiException.Message.Should().Contain("invalid configuration ID");
    }


    [Fact]
    public async Task GetNodesAsync_ProxmoxErrorMessage_IsIncluded()
    {
        var handler = new TestHttpHandler().Add(
            @"^/api2/json/nodes$",
            """
            {"error":"no valid auth ticket or API token"}
            """,
            (int)HttpStatusCode.Forbidden);
        using var client = Client(handler);

        var act = () => client.GetNodesAsync();

        var apiException = (await act.Should().ThrowAsync<ProxmoxApiException>())
            .Which as ProxmoxApiException;
        apiException.Message.Should().Contain("no valid auth ticket or API token");
    }

    [Fact]
    public async Task GetVmsAsync_NullData_ThrowsApiException()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu$", "{\"data\":null}");
        using var client = Client(handler);

        var act = () => client.GetVmsAsync("pve1");

        await act.Should().ThrowAsync<ProxmoxApiException>()
            .WithMessage("*returned no data*");
    }

    [Fact]
    public async Task GetVmsAsync_ObjectWhereArrayExpected_ThrowsApiException()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/qemu$",
            """
            {"data":{"vmid":100,"name":"web-vm"}}
            """);
        using var client = Client(handler);

        var act = () => client.GetVmsAsync("pve1");

        await act.Should().ThrowAsync<ProxmoxApiException>()
            .WithMessage("*does not match the expected shape*");
    }

    [Fact]
    public async Task GetNodeStatusAsync_NullData_ThrowsApiException()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes/pve1/status$", "{\"data\":null}");
        using var client = Client(handler);

        var act = () => client.GetNodeStatusAsync("pve1");

        await act.Should().ThrowAsync<ProxmoxApiException>()
            .WithMessage("*returned no data*");
    }

    [Fact]
    public async Task GetNodesAsync_HtmlErrorPage_ThrowsApiExceptionNotJsonException()
    {
        var handler = new TestHttpHandler().Add(
            @"^/api2/json/nodes$",
            "<!DOCTYPE html><html><head><title>502 Bad Gateway</title></head></html>",
            (int)HttpStatusCode.BadGateway);
        using var client = Client(handler);

        var act = () => client.GetNodesAsync();

        var apiException = (await act.Should().ThrowAsync<ProxmoxApiException>())
            .Which as ProxmoxApiException;
        apiException.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        apiException.Message.Should().Contain("502");
    }

    [Fact]
    public async Task GetNodesAsync_UnknownRoute_ThrowsApiException()
    {
        using var client = Client(new TestHttpHandler());

        var act = () => client.GetNodesAsync();

        await act.Should().ThrowAsync<ProxmoxApiException>()
            .WithMessage("*404*");
    }

    [Fact]
    public async Task GetClusterResourcesAsync_EmptyArray_ReturnsEmptyList()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/cluster/resources$", "{\"data\":[]}");
        using var client = Client(handler);

        var result = await client.GetClusterResourcesAsync();

        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNodesAsync_CancellationRequested_StopsRequest()
    {
        var handler = new TestHttpHandler().Add(@"^/api2/json/nodes$", "{\"data\":[]}");
        using var client = Client(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.GetNodesAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
