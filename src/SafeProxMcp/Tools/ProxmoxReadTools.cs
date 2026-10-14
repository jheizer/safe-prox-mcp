using System.ComponentModel;
using ModelContextProtocol.Server;
using SafeProxMcp.Services;

namespace SafeProxMcp.Tools;

[McpServerToolType]
public class ProxmoxReadTools
{
    private const int MaxSyslogLines = 1000;
    private const int DefaultSyslogLines = 200;

    private readonly IMultiClusterProxmoxClient _multi;

    public ProxmoxReadTools(IMultiClusterProxmoxClient multi)
    {
        _multi = multi ?? throw new ArgumentNullException(nameof(multi));
    }

    // -- Identifier lookup ----------------------------------------------
    [McpServerTool, Description("Cross-reference lookup for the whole environment: every virtual machine and every LXC container on every node of every configured cluster, as {cluster, node, vmid, name, type, status}. USE THIS FIRST whenever a cluster id, node name or VMID is unknown, or when only a machine name is known - it is the map from names to the exact identifiers every other tool needs. type 'vm' means use the VM (qemu) tools, type 'container' means use the LXC tools. Takes no arguments and covers every cluster in one call; anything that could not be read is reported in 'errors' instead of silently missing.")]
    public async Task<object> LookupAllVmsAndContainersAsync()
    {
        var inventory = await _multi.GetInventoryAsync();

        return new
        {
            machines = inventory.Entries.Select(e => new
            {
                cluster = e.Cluster,
                node = e.Node,
                vmid = e.VmId,
                name = e.Name,
                type = e.Type,
                status = e.Status
            }),
            errors = inventory.Errors
        };
    }

    // -- Clusters and nodes --------------------------------------------
    [McpServerTool, Description("List the Proxmox clusters this server is configured to talk to.")]
    public Task<object> GetClustersAsync()
    {
        var clusters = _multi.GetClusterInfo();
        return Task.FromResult<object>(clusters.Select(c => new
        {
            id = c.Id,
            url = c.Url
        }));
    }

    [McpServerTool, Description("List all nodes in a Proxmox cluster with their status and capacity.")]
    public async Task<object> GetNodesAsync(
        [Description("Cluster id, as returned by get_clusters or lookup_all_vms_and_containers.")] string cluster)
    {
        var response = await Client(cluster).GetNodesAsync();
        return response.Data.Select(n => new
        {
            name = n.Name,
            type = n.Type,
            status = n.Status,
            level = n.Level,
            maxcpu = n.MaxCpu,
            maxmem = n.MaxMem,
            maxdisk = n.MaxDisk,
            cpuUsage = n.CpuUsage,
            mem = n.Mem,
            disk = n.Disk,
            uptimeSeconds = n.Uptime
        });
    }

