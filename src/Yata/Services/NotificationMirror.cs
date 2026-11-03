using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>A Windows toast another app showed, as read back from Windows' notification store.</summary>
public sealed record MirroredNotification(long Id, string AppId, string AppName, string? Title, string? Body, DateTime ArrivedLocal);

/// <summary>
/// Mirrors the toasts other apps show (Phone Link, Telegram, WhatsApp…) so the notch can echo them.
/// Windows' own listener (UserNotificationListener) needs package identity, which Yata doesn't have,
/// so this reads Windows' own store instead: %LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db,
/// a SQLite database in WAL mode that belongs to the current user. It is only ever opened read-only,
/// and nothing read from it is kept or logged: each toast goes to <see cref="Arrived"/> and is gone.
/// </summary>
internal sealed class NotificationMirror : IDisposable
{
    private const string Area = "notifications";
    // A burst (Phone Link reconnecting, say) shows the newest few, not a minute of notch.
    private const int MaxPerPoll = 10;
    // The connection stays open between polls (a read-only WAL reader sees new rows right away), but
    // is renewed now and then, in case Windows ever recreates the file under the old handle.
    private static readonly TimeSpan Renew = TimeSpan.FromMinutes(10);

    private readonly Dispatcher _dispatcher;
    private readonly string _path;
    private readonly TimeSpan _interval;
    // Held by a poll and by Start/Stop: one poll at a time, and none against a closed connection.
    private readonly object _gate = new();
    private Timer? _timer;
    private int _generation;
    private SqliteConnection? _connection;
    private DateTime _openedAt;
    private long _lastSeenId;
    private bool _primed;
    private string? _failure;
    private bool _disposed;

    /// <summary>A new toast from an allowed app, raised on the dispatcher given to the constructor.</summary>
    public event Action<MirroredNotification>? Arrived;

    /// <summary>
    /// Which apps (by AUMID, e.g. "Microsoft.YourPhone_8wekyb3d8bbwe!App") are mirrored. Called on the
    /// polling thread, so it should only read. Everything passes until the caller sets it.
    /// </summary>
    public Func<string, bool> Allowed { get; set; } = _ => true;

    public NotificationMirror(Dispatcher dispatcher, string? databasePath = null, TimeSpan? interval = null)
    {
        _dispatcher = dispatcher;
        _path = databasePath ?? DefaultPath;
        _interval = interval ?? TimeSpan.FromSeconds(3);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    /// <summary>
    /// Starts polling. What is already in the store isn't replayed: the first poll only notes where
    /// it stands. It runs on the timer thread, so Start never touches the disk itself.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer != null)
                return;
            _primed = false;
            int generation = ++_generation;
            _timer = new Timer(_ => Tick(generation), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Stops polling and lets go of the file. Waits for a poll in progress (milliseconds).</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
            Close();
            // Started again later, it begins from that moment: what came while off isn't replayed.
            _primed = false;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
            _disposed = true;
    }

    private void Tick(int generation)
    {
        IReadOnlyList<MirroredNotification> found;
        lock (_gate)
        {
            if (generation != _generation)
                return;
            found = Poll();
            // One-shot timer, armed again after each poll: a slow one never overlaps the next.
            _timer?.Change(_interval, Timeout.InfiniteTimeSpan);
        }

        foreach (var notification in found)
            _dispatcher.Post(Area, () => Arrived?.Invoke(notification));
    }

    /// <summary>
    /// One poll: the new toasts from allowed apps, oldest first. Never throws; a failure (store busy,
    /// missing, damaged) is logged once until a poll works again, and the next poll retries.
    /// </summary>
    internal IReadOnlyList<MirroredNotification> Poll()
    {
        try
        {
            var found = Read();
            if (_failure != null)
            {
                _failure = null;
                Log.Info(Area, "Voltou a ler as notificações do Windows");
            }
            return found;
        }
        catch (Exception ex)
        {
            Close();
            Report(ex);
            return Array.Empty<MirroredNotification>();
        }
    }

    private IReadOnlyList<MirroredNotification> Read()
    {
        var connection = Connection();
        if (!_primed)
        {
            _lastSeenId = MaxId(connection) ?? 0;
            _primed = true;
            return Array.Empty<MirroredNotification>();
        }

        var rows = new List<(long Id, string AppId, object Payload, long Arrival)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT n.Id, h.PrimaryId, n.Payload, n.ArrivalTime
                FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
                WHERE n.Type = 'toast' AND n.Id > $last
                ORDER BY n.Id DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$last", _lastSeenId);
            command.Parameters.AddWithValue("$limit", MaxPerPoll);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetValue(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
            }
        }

        if (rows.Count == 0)
        {
            // Dismissed toasts leave the table; if the newest ones went (or the store was reset), step
            // back to what's there, so the next new Id is seen even if it isn't above the old mark.
            long max = MaxId(connection) ?? 0;
            if (max < _lastSeenId)
                _lastSeenId = max;
            return Array.Empty<MirroredNotification>();
        }

        _lastSeenId = rows[0].Id;
        var found = new List<MirroredNotification>();
        foreach (var row in Enumerable.Reverse(rows))
        {
            if (!IsAllowed(row.AppId))
                continue;
            var text = row.Payload switch
            {
                byte[] bytes => ToastPayload.Parse(bytes),
                string xml => ToastPayload.Parse(xml),
                _ => null,
            };
            if (text is not { } parsed)
                continue;
            found.Add(new MirroredNotification(row.Id, row.AppId, AppNames.Friendly(row.AppId),
                parsed.Title, parsed.Body, ArrivedLocal(row.Arrival)));
        }
        return found;
    }

