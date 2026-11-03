using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>What Spotify's player says right now (GET /me/player).</summary>
public sealed record SpotifyPlayer(string? TrackUri, string? TrackName, bool IsPlaying, bool Shuffle, string Repeat);

public sealed class SpotifyException : Exception
{
    public bool NeedsReconnect { get; }

    public SpotifyException(string message, bool needsReconnect = false) : base(message) => NeedsReconnect = needsReconnect;
}

/// <summary>
/// The optional Spotify Web API: like the song, shuffle and repeat. Play, pause, skip, seek and lyrics
/// don't need it (Windows' media controls and LRCLIB cover them, for free accounts too).
///
/// Since February 2026 Spotify only gives Development Mode apps to Premium owners, with up to five
/// users each, so Yata ships no client of its own: each user registers an app on developer.spotify.com
/// and pastes its Client ID. PKCE means there's no secret to keep. The refresh token is kept with DPAPI.
/// </summary>
public sealed class SpotifyAccount
{
    /// <summary>Register exactly this in the Spotify app; the port is picked at sign-in (Spotify allows it for loopback IPs).</summary>
    public const string RedirectUri = "http://127.0.0.1/callback";
    public const string DashboardUrl = "https://developer.spotify.com/dashboard";

    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string ApiUrl = "https://api.spotify.com/v1";
    private static readonly string[] Scopes =
    {
        "user-library-read", "user-library-modify", "user-read-playback-state", "user-modify-playback-state",
    };
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = HttpPool.Client(TimeSpan.FromSeconds(15));

    private readonly AppStore _store;
    private readonly string _file = Path.Combine(AppPaths.DataDir, "spotify.dat");
    private SpotifyCredential? _credential;
    private string? _accessToken;
    private DateTime _accessExpiresUtc;

    public SpotifyAccount(AppStore store)
    {
        _store = store;
        _credential = SecretFile.Read<SpotifyCredential>(_file);
    }

    /// <summary>Signing in, out, or the access lost: Settings and the player follow.</summary>
    public event Action? Changed;

    public string? ClientId => _store.Settings.SpotifyClientId;

    /// <summary>Signed in with the Client ID that's in Settings now.</summary>
    public bool IsSignedIn => _credential != null && !NeedsReconnect && _credential.ClientId == ClientId;

    public bool NeedsReconnect { get; private set; }

    public string? DisplayName => _credential?.DisplayName;

    public bool IsBusy { get; private set; }

    public async Task SignInAsync(CancellationToken ct)
    {
        string clientId = ClientId ?? throw new SpotifyException(Strings.Spotify_NoClientId);
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/callback";
            string verifier = Pkce.NewVerifier();
            string state = Pkce.NewVerifier();
            string url = AuthorizeUrl + "?" + Form(
                ("client_id", clientId),
                ("response_type", "code"),
                ("redirect_uri", redirect),
                ("scope", string.Join(' ', Scopes)),
                ("code_challenge_method", "S256"),
                ("code_challenge", Pkce.Challenge(verifier)),
                ("state", state));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SignInTimeout);
            string code = await WaitForCodeAsync(listener, state, timeout.Token);

            using var tokens = await PostTokenAsync(ct,
                ("grant_type", "authorization_code"),
                ("code", code),
                ("redirect_uri", redirect),
                ("client_id", clientId),
                ("code_verifier", verifier));
            var root = tokens.RootElement;
            string refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
            if (refresh.Length == 0)
                throw new SpotifyException(Strings.Spotify_Refused);

            _credential = new SpotifyCredential { ClientId = clientId, RefreshToken = refresh };
            NeedsReconnect = false;
            SetAccessToken(root);
            _credential.DisplayName = await ReadDisplayNameAsync(ct);
            SecretFile.Write(_file, _credential);
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Forgets the access here. Spotify has no revoke endpoint: spotify.com/account/apps removes it there.</summary>
    public void SignOut()
    {
        _credential = null;
        _accessToken = null;
        NeedsReconnect = false;
        SecretFile.Delete(_file);
        Changed?.Invoke();
    }

    // ---- Player and library ------------------------------------------------------------------

