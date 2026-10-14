using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeProxMcp.Models;

// Field names and types below mirror real Proxmox VE API responses
// (verified against a live PVE 9.2 cluster). Proxmox returns 0/1 for booleans
// and sometimes numbers as strings, hence the custom converters.

// -- Nodes -------------------------------------------------------------
public record ProxmoxNodesResponse(List<ProxmoxNodeInfo> Data);

public record ProxmoxNodeInfo(
    [property: JsonPropertyName("node")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("level")] string? Level,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("maxcpu")] int? MaxCpu,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("uptime")] long? Uptime
);

/// <summary>GET /nodes/{node}/status. Note: /status/current is not implemented by Proxmox.</summary>
public record ProxmoxNodeStatusResponse(
    [property: JsonPropertyName("pveversion")] string? PveVersion,
    [property: JsonPropertyName("kversion")] string? KernelVersion,
    [property: JsonPropertyName("uptime")] long? Uptime,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("wait")] double? Wait,
    [property: JsonPropertyName("idle")] double? Idle,
    [property: JsonPropertyName("memory")] ProxmoxMemoryUsage? Memory,
    [property: JsonPropertyName("swap")] ProxmoxSwapUsage? Swap,
    [property: JsonPropertyName("rootfs")] ProxmoxDiskUsage? RootFs,
    [property: JsonPropertyName("cpuinfo")] ProxmoxCpuInfo? CpuInfo,
    [property: JsonPropertyName("loadavg")] JsonElement? LoadAvg
);

public record ProxmoxMemoryUsage(
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("used")] long Used,
    [property: JsonPropertyName("free")] long Free,
    [property: JsonPropertyName("available")] long? Available
);

public record ProxmoxSwapUsage(
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("used")] long Used,
    [property: JsonPropertyName("free")] long? Free
);

public record ProxmoxDiskUsage(
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("used")] long Used,
    [property: JsonPropertyName("free")] long? Free,
    [property: JsonPropertyName("avail")] long? Avail
);

public record ProxmoxCpuInfo(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("cpus")] int? Cpus,
    [property: JsonPropertyName("sockets")] int? Sockets,
    [property: JsonPropertyName("cores")] int? Cores,
    [property: JsonPropertyName("mhz")] string? Mhz
);

// -- Cluster -----------------------------------------------------------
public record ProxmoxClusterStatusResponse(List<ProxmoxClusterNode> Data);

public record ProxmoxClusterNode(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("level")] string? Level,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("online")] int? Online,
    [property: JsonPropertyName("local")] int? Local,
    [property: JsonPropertyName("ip")] string? Ip,
    [property: JsonPropertyName("nodeid")] int? NodeId
);

public record ProxmoxResourcesResponse(List<ProxmoxResource> Data);

public record ProxmoxResource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("node")] string? Node,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("storage")] string? Storage,
    [property: JsonPropertyName("plugintype")] string? PluginType,
    [property: JsonPropertyName("vmid")] int? VmId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("uptime")] long? Uptime,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("cpus")] int? Cpus,
    [property: JsonPropertyName("maxcpu")] int? MaxCpu,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("netin")] long? NetIn,
    [property: JsonPropertyName("netout")] long? NetOut,
    [property: JsonPropertyName("diskread")] long? DiskRead,
    [property: JsonPropertyName("diskwrite")] long? DiskWrite,
    [property: JsonPropertyName("template")] int? Template,
    [property: JsonPropertyName("lock")] string? Lock,
    [property: JsonPropertyName("maxlock")] string? MaxLock,
    [property: JsonPropertyName("poolname")] string? PoolName,
    [property: JsonPropertyName("tags")] string? Tags
);

// -- Virtual machines --------------------------------------------------
public record ProxmoxVmListResponse(List<ProxmoxVmInfo> Data);

