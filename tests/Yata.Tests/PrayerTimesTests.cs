using Yata.Services;

namespace Yata.Tests;

/// <summary>
/// Prayer times against published values. Unless noted, references are from the Aladhan API (api.aladhan.com/v1/timings),
/// which implements the praytimes.org algorithm, fetched 2026-11-03 for the given coordinates, method and IANA zone.
/// </summary>
public class PrayerTimesTests
{
    private const double BakuLat = 40.41, BakuLon = 49.87;
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Azerbaijan Standard Time");

    private const double ToleranceMinutes = 2;

    private static void AssertTimes(PrayerDay day, params (Prayer Prayer, string At)[] expected)
    {
        var report = new List<string>();
        double worst = 0;
        foreach (var (prayer, at) in expected)
        {
            var want = DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(day.Times.TryGetValue(prayer, out var got), $"{prayer} missing");
            double delta = (got - want).TotalMinutes;
            worst = Math.Max(worst, Math.Abs(delta));
            report.Add($"{prayer} {got:yyyy-MM-dd HH:mm} ({delta:+0;-0;0})");
        }
        Assert.True(worst <= ToleranceMinutes, string.Join(", ", report));
    }

    [Fact]
    public void Baku_Diyanet_MatchesAladhanMethod13()
    {
        // method=13 (Diyanet 18/17, offsets sunrise -7, dhuhr +5, asr +4, maghrib +7), Asia/Baku.
        var day = PrayerCalculator.For(new DateOnly(2026, 3, 15), BakuLat, BakuLon, Baku, PrayerMethod.Turkey);
        AssertTimes(day,
            (Prayer.Fajr, "2026-03-15 05:22"), (Prayer.Sunrise, "2026-03-15 06:46"), (Prayer.Dhuhr, "2026-03-15 12:54"),
            (Prayer.Asr, "2026-03-15 16:14"), (Prayer.Maghrib, "2026-03-15 18:54"), (Prayer.Isha, "2026-03-15 20:13"));
    }

    [Fact]
    public void Baku_Diyanet_MatchesTurkishTimetable()
    {
        // gzt.com/namaz-vakitleri/baku for 3 November 2026 (Turkish Diyanet-style timetable: Ä°msak, GÃ¼neÅŸ, Ã–ÄŸle, Ä°kindi, AkÅŸam, YatsÄ±).
        var day = PrayerCalculator.For(new DateOnly(2026, 11, 3), BakuLat, BakuLon, Baku, PrayerMethod.Turkey);
        AssertTimes(day,
            (Prayer.Fajr, "2026-11-03 05:40"), (Prayer.Sunrise, "2026-11-03 07:05"), (Prayer.Dhuhr, "2026-11-03 12:29"),
            (Prayer.Asr, "2026-11-03 15:17"), (Prayer.Maghrib, "2026-11-03 17:43"), (Prayer.Isha, "2026-11-03 19:03"));
    }

    [Fact]
    public void NewYork_InJuly_UsesDaylightTime()
    {
        // method=2 (ISNA 15/15), America/New_York: EDT, UTC-4.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var day = PrayerCalculator.For(new DateOnly(2026, 7, 15), 40.7128, -74.006, zone, PrayerMethod.Isna);
        AssertTimes(day,
            (Prayer.Fajr, "2026-07-15 04:03"), (Prayer.Sunrise, "2026-07-15 05:38"), (Prayer.Dhuhr, "2026-07-15 13:02"),
            (Prayer.Asr, "2026-07-15 17:01"), (Prayer.Maghrib, "2026-07-15 20:26"), (Prayer.Isha, "2026-07-15 22:01"));
    }

    [Fact]
    public void Makkah_IshaIsNinetyMinutesAfterMaghrib()
    {
        // method=4 (Umm al-Qura 18.5 / 90 min), Asia/Riyadh.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
        var day = PrayerCalculator.For(new DateOnly(2026, 9, 1), 21.4225, 39.8262, zone, PrayerMethod.Makkah);
        AssertTimes(day,
            (Prayer.Fajr, "2026-09-01 04:46"), (Prayer.Sunrise, "2026-09-01 06:04"), (Prayer.Dhuhr, "2026-09-01 12:21"),
            (Prayer.Asr, "2026-09-01 15:46"), (Prayer.Maghrib, "2026-09-01 18:37"), (Prayer.Isha, "2026-09-01 20:07"));
        Assert.Equal(TimeSpan.FromMinutes(90), day.Times[Prayer.Isha] - day.Times[Prayer.Maghrib]);
    }

