using System.Text.RegularExpressions;

namespace SafeProxMcp.Tools;

/// <summary>
/// Argument validators shared by the read and write tool classes.
/// </summary>
internal static class ToolArguments
{
    // Proxmox node names are cluster hostnames: letters, digits, '.', '_' or '-',
    // never starting with '.'. Slashes and leading dots are rejected so a crafted
    // node argument cannot escape the fixed "nodes/{node}/..." path template via
    // dot-segment traversal, even before Uri.EscapeDataString is applied.
    private static readonly Regex NodeNamePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,254}$", RegexOptions.Compiled);

    // Proxmox snapshot names (PVE::Snapshot::snapshot_name_regex): an alphanumeric
    // first character, then letters, digits, '_', '.' or '-', at most 64 characters.
    private static readonly Regex SnapshotNamePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,63}$", RegexOptions.Compiled);

    public static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        return value;
    }

    public static string RequireNode(string? node, string parameterName = "node")
    {
        var value = Require(node, parameterName);
        if (!NodeNamePattern.IsMatch(value))
            throw new ArgumentException(
                $"{parameterName} must be a node name: letters, digits, '.', '_' or '-', starting with a letter or digit.",
                parameterName);
        return value;
    }

    public static string RequireSnapshotName(string? name, string parameterName = "name")
    {
        var value = Require(name, parameterName);
        if (!SnapshotNamePattern.IsMatch(value))
            throw new ArgumentException(
                $"{parameterName} must start with a letter or digit and contain only letters, digits, '_', '.' or '-', at most 64 characters.",
                parameterName);
        return value;
    }

    public static int RequireVmId(int vmId)
    {
        if (vmId <= 0)
            throw new ArgumentException("vmId must be a positive Proxmox VMID.", nameof(vmId));
        return vmId;
    }

    public static string? NormalizeChoice(string? value, string[] allowed, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var match = Array.Find(allowed, a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new ArgumentException($"{parameterName} must be one of: {string.Join(", ", allowed)}.", parameterName);
        return match;
    }
}
