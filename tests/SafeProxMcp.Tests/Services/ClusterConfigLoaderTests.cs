using FluentAssertions;
using SafeProxMcp.Services;
using Xunit;

namespace SafeProxMcp.Tests.Services;

public class ClusterConfigLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "safe-prox-mcp-config-" + Guid.NewGuid().ToString("N"));

    public ClusterConfigLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDir, "clusters.json");
        File.WriteAllText(path, json);
        return path;
    }

    // -- Required config file -------------------------------------------

    [Fact]
    public void Load_ReadsClustersJsonFromTheGivenPath()
    {
        var path = WriteConfig("""[{"id":"home","url":"https://pve:8006","token":"u@pam!t=uuid"}]""");

        var clusters = ClusterConfigLoader.Load(path);

        clusters.Should().ContainSingle().Which.Id.Should().Be("home");
    }

    [Fact]
    public void Load_MissingConfigFile_ExplainsHowToCreateIt()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        var act = () => ClusterConfigLoader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{path}' was not found*Copy clusters.example.json*");
    }

    [Fact]
    public void Load_WithoutAConfigFileInTheWorkingDirectory_Throws()
    {
        var act = () => ClusterConfigLoader.Load();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*clusters.json*was not found*");
    }

    [Fact]
    public void DefaultConfigFile_IsClustersJson()
    {
        ClusterConfigLoader.DefaultConfigFile.Should().Be("clusters.json");
        File.Exists(Path.Combine(AppContext.BaseDirectory, ClusterConfigLoader.DefaultConfigFile))
            .Should().BeFalse("unit tests must not silently pick up a real cluster config");
    }

    // -- Field handling --------------------------------------------------

    [Fact]
    public void Load_IgnoresUnknownFieldsIncludingRetiredOnes()
    {
        var path = WriteConfig("""
            [{"id":"home","url":"https://pve:8006","token":"u@pam!t=uuid",
              "requestTimeoutSeconds":5,"insecureTls":true,"comment":"ignored"}]
            """);

        var cluster = ClusterConfigLoader.Load(path).Single();

        cluster.Id.Should().Be("home");
        cluster.Url.Should().Be("https://pve:8006");
        cluster.Token.Should().Be("u@pam!t=uuid");
    }

    // -- Validation -------------------------------------------------------

    [Theory]
    [InlineData("""[]""", "*at least one cluster*")]
    [InlineData("""{"id":"home"}""", "*not valid JSON*")]
    [InlineData("""null""", "*must be a JSON array*")]
    [InlineData("""[{"url":"https://pve:8006","token":"u@pam!t=uuid"}]""", "*must have an 'id'*")]
    [InlineData("""[{"id":"","url":"https://pve:8006","token":"u@pam!t=uuid"}]""", "*must have an 'id'*")]
    [InlineData("""[{"id":"home","token":"u@pam!t=uuid"}]""", "*must have a 'url'*")]
    [InlineData("""[{"id":"home","url":"https://pve:8006"}]""", "*must have a 'token'*")]
    [InlineData("""[{"id":"home","url":"https://a:8006","token":"t=1"},{"id":"HOME","url":"https://b:8006","token":"t=1"}]""",
        "*duplicate cluster id 'HOME'*")]
    public void Load_RejectsInvalidConfiguration(string json, string expectedMessage)
    {
        var path = WriteConfig(json);

        var act = () => ClusterConfigLoader.Load(path);

        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Fact]
    public void Load_ReportsMalformedConfigFileWithItsPath()
    {
        var path = WriteConfig("{ not json");

        var act = () => ClusterConfigLoader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{path}' is not valid JSON*");
    }
}
