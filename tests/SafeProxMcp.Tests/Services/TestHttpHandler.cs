using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SafeProxMcp.Tests.Services;

/// <summary>
/// HttpMessageHandler that matches request paths against regex patterns and
/// returns predefined JSON responses. Records the last request for assertions.
/// </summary>
internal sealed class TestHttpHandler : HttpMessageHandler
{
    private readonly List<(Regex Pattern, string Response, int StatusCode)> _routes = new();

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string LastRequestPath => LastRequest!.RequestUri!.AbsolutePath;
    public string LastRequestQuery => LastRequest!.RequestUri!.Query;

    public TestHttpHandler Add(string pattern, string response, int statusCode = 200)
    {
        _routes.Add((new Regex(pattern, RegexOptions.Compiled), response, statusCode));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real transport only copies the request body after awaiting (connection
        // setup), so yield before touching the request: a handler that reads the body
        // synchronously hides request-lifetime bugs in the caller.
        await Task.Yield();

        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var path = request.RequestUri!.AbsolutePath + request.RequestUri.Query;

        foreach (var (pattern, response, statusCode) in _routes)
        {
            if (pattern.IsMatch(path))
            {
                return new HttpResponseMessage((HttpStatusCode)statusCode)
                {
                    Content = new StringContent(response, Encoding.UTF8, "application/json")
                };
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"data\": null}")
        };
    }
}