public record ProxmoxVmInfo(
    [property: JsonPropertyName("vmid")] int VmId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("uptime")] long? Uptime,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("cpus")] int? Cpus,
    [property: JsonPropertyName("maxcpu")] int? MaxCpu,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("netin")] long? NetIn,
    [property: JsonPropertyName("netout")] long? NetOut,
    [property: JsonPropertyName("diskread")] long? DiskRead,
    [property: JsonPropertyName("diskwrite")] long? DiskWrite,
    [property: JsonPropertyName("template")] int? Template,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("lock")] string? Lock,
    [property: JsonPropertyName("id")] string? Id
);

/// <summary>
/// Shared shape of GET /nodes/{node}/qemu/{vmid}/status/current and
/// GET /nodes/{node}/lxc/{vmid}/status/current.
/// </summary>
public record ProxmoxVmStatusResponse(
    [property: JsonPropertyName("vmid")] int VmId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("qmpstatus")] string? QmpStatus,
    [property: JsonPropertyName("uptime")] long? Uptime,
    [property: JsonPropertyName("pid")] int? Pid,
    [property: JsonPropertyName("cpus")] int? Cpus,
    [property: JsonPropertyName("maxcpu")] int? MaxCpu,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("swap")] long? Swap,
    [property: JsonPropertyName("maxswap")] long? MaxSwap,
    [property: JsonPropertyName("balloon_min")] long? BalloonMin,
    [property: JsonPropertyName("shares")] int? Shares,
    [property: JsonPropertyName("netin")] long? NetIn,
    [property: JsonPropertyName("netout")] long? NetOut,
    [property: JsonPropertyName("diskread")] long? DiskRead,
    [property: JsonPropertyName("diskwrite")] long? DiskWrite,
    [property: JsonPropertyName("template")] int? Template,
    [property: JsonPropertyName("lock")] string? Lock,
    [property: JsonPropertyName("digest")] string? Digest
);