    /// <summary>The player's state; null when nothing is active on any device.</summary>
    public async Task<SpotifyPlayer?> GetPlayerAsync(CancellationToken ct)
    {
        string? body = await SendAsync(HttpMethod.Get, "/me/player", ct);
        if (string.IsNullOrWhiteSpace(body))
            return null; // 204: no active device.
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string? uri = null, name = null;
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            uri = item.TryGetProperty("uri", out var u) ? u.GetString() : null;
            name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
        }
        return new SpotifyPlayer(
            uri, name,
            root.TryGetProperty("is_playing", out var p) && p.ValueKind == JsonValueKind.True,
            root.TryGetProperty("shuffle_state", out var s) && s.ValueKind == JsonValueKind.True,
            root.TryGetProperty("repeat_state", out var rep) ? rep.GetString() ?? "off" : "off");
    }

    public async Task<bool> IsLikedAsync(string uri, CancellationToken ct)
    {
        string? body = await SendAsync(HttpMethod.Get, "/me/library/contains?uris=" + Uri.EscapeDataString(uri), ct);
        using var json = JsonDocument.Parse(body ?? "[]");
        return json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0 &&
               json.RootElement[0].ValueKind == JsonValueKind.True;
    }

    public Task SetLikedAsync(string uri, bool liked, CancellationToken ct) =>
        SendAsync(liked ? HttpMethod.Put : HttpMethod.Delete, "/me/library?uris=" + Uri.EscapeDataString(uri), ct);

    public Task SetShuffleAsync(bool on, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/me/player/shuffle?state=" + (on ? "true" : "false"), ct);

    /// <summary>"off", "context" (the playlist or album) or "track".</summary>
    public Task SetRepeatAsync(string state, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/me/player/repeat?state=" + state, ct);

    public static string NextRepeat(string state) => state switch
    {
        "off" => "context",
        "context" => "track",
        _ => "off",
    };

    // ---- HTTP ---------------------------------------------------------------------------------

    /// <summary>An API call; a rejected token is renewed once. Returns the body (empty for 204).</summary>
    private async Task<string?> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
            using var request = new HttpRequestMessage(method, ApiUrl + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (method == HttpMethod.Put || method == HttpMethod.Delete)
                request.Content = new StringContent("", Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;

            string body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return body;

            Log.Warn("spotify", $"Spotify respondeu {(int)response.StatusCode} para {method} {path.Split('?')[0]}");
            throw response.StatusCode switch
            {
                // Player calls need Premium; a user not on the app's allowlist is refused too.
                HttpStatusCode.Forbidden => new SpotifyException(Strings.Spotify_Forbidden),
                HttpStatusCode.NotFound => new SpotifyException(Strings.Spotify_NoDevice),
                HttpStatusCode.TooManyRequests => new SpotifyException(Strings.Spotify_TooMany),
                _ => new SpotifyException(string.Format(Strings.Spotify_Failed, (int)response.StatusCode)),
            };
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _accessToken != null && DateTime.UtcNow < _accessExpiresUtc)
            return _accessToken;
        var credential = _credential;
        if (credential == null || NeedsReconnect || credential.ClientId != ClientId)
            throw new SpotifyException(Strings.Spotify_NotConnected, needsReconnect: true);

        try
        {
            using var tokens = await PostTokenAsync(ct,
                ("grant_type", "refresh_token"),
                ("refresh_token", credential.RefreshToken),
                ("client_id", credential.ClientId));
            var root = tokens.RootElement;
            // Spotify may hand out a new refresh token; the old one then stops working.
            if (root.TryGetProperty("refresh_token", out var r) && r.GetString() is { Length: > 0 } rotated &&
                rotated != credential.RefreshToken)
            {
                credential.RefreshToken = rotated;
                SecretFile.Write(_file, credential);
            }
            SetAccessToken(root);
            return _accessToken!;
        }
        catch (TokenEndpointException ex) when (ex.Error is "invalid_grant" or "invalid_client")
        {
            NeedsReconnect = true;
            _accessToken = null;
            Changed?.Invoke();
            throw new SpotifyException(Strings.Spotify_Expired, needsReconnect: true);
        }
        catch (TokenEndpointException ex)
        {
            throw new SpotifyException(string.Format(Strings.Spotify_Failed, ex.Error));
        }
    }

    private void SetAccessToken(JsonElement root)
    {
        _accessToken = root.GetProperty("access_token").GetString();
        int seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int v) ? v : 3600;
        _accessExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
    }

    private async Task<string?> ReadDisplayNameAsync(CancellationToken ct)
    {
        try
        {
            string? body = await SendAsync(HttpMethod.Get, "/me", ct);
            using var json = JsonDocument.Parse(body ?? "{}");
            return json.RootElement.TryGetProperty("display_name", out var n) ? n.GetString() : null;
        }
        catch (Exception ex) when (ex is SpotifyException or HttpRequestException or JsonException)
        {
            return null; // The name is a nicety; the access works without it.
        }
    }

    private static async Task<JsonDocument> PostTokenAsync(CancellationToken ct, params (string Key, string Value)[] fields)
    {
        using var content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
        using var response = await Http.PostAsync(TokenUrl, content, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new SpotifyException(string.Format(Strings.Spotify_Failed, (int)response.StatusCode));
        }
        if (!response.IsSuccessStatusCode)
        {
            string error = json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "" : "";
            json.Dispose();
            throw new TokenEndpointException(error.Length > 0 ? error : $"HTTP {(int)response.StatusCode}");
        }
        return json;
    }

    // ---- The redirect back from the browser ---------------------------------------------------

    private static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync(ct);
            await using var stream = connection.GetStream();
            Dictionary<string, string>? query;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                read.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    query = await ReadQueryAsync(stream, read.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    continue;
                }
            }

            if (query == null || !query.TryGetValue("state", out var got) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(got), Encoding.UTF8.GetBytes(state)))
            {
                await RespondAsync(connection, "404 Not Found", "", ct);
                continue;
            }
            if (query.TryGetValue("code", out var code) && code.Length > 0)
            {
                await RespondAsync(connection, "200 OK", ResultPage(Strings.Spotify_PageConnected, Strings.GoogleAuth_PageConnectedText), ct);
                return code;
            }
            await RespondAsync(connection, "200 OK", ResultPage(Strings.GoogleAuth_PageCancelled, Strings.GoogleAuth_PageCancelledText), ct);
            throw new SpotifyException(query.GetValueOrDefault("error") == "access_denied"
                ? Strings.Spotify_Cancelled
                : Strings.Spotify_Refused);
        }
    }

    private static async Task<Dictionary<string, string>?> ReadQueryAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(ct);
        // Read the headers to the end: closing with unread data resets the connection (blank tab).
        for (int i = 0; i < 100; i++)
        {
            string? header = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(header))
                break;
        }
        // "GET /callback?code=...&state=... HTTP/1.1"
        var parts = requestLine?.Split(' ');
        if (parts is not { Length: 3 } || parts[0] != "GET")
            return null;
        string target = parts[1];
        int q = target.IndexOf('?');
        if ((q < 0 ? target : target[..q]) != "/callback")
            return null;
        return GoogleAuth.ParseRedirect("/" + (q < 0 ? "" : target[q..]));
    }

    private static async Task RespondAsync(TcpClient connection, string status, string html, CancellationToken ct)
    {
        var stream = connection.GetStream();
        byte[] body = Encoding.UTF8.GetBytes(html);
        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
        connection.Client.Shutdown(SocketShutdown.Send);
    }

    private static string ResultPage(string title, string text) =>
        $"<!doctype html><html lang=\"{Lang.Culture.Name}\"><head><meta charset=\"utf-8\"><title>Yata</title>" +
        "<style>body{margin:0;height:100vh;display:grid;place-items:center;background:#000;color:#fff;" +
        "font-family:'Segoe UI Variable Text','Segoe UI',sans-serif}h1{font-size:20px;font-weight:600;margin:0 0 6px}" +
        "p{margin:0;color:#ffffff99;font-size:14px}</style></head>" +
        $"<body><main><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(text)}</p></main></body></html>";

    private static string Form(params (string Key, string Value)[] fields) =>
        string.Join('&', fields.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)));
}

internal sealed class SpotifyCredential
{
    public string ClientId { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string? DisplayName { get; set; }
}
