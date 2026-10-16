using System.Net;
using System.Text.Json;
using SafeProxMcp.Models;

namespace SafeProxMcp.Services;

/// <summary>
/// Raised when a Proxmox API call fails. The message is safe to show to an MCP
/// client: it names the cluster, the endpoint and Proxmox's own explanation.
/// </summary>
public class ProxmoxApiException : IOException
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    public ProxmoxApiException(string message, HttpStatusCode? statusCode = null, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public interface IProxmoxClient : IDisposable
{
    string ClusterId { get; }

    Task<ProxmoxNodesResponse> GetNodesAsync(CancellationToken ct = default);
    Task<ProxmoxNodeStatusResponse> GetNodeStatusAsync(string node, CancellationToken ct = default);
    Task<ProxmoxClusterStatusResponse> GetClusterStatusAsync(CancellationToken ct = default);
    Task<ProxmoxResourcesResponse> GetClusterResourcesAsync(string? type = null, CancellationToken ct = default);
    Task<ProxmoxVmListResponse> GetVmsAsync(string node, CancellationToken ct = default);
    Task<ProxmoxVmStatusResponse> GetVmStatusAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxVmConfigResponse> GetVmConfigAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxLxcListResponse> GetContainersAsync(string node, CancellationToken ct = default);
    Task<ProxmoxVmStatusResponse> GetContainerStatusAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxLxcConfigResponse> GetContainerConfigAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxStorageListResponse> GetStorageAsync(string? node = null, CancellationToken ct = default);
    Task<ProxmoxNetworkListResponse> GetNetworksAsync(string node, CancellationToken ct = default);
    Task<ProxmoxUserListResponse> GetUsersAsync(CancellationToken ct = default);
    Task<ProxmoxGroupListResponse> GetGroupsAsync(CancellationToken ct = default);
    Task<ProxmoxDiskListResponse> GetDisksAsync(string node, CancellationToken ct = default);
    Task<ProxmoxVersionResponse> GetVersionAsync(string node, CancellationToken ct = default);
    Task<ProxmoxNodeSyslogResponse> GetNodeSyslogAsync(string node, long start, long limit, CancellationToken ct = default);
    Task<ProxmoxSnapshotListResponse> GetVmSnapshotsAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxSnapshotListResponse> GetContainerSnapshotsAsync(string node, int vmId, CancellationToken ct = default);
    Task<ProxmoxSnapshotOperationResponse> CreateVmSnapshotAsync(string node, int vmId, string name, string? description = null, CancellationToken ct = default);
    Task<ProxmoxSnapshotOperationResponse> CreateContainerSnapshotAsync(string node, int vmId, string name, string? description = null, CancellationToken ct = default);
    Task<ProxmoxBackupOperationResponse> CreateBackupAsync(string node, int vmId, string storage, string? mode = null, string? compress = null, string? notes = null, CancellationToken ct = default);
}

public class ProxmoxApiClient : IProxmoxClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    // PVE API tokens are always "user@realm!name=uuid".
    private static readonly System.Text.RegularExpressions.Regex ApiTokenPattern =
        new(@"^[^!@]+@[^!@]+![^=]+=.*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Fixed per-request timeout: a hung Proxmox call should surface as a tool
    // error rather than stalling the MCP client.
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(60);

    public ProxmoxApiClient(ProxmoxClusterConfig config)
        : this(CreateHandler(), config)
    {
    }

    internal ProxmoxApiClient(HttpMessageHandler handler, ProxmoxClusterConfig config)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id))
            throw new ArgumentException("Cluster config must have an id.", nameof(config));
        if (string.IsNullOrWhiteSpace(config.Token))
            throw new ArgumentException($"Cluster '{config.Id}' must have an API token.", nameof(config));
        if (!ApiTokenPattern.IsMatch(config.Token))
            throw new ArgumentException(
                $"Cluster '{config.Id}' has an invalid API token '{config.Token.Split('=')[0]}'; " +
                "expected the form user@realm!name=uuid.",
                nameof(config));

        var baseUri = ParseBaseUrl(config);
        _http = new HttpClient(handler) { BaseAddress = baseUri };
        _http.DefaultRequestHeaders.Add("Authorization", $"PVEAPIToken {config.Token}");
        _http.Timeout = DefaultRequestTimeout;
        ClusterId = config.Id;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string ClusterId { get; }

    internal TimeSpan RequestTimeout => _http.Timeout;

    private static Uri ParseBaseUrl(ProxmoxClusterConfig config)
    {
        if (!Uri.TryCreate(config.Url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"Cluster '{config.Id}' has an invalid url '{config.Url}'; expected an absolute http(s) URL such as https://pve.example.com:8006.",
                nameof(config));
        }

        // Accept either the cluster root (https://host:8006) or an explicit
        // /api2/json URL; both end up at the same API root.
        var root = uri.ToString().TrimEnd('/');
        if (!root.EndsWith("/api2/json", StringComparison.OrdinalIgnoreCase))
            root += "/api2/json";
        return new Uri(root + "/");
    }

    private static HttpMessageHandler CreateHandler()
    {
        // Proxmox ships a self-signed certificate by default, so the API token is
        // the access control here, not the certificate.
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
    }

    // -- Nodes ---------------------------------------------------------
    public Task<ProxmoxNodesResponse> GetNodesAsync(CancellationToken ct = default)
        => GetAsync<ProxmoxNodesResponse>("nodes", ct);

    public Task<ProxmoxNodeStatusResponse> GetNodeStatusAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxNodeStatusResponse>($"nodes/{Escape(node)}/status", ct);

    // -- Cluster -------------------------------------------------------
    public Task<ProxmoxClusterStatusResponse> GetClusterStatusAsync(CancellationToken ct = default)
        => GetAsync<ProxmoxClusterStatusResponse>("cluster/status", ct);

    public Task<ProxmoxResourcesResponse> GetClusterResourcesAsync(string? type = null, CancellationToken ct = default)
        => GetAsync<ProxmoxResourcesResponse>($"cluster/resources{Query(("type", type))}", ct);

    // -- Virtual machines ----------------------------------------------
    public Task<ProxmoxVmListResponse> GetVmsAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxVmListResponse>($"nodes/{Escape(node)}/qemu", ct);

    // Proxmox's /status is a sub-resource index; the live values live under /status/current.
    public Task<ProxmoxVmStatusResponse> GetVmStatusAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxVmStatusResponse>($"nodes/{Escape(node)}/qemu/{vmId}/status/current", ct);

    public Task<ProxmoxVmConfigResponse> GetVmConfigAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxVmConfigResponse>($"nodes/{Escape(node)}/qemu/{vmId}/config", ct);

    // -- Containers ----------------------------------------------------
    public Task<ProxmoxLxcListResponse> GetContainersAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxLxcListResponse>($"nodes/{Escape(node)}/lxc", ct);

    public Task<ProxmoxVmStatusResponse> GetContainerStatusAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxVmStatusResponse>($"nodes/{Escape(node)}/lxc/{vmId}/status/current", ct);

    public Task<ProxmoxLxcConfigResponse> GetContainerConfigAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxLxcConfigResponse>($"nodes/{Escape(node)}/lxc/{vmId}/config", ct);

    // -- Storage -------------------------------------------------------
    /// <summary>
    /// Without a node this returns the cluster storage configuration; with a node
    /// it returns that node's view of the storage, which is where the usage
    /// numbers (total/used/avail) live.
    /// </summary>
    public Task<ProxmoxStorageListResponse> GetStorageAsync(string? node = null, CancellationToken ct = default)
        => GetAsync<ProxmoxStorageListResponse>(
            string.IsNullOrEmpty(node) ? "storage" : $"nodes/{Escape(node)}/storage", ct);

    // -- Network -------------------------------------------------------
    public Task<ProxmoxNetworkListResponse> GetNetworksAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxNetworkListResponse>($"nodes/{Escape(node)}/network", ct);

    // -- Users and groups ----------------------------------------------
    public Task<ProxmoxUserListResponse> GetUsersAsync(CancellationToken ct = default)
        => GetAsync<ProxmoxUserListResponse>("access/users", ct);

    public Task<ProxmoxGroupListResponse> GetGroupsAsync(CancellationToken ct = default)
        => GetAsync<ProxmoxGroupListResponse>("access/groups", ct);

    // -- Disks ---------------------------------------------------------
    public Task<ProxmoxDiskListResponse> GetDisksAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxDiskListResponse>($"nodes/{Escape(node)}/disks/list", ct);

    // -- Version -------------------------------------------------------
    public Task<ProxmoxVersionResponse> GetVersionAsync(string node, CancellationToken ct = default)
        => GetAsync<ProxmoxVersionResponse>($"nodes/{Escape(node)}/version", ct);

    // -- Syslog --------------------------------------------------------
    public Task<ProxmoxNodeSyslogResponse> GetNodeSyslogAsync(string node, long start, long limit, CancellationToken ct = default)
        => GetAsync<ProxmoxNodeSyslogResponse>($"nodes/{Escape(node)}/syslog?start={start}&limit={limit}", ct);

    // -- Snapshots -----------------------------------------------------
    public Task<ProxmoxSnapshotListResponse> GetVmSnapshotsAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxSnapshotListResponse>($"nodes/{Escape(node)}/qemu/{vmId}/snapshot", ct);

    public Task<ProxmoxSnapshotListResponse> GetContainerSnapshotsAsync(string node, int vmId, CancellationToken ct = default)
        => GetAsync<ProxmoxSnapshotListResponse>($"nodes/{Escape(node)}/lxc/{vmId}/snapshot", ct);

    public Task<ProxmoxSnapshotOperationResponse> CreateVmSnapshotAsync(string node, int vmId, string name, string? description = null, CancellationToken ct = default)
        => PostAsync<ProxmoxSnapshotOperationResponse>($"nodes/{Escape(node)}/qemu/{vmId}/snapshot", name, description, ct);

    public Task<ProxmoxSnapshotOperationResponse> CreateContainerSnapshotAsync(string node, int vmId, string name, string? description = null, CancellationToken ct = default)
        => PostAsync<ProxmoxSnapshotOperationResponse>($"nodes/{Escape(node)}/lxc/{vmId}/snapshot", name, description, ct);

    // -- Backups ---------------------------------------------------------
    // vzdump is node-level and infers the guest type from the vmid, so the same
    // endpoint backs up both qemu and lxc guests.
    public Task<ProxmoxBackupOperationResponse> CreateBackupAsync(
        string node, int vmId, string storage, string? mode = null, string? compress = null, string? notes = null,
        CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["vmid"] = vmId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["storage"] = storage
        };
        if (!string.IsNullOrEmpty(mode)) form["mode"] = mode;
        if (!string.IsNullOrEmpty(compress)) form["compress"] = compress;
        if (!string.IsNullOrEmpty(notes)) form["notes"] = notes;

        return PostFormAsync<ProxmoxBackupOperationResponse>($"nodes/{Escape(node)}/vzdump", form, ct);
    }

    // -- Plumbing ------------------------------------------------------
    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Query(params (string Name, string? Value)[] parts)
    {
        var pairs = new List<string>();
        foreach (var (name, value) in parts)
        {
            if (!string.IsNullOrEmpty(value))
                pairs.Add($"{Escape(name)}={Escape(value)}");
        }

        return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    // The request message - and its content - must stay alive until the send
    // completes. Returning the Task un-awaited ends the `using` scope immediately, so
    // the transport later finds a disposed body: GETs survive it (no content to copy),
    // POSTs fail with ObjectDisposedException.
    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, path, ct).ConfigureAwait(false);
    }

    private Task<T> PostAsync<T>(string path, string name, string? description, CancellationToken ct)
        => PostFormAsync<T>(path, new Dictionary<string, string>
        {
            // The new snapshot name must be sent as `snapname`: PVE's schema rejects
            // `snapshot` outright ("property is not defined in schema") and reports
            // `snapname` as required.
            ["snapname"] = name,
            ["description"] = description ?? string.Empty
        }, ct);

    private async Task<T> PostFormAsync<T>(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // Proxmox accepts form-encoded parameters; JSON bodies are rejected by
            // several endpoints, so this is the only parameter encoding used.
            Content = new FormUrlEncodedContent(form)
        };
        return await SendAsync<T>(request, path, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            var seconds = $"{_http.Timeout.TotalSeconds:0.#}s";
            throw new ProxmoxApiException(
                $"Cluster '{ClusterId}' did not respond for {request.Method} {path} (timeout {seconds}).", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProxmoxApiException(
                $"Cannot reach cluster '{ClusterId}' at {_http.BaseAddress}: {ex.Message}", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw BuildApiException(request, path, response, body);

            return ParseResponse<T>(body, path);
        }
    }

    private ProxmoxApiException BuildApiException(
        HttpRequestMessage request, string path, HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        var reason = TryExtractProxmoxMessage(body) ?? Truncate(body) ?? response.ReasonPhrase;
        return new ProxmoxApiException(
            $"Proxmox cluster '{ClusterId}' returned {(int)status} {response.ReasonPhrase} for {request.Method} {path}: {reason}",
            status,
            Truncate(body));
    }

    private static string? TryExtractProxmoxMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            string? message = null;
            if (doc.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString()?.Trim();
            }

            // PVE names the rejected arguments in `errors`. Without them a 400 reads
            // only as "Parameter verification failed" and the caller cannot tell which
            // parameter to fix.
            var details = TryExtractParameterErrors(doc.RootElement);

            if (message is null)
                return details;
            if (details is null)
                return message;
            return $"{message} ({details})";
        }
        catch (JsonException)
        {
            // Not JSON; the caller falls back to the raw body.
        }

        return null;
    }

    private static string? TryExtractParameterErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
            return null;

        var parts = new List<string>();
        foreach (var property in errors.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim()
                : property.Value.GetRawText();
            parts.Add($"{property.Name}: {value}");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string? Truncate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var trimmed = body.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "...";
    }

    private T ParseResponse<T>(string body, string path)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ProxmoxApiException($"Proxmox cluster '{ClusterId}' returned an empty body for {path}.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ProxmoxApiException(
                $"Proxmox cluster '{ClusterId}' returned a non-JSON body for {path}: {Truncate(body)}", inner: ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Null)
                    throw new ProxmoxApiException(
                        $"Proxmox cluster '{ClusterId}' returned no data for {path}.");

                // Wrapper models expose a Data property and bind the whole envelope
                // (they may also read siblings such as "total"). Flat models - node/VM
                // status, VM/CT configs - bind the "data" element itself.
                var payload = HasDataProperty(typeof(T)) ? root.GetRawText() : data.GetRawText();
                return DeserializeOrThrow<T>(payload, path);
            }

            // A bare array shows up on a couple of Proxmox endpoints; wrap it so the
            // list-shaped response models still bind.
            if (root.ValueKind == JsonValueKind.Array && HasListDataProperty(typeof(T)))
                return DeserializeOrThrow<T>($"{{\"data\":{root.GetRawText()}}}", path);

            return DeserializeOrThrow<T>(root.GetRawText(), path);
        }
    }

    private T DeserializeOrThrow<T>(string json, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                ?? throw new ProxmoxApiException(
                    $"Proxmox cluster '{ClusterId}' returned no usable payload for {path}.");
        }
        catch (JsonException ex)
        {
            throw new ProxmoxApiException(
                $"Proxmox cluster '{ClusterId}' returned a payload that does not match the expected shape for {path}: {ex.Message}",
                inner: ex);
        }
    }

    private static bool HasDataProperty(Type type) =>
        type.GetProperty("Data") is not null;

    private static bool HasListDataProperty(Type type)
    {
        var dataProperty = type.GetProperty("Data");
        return dataProperty is not null
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(dataProperty.PropertyType);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
