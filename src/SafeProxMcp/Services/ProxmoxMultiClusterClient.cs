using SafeProxMcp.Models;

namespace SafeProxMcp.Services;

/// <summary>
/// Owns one API client per configured cluster.
/// </summary>
public interface IMultiClusterProxmoxClient : IDisposable
{
    /// <summary>Configured clusters (id + url, never the token).</summary>
    IReadOnlyList<ProxmoxClusterInfo> GetClusterInfo();

    /// <summary>Client for one cluster; throws for an unknown id.</summary>
    IProxmoxClient GetClient(string clusterId);

    /// <summary>Every VM and container on every node of every cluster, plus any
    /// cluster or node that could not be read.</summary>
    Task<ProxmoxInventoryResult> GetInventoryAsync();
}

public class ProxmoxMultiClusterClient : IMultiClusterProxmoxClient
{
    private readonly Dictionary<string, IProxmoxClient> _clients;
    private readonly IReadOnlyList<ProxmoxClusterInfo> _clusterInfo;

    public ProxmoxMultiClusterClient(IEnumerable<ProxmoxClusterConfig> configs)
        : this(configs, config => new ProxmoxApiClient(config))
    {
    }

    internal ProxmoxMultiClusterClient(
        IEnumerable<ProxmoxClusterConfig> configs,
        Func<ProxmoxClusterConfig, IProxmoxClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(clientFactory);

        var clients = new Dictionary<string, IProxmoxClient>(StringComparer.OrdinalIgnoreCase);
        var info = new List<ProxmoxClusterInfo>();

        foreach (var config in configs)
        {
            if (config is null || string.IsNullOrWhiteSpace(config.Id))
                throw new ArgumentException("Every cluster config needs a non-empty id.");

            if (!clients.TryAdd(config.Id, clientFactory(config)))
                throw new ArgumentException($"Duplicate cluster id '{config.Id}'.");

            info.Add(new ProxmoxClusterInfo(config.Id, config.Url));
        }

        if (clients.Count == 0)
            throw new ArgumentException("At least one cluster config is required.");

        _clients = clients;
        _clusterInfo = info;
    }

    public IReadOnlyList<ProxmoxClusterInfo> GetClusterInfo() => _clusterInfo;

    public IProxmoxClient GetClient(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            throw new ArgumentException("A cluster id is required.", nameof(clusterId));

        if (!_clients.TryGetValue(clusterId, out var client))
            throw new ArgumentException(
                $"Unknown cluster '{clusterId}'. Configured clusters: {string.Join(", ", _clients.Keys)}");

        return client;
    }

    public async Task<ProxmoxInventoryResult> GetInventoryAsync()
    {
        var entries = new List<ProxmoxInventoryEntry>();
        var errors = new List<string>();

        foreach (var cluster in _clusterInfo)
        {
            var client = GetClient(cluster.Id);

            List<ProxmoxNodeInfo> nodes;
            try
            {
                nodes = (await client.GetNodesAsync().ConfigureAwait(false)).Data;
            }
            catch (ProxmoxApiException ex)
            {
                errors.Add($"cluster '{cluster.Id}': nodes could not be read ({ex.Message})");
                continue;
            }

            foreach (var node in nodes)
            {
                // The two per-node lists are independent, so they run together.
                var perNode = await Task.WhenAll(
                    CollectVmsAsync(client, cluster.Id, node.Name),
                    CollectContainersAsync(client, cluster.Id, node.Name)).ConfigureAwait(false);

                foreach (var (nodeEntries, error) in perNode)
                {
                    entries.AddRange(nodeEntries);
                    if (error is not null)
                        errors.Add(error);
                }
            }
        }

        entries.Sort(static (left, right) =>
        {
            var byCluster = string.Compare(left.Cluster, right.Cluster, StringComparison.OrdinalIgnoreCase);
            if (byCluster != 0)
                return byCluster;

            var byNode = string.Compare(left.Node, right.Node, StringComparison.OrdinalIgnoreCase);
            return byNode != 0 ? byNode : left.VmId.CompareTo(right.VmId);
        });

        return new ProxmoxInventoryResult(entries, errors);
    }

    private static async Task<(List<ProxmoxInventoryEntry> Entries, string? Error)> CollectVmsAsync(
        IProxmoxClient client, string clusterId, string nodeName)
    {
        try
        {
            var response = await client.GetVmsAsync(nodeName).ConfigureAwait(false);
            var mapped = response.Data
                .Select(vm => new ProxmoxInventoryEntry(clusterId, nodeName, vm.VmId, vm.Name, "vm", vm.Status))
                .ToList();
            return (mapped, null);
        }
        catch (ProxmoxApiException ex)
        {
            return (new List<ProxmoxInventoryEntry>(), $"cluster '{clusterId}' node '{nodeName}' VMs: {ex.Message}");
        }
    }

    private static async Task<(List<ProxmoxInventoryEntry> Entries, string? Error)> CollectContainersAsync(
        IProxmoxClient client, string clusterId, string nodeName)
    {
        try
        {
            var response = await client.GetContainersAsync(nodeName).ConfigureAwait(false);
            var mapped = response.Data
                .Select(container => new ProxmoxInventoryEntry(clusterId, nodeName, container.VmId, container.Name, "container", container.Status))
                .ToList();
            return (mapped, null);
        }
        catch (ProxmoxApiException ex)
        {
            return (new List<ProxmoxInventoryEntry>(), $"cluster '{clusterId}' node '{nodeName}' containers: {ex.Message}");
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
    }
}