    [Fact]
    public void Tehran_MaghribIsAfterSunsetByAngle()
    {
        // method=7 (Tehran 17.7 / 14, maghrib 4.5Â°), Asia/Tehran. Aladhan's sunset is 18:12.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        var day = PrayerCalculator.For(new DateOnly(2026, 3, 15), 35.6892, 51.389, zone, PrayerMethod.Tehran);
        AssertTimes(day,
            (Prayer.Fajr, "2026-03-15 04:52"), (Prayer.Sunrise, "2026-03-15 06:16"), (Prayer.Dhuhr, "2026-03-15 12:13"),
            (Prayer.Asr, "2026-03-15 15:36"), (Prayer.Maghrib, "2026-03-15 18:30"), (Prayer.Isha, "2026-03-15 19:17"));
    }

    [Fact]
    public void Karachi_HanafiAsr()
    {
        // method=1 (Karachi 18/18), school=1 (Hanafi), Asia/Karachi.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
        var day = PrayerCalculator.For(new DateOnly(2026, 1, 10), 24.8607, 67.0011, zone, PrayerMethod.Karachi, AsrJuristic.Hanafi);
        AssertTimes(day,
            (Prayer.Fajr, "2026-01-10 05:58"), (Prayer.Sunrise, "2026-01-10 07:18"), (Prayer.Dhuhr, "2026-01-10 12:39"),
            (Prayer.Asr, "2026-01-10 16:24"), (Prayer.Maghrib, "2026-01-10 18:01"), (Prayer.Isha, "2026-01-10 19:21"));
    }

    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    private static readonly DateOnly Midsummer = new(2026, 6, 21);

    [Fact]
    public void Oslo_Midsummer_AngleBased()
    {
        // method=3 (MWL 18/17), latitudeAdjustmentMethod=3 (angle-based), Europe/Oslo. The sun never gets 18Â° low there in June,
        // so Fajr is sunrise - 18/60 of the night and Isha sunset + 17/60 of it, which falls after midnight.
        var day = PrayerCalculator.For(Midsummer, 59.9139, 10.7522, Oslo, PrayerMethod.MuslimWorldLeague);
        AssertTimes(day,
            (Prayer.Fajr, "2026-06-21 02:21"), (Prayer.Sunrise, "2026-06-21 03:54"), (Prayer.Dhuhr, "2026-06-21 13:19"),
            (Prayer.Asr, "2026-06-21 18:00"), (Prayer.Maghrib, "2026-06-21 22:44"), (Prayer.Isha, "2026-06-22 00:12"));
    }

    [Fact]
    public void Oslo_Midsummer_OtherRules()
    {
        var none = PrayerCalculator.For(Midsummer, 59.9139, 10.7522, Oslo, PrayerMethod.MuslimWorldLeague, rule: HighLatitudeRule.None);
        Assert.False(none.Times.ContainsKey(Prayer.Fajr));
        Assert.False(none.Times.ContainsKey(Prayer.Isha));
        Assert.True(none.Times.ContainsKey(Prayer.Dhuhr));

        var middle = PrayerCalculator.For(Midsummer, 59.9139, 10.7522, Oslo, PrayerMethod.MuslimWorldLeague, rule: HighLatitudeRule.MiddleOfNight);
        var night = middle.Times[Prayer.Sunrise].AddDays(1) - middle.Times[Prayer.Maghrib];
        // Both meet in the middle of the night.
        Assert.True(Math.Abs((middle.Times[Prayer.Fajr].AddDays(1) - middle.Times[Prayer.Isha]).TotalMinutes) <= 1);
        Assert.True(Math.Abs((middle.Times[Prayer.Isha] - middle.Times[Prayer.Maghrib] - night / 2).TotalMinutes) <= 2);

        var seventh = PrayerCalculator.For(Midsummer, 59.9139, 10.7522, Oslo, PrayerMethod.MuslimWorldLeague, rule: HighLatitudeRule.OneSeventh);
        Assert.True(Math.Abs((seventh.Times[Prayer.Sunrise] - seventh.Times[Prayer.Fajr] - night / 7).TotalMinutes) <= 2);
    }