public record ProxmoxVmConfigResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("ostype")] string? OsType,
    [property: JsonPropertyName("memory")]
    [property: JsonConverter(typeof(PveInt64Converter))] long? Memory,
    [property: JsonPropertyName("cores")] int? Cores,
    [property: JsonPropertyName("sockets")] int? Sockets,
    [property: JsonPropertyName("cpu")] string? CpuType,
    [property: JsonPropertyName("cpulimit")] double? CpuLimit,
    [property: JsonPropertyName("cpuunits")] int? CpuUnits,
    [property: JsonPropertyName("vcpus")] int? Vcpus,
    [property: JsonPropertyName("balloon")] long? Balloon,
    [property: JsonPropertyName("numa")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? NumaEnabled,
    [property: JsonPropertyName("onboot")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? OnBoot,
    [property: JsonPropertyName("startup")] string? Startup,
    [property: JsonPropertyName("boot")] string? BootOrder,
    [property: JsonPropertyName("bootdisk")] string? BootDisk,
    [property: JsonPropertyName("kvm")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Kvm,
    [property: JsonPropertyName("bios")] string? Bios,
    [property: JsonPropertyName("machine")] string? Machine,
    [property: JsonPropertyName("tablet")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Tablet,
    [property: JsonPropertyName("serial0")] string? SerialPort,
    [property: JsonPropertyName("agent")] JsonElement? Agent,
    [property: JsonPropertyName("autostart")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? AutoStart,
    [property: JsonPropertyName("protect")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Protect,
    [property: JsonPropertyName("template")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Template,
    [property: JsonPropertyName("reboot")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Reboot,
    [property: JsonPropertyName("localtime")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? LocalTime,
    [property: JsonPropertyName("migrate_downtime")] double? MigrateDowntime,
    [property: JsonPropertyName("migrate_speed")] long? MigrateSpeed,
    [property: JsonPropertyName("smbios1")] string? SmBios1,
    [property: JsonPropertyName("efidisk0")] string? EfiDisk0,
    [property: JsonPropertyName("pflash0")] string? PFlash0,
    [property: JsonPropertyName("tpmstate0")] string? TpmState0,
    [property: JsonPropertyName("rng0")] string? Rng0,
    [property: JsonPropertyName("watchdog0")] string? Watchdog0,
    [property: JsonPropertyName("scsihw")] string? ScsiHw,
    [property: JsonPropertyName("hotplug")] string? Hotplug,
    [property: JsonPropertyName("hugepages")] string? HugePages,
    [property: JsonPropertyName("shares")] int? Shares,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("digest")] string? Digest,
    [property: JsonPropertyName("vmgenid")] string? VmGenId,
    [property: JsonPropertyName("meta")] string? Meta
)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// -- Containers --------------------------------------------------------
public record ProxmoxLxcListResponse(List<ProxmoxLxcInfo> Data);

public record ProxmoxLxcInfo(
    [property: JsonPropertyName("vmid")] int VmId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("uptime")] long? Uptime,
    [property: JsonPropertyName("pid")] int? Pid,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("cpu")] double? CpuUsage,
    [property: JsonPropertyName("cpus")] int? Cpus,
    [property: JsonPropertyName("maxcpu")] int? MaxCpu,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("swap")] long? Swap,
    [property: JsonPropertyName("maxswap")] long? MaxSwap,
    [property: JsonPropertyName("netin")] long? NetIn,
    [property: JsonPropertyName("netout")] long? NetOut,
    [property: JsonPropertyName("diskread")] long? DiskRead,
    [property: JsonPropertyName("diskwrite")] long? DiskWrite,
    [property: JsonPropertyName("template")] int? Template,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("lock")] string? Lock,
    [property: JsonPropertyName("id")] string? Id
);

public record ProxmoxLxcConfigResponse(
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("arch")] string? Arch,
    [property: JsonPropertyName("ostype")] string? OsType,
    [property: JsonPropertyName("memory")]
    [property: JsonConverter(typeof(PveInt64Converter))] long? Memory,
    [property: JsonPropertyName("swap")]
    [property: JsonConverter(typeof(PveInt64Converter))] long? Swap,
    [property: JsonPropertyName("cores")] int? Cores,
    [property: JsonPropertyName("cpulimit")] double? CpuLimit,
    [property: JsonPropertyName("cpuunits")] int? CpuUnits,
    [property: JsonPropertyName("cpuset")] string? CpuSet,
    [property: JsonPropertyName("rootfs")] string? RootFs,
    [property: JsonPropertyName("onboot")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? OnBoot,
    [property: JsonPropertyName("startup")] string? Startup,
    [property: JsonPropertyName("startdate")] string? StartDate,
    [property: JsonPropertyName("startdelay")] int? StartDelay,
    [property: JsonPropertyName("stopdelay")] int? StopDelay,
    [property: JsonPropertyName("unprivileged")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Unprivileged,
    [property: JsonPropertyName("features")] string? Features,
    [property: JsonPropertyName("tty")] int? Tty,
    [property: JsonPropertyName("migrate_downtime")] double? MigrateDowntime,
    [property: JsonPropertyName("bwlimit")] long? BandwidthLimit,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("template")] string? Template,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("protection")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Protection,
    [property: JsonPropertyName("searchdomain")] string? SearchDomain,
    [property: JsonPropertyName("nameserver")] string? NameServer,
    [property: JsonPropertyName("digest")] string? Digest
)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// -- Storage -----------------------------------------------------------
public record ProxmoxStorageListResponse(List<ProxmoxStorageInfo> Data);

/// <summary>
/// Union of GET /storage (cluster storage config) and GET /nodes/{node}/storage
/// (per-node usage). Usage fields only appear on the per-node endpoint.
/// </summary>
public record ProxmoxStorageInfo(
    [property: JsonPropertyName("storage")] string Storage,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("nodes")] string? Nodes,
    [property: JsonPropertyName("pool")] string? Pool,
    [property: JsonPropertyName("digest")] string? Digest,
    [property: JsonPropertyName("enabled")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Enabled,
    [property: JsonPropertyName("active")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Active,
    [property: JsonPropertyName("shared")] int? Shared,
    [property: JsonPropertyName("total")] long? Total,
    [property: JsonPropertyName("used")] long? Used,
    [property: JsonPropertyName("avail")] long? Avail,
    [property: JsonPropertyName("used_fraction")] double? UsedFraction,
    [property: JsonPropertyName("sparse")] int? Sparse,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("mountpoint")] string? MountPoint,
    [property: JsonPropertyName("server")] string? Server,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("vgname")] string? VgName,
    [property: JsonPropertyName("thinpool")] string? ThinPool,
    [property: JsonPropertyName("datastore")] string? DataStore,
    [property: JsonPropertyName("prune-backups")] string? PruneBackups,
    [property: JsonPropertyName("fingerprint")] string? Fingerprint
);

// -- Network -----------------------------------------------------------
public record ProxmoxNetworkListResponse(List<ProxmoxNetworkInterface> Data);

/// <summary>
/// GET /nodes/{node}/network. Names mirror what Proxmox actually returns: an
/// interface's address is "address"/"netmask" (or the compact "cidr"), bridge
/// options are "bridge_ports"/"bridge_fd"/"bridge_stp"/"bridge_vlan_aware", and
/// bridge_fd/bridge_stp arrive as strings ("0", "off").
/// </summary>
public record ProxmoxNetworkInterface(
    [property: JsonPropertyName("iface")] string Iface,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("method6")] string? Method6,
    [property: JsonPropertyName("active")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Active,
    [property: JsonPropertyName("exists")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Exists,
    [property: JsonPropertyName("autostart")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? AutoStart,
    [property: JsonPropertyName("priority")] int? Priority,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("netmask")] string? Netmask,
    [property: JsonPropertyName("cidr")] string? Cidr,
    [property: JsonPropertyName("gateway")] string? Gateway,
    [property: JsonPropertyName("address6")] string? Address6,
    [property: JsonPropertyName("netmask6")] string? Netmask6,
    [property: JsonPropertyName("cidr6")] string? Cidr6,
    [property: JsonPropertyName("gateway6")] string? Gateway6,
    [property: JsonPropertyName("bridge_ports")] string? BridgePorts,
    [property: JsonPropertyName("bridge_fd")] string? BridgeFd,
    [property: JsonPropertyName("bridge_stp")] string? BridgeStp,
    [property: JsonPropertyName("bridge_tag")] string? BridgeTag,
    [property: JsonPropertyName("bridge_access")] string? BridgeAccess,
    [property: JsonPropertyName("bridge_vlan_aware")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? VlanAware,
    [property: JsonPropertyName("bond_slaves")] string? BondSlaves,
    [property: JsonPropertyName("bond_mode")] string? BondMode,
    [property: JsonPropertyName("bond_primary")] string? BondPrimary,
    [property: JsonPropertyName("families")] List<string>? Families,
    [property: JsonPropertyName("altnames")] List<string>? AltNames
);

// -- Users and groups --------------------------------------------------
public record ProxmoxUserListResponse(List<ProxmoxUserInfo> Data);

public record ProxmoxUserInfo(
    [property: JsonPropertyName("userid")] string UserId,
    [property: JsonPropertyName("enable")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Enable,
    [property: JsonPropertyName("expire")] long? Expire,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("firstname")] string? FirstName,
    [property: JsonPropertyName("lastname")] string? LastName,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("groups")] string? Groups,
    [property: JsonPropertyName("realm-type")] string? RealmType
);

public record ProxmoxGroupListResponse(List<ProxmoxGroupInfo> Data);

public record ProxmoxGroupInfo(
    [property: JsonPropertyName("groupid")] string GroupId,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("users")] string? Users,
    [property: JsonPropertyName("expire")] long? Expire,
    [property: JsonPropertyName("realm-type")] string? RealmType
);

// -- Disks -------------------------------------------------------------
public record ProxmoxDiskListResponse(List<ProxmoxDiskInfo> Data);

/// <summary>
/// GET /nodes/{node}/disks/list. "used" is a usage label ("ZFS", "swap",
/// "BIOS boot"), not a byte count; rpm/wearout arrive as numbers on some disk
/// types and as strings ("7200", "N/A") on others.
/// </summary>
public record ProxmoxDiskInfo(
    [property: JsonPropertyName("devname")] string? DevName,
    [property: JsonPropertyName("devpath")] string? DevPath,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("size")]
    [property: JsonConverter(typeof(PveInt64Converter))] long? Size,
    [property: JsonPropertyName("used")] string? Used,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("vendor")] string? Vendor,
    [property: JsonPropertyName("serial")] string? Serial,
    [property: JsonPropertyName("wwn")] string? Wwn,
    [property: JsonPropertyName("health")] string? Health,
    [property: JsonPropertyName("rpm")]
    [property: JsonConverter(typeof(PveInt32Converter))] int? Rpm,
    [property: JsonPropertyName("wearout")]
    [property: JsonConverter(typeof(PveInt32Converter))] int? Wearout,
    [property: JsonPropertyName("rotational")]
    [property: JsonConverter(typeof(PveInt32Converter))] int? Rotational,
    [property: JsonPropertyName("sectors")]
    [property: JsonConverter(typeof(PveInt64Converter))] long? Sectors,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("smart")]
    [property: JsonConverter(typeof(PveBooleanConverter))] bool? Smart,
    [property: JsonPropertyName("plugin")] string? Plugin,
    [property: JsonPropertyName("filesystem")] string? FileSystem,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("scheduler")] string? Scheduler,
    [property: JsonPropertyName("gpt")] int? Gpt,
    [property: JsonPropertyName("osdid")] int? OsDid,
    [property: JsonPropertyName("by_id_link")] string? ByIdLink
);

