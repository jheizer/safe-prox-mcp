using System.ComponentModel;
using ModelContextProtocol.Server;
using SafeProxMcp.Services;
using static SafeProxMcp.Tools.ToolArguments;

namespace SafeProxMcp.Tools;

/// <summary>
/// The only tools that change cluster state. Program registers this class only
/// when SAFEPROX_DISABLE_WRITE_TOOLS is not set to 1/true/yes; when disabled the
/// tools are absent from tools/list and the server never sends a write request.
/// </summary>
[McpServerToolType]
public class ProxmoxWriteTools
{
    public const string DisableEnvironmentVariable = "SAFEPROX_DISABLE_WRITE_TOOLS";

    private static readonly string[] BackupModes = ["stop", "snapshot", "suspend"];
    private static readonly string[] BackupCompressions = ["zstd", "gzip", "lzo", "bzip2", "none"];

    private readonly IMultiClusterProxmoxClient _multi;

    public ProxmoxWriteTools(IMultiClusterProxmoxClient multi)
    {
        _multi = multi ?? throw new ArgumentNullException(nameof(multi));
    }

    /// <summary>False only when the variable is 1, true or yes (case-insensitive);
    /// unset, empty or any other value leaves the write tools enabled.</summary>
    public static bool EnabledFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(DisableEnvironmentVariable);
        return value is null
            || !(value.Equals("1", StringComparison.OrdinalIgnoreCase)
              || value.Equals("true", StringComparison.OrdinalIgnoreCase)
              || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private IProxmoxClient Client(string cluster) => _multi.GetClient(cluster);

    // -- Snapshots ------------------------------------------------------
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

    // -- Backups --------------------------------------------------------
    [McpServerTool, Description("Create a new backup (vzdump archive) of one VM or LXC container on a node. This changes cluster state: it writes a new backup file to the storage you name. Works for VMs and containers alike - vmId comes from lookup_all_vms_and_containers either way. storage is required and must be a storage that accepts backups (see get_storage). The backup runs asynchronously: the tool returns the Proxmox task id (UPID) as soon as the job is queued, not when the archive is finished.")]
    public async Task<object> CreateBackupAsync(
        [Description("Cluster id, exactly as returned by lookup_all_vms_and_containers.")] string cluster,
        [Description("Node name, exactly as returned by lookup_all_vms_and_containers.")] string node,
        [Description("VMID of the VM or container to back up, from lookup_all_vms_and_containers.")] int vmId,
        [Description("Backup storage to write the archive to, as listed by get_storage.")] string storage,
        [Description("Backup mode: 'stop' powers the guest off first, 'snapshot' backs it up live, 'suspend' pauses it during the backup. Omit for Proxmox's default (snapshot).")] string? mode = null,
        [Description("Archive compression: zstd, gzip, lzo, bzip2 or none. Omit for Proxmox's default.")] string? compress = null,
        [Description("Optional note stored with the backup archive.")] string? notes = null)
    {
        var result = await Client(cluster).CreateBackupAsync(
            Require(node, nameof(node)),
            RequireVmId(vmId),
            Require(storage, nameof(storage)),
            NormalizeChoice(mode, BackupModes, nameof(mode)),
            NormalizeChoice(compress, BackupCompressions, nameof(compress)),
            string.IsNullOrWhiteSpace(notes) ? null : notes);
        return new { task = result.Data };
    }
}
