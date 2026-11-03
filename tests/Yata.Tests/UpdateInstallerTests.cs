using System.Text.Json;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Which download "Update and restart" trusts. No network involved.</summary>
public class UpdateInstallerTests
{
    private const string Sha = "e972a47ca26e81e169158967e037cc100ef33457d582eebef0a85a83d1d1b7d5";

    private static JsonElement Release(string tag = "v0.2.1", string name = "Yata-Setup-0.2.1.exe",
        string url = "https://github.com/ShamilJafarov20/yata/releases/download/v0.2.1/Yata-Setup-0.2.1.exe",
        string? digest = "sha256:" + Sha)
    {
        var asset = new Dictionary<string, object?> { ["name"] = name, ["browser_download_url"] = url };
        if (digest != null)
            asset["digest"] = digest;
        var release = new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["assets"] = new object[] { new Dictionary<string, object?> { ["name"] = "SHA256SUMS.txt" }, asset },
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(release)).RootElement;
    }

    [Fact]
    public void Picks_the_installer_of_the_release_with_its_digest()
    {
        var installer = UpdateInstaller.Pick(Release());

        Assert.NotNull(installer);
        Assert.Equal("0.2.1", installer.Version);
        Assert.Equal(Sha, installer.Sha256);
        Assert.Equal("github.com", installer.Url.Host);
    }

    [Fact]
    public void Upper_case_digests_are_compared_in_lower_case()
    {
        Assert.Equal(Sha, UpdateInstaller.Pick(Release(digest: "sha256:" + Sha.ToUpperInvariant()))!.Sha256);
    }

    [Theory]
    [InlineData("http://github.com/ShamilJafarov20/yata/releases/download/v0.2.1/Yata-Setup-0.2.1.exe")] // not HTTPS
    [InlineData("https://example.com/ShamilJafarov20/yata/releases/download/v0.2.1/Yata-Setup-0.2.1.exe")] // other host
    [InlineData("https://github.com/someone/yata/releases/download/v0.2.1/Yata-Setup-0.2.1.exe")] // other repository
    public void Refuses_a_download_from_anywhere_else(string url)
    {
        Assert.Null(UpdateInstaller.Pick(Release(url: url)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:abc")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:zz72a47ca26e81e169158967e037cc100ef33457d582eebef0a85a83d1d1b7d5")]
    public void Refuses_an_installer_without_a_usable_sha256(string? digest)
    {
        Assert.Null(UpdateInstaller.Pick(Release(digest: digest)));
    }

    [Fact]
    public void Ignores_an_installer_named_for_another_version()
    {
        Assert.Null(UpdateInstaller.Pick(Release(name: "Yata-Setup-0.2.0.exe")));
    }

    [Theory]
    [InlineData("https://github.com/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("http://objects.githubusercontent.com/x", false)]
    [InlineData("https://githubusercontent.com.example.com/x", false)]
    public void Only_github_storage_may_serve_the_file_after_redirects(string url, bool allowed)
    {
        Assert.Equal(allowed, UpdateInstaller.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData(null, null, true)]            // Unsigned Yata (today): the hash alone.
    [InlineData(null, "CN=Anyone", true)]
    [InlineData("CN=Yata", "CN=Yata", true)]
    [InlineData("CN=Yata", null, false)]      // Signed Yata, unsigned installer.
    [InlineData("CN=Yata", "CN=Mallory", false)]
    public void A_signed_Yata_takes_only_its_own_publishers_installer(string? ours, string? theirs, bool ok)
    {
        Assert.Equal(ok, UpdateInstaller.SamePublisher(ours, theirs));
    }

    [Fact]
    public void Reads_who_signed_a_file()
    {
        // The .NET runtime is Microsoft-signed; this test assembly isn't signed at all.
        Assert.Contains("Microsoft", Yata.Helpers.Authenticode.Publisher(typeof(object).Assembly.Location));
        Assert.Null(Yata.Helpers.Authenticode.Publisher(typeof(UpdateInstallerTests).Assembly.Location));
    }
}