    [Fact]
    public void Next_RollsOverToTomorrowsFajrAfterIsha()
    {
        var next = PrayerCalculator.Next(new DateTime(2026, 3, 15, 21, 0, 0), BakuLat, BakuLon, Baku, PrayerMethod.Turkey, AsrJuristic.Standard);
        Assert.NotNull(next);
        Assert.Equal(Prayer.Fajr, next.Value.Prayer);
        Assert.Equal(new DateOnly(2026, 3, 16), DateOnly.FromDateTime(next.Value.At));
        Assert.Equal(PrayerCalculator.For(new DateOnly(2026, 3, 16), BakuLat, BakuLon, Baku, PrayerMethod.Turkey).Times[Prayer.Fajr], next.Value.At);
    }

    [Fact]
    public void Next_SkipsSunrise()
    {
        // Between Fajr (05:22) and sunrise (06:46): the next prayer is Dhuhr.
        var next = PrayerCalculator.Next(new DateTime(2026, 3, 15, 6, 0, 0), BakuLat, BakuLon, Baku, PrayerMethod.Turkey, AsrJuristic.Standard);
        Assert.Equal(Prayer.Dhuhr, next!.Value.Prayer);
    }

    [Fact]
    public void Next_ConvertsUtcNowToTheZone()
    {
        // 12:00 UTC is 16:00 in Baku, just before Asr (16:14).
        var next = PrayerCalculator.Next(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), BakuLat, BakuLon, Baku, PrayerMethod.Turkey, AsrJuristic.Standard);
        Assert.Equal(Prayer.Asr, next!.Value.Prayer);
    }

    [Fact]
    public void Next_FindsYesterdaysIshaAfterMidnight()
    {
        var next = PrayerCalculator.Next(new DateTime(2026, 6, 22, 0, 5, 0), 59.9139, 10.7522, Oslo, PrayerMethod.MuslimWorldLeague, AsrJuristic.Standard);
        Assert.Equal(Prayer.Isha, next!.Value.Prayer);
        Assert.Equal(new DateTime(2026, 6, 22, 0, 12, 0), next.Value.At, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(40.41, 49.87, PrayerMethod.Turkey)]        // Baku
    [InlineData(41.01, 28.98, PrayerMethod.Turkey)]        // Istanbul
    [InlineData(39.93, 32.86, PrayerMethod.Turkey)]        // Ankara
    [InlineData(35.69, 51.39, PrayerMethod.Tehran)]        // Tehran
    [InlineData(38.07, 46.29, PrayerMethod.Tehran)]        // Tabriz
    [InlineData(27.19, 56.27, PrayerMethod.Tehran)]        // Bandar Abbas
    [InlineData(24.86, 67.00, PrayerMethod.Karachi)]       // Karachi
    [InlineData(28.61, 77.21, PrayerMethod.Karachi)]       // Delhi
    [InlineData(23.81, 90.41, PrayerMethod.Karachi)]       // Dhaka
    [InlineData(21.42, 39.83, PrayerMethod.Makkah)]        // Mecca
    [InlineData(24.71, 46.68, PrayerMethod.Makkah)]        // Riyadh
    [InlineData(25.20, 55.27, PrayerMethod.Makkah)]        // Dubai
    [InlineData(29.37, 47.98, PrayerMethod.Makkah)]        // Kuwait City
    [InlineData(30.04, 31.24, PrayerMethod.Egypt)]         // Cairo
    [InlineData(6.52, 3.38, PrayerMethod.Egypt)]           // Lagos
    [InlineData(33.31, 44.37, PrayerMethod.Egypt)]         // Baghdad
    [InlineData(40.71, -74.01, PrayerMethod.Isna)]         // New York
    [InlineData(43.65, -79.38, PrayerMethod.Isna)]         // Toronto
    [InlineData(51.51, -0.13, PrayerMethod.MuslimWorldLeague)]  // London
    [InlineData(-6.21, 106.85, PrayerMethod.MuslimWorldLeague)] // Jakarta
    [InlineData(36.72, -4.42, PrayerMethod.MuslimWorldLeague)]  // MÃ¡laga
    public void DefaultFor_PicksTheRegionalMethod(double lat, double lon, PrayerMethod expected)
        => Assert.Equal(expected, PrayerCalculator.DefaultFor(lat, lon));
}
