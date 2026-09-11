using RevitCortex.Plugin.Updates;
using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Updates;

/// <summary>
/// Security regression tests for the reusable download validation helpers.
/// Automatic updates are currently disabled in the Revit 2026 fork, but the URL
/// allowlist and SHA-256 helpers are retained for a future fork-owned release channel.
/// </summary>
public class UpdateSecurityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"rcsec_{Guid.NewGuid():N}");
    public UpdateSecurityTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    // SHA-256 of the ASCII bytes of "hello" (no newline).
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    [Theory]
    [InlineData("https://raw.githubusercontent.com/AlexFspb/RevitCortex/main/release.zip")]
    [InlineData("https://github.com/AlexFspb/RevitCortex/releases/download/v1/latest.zip")]
    [InlineData("https://1drv.ms/u/s!AbCdEf")]
    [InlineData("https://onedrive.live.com/download?cid=1&resid=2")]
    [InlineData("https://example.sharepoint.com/sites/x/latest.zip")]
    public void IsTrustedDownloadUrl_TrustedHosts_True(string url)
        => Assert.True(UpdateChecker.IsTrustedDownloadUrl(url));

    [Theory]
    [InlineData("http://1drv.ms/u/s!AbCdEf")]
    [InlineData("https://evil.example.com/latest.zip")]
    [InlineData("https://evilgithub.com/latest.zip")]
    [InlineData("https://github.com.evil.com/x.zip")]
    [InlineData("https://notsharepoint.com/x.zip")]
    [InlineData("file:///C:/evil.zip")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTrustedDownloadUrl_UntrustedOrMalformed_False(string? url)
        => Assert.False(UpdateChecker.IsTrustedDownloadUrl(url));

    [Fact]
    public void Sha256Matches_CorrectHashLowercase_True()
    {
        var path = Path.Combine(_tempDir, "a.bin");
        File.WriteAllText(path, "hello");
        Assert.True(UpdateChecker.Sha256Matches(path, HelloSha256));
    }

    [Fact]
    public void Sha256Matches_CorrectHashUppercase_True()
    {
        var path = Path.Combine(_tempDir, "b.bin");
        File.WriteAllText(path, "hello");
        Assert.True(UpdateChecker.Sha256Matches(path, HelloSha256.ToUpperInvariant()));
    }

    [Fact]
    public void Sha256Matches_WrongHash_False()
    {
        var path = Path.Combine(_tempDir, "c.bin");
        File.WriteAllText(path, "hello");
        Assert.False(UpdateChecker.Sha256Matches(path, new string('0', 64)));
    }

    [Fact]
    public void Sha256Matches_EmptyOrNullExpected_False()
    {
        var path = Path.Combine(_tempDir, "d.bin");
        File.WriteAllText(path, "hello");
        Assert.False(UpdateChecker.Sha256Matches(path, ""));
        Assert.False(UpdateChecker.Sha256Matches(path, null));
    }
}
