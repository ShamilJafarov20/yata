using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>The newest release's installer, as GitHub lists it.</summary>
public sealed record ReleaseInstaller(string Version, Uri Url, string Sha256);

/// <summary>Why an update didn't go ahead, in words for the settings window.</summary>
public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// "Update and restart", only on a click: asks GitHub for the newest release, downloads its installer,
/// checks it against the SHA-256 GitHub publishes for that file, and runs it. The installer closes
/// Yata, updates it in place (same folder, settings and data kept) and opens it again.
/// </summary>
public sealed class UpdateInstaller
{
    private const string Repo = "/ShamilJafarov20/yata/releases/download/";
    // The download link on github.com redirects to GitHub's file storage.
    private static readonly string[] Hosts = { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };
    private static readonly HttpClient Api = UpdateChecker.CreateClient();
    // The installer is about 3 MB; slow connections get a couple of minutes.
    private static readonly HttpClient Http = HttpPool.Client(TimeSpan.FromMinutes(3));

    private readonly string _folder = Path.Combine(AppPaths.DataDir, "updates");

    /// <summary>Downloads and checks the newest installer; returns its path.</summary>
    public async Task<string> DownloadAsync(CancellationToken ct)
    {
        var installer = await FetchAsync(ct);
        if (!UpdateChecker.IsNewer(installer.Version, UpdateChecker.Current))
            throw new UpdateException(Strings.Update_UpToDate);

        string path = Path.Combine(_folder, $"Yata-Setup-{installer.Version}.exe");
        try
        {
            // Only the installer about to run is kept; older ones are leftovers. One still open (or
            // held by an antivirus) makes this fail, and that's a failed update, not a stuck button.
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
            Directory.CreateDirectory(_folder);

            using var response = await Http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!IsAllowed(response.RequestMessage?.RequestUri))
                throw new UpdateException(Strings.Update_Failed);
            response.EnsureSuccessStatusCode();

            string sha256;
            await using (var file = File.Create(path))
            {
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (sha256 != installer.Sha256)
            {
                Log.Warn("updates", $"SHA-256 do instalador {installer.Version} não confere com o publicado no GitHub");
                throw new UpdateException(Strings.Update_BadFile);
            }

            // The SHA-256 comes from GitHub, like the file: it catches a broken download, not a
            // release made with a stolen token. A signed Yata also wants the same publisher's signature.
            string? ours = Environment.ProcessPath is string self ? Authenticode.Publisher(self) : null;
            if (!SamePublisher(ours, Authenticode.Publisher(path)))
            {
                Log.Warn("updates", $"Instalador {installer.Version} sem a assinatura do mesmo editor do Yata");
                throw new UpdateException(Strings.Update_BadFile);
            }
            if (ours == null)
                Log.Info("updates", "Yata sem assinatura digital: instalador conferido só pelo SHA-256");
            Log.Info("updates", $"Instalador {installer.Version} baixado e conferido");
            return path;
        }
        catch (Exception ex) when (ex is not UpdateException)
        {
            TryDelete(path);
            Log.Info("updates", $"Falha ao baixar a versão nova ({ex.GetType().Name})");
            throw new UpdateException(ex is OperationCanceledException ? Strings.Update_Cancelled : Strings.Update_Failed, ex);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>
    /// An unsigned Yata (until code signing starts) takes any installer that passed the hash; a signed
    /// one only an installer signed by the same publisher.
    /// </summary>
    internal static bool SamePublisher(string? ours, string? theirs) => ours == null || ours == theirs;

    /// <summary>
    /// Runs the downloaded installer silently (a progress window shows) and lets it reopen Yata;
    /// the caller then quits so the files can be replaced.
    /// </summary>
    public static void Launch(string path)
    {
        try
        {
            // /UPDATE=1 tells the installer to open Yata again when it's done (see installer/yata.iss).
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // Includes the user turning down the .NET runtime's admin prompt before Setup even starts.
            Log.Warn("updates", "Não foi possível abrir o instalador", ex);
            throw new UpdateException(Strings.Update_Failed, ex);
        }
    }

    private static async Task<ReleaseInstaller> FetchAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Api.GetAsync(UpdateChecker.LatestApi, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Pick(json.RootElement) ?? throw new UpdateException(Strings.Update_Failed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Info("updates", $"Não foi possível consultar a versão nova ({ex.GetType().Name})");
            throw new UpdateException(ex is OperationCanceledException && ct.IsCancellationRequested ? Strings.Update_Cancelled : Strings.Update_Failed, ex);
        }
    }

    /// <summary>
    /// The release's "Yata-Setup-x.y.z.exe", only from this repository's releases over HTTPS and only
    /// with GitHub's "sha256:" digest, which the download is checked against.
    /// </summary>
    internal static ReleaseInstaller? Pick(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } name)
            return null;
        string version = name.Trim().TrimStart('v', 'V');
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (Str(asset, "name") != $"Yata-Setup-{version}.exe")
                continue;
            if (!Uri.TryCreate(Str(asset, "browser_download_url"), UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" || !url.AbsolutePath.StartsWith(Repo, StringComparison.Ordinal))
                return null;
            if (Str(asset, "digest") is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal))
                return null;
            string sha256 = digest["sha256:".Length..].ToLowerInvariant();
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
                return null;
            return new ReleaseInstaller(version, url, sha256);
        }
        return null;
    }

    internal static bool IsAllowed(Uri? url) =>
        url != null && url.Scheme == Uri.UriSchemeHttps && Hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleaned up on the next update.
        }
    }
}
