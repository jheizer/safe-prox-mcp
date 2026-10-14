using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SafeProxMcp.Services;

var builder = WebApplication.CreateBuilder(args);

var clusters = ClusterConfigLoader.Load();

Console.WriteLine("SafeProxMcp - MCP server for Proxmox Virtual Environment");
foreach (var cluster in clusters)
{
    Console.WriteLine($"  cluster '{cluster.Id}' -> {cluster.Url}");
}

var mcpApiKey = Environment.GetEnvironmentVariable("MCP_SERVER_API_KEY");

builder.Services.AddSingleton<IMultiClusterProxmoxClient>(_ => new ProxmoxMultiClusterClient(clusters));

var toolLogger = LoggerFactory.Create(logging =>
    logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole())
    .CreateLogger("SafeProxMcp.Tools");

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters =>
        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var toolName = context.Params?.Name ?? "<unknown>";
                toolLogger.LogError(ex, "Tool {ToolName} failed", toolName);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"{toolName}: {ex.Message}" }]
                };
            }
        }));

var app = builder.Build();

if (!string.IsNullOrEmpty(mcpApiKey))
{
    app.Use(async (context, next) =>
    {
        // Health stays reachable for container orchestration probes.
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/health")
        {
            await next();
            return;
        }

        const string scheme = "Bearer ";
        var authorization = context.Request.Headers.Authorization.ToString();
        // RFC 7235: the scheme token is case-insensitive; the credential is not.
        if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            || !FixedTimeEquals(authorization[scheme.Length..], mcpApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"safe-prox-mcp\"";
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next();
    });
}

// Reports only how many clusters are configured; ids and URLs stay private.
app.MapGet("/health", (IMultiClusterProxmoxClient multi) =>
    Results.Ok(new { status = "ok", clusterCount = multi.GetClusterInfo().Count }));

app.MapMcp("/mcp");

app.Run();

static bool FixedTimeEquals(string provided, string expected)
    => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