// -- Version -----------------------------------------------------------
public record ProxmoxVersionResponse(ProxmoxVersionInfo Data);

public record ProxmoxVersionInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("release")] string? Release,
    [property: JsonPropertyName("repoid")] string? RepoId
);

// -- Syslog -----------------------------------------------------------
public record ProxmoxNodeSyslogResponse(
    List<ProxmoxSyslogEntry> Data,
    [property: JsonPropertyName("total")] long? Total
);

public record ProxmoxSyslogEntry(
    [property: JsonPropertyName("n")] long? Number,
    [property: JsonPropertyName("t")] string? Text
);

// -- Snapshots ---------------------------------------------------------
public record ProxmoxSnapshotListResponse(List<ProxmoxSnapshotInfo> Data);

public record ProxmoxSnapshotInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("timestamp")] long? Timestamp,
    [property: JsonPropertyName("snaptime")] long? SnapTime,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("running")] int? Running,
    [property: JsonPropertyName("vmstate")] int? VmState,
    [property: JsonPropertyName("online")] int? Online,
    [property: JsonPropertyName("digest")] string? Digest
);

public record ProxmoxSnapshotOperationResponse(string? Data);

// -- Configuration ----------------------------------------------------
public class ProxmoxClusterConfig
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public record ProxmoxClusterInfo(string Id, string Url);

// -- Inventory ---------------------------------------------------------
// Aggregate built by the multi-cluster client; not a Proxmox payload.
public record ProxmoxInventoryEntry(
    string Cluster,
    string Node,
    int VmId,
    string Name,
    string Type,
    string Status);

// Carries the partial map plus whatever could not be read, so a restricted token
// or an unreachable node still leaves the caller with the usable part.
public record ProxmoxInventoryResult(
    List<ProxmoxInventoryEntry> Entries,
    List<string> Errors);
