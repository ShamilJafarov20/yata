using System.IO;
using System.Text;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Reading a toast's title and body out of its XML.</summary>
public class ToastPayloadTests
{
    [Fact]
    public void ToastGeneric_takes_the_first_text_as_title_and_the_rest_as_body()
    {
        var parsed = ToastPayload.Parse("""
            <toast launch="x"><visual><binding template="ToastGeneric">
              <text>Ana</text><text>are you coming?</text><text>bring the cake</text>
              <image src="a.png"/>
            </binding></visual></toast>
            """);

        Assert.Equal(("Ana", "are you coming?\nbring the cake"), parsed);
    }

    [Fact]
    public void A_title_alone_has_no_body()
    {
        Assert.Equal(("Incoming call", (string?)null),
            ToastPayload.Parse("<toast><visual><binding template='ToastGeneric'><text>Incoming call</text></binding></visual></toast>"));
    }

    [Fact]
    public void Attribution_and_empty_texts_are_left_out()
    {
        var parsed = ToastPayload.Parse("""
            <toast><visual><binding template="ToastGeneric">
              <text>  </text><text>Ana</text><text>hi</text><text placement="attribution">via SMS</text>
            </binding></visual></toast>
            """);

        Assert.Equal(("Ana", "hi"), parsed);
    }

    [Fact]
    public void Entities_are_decoded()
    {
        Assert.Equal(("Tom & Jerry", "1 < 2"),
            ToastPayload.Parse("<toast><visual><binding template=\"ToastGeneric\"><text>Tom &amp; Jerry</text><text>1 &lt; 2</text></binding></visual></toast>"));
    }

    [Fact]
    public void The_generic_binding_wins_over_a_legacy_fallback()
    {
        var parsed = ToastPayload.Parse("""
            <toast><visual>
              <binding template="ToastText02"><text id="1">old</text><text id="2">old body</text></binding>
              <binding template="ToastGeneric"><text>new</text><text>new body</text></binding>
            </visual></toast>
            """);

        Assert.Equal(("new", "new body"), parsed);
    }

    [Theory]
    [InlineData("ToastText02")]
    [InlineData("ToastText04")]
    [InlineData("ToastImageAndText02")]
    public void Legacy_templates_with_a_heading_give_title_and_body(string template)
    {
        Assert.Equal(("Heading", "Line"),
            ToastPayload.Parse($"<toast><visual><binding template=\"{template}\"><text id=\"1\">Heading</text><text id=\"2\">Line</text></binding></visual></toast>"));
    }

    [Theory]
    [InlineData("ToastText01")]
    [InlineData("ToastImageAndText01")]
    public void Legacy_single_text_templates_are_body_only(string template)
    {
        Assert.Equal(((string?)null, "Just one line"),
            ToastPayload.Parse($"<toast><visual><binding template=\"{template}\"><text id=\"1\">Just one line</text></binding></visual></toast>"));
    }

