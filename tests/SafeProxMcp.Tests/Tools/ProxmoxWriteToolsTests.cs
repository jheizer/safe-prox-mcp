using System.Text.Json;
using FluentAssertions;
using Moq;
using SafeProxMcp.Models;
using SafeProxMcp.Services;
using SafeProxMcp.Tools;
using Xunit;

namespace SafeProxMcp.Tests.Tools;

public class ProxmoxWriteToolsTests
{
    private readonly Mock<IProxmoxClient> _client = new();
    private readonly Mock<IMultiClusterProxmoxClient> _multi = new();
    private readonly ProxmoxWriteTools _tools;

    public ProxmoxWriteToolsTests()
    {
        _multi.Setup(m => m.GetClient(It.IsAny<string>())).Returns((string cluster) =>
            cluster == "home"
                ? _client.Object
                : throw new ArgumentException($"Unknown cluster '{cluster}'. Configured clusters: home"));
        _tools = new ProxmoxWriteTools(_multi.Object);
    }

    private static JsonElement Json(object result) => JsonSerializer.SerializeToElement(result);

    // -- Snapshots ---------------------------------------------------------

    [Fact]
    public async Task CreateVmSnapshotAsync_ReturnsUpidTask()
    {
        _client.Setup(c => c.CreateVmSnapshotAsync("pve1", 100, "before-update", "Before system update",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxmoxSnapshotOperationResponse("UPID:pve1:1234:createSnapshot:100:root@pam:"));

        var result = Json(await _tools.CreateVmSnapshotAsync("home", "pve1", 100, "before-update",
            "Before system update"));

        result.GetProperty("task").GetString().Should().StartWith("UPID:");
    }

    [Fact]
    public async Task CreateVmSnapshotAsync_RejectsBlankName()
    {
        var act = () => _tools.CreateVmSnapshotAsync("home", "pve1", 100, "  ");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("name is required*");
        _client.Verify(c => c.CreateVmSnapshotAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateContainerSnapshotAsync_ForwardsToContainerEndpoint()
    {
        _client.Setup(c => c.CreateContainerSnapshotAsync("pve1", 200, "ct-snap", null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxmoxSnapshotOperationResponse("UPID:pve1:1234:createSnapshot:200:root@pam:"));

        var result = Json(await _tools.CreateContainerSnapshotAsync("home", "pve1", 200, "ct-snap"));

        result.GetProperty("task").GetString().Should().StartWith("UPID:");
    }

    // -- Backups ----------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_ReturnsUpidTask()
    {
        _client.Setup(c => c.CreateBackupAsync("pve1", 100, "nas", "snapshot", "zstd", "pre upgrade",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxmoxBackupOperationResponse("UPID:pve1:1234:vzdump:100:root@pam:"));

        var result = Json(await _tools.CreateBackupAsync("home", "pve1", 100, "nas", "snapshot", "zstd",
            "pre upgrade"));

        result.GetProperty("task").GetString().Should().StartWith("UPID:");
    }

    [Fact]
    public async Task CreateBackupAsync_RejectsBlankStorage()
    {
        var act = () => _tools.CreateBackupAsync("home", "pve1", 100, "  ");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("storage is required*");
        _client.Verify(c => c.CreateBackupAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBackupAsync_RejectsUnknownMode()
    {
        var act = () => _tools.CreateBackupAsync("home", "pve1", 100, "nas", "teleport");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("mode must be one of*");
        _client.Verify(c => c.CreateBackupAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBackupAsync_RejectsUnknownCompression()
    {
        var act = () => _tools.CreateBackupAsync("home", "pve1", 100, "nas", "snapshot", "zip");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("compress must be one of*");
        _client.Verify(c => c.CreateBackupAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBackupAsync_NormalizesModeAndCompressionCase()
    {
        _client.Setup(c => c.CreateBackupAsync("pve1", 100, "nas", "snapshot", "zstd", null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxmoxBackupOperationResponse("UPID:pve1:1234:vzdump:100:root@pam:"));

        var result = Json(await _tools.CreateBackupAsync("home", "pve1", 100, "nas", "SNAPSHOT", "Zstd"));

        result.GetProperty("task").GetString().Should().StartWith("UPID:");
        _client.Verify(c => c.CreateBackupAsync("pve1", 100, "nas", "snapshot", "zstd", null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -- Write-tool gating -------------------------------------------------

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    public void WriteTools_AreDisabledByExplicitDisableValues(string value)
    {
        WithEnvironmentVariable(value,
            () => ProxmoxWriteTools.EnabledFromEnvironment().Should().BeFalse());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("maybe")]
    public void WriteTools_StayEnabledUnlessExplicitlyDisabled(string? value)
    {
        WithEnvironmentVariable(value,
            () => ProxmoxWriteTools.EnabledFromEnvironment().Should().BeTrue());
    }

    private static void WithEnvironmentVariable(string? value, Action assertion)
    {
        var original = Environment.GetEnvironmentVariable(ProxmoxWriteTools.DisableEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ProxmoxWriteTools.DisableEnvironmentVariable, value);
            assertion();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProxmoxWriteTools.DisableEnvironmentVariable, original);
        }
    }
}