    private bool IsAllowed(string appId)
    {
        try
        {
            return Allowed(appId);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private SqliteConnection Connection()
    {
        if (_connection != null && DateTime.UtcNow - _openedAt < Renew)
            return _connection;
        Close();
        if (!File.Exists(_path))
            throw new FileNotFoundException("Banco de notificações do Windows não encontrado");

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            // Closed means closed: no pooled handle keeping Windows' file open after Stop.
            Pooling = false,
            // Seconds Microsoft.Data.Sqlite keeps retrying SQLITE_BUSY; short, the next tick retries anyway.
            DefaultTimeout = 1,
        }.ToString());
        try
        {
            connection.Open();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        _connection = connection;
        _openedAt = DateTime.UtcNow;
        return connection;
    }

    private static long? MaxId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(Id) FROM Notification";
        return command.ExecuteScalar() is long max ? max : null;
    }

    private void Close()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private void Report(Exception error)
    {
        string kind = error switch
        {
            SqliteException sqlite => "sqlite:" + sqlite.SqliteErrorCode.ToString(CultureInfo.InvariantCulture),
            _ => error.GetType().FullName ?? "?",
        };
        if (kind == _failure)
            return; // Same trouble as the last poll; it was logged then.
        _failure = kind;

        string message = error switch
        {
            FileNotFoundException => "Banco de notificações do Windows não encontrado; tenta de novo depois",
            SqliteException { SqliteErrorCode: 5 or 6 } => "Banco de notificações do Windows ocupado; tenta de novo depois",
            _ => "Falha ao ler as notificações do Windows; tenta de novo depois",
        };
        Log.Warn(Area, message, error);
    }

    private static DateTime ArrivedLocal(long fileTime)
    {
        try
        {
            if (fileTime > 0)
                return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Not a time; when it was read is close enough.
        }
        return DateTime.Now;
    }
}

