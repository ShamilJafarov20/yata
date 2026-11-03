using Yata.Services;

namespace Yata.Tests;

public class ClockInputTests
{
    [Theory]
    [InlineData("10m", 10 * 60, "")]
    [InlineData("pizza 12m", 12 * 60, "pizza")]
    [InlineData("1h30m tea", 90 * 60, "tea")]
    [InlineData("90s", 90, "")]
    [InlineData("25", 25 * 60, "")]
    [InlineData("5 мин чай", 5 * 60, "чай")]
    public void Durations_make_timers(string text, int seconds, string label)
    {
        var timer = Assert.IsType<ClockInput.Timer>(ClockTools.Parse(text));
        Assert.Equal(TimeSpan.FromSeconds(seconds), timer.Duration);
        Assert.Equal(label, timer.Label);
    }

    [Theory]
    [InlineData("7:30", 7, 30, 0, "")]
    [InlineData("6am run", 6, 0, 0, "run")]
    [InlineData("10:15pm", 22, 15, 0, "")]
    [InlineData("07:00 daily", 7, 0, AlarmData.EveryDay, "")]
    [InlineData("gym 18:45 weekdays", 18, 45, AlarmData.Weekdays, "gym")]
    public void Clock_times_make_alarms(string text, int hour, int minute, int days, string label)
    {
        var alarm = Assert.IsType<ClockInput.Alarm>(ClockTools.Parse(text));
        Assert.Equal(new TimeOnly(hour, minute), alarm.Time);
        Assert.Equal(days, alarm.Days);
        Assert.Equal(label, alarm.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("25:99")]
    [InlineData("30h")]
    public void Nonsense_is_refused(string text) => Assert.Null(ClockTools.Parse(text));

    [Fact]
    public void A_one_off_alarm_rings_next_at_its_time()
    {
        var alarm = new AlarmData { Time = "07:30" };
        var now = new DateTime(2026, 11, 3, 8, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 4, 7, 30, 0), alarm.Next(now));
        alarm.Days = AlarmData.Weekdays; // 2026-11-07 is a Saturday
        Assert.Equal(new DateTime(2026, 11, 9, 7, 30, 0), alarm.Next(new DateTime(2026, 11, 6, 9, 0, 0)));
        alarm.Enabled = false;
        Assert.Null(alarm.Next(now));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Formats_countdowns(int seconds, string text) => Assert.Equal(text, ClockTools.Format(TimeSpan.FromSeconds(seconds)));
}

public class GameTrackerTests
{
    [Theory]
    [InlineData("VALORANT-Win64-Shipping", null, "VALORANT")]
    [InlineData("cs2", "Counter-Strike 2", "Counter-Strike 2")]
    [InlineData("RocketLeague", "Epic Games Launcher", "RocketLeague")]
    public void Names_the_game(string process, string? description, string expected) =>
        Assert.Equal(expected, GameTracker.Name(process, description));

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("vlc", true)]
    [InlineData("POWERPNT", true)]
    [InlineData("eldenring", false)]
    public void Browsers_and_players_are_not_games(string process, bool notGame) =>
        Assert.Equal(notGame, GameTracker.IsNotGame(process));

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(134, "2 h 14 min")]
    public void Formats_play_time(int minutes, string text) => Assert.Equal(text, GameTracker.Format(TimeSpan.FromMinutes(minutes)));
}

public class PixelCharacterTests
{
    [Fact]
    public void Every_character_fits_the_raven_grid_and_has_eyes_that_blink()
    {
        Assert.NotEmpty(Yata.Views.PixelCharacter.Ids);
        foreach (string id in Yata.Views.PixelCharacter.Ids)
        {
            int open = 0, closed = 0;
            for (int y = 0; y < Yata.Views.PixelCharacter.Rows; y++)
                for (int x = 0; x < Yata.Views.PixelCharacter.Columns; x++)
                {
                    if (Yata.Views.PixelCharacter.IsOn(id, x, y, blink: false)) open++;
                    if (Yata.Views.PixelCharacter.IsOn(id, x, y, blink: true)) closed++;
                }
            Assert.True(open > 40, $"{id} is nearly empty");
            Assert.NotEqual(open, closed); // the blink changes something
        }
    }

