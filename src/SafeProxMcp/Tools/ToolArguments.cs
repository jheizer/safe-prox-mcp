namespace SafeProxMcp.Tools;

/// <summary>
/// Argument validators shared by the read and write tool classes.
/// </summary>
internal static class ToolArguments
{
    public static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
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