/// <summary>Title and body out of a toast's XML.</summary>
internal static class ToastPayload
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1 << 20,
    };

    // Legacy single-text templates (ToastText01, ToastImageAndText01): that one text is the body.
    private static readonly Regex SingleTextTemplate = new(@"^Toast(?:ImageAnd)?Text01$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The payload as stored (UTF-8, or UTF-16 with or without a BOM); null if it isn't a readable toast.</summary>
    public static (string? Title, string? Body)? Parse(byte[]? payload)
    {
        if (payload == null || payload.Length == 0)
            return null;
        return Parse(Decode(payload));
    }

    /// <summary>
    /// ToastGeneric: the first text is the title, the others the body. Legacy ToastText0x templates the
    /// same, except the single-text ones (body only). Attribution text ("via …") is left out.
    /// </summary>
    public static (string? Title, string? Body)? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XElement root;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Settings);
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
        if (root.Name.LocalName != "toast")
            return null;

        var bindings = root.Elements().Where(e => e.Name.LocalName == "visual")
            .SelectMany(v => v.Elements().Where(e => e.Name.LocalName == "binding"))
            .ToList();
        var binding = bindings.FirstOrDefault(b => string.Equals((string?)b.Attribute("template"), "ToastGeneric", StringComparison.OrdinalIgnoreCase))
            ?? bindings.FirstOrDefault();
        if (binding == null)
            return null;

        var texts = binding.Descendants()
            .Where(e => e.Name.LocalName == "text"
                && !string.Equals((string?)e.Attribute("placement"), "attribution", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (texts.Count == 0)
            return null;

        if (SingleTextTemplate.IsMatch((string?)binding.Attribute("template") ?? ""))
            return (null, string.Join("\n", texts));
        return (texts[0], texts.Count > 1 ? string.Join("\n", texts.Skip(1)) : null);
    }

    private static string Decode(byte[] bytes)
    {
        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] != 0 && bytes[1] == 0)
            text = Encoding.Unicode.GetString(bytes); // "<\0t\0…": UTF-16 without a BOM.
        else
            text = Encoding.UTF8.GetString(bytes);
        return text.TrimEnd('\0');
    }
}

/// <summary>A name to show for the app behind an AUMID.</summary>
internal static class AppNames
{
    private static readonly (string Part, string Name)[] Known =
    {
        ("YourPhone", "Phone Link"),
        ("CrossDevice", "Phone Link"),
        ("Telegram", "Telegram"),
        ("WhatsApp", "WhatsApp"),
        ("Discord", "Discord"),
        ("MSTeams", "Teams"),
        ("Microsoft.Teams", "Teams"),
    };

    // Pieces of an AUMID that say nothing about the app.
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "io", "app", "desktop", "client", "exe", "beta", "squirrel", "systray",
        "microsoft", "microsoftwindows", "microsoftcorporationii",
    };

    private static readonly Regex Hex = new("^[0-9A-Fa-f]+$", RegexOptions.CultureInvariant);
    // "WindowsTerminal" → "Windows Terminal", "OperaGXWebBrowser" → "Opera GX Web Browser"; "iCloud" stays.
    private static readonly Regex WordBreak = new(@"(?<=\w[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant);

    public static string Friendly(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return "App";
        aumid = aumid.Trim();

        foreach (var (part, name) in Known)
        {
            if (aumid.Contains(part, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        if (aumid.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase)
            || (aumid.StartsWith("Microsoft.Windows.", StringComparison.OrdinalIgnoreCase) && !aumid.Contains('!'))
            || aumid.StartsWith("NotifyIconGeneratedAumid_", StringComparison.OrdinalIgnoreCase))
            return "Windows";

        string label;
        if (aumid.Contains('\\') || aumid.Contains('/'))
        {
            // "{GUID}\WindowsPowerShell\v1.0\powershell.exe" or a full path: the program's name.
            label = Path.GetFileNameWithoutExtension(aumid.Replace('/', '\\').Split('\\')[^1]);
        }
        else
        {
            // "Publisher.Name_hash!AppId" (packaged) or "Company.Product.Thing" (desktop).
            string family = aumid.Split('!')[0];
            int hash = family.LastIndexOf('_');
            if (hash > 0 && aumid.Contains('!'))
                family = family[..hash];
            label = family.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !Filler.Contains(p) && !(Hex.IsMatch(p) && p.Any(char.IsDigit)))
                .LastOrDefault() ?? family;
        }

        label = WordBreak.Replace(label.Replace('_', ' ').Replace('-', ' '), " ").Trim();
        if (label.Length == 0)
            return "App";
        if (label.All(c => !char.IsLetter(c) || char.IsLower(c)))
            label = char.ToUpperInvariant(label[0]) + label[1..];
        return label;
    }
}