    [McpServerTool, Description("Get detailed status of a node: CPU usage, memory, swap, root filesystem and versions.")]
    public async Task<object> GetNodeStatusAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var status = await Client(cluster).GetNodeStatusAsync(Require(node, nameof(node)));
        return new
        {
            pveVersion = status.PveVersion,
            kernelVersion = status.KernelVersion,
            uptimeSeconds = status.Uptime,
            cpuUsage = status.CpuUsage,
            cpu = status.CpuInfo == null ? null : new
            {
                model = status.CpuInfo.Model,
                cpus = status.CpuInfo.Cpus,
                sockets = status.CpuInfo.Sockets,
                cores = status.CpuInfo.Cores,
                mhz = status.CpuInfo.Mhz
            },
            memory = status.Memory == null ? null : new
            {
                total = status.Memory.Total,
                used = status.Memory.Used,
                free = status.Memory.Free,
                available = status.Memory.Available
            },
            swap = status.Swap == null ? null : new
            {
                total = status.Swap.Total,
                used = status.Swap.Used,
                free = status.Swap.Free
            },
            rootFs = status.RootFs == null ? null : new
            {
                total = status.RootFs.Total,
                used = status.RootFs.Used,
                avail = status.RootFs.Avail
            },
            loadAvg = status.LoadAvg
        };
    }

    // -- Cluster level --------------------------------------------------
    [McpServerTool, Description("Get cluster status: member nodes and whether each is online.")]
    public async Task<object> GetClusterStatusAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster)
    {
        var response = await Client(cluster).GetClusterStatusAsync();
        return response.Data.Select(c => new
        {
            name = c.Name,
            type = c.Type,
            id = c.Id,
            level = c.Level,
            status = c.Status,
            online = c.Online,
            ip = c.Ip,
            nodeid = c.NodeId
        });
    }

    [McpServerTool, Description("Get cluster resources (VMs, containers, storage, nodes) with usage. Optional type filter: vm, storage, node, pool or all.")]
    public async Task<object> GetClusterResourcesAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Optional resource type filter: vm, storage, node, pool or all.")] string? type = null)
    {
        var response = await Client(cluster).GetClusterResourcesAsync(type);
        return response.Data.Select(r => new
        {
            type = r.Type,
            id = r.Id,
            node = r.Node,
            name = r.Name,
            storage = r.Storage,
            plugintype = r.PluginType,
            vmid = r.VmId,
            status = r.Status,
            uptimeSeconds = r.Uptime,
            cpuUsage = r.CpuUsage,
            cpus = r.Cpus,
            maxcpu = r.MaxCpu,
            mem = r.Mem,
            maxmem = r.MaxMem,
            disk = r.Disk,
            maxdisk = r.MaxDisk,
            netin = r.NetIn,
            netout = r.NetOut,
            diskread = r.DiskRead,
            diskwrite = r.DiskWrite,
            template = r.Template,
            @lock = r.Lock,
            poolname = r.PoolName,
            tags = r.Tags
        });
    }

    // -- Virtual machines -----------------------------------------------
    [McpServerTool, Description("List virtual machines (QEMU/KVM) on a node with status and resource usage.")]
    public async Task<object> GetVmsAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var response = await Client(cluster).GetVmsAsync(Require(node, nameof(node)));
        return response.Data.Select(vm => new
        {
            vmid = vm.VmId,
            name = vm.Name,
            status = vm.Status,
            uptimeSeconds = vm.Uptime,
            cpuUsage = vm.CpuUsage,
            cpus = vm.Cpus,
            maxcpu = vm.MaxCpu,
            mem = vm.Mem,
            maxmem = vm.MaxMem,
            disk = vm.Disk,
            maxdisk = vm.MaxDisk,
            netin = vm.NetIn,
            netout = vm.NetOut,
            diskread = vm.DiskRead,
            diskwrite = vm.DiskWrite,
            template = vm.Template,
            tags = vm.Tags,
            @lock = vm.Lock
        });
    }

    [McpServerTool, Description("Get live status of a virtual machine (CPU, memory, disk, network counters).")]
    public async Task<object> GetVmStatusAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric VM id) of a type 'vm' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var status = await Client(cluster).GetVmStatusAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return ProjectStatus(status);
    }

    [McpServerTool, Description("Get the stored configuration of a virtual machine, including its device lines (net0, scsi0, ...).")]
    public async Task<object> GetVmConfigAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric VM id) of a type 'vm' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var config = await Client(cluster).GetVmConfigAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return new
        {
            name = config.Name,
            description = config.Description,
            ostype = config.OsType,
            memoryMB = config.Memory,
            cores = config.Cores,
            sockets = config.Sockets,
            cpuType = config.CpuType,
            cpulimit = config.CpuLimit,
            cpuunits = config.CpuUnits,
            vcpus = config.Vcpus,
            balloon = config.Balloon,
            numa = config.NumaEnabled,
            onboot = config.OnBoot,
            startup = config.Startup,
            bootOrder = config.BootOrder,
            bootdisk = config.BootDisk,
            kvm = config.Kvm,
            bios = config.Bios,
            machine = config.Machine,
            tablet = config.Tablet,
            serial0 = config.SerialPort,
            agent = config.Agent,
            autostart = config.AutoStart,
            protect = config.Protect,
            template = config.Template,
            reboot = config.Reboot,
            localtime = config.LocalTime,
            smbios1 = config.SmBios1,
            efidisk0 = config.EfiDisk0,
            pflash0 = config.PFlash0,
            tpmstate0 = config.TpmState0,
            rng0 = config.Rng0,
            watchdog0 = config.Watchdog0,
            scsihw = config.ScsiHw,
            hotplug = config.Hotplug,
            hugepages = config.HugePages,
            shares = config.Shares,
            tags = config.Tags,
            vmgenid = config.VmGenId,
            devices = config.ExtensionData
        };
    }

    // -- Containers -----------------------------------------------------
    [McpServerTool, Description("List LXC containers on a node with status and resource usage.")]
    public async Task<object> GetContainersAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var response = await Client(cluster).GetContainersAsync(Require(node, nameof(node)));
        return response.Data.Select(c => new
        {
            vmid = c.VmId,
            name = c.Name,
            status = c.Status,
            uptimeSeconds = c.Uptime,
            pid = c.Pid,
            cpuUsage = c.CpuUsage,
            cpus = c.Cpus,
            maxcpu = c.MaxCpu,
            mem = c.Mem,
            maxmem = c.MaxMem,
            disk = c.Disk,
            maxdisk = c.MaxDisk,
            swap = c.Swap,
            maxswap = c.MaxSwap,
            netin = c.NetIn,
            netout = c.NetOut,
            diskread = c.DiskRead,
            diskwrite = c.DiskWrite,
            template = c.Template,
            tags = c.Tags,
            @lock = c.Lock
        });
    }

    [McpServerTool, Description("Get live status of an LXC container (CPU, memory, disk, network counters).")]
    public async Task<object> GetContainerStatusAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric container id) of a type 'container' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var status = await Client(cluster).GetContainerStatusAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return ProjectStatus(status);
    }

    [McpServerTool, Description("Get the stored configuration of an LXC container, including its device lines (net0, rootfs, mp0, ...).")]
    public async Task<object> GetContainerConfigAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric container id) of a type 'container' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var config = await Client(cluster).GetContainerConfigAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return new
        {
            hostname = config.Hostname,
            description = config.Description,
            arch = config.Arch,
            ostype = config.OsType,
            memoryMB = config.Memory,
            swap = config.Swap,
            cores = config.Cores,
            cpulimit = config.CpuLimit,
            cpuunits = config.CpuUnits,
            cpuset = config.CpuSet,
            rootfs = config.RootFs,
            onboot = config.OnBoot,
            startup = config.Startup,
            startdate = config.StartDate,
            startdelay = config.StartDelay,
            stopdelay = config.StopDelay,
            unprivileged = config.Unprivileged,
            features = config.Features,
            tty = config.Tty,
            bwlimit = config.BandwidthLimit,
            tags = config.Tags,
            template = config.Template,
            parent = config.Parent,
            protection = config.Protection,
            searchdomain = config.SearchDomain,
            nameserver = config.NameServer,
            devices = config.ExtensionData
        };
    }

    // -- Infrastructure -------------------------------------------------
    [McpServerTool, Description("List storage configurations. Without a node this returns cluster storage config; with a node it also returns that node's usage numbers (total/used/avail).")]
    public async Task<object> GetStorageAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Optional node name; when given, per-node usage numbers are included.")] string? node = null)
    {
        var response = await Client(cluster).GetStorageAsync(node);
        return response.Data.Select(s => new
        {
            storage = s.Storage,
            type = s.Type,
            content = s.Content,
            nodes = s.Nodes,
            pool = s.Pool,
            enabled = s.Enabled,
            active = s.Active,
            shared = s.Shared,
            total = s.Total,
            used = s.Used,
            avail = s.Avail,
            usedFraction = s.UsedFraction,
            sparse = s.Sparse,
            path = s.Path,
            mountPoint = s.MountPoint,
            server = s.Server,
            username = s.Username,
            vgname = s.VgName,
            thinpool = s.ThinPool,
            datastore = s.DataStore,
            pruneBackups = s.PruneBackups,
            fingerprint = s.Fingerprint
        });
    }

    [McpServerTool, Description("List network interfaces configured on a node.")]
    public async Task<object> GetNetworksAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var response = await Client(cluster).GetNetworksAsync(Require(node, nameof(node)));
        return response.Data.Select(n => new
        {
            iface = n.Iface,
            type = n.Type,
            method = n.Method,
            method6 = n.Method6,
            active = n.Active,
            exists = n.Exists,
            autostart = n.AutoStart,
            vlanAware = n.VlanAware,
            address = n.Address,
            netmask = n.Netmask,
            cidr = n.Cidr,
            gateway = n.Gateway,
            address6 = n.Address6,
            netmask6 = n.Netmask6,
            cidr6 = n.Cidr6,
            gateway6 = n.Gateway6,
            bridgePorts = n.BridgePorts,
            bridgeFd = n.BridgeFd,
            bridgeStp = n.BridgeStp,
            bridgeTag = n.BridgeTag,
            bridgeAccess = n.BridgeAccess,
            bondSlaves = n.BondSlaves,
            bondMode = n.BondMode,
            bondPrimary = n.BondPrimary,
            priority = n.Priority,
            families = n.Families,
            altNames = n.AltNames
        });
    }

    [McpServerTool, Description("List users known to a cluster.")]
    public async Task<object> GetUsersAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster)
    {
        var response = await Client(cluster).GetUsersAsync();
        return response.Data.Select(u => new
        {
            userid = u.UserId,
            enable = u.Enable,
            expire = u.Expire,
            email = u.Email,
            firstname = u.FirstName,
            lastname = u.LastName,
            comment = u.Comment,
            groups = u.Groups,
            realmType = u.RealmType
        });
    }

    [McpServerTool, Description("List user groups of a cluster.")]
    public async Task<object> GetGroupsAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster)
    {
        var response = await Client(cluster).GetGroupsAsync();
        return response.Data.Select(g => new
        {
            groupid = g.GroupId,
            comment = g.Comment,
            users = g.Users,
            expire = g.Expire,
            realmType = g.RealmType
        });
    }

    [McpServerTool, Description("List local disks of a node with size, model and health information.")]
    public async Task<object> GetDisksAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var response = await Client(cluster).GetDisksAsync(Require(node, nameof(node)));
        return response.Data.Select(d => new
        {
            devname = d.DevName,
            devpath = d.DevPath,
            type = d.Type,
            size = d.Size,
            used = d.Used,
            model = d.Model,
            vendor = d.Vendor,
            serial = d.Serial,
            wwn = d.Wwn,
            health = d.Health,
            rpm = d.Rpm,
            wearout = d.Wearout,
            rotational = d.Rotational,
            scheduler = d.Scheduler,
            sectors = d.Sectors,
            status = d.Status,
            smart = d.Smart,
            filesystem = d.FileSystem,
            label = d.Label,
            plugin = d.Plugin,
            gpt = d.Gpt,
            byIdLink = d.ByIdLink
        });
    }

    [McpServerTool, Description("Get the Proxmox VE version of a node.")]
    public async Task<object> GetVersionAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node)
    {
        var response = await Client(cluster).GetVersionAsync(Require(node, nameof(node)));
        return new
        {
            version = response.Data.Version,
            release = response.Data.Release,
            repoid = response.Data.RepoId
        };
    }

    [McpServerTool, Description("Read a page of a node's system log. Returns the log lines plus the total number of available lines.")]
    public async Task<object> GetNodeSyslogAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("First line to return (0-based). Default 0.")] long start = 0,
        [Description("Number of lines to return, 1-1000. Default 200.")] long limit = DefaultSyslogLines)
    {
        if (start < 0)
            throw new ArgumentException("start must be 0 or greater.", nameof(start));
        if (limit < 1 || limit > MaxSyslogLines)
            throw new ArgumentException($"limit must be between 1 and {MaxSyslogLines}.", nameof(limit));

        var response = await Client(cluster).GetNodeSyslogAsync(Require(node, nameof(node)), start, limit);
        return new
        {
            start,
            limit,
            total = response.Total,
            lines = response.Data.Select(e => new { number = e.Number, text = e.Text })
        };
    }

    // -- Snapshots ------------------------------------------------------
    [McpServerTool, Description("List snapshots of a virtual machine.")]
    public async Task<object> GetVmSnapshotsAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric VM id) of a type 'vm' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var response = await Client(cluster).GetVmSnapshotsAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return ProjectSnapshots(response.Data);
    }

    [McpServerTool, Description("List snapshots of an LXC container.")]
    public async Task<object> GetContainerSnapshotsAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric container id) of a type 'container' entry from lookup_all_vms_and_containers.")] int vmId)
    {
        var response = await Client(cluster).GetContainerSnapshotsAsync(Require(node, nameof(node)), RequireVmId(vmId));
        return ProjectSnapshots(response.Data);
    }

    [McpServerTool, Description("Create a snapshot of a virtual machine. This changes cluster state; the name must satisfy Proxmox's snapshot naming rules.")]
    public async Task<object> CreateVmSnapshotAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric VM id) of a type 'vm' entry from lookup_all_vms_and_containers.")] int vmId,
        [Description("Snapshot name.")] string name,
        [Description("Optional snapshot description.")] string? description = null)
    {
        var result = await Client(cluster).CreateVmSnapshotAsync(
            Require(node, nameof(node)), RequireVmId(vmId), Require(name, nameof(name)), description);
        return new { task = result.Data };
    }

    [McpServerTool, Description("Create a snapshot of an LXC container. This changes cluster state; the name must satisfy Proxmox's snapshot naming rules.")]
    public async Task<object> CreateContainerSnapshotAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID (numeric container id) of a type 'container' entry from lookup_all_vms_and_containers.")] int vmId,
        [Description("Snapshot name.")] string name,
        [Description("Optional snapshot description.")] string? description = null)
    {
        var result = await Client(cluster).CreateContainerSnapshotAsync(
            Require(node, nameof(node)), RequireVmId(vmId), Require(name, nameof(name)), description);
        return new { task = result.Data };
    }

    // -- Helpers --------------------------------------------------------
    private IProxmoxClient Client(string cluster) => _multi.GetClient(cluster);

    private static object ProjectStatus(Models.ProxmoxVmStatusResponse status) => new
    {
        vmid = status.VmId,
        name = status.Name,
        type = status.Type,
        status = status.Status,
        qmpstatus = status.QmpStatus,
        uptimeSeconds = status.Uptime,
        pid = status.Pid,
        cpus = status.Cpus,
        maxcpu = status.MaxCpu,
        cpuUsage = status.CpuUsage,
        mem = status.Mem,
        maxmem = status.MaxMem,
        disk = status.Disk,
        maxdisk = status.MaxDisk,
        swap = status.Swap,
        maxswap = status.MaxSwap,
        netin = status.NetIn,
        netout = status.NetOut,
        diskread = status.DiskRead,
        diskwrite = status.DiskWrite,
        template = status.Template,
        @lock = status.Lock
    };

    private static object ProjectSnapshots(List<Models.ProxmoxSnapshotInfo> snapshots)
        => snapshots.Select(s => new
        {
            name = s.Name,
            description = s.Description,
            timestamp = s.Timestamp ?? s.SnapTime,
            parent = s.Parent,
            running = s.Running,
            vmstate = s.VmState,
            online = s.Online
        });

    private static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        return value;
    }

    private static int RequireVmId(int vmId)
    {
        if (vmId <= 0)
            throw new ArgumentException("vmId must be a positive Proxmox VMID.", nameof(vmId));
        return vmId;
    }
}