    [Fact]
    public void Bytes_in_utf8_with_or_without_a_bom_and_utf16_all_read()
    {
        const string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>Olá</text><text>tudo bem?</text></binding></visual></toast>";
        var expected = ("Olá", "tudo bem?");

        Assert.Equal(expected, ToastPayload.Parse(Encoding.UTF8.GetBytes(xml)));
        Assert.Equal(expected, ToastPayload.Parse(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(xml)).ToArray()));
        Assert.Equal(expected, ToastPayload.Parse(Encoding.Unicode.GetBytes(xml)));
        Assert.Equal(expected, ToastPayload.Parse(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray()));
        Assert.Equal(expected, ToastPayload.Parse(Encoding.UTF8.GetBytes(xml + "\0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<toast><visual><binding template=\"ToastGeneric\"><text>cut short")]
    [InlineData("<tile><visual><binding template=\"TileSmall\"><text>Tile</text></binding></visual></tile>")]
    [InlineData("<badge value=\"3\"/>")]
    [InlineData("<toast><visual><binding template=\"ToastGeneric\"><image src=\"a.png\"/></binding></visual></toast>")]
    [InlineData("<toast><visual/></toast>")]
    [InlineData("<toast/>")]
    [InlineData("<!DOCTYPE toast [<!ENTITY x \"boom\">]><toast><visual><binding template=\"ToastGeneric\"><text>&x;</text></binding></visual></toast>")]
    public void Anything_that_isnt_a_readable_toast_is_null(string xml)
    {
        Assert.Null(ToastPayload.Parse(xml));
    }

    [Fact]
    public void Garbage_bytes_are_null()
    {
        Assert.Null(ToastPayload.Parse((byte[]?)null));
        Assert.Null(ToastPayload.Parse(Array.Empty<byte>()));
        Assert.Null(ToastPayload.Parse(new byte[] { 0x00, 0xFF, 0x13, 0x37, 0x80, 0x81 }));
    }
}

/// <summary>The name shown for the app behind an AUMID.</summary>
public class AppNamesTests
{
    [Theory]
    [InlineData("Microsoft.YourPhone_8wekyb3d8bbwe!App", "Phone Link")]
    [InlineData("MicrosoftWindows.CrossDevice_cw5n1h2txyewy!App", "Phone Link")]
    [InlineData("TelegramDesktop", "Telegram")]
    [InlineData("Telegram.TelegramDesktop", "Telegram")]
    [InlineData("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store", "Telegram")]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "WhatsApp")]
    [InlineData("com.squirrel.Discord.Discord", "Discord")]
    [InlineData("MSTeams_8wekyb3d8bbwe!MSTeams", "Teams")]
    public void Known_apps_get_their_product_name(string aumid, string name)
    {
        Assert.Equal(name, AppNames.Friendly(aumid));
    }

    [Theory]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Windows Terminal")]
    [InlineData("Microsoft.ScreenSketch_8wekyb3d8bbwe!App", "Screen Sketch")]
    [InlineData("Notion_8wekyb3d8bbwe!Notion", "Notion")]
    [InlineData("AppleInc.iCloud_nzyj5cx40ttqa!iCloud", "iCloud")]
    [InlineData("Spotify.desktop.client", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("OperaSoftware.OperaGXWebBrowser.1712506900", "Opera GX Web Browser")]
    [InlineData("ChemTableSoftware.Reg_Organizer.RegOrganizer.exe", "Reg Organizer")]
    [InlineData("com.squirrel.slack.slack", "Slack")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe", "Powershell")]
    [InlineData(@"C:\Program Files\Some Tool\SomeTool.exe", "Some Tool")]
    public void Other_apps_get_a_name_cleaned_up_from_the_aumid(string aumid, string name)
    {
        Assert.Equal(name, AppNames.Friendly(aumid));
    }

    [Theory]
    [InlineData("Windows.SystemToast.SecurityAndMaintenance")]
    [InlineData("Microsoft.Windows.InputSwitchToastHandler")]
    [InlineData("NotifyIconGeneratedAumid_10013812512636380972")]
    public void System_toasts_are_windows(string aumid)
    {
        Assert.Equal("Windows", AppNames.Friendly(aumid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("!")]
    public void Nothing_to_go_on_is_app(string aumid)
    {
        Assert.Equal("App", AppNames.Friendly(aumid));
    }
}

/// <summary>
/// Polling a stand-in for Windows' notification store: a synthetic SQLite file with the same tables,
/// written in WAL mode by a separate connection while the mirror reads it, like Windows does.
/// </summary>
public sealed class NotificationMirrorTests : IDisposable
{
    private const string Telegram = "Telegram.TelegramDesktop";
    private const string Explorer = "Windows.SystemToast.Explorer";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));
    private readonly string _db;
    private readonly List<NotificationMirror> _mirrors = new();
    private SqliteConnection? _writer;
    private long _nextId = 100;

    public NotificationMirrorTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "wpndatabase.db");
    }

    public void Dispose()
    {
        foreach (var mirror in _mirrors)
            mirror.Dispose();
        _writer?.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; whatever is left goes with the next cleanup.
        }
    }

    private void CreateStore()
    {
        _writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        _writer.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE [NotificationHandler] ([RecordId] INTEGER PRIMARY KEY, [PrimaryId] TEXT NOT NULL COLLATE NOCASE, [HandlerType] TEXT);
            CREATE TABLE [Notification] ([Order] INTEGER NOT NULL PRIMARY KEY, [Id] INTEGER NOT NULL, [HandlerId] INTEGER,
                [Type] TEXT NOT NULL, [Payload] BLOB, [ArrivalTime] INT64, [PayloadType] TEXT NOT NULL, UNIQUE([Id]) ON CONFLICT REPLACE);
            INSERT INTO NotificationHandler (RecordId, PrimaryId, HandlerType) VALUES (1, 'Telegram.TelegramDesktop', 'app:desktop');
            INSERT INTO NotificationHandler (RecordId, PrimaryId, HandlerType) VALUES (2, 'Windows.SystemToast.Explorer', 'app:system');
            """);
    }

    private void Execute(string sql)
    {
        using var command = _writer!.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Add(string app, string title, string body, string type = "toast", DateTime? arrived = null, long? id = null)
    {
        long rowId = id ?? _nextId++;
        string xml = type == "toast"
            ? $"<toast><visual><binding template=\"ToastGeneric\"><text>{title}</text><text>{body}</text></binding></visual></toast>"
            : "<badge value=\"1\"/>";
        using var command = _writer!.CreateCommand();
        command.CommandText = """
            INSERT INTO Notification (Id, HandlerId, Type, Payload, ArrivalTime, PayloadType)
            VALUES ($id, (SELECT RecordId FROM NotificationHandler WHERE PrimaryId = $app), $type, $payload, $arrival, 'Xml')
            """;
        command.Parameters.AddWithValue("$id", rowId);
        command.Parameters.AddWithValue("$app", app);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$payload", Encoding.UTF8.GetBytes(xml));
        command.Parameters.AddWithValue("$arrival", (arrived ?? DateTime.Now).ToFileTimeUtc());
        command.ExecuteNonQuery();
        return rowId;
    }

    private NotificationMirror Mirror(TimeSpan? interval = null)
    {
        var mirror = new NotificationMirror(Dispatcher.CurrentDispatcher, _db, interval);
        _mirrors.Add(mirror);
        return mirror;
    }

    [Fact]
    public void What_was_already_there_isnt_replayed_and_new_toasts_are()
    {
        CreateStore();
        Add(Telegram, "Old", "already seen");
        var mirror = Mirror();

        Assert.Empty(mirror.Poll()); // Notes where the store stands.
        var arrived = new DateTime(2026, 11, 3, 18, 30, 15, DateTimeKind.Local);
        long id = Add(Telegram, "Ana", "are you coming?", arrived: arrived);
        var found = Assert.Single(mirror.Poll());

        Assert.Equal(new MirroredNotification(id, Telegram, "Telegram", "Ana", "are you coming?", arrived), found);
        Assert.Empty(mirror.Poll());
    }

    [Fact]
    public void Only_toasts_from_allowed_apps_come_through()
    {
        CreateStore();
        var mirror = Mirror();
        mirror.Allowed = aumid => aumid == Telegram;
        mirror.Poll();

        Add(Explorer, "Copying", "3 files");
        Add(Telegram, "Badge", "", type: "badge");
        Add(Telegram, "Ana", "hi");

        Assert.Equal(new[] { "Ana" }, mirror.Poll().Select(n => n.Title));
    }

    [Fact]
    public void A_filter_that_throws_counts_as_not_allowed()
    {
        CreateStore();
        var mirror = Mirror();
        mirror.Allowed = _ => throw new InvalidOperationException("filter broke");
        mirror.Poll();
        Add(Telegram, "Ana", "hi");

        Assert.Empty(mirror.Poll());
    }

    [Fact]
    public void A_burst_comes_through_as_its_newest_ten_oldest_first()
    {
        CreateStore();
        var mirror = Mirror();
        mirror.Poll();
        for (int i = 1; i <= 15; i++)
            Add(Telegram, $"#{i}", "msg");

        Assert.Equal(Enumerable.Range(6, 10).Select(i => $"#{i}"), mirror.Poll().Select(n => n.Title));
        Assert.Empty(mirror.Poll());
    }

    [Fact]
    public void Dismissing_the_newest_toasts_doesnt_hide_the_next_ones()
    {
        CreateStore();
        Add(Telegram, "Old", "kept");
        var mirror = Mirror();
        mirror.Poll();
        Add(Telegram, "A", "1", id: 500);
        Assert.Single(mirror.Poll());

        Execute("DELETE FROM Notification WHERE Id = 500");
        Assert.Empty(mirror.Poll());
        Add(Telegram, "B", "2", id: 300); // Below the old mark: the store was reset, say.

        Assert.Equal(new[] { "B" }, mirror.Poll().Select(n => n.Title));
    }

    [Fact]
    public void Unreadable_payloads_are_skipped()
    {
        CreateStore();
        var mirror = Mirror();
        mirror.Poll();
        Execute("INSERT INTO Notification (Id, HandlerId, Type, Payload, ArrivalTime, PayloadType) VALUES (900, 1, 'toast', X'00FF1337', 0, 'Xml')");
        Execute("INSERT INTO Notification (Id, HandlerId, Type, Payload, ArrivalTime, PayloadType) VALUES (901, 1, 'toast', NULL, NULL, 'Xml')");
        long id = Add(Telegram, "Ana", "hi");

        Assert.Equal(new[] { id }, mirror.Poll().Select(n => n.Id));
    }

    [Fact]
    public void A_missing_store_is_waited_for_without_throwing_or_replaying()
    {
        var mirror = Mirror();
        Assert.Empty(mirror.Poll());
        Assert.Empty(mirror.Poll());

        CreateStore();
        Add(Telegram, "Before", "the first good poll");
        Assert.Empty(mirror.Poll()); // The first poll that works only notes where the store stands.
        Add(Telegram, "Ana", "hi");

        Assert.Equal(new[] { "Ana" }, mirror.Poll().Select(n => n.Title));
    }

    [Fact]
    public void A_damaged_store_doesnt_throw()
    {
        File.WriteAllText(_db, "this is not a database");
        var mirror = Mirror();

        Assert.Empty(mirror.Poll());
        Assert.Empty(mirror.Poll());
    }

    [Fact]
    public void Started_it_raises_new_toasts_on_the_dispatcher()
    {
        CreateStore();
        Add(Telegram, "Old", "already seen");
        var dispatcher = Dispatcher.CurrentDispatcher;
        var mirror = Mirror(TimeSpan.FromMilliseconds(50));
        var received = new List<(MirroredNotification Notification, int Thread)>();
        mirror.Arrived += n => received.Add((n, Environment.CurrentManagedThreadId));

        mirror.Start();
        Pump(dispatcher, TimeSpan.FromMilliseconds(300), () => false); // Lets the first poll note the old one.
        Add(Telegram, "Ana", "hi");
        Pump(dispatcher, TimeSpan.FromSeconds(5), () => received.Count > 0);
        mirror.Stop();

        var (notification, thread) = Assert.Single(received);
        Assert.Equal("Ana", notification.Title);
        Assert.Equal(dispatcher.Thread.ManagedThreadId, thread);
    }

    [Fact]
    public void Stopped_it_lets_go_of_the_file()
    {
        CreateStore();
        var mirror = Mirror(TimeSpan.FromMilliseconds(20));
        mirror.Start();
        Thread.Sleep(200);
        mirror.Stop();
        _writer!.Dispose();
        _writer = null;

        File.Delete(_db); // Would fail with the file still open.
        Assert.False(File.Exists(_db));
    }

    private static void Pump(Dispatcher dispatcher, TimeSpan timeout, Func<bool> done)
    {
        var until = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
}
