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