    [Theory]
    [InlineData("cat", "cat")]
    [InlineData("none", "none")]
    [InlineData("spiderman", "yata")]
    [InlineData(null, "yata")]
    public void An_unknown_companion_falls_back_to_Yata(string? saved, string expected)
    {
        var data = new StoreData();
        data.Settings.ClockCompanion = saved!;
        data.Normalize();
        Assert.Equal(expected, data.Settings.ClockCompanion);
    }
}

public class CharacterAnimationTests
{
    [Fact]
    public void Every_pose_of_every_character_is_16_by_14()
    {
        foreach (string id in Yata.Views.PixelCharacter.Ids)
        {
            var sprite = Yata.Views.PixelCharacter.Sprite(id)!;
            foreach (var rows in sprite.Poses.Values.Prepend(sprite.Base))
            {
                Assert.Equal(Yata.Views.PixelCharacter.Rows, rows.Length);
                Assert.All(rows, r => Assert.Equal(Yata.Views.PixelCharacter.Columns, r.Length));
            }
            // Every action points at poses that exist, and something happens when poked.
            Assert.NotNull(sprite.Poke);
            foreach (var action in sprite.Idle.Append(sprite.Poke!))
                Assert.All(action.Steps, step => Assert.True(step.Pose == "base" || sprite.Poses.ContainsKey(step.Pose), $"{id}: {step.Pose}"));
        }
    }

    [Fact]
    public void A_drawing_is_cleaned_to_the_grid()
    {
        var rows = Yata.Views.PixelCharacter.Normalize(new[] { "##x##", "", null!, new string('#', 30) });
        Assert.Equal(14, rows.Length);
        Assert.All(rows, r => Assert.Equal(16, r.Length));
        Assert.Equal("##.##...........", rows[0]);
        Assert.Equal(new string('#', 16), rows[3]);
    }
}

public class SpriteEditorTests
{
    /// <summary>The editor builds with the app's own styles and colors, and saves what was drawn.</summary>
    [Fact]
    public void The_editor_opens_and_saves_a_character()
    {
        Exception? failure = null;
        CustomCharacterData? saved = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                    new Yata.App().InitializeComponent();
                var editing = new CustomCharacterData { Id = "custom-test", Name = "Test", Rows = Enumerable.Repeat("####............", 14).ToList() };
                var editor = new Yata.SpriteEditorWindow(editing);
                var save = typeof(Yata.SpriteEditorWindow).GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                save.Invoke(editor, null);
                saved = editor.Result;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.NotNull(saved);
        Assert.Equal("custom-test", saved!.Id);
        Assert.Equal("####............", saved.Rows[0]);
    }
}

public class ChoiceMenuTests
{
    /// <summary>
    /// The settings menus give every entry the MenuItem style. A plain Separator among them made WPF throw and
    /// closed the app (the clock companion menu); the one SettingsWindow uses brings its own style.
    /// </summary>
    [Fact]
    public void The_companion_menu_divider_survives_the_menu_item_style()
    {
        Exception? plain = null, ours = null;
        var thread = new Thread(() =>
        {
            plain = Measure(new System.Windows.Controls.Separator());
            ours = Measure(Yata.SettingsWindow.MenuDivider());
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.NotNull(plain); // The crash, reproduced.
        Assert.Null(ours);
    }

    private static Exception? Measure(System.Windows.Controls.Separator divider)
    {
        try
        {
            var menu = new System.Windows.Controls.ItemsControl
            {
                ItemContainerStyle = new System.Windows.Style(typeof(System.Windows.Controls.MenuItem)),
            };
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Cat" });
            menu.Items.Add(divider);
            menu.ApplyTemplate();
            menu.Measure(new System.Windows.Size(300, 300));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
