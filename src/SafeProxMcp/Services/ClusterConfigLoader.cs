using System.Text.Json;
using SafeProxMcp.Models;

namespace SafeProxMcp.Services;

/// <summary>
/// Loads the required clusters.json config file from the working directory.
/// </summary>
public static class ClusterConfigLoader
{
    public const string DefaultConfigFile = "clusters.json";

    /// <summary>Loads "clusters.json" from the working directory.</summary>
    public static List<ProxmoxClusterConfig> Load() => Load(DefaultConfigFile);

    internal static List<ProxmoxClusterConfig> Load(string configFilePath)
    {
        var path = configFilePath.Trim();

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Cluster config '{path}' was not found (the working directory is " +
                $"{Directory.GetCurrentDirectory()}). Copy clusters.example.json to {DefaultConfigFile} " +
                "and fill in your clusters.");
        }

        try
        {
            return Validate(Parse(File.ReadAllText(path)), $"Cluster config file '{path}'");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Cluster config file '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static List<ProxmoxClusterConfig> Parse(string json)
    {
        var configs = JsonSerializer.Deserialize<List<ProxmoxClusterConfig>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return configs ?? throw new InvalidOperationException(
            "Cluster configuration must be a JSON array of cluster objects.");
    }

    private static List<ProxmoxClusterConfig> Validate(List<ProxmoxClusterConfig> configs, string source)
    {
        if (configs.Count == 0)
            throw new InvalidOperationException($"{source} must contain at least one cluster config.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            if (string.IsNullOrWhiteSpace(config.Id))
                throw new InvalidOperationException($"{source}: each cluster must have an 'id'.");
            if (!seen.Add(config.Id))
                throw new InvalidOperationException($"{source}: duplicate cluster id '{config.Id}'.");
            if (string.IsNullOrWhiteSpace(config.Url))
                throw new InvalidOperationException($"{source}: cluster '{config.Id}' must have a 'url'.");
            if (string.IsNullOrWhiteSpace(config.Token))
                throw new InvalidOperationException($"{source}: cluster '{config.Id}' must have a 'token'.");
        }

        return configs;
    }
}
