using System;
using System.Collections.Generic;

namespace Yata.Services;

public enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }

/// <summary>Calculation conventions (twilight angles) as in praytimes.org, plus Turkey's Diyanet.</summary>
public enum PrayerMethod { MuslimWorldLeague, Isna, Egypt, Makkah, Karachi, Tehran, Jafari, Turkey }

/// <summary>Asr starts when an object's shadow is 1× (Standard: Shafi'i, Maliki, Hanbali) or 2× (Hanafi) its length, plus the noon shadow.</summary>
public enum AsrJuristic { Standard, Hanafi }

/// <summary>
/// How Fajr and Isha are bounded where twilight lasts all night (or nearly).
/// Each caps the time between Fajr and sunrise (and sunset and Isha) at a portion of the night:
/// AngleBased angle/60, MiddleOfNight 1/2, OneSeventh 1/7. None leaves them unbounded (and missing when the sun never gets that low).
/// </summary>
public enum HighLatitudeRule { None, AngleBased, MiddleOfNight, OneSeventh }

/// <param name="Times">Wall-clock times in the requested zone, rounded to the minute. A prayer the sun never reaches
/// (polar day or night, or Fajr/Isha with <see cref="HighLatitudeRule.None"/>) is left out.</param>
public sealed record PrayerDay(DateOnly Date, IReadOnlyDictionary<Prayer, DateTime> Times);

/// <summary>
/// Prayer times computed locally, with the algorithm of praytimes.org's PrayTimes.js (v2.3): sun declination and
/// equation of time from the Julian day, then the hour angle at which the sun reaches each prayer's altitude.
/// Pure, no network.
/// </summary>
public static class PrayerCalculator
{
    /// <summary>
    /// Angles in degrees below the horizon; IshaMinutes &gt; 0 means Isha is that long after Maghrib instead.
    /// MaghribAngle 0 means Maghrib is sunset. Offsets are minutes added at the end (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha).
    /// </summary>
    private sealed record Parameters(double Fajr, double Isha, double IshaMinutes = 0, double MaghribAngle = 0, int[]? Offsets = null);

    private static Parameters ParametersFor(PrayerMethod method) => method switch
    {
        PrayerMethod.Isna => new(15, 15),
        PrayerMethod.Egypt => new(19.5, 17.5),
        // Umm al-Qura. The 120 minutes in Ramadan isn't applied.
        PrayerMethod.Makkah => new(18.5, 0, IshaMinutes: 90),
        PrayerMethod.Karachi => new(18, 18),
        PrayerMethod.Tehran => new(17.7, 14, MaghribAngle: 4.5),
        PrayerMethod.Jafari => new(16, 14, MaghribAngle: 4),
        // Diyanet: 18/17 with its "temkin" safety margins, as in the adhan libraries (batoulapps) and Aladhan's method 13:
        // sunrise -7, dhuhr +5, asr +4, maghrib +7 minutes.
        PrayerMethod.Turkey => new(18, 17, Offsets: [0, -7, 5, 4, 7, 0]),
        _ => new(18, 17), // Muslim World League
    };

    /// <summary>
    /// A rough regional default from coordinates, by simple boxes checked in order (borders are approximate):
    /// Iran → Tehran; Turkey, Azerbaijan and the south Caucasus → Turkey (Diyanet); Arabian Peninsula → Makkah;
    /// Pakistan, India, Bangladesh, Afghanistan → Karachi; North America → ISNA; Africa and the Levant/Iraq → Egypt; else MWL.
    /// </summary>
    public static PrayerMethod DefaultFor(double latitude, double longitude)
    {
        double lat = latitude, lon = longitude;

        // Iran: west border steps east going south, north border steps south going east, the Gulf coast is a diagonal.
        double west = lat >= 37 ? 44.4 : lat >= 34 ? 45.5 : 47.5;
        double north = lon < 48 ? 39.0 : lon < 54 ? 38.4 : 37.4;
        double gulf = lon <= 48 ? 30 : 30 - 0.4 * (lon - 48);
        if (lat > gulf && lat < north && lon > west && lon < 61.3)
            return PrayerMethod.Tehran;

        // Turkey and Azerbaijan; south of ~36.8° east of 37°E is Syria/Iraq.
        if (lat < 42.3 && lon > 25.6 && lon < 50.9 && lat > (lon < 37 ? 36.0 : 36.8))
            return PrayerMethod.Turkey;

        // Arabian Peninsula: east of the Red Sea (a diagonal from Aqaba to Bab el-Mandeb), north edge at Kuwait/Jordan.
        double redSea = Math.Max(34.5, 34.5 + 0.571 * (28 - lat));
        if (lat > 12 && lat < (lon > 46 ? 29.5 : 31.5) && lon > redSea && lon < 60)
            return PrayerMethod.Makkah;

        if (lat > 0 && lat < 37.5 && lon > 60.5 && lon < 97.5)
            return PrayerMethod.Karachi;

        if (lat > 14 && lat < 72 && lon > -170 && lon < -50)
            return PrayerMethod.Isna;

        // Africa plus the Levant and Iraq, without southern Spain, Sicily, Malta and Crete.
        double africaNorth = lon < -2 ? 35.9 : lon < 11.5 ? 37.4 : lon < 27 ? 33.5 : 37.5;
        if (lat > -35 && lat < africaNorth && lon > -26 && lon < 52)
            return PrayerMethod.Egypt;

        return PrayerMethod.MuslimWorldLeague;
    }

    public static PrayerDay For(DateOnly date, double latitude, double longitude, TimeZoneInfo zone, PrayerMethod method,
        AsrJuristic asr = AsrJuristic.Standard, HighLatitudeRule rule = HighLatitudeRule.AngleBased)
    {
        var p = ParametersFor(method);
        double lat = latitude;

        // The solar day whose prayers fall on this local date: the same one, unless the zone is a day away from the
        // sun (e.g. Kiribati at UTC+14 on 157°W).
        double offset = zone.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0))).TotalHours;
        var solarDate = date.AddDays(-(int)Math.Round((offset - longitude / 15) / 24));
        double jd = Julian(solarDate) - longitude / (15 * 24);

        // One refinement from rough guesses, in local solar hours, as PrayTimes.js does.
        double fajr = SunAngleTime(jd, lat, p.Fajr, 5, ccw: true);
        double sunrise = SunAngleTime(jd, lat, RiseSetAngle, 6, ccw: true);
        double dhuhr = MidDay(jd, 12);
        double asrTime = AsrTime(jd, lat, asr == AsrJuristic.Hanafi ? 2 : 1, 13);
        double sunset = SunAngleTime(jd, lat, RiseSetAngle, 18, ccw: false);
        double maghrib = p.MaghribAngle > 0 ? SunAngleTime(jd, lat, p.MaghribAngle, 18, ccw: false) : sunset;
        double isha = p.IshaMinutes > 0 ? double.NaN : SunAngleTime(jd, lat, p.Isha, 18, ccw: false);

        if (rule != HighLatitudeRule.None)
        {
            double night = FixHour(sunrise - sunset);
            fajr = AdjustHighLatitude(fajr, sunrise, p.Fajr, night, rule, ccw: true);
            if (p.IshaMinutes <= 0)
                isha = AdjustHighLatitude(isha, sunset, p.Isha, night, rule, ccw: false);
            if (p.MaghribAngle > 0)
                maghrib = AdjustHighLatitude(maghrib, sunset, p.MaghribAngle, night, rule, ccw: false);
        }
        if (p.IshaMinutes > 0)
            isha = maghrib + p.IshaMinutes / 60;

        double[] solar = [fajr, sunrise, dhuhr, asrTime, maghrib, isha];
        var utcMidnight = new DateTime(solarDate.Year, solarDate.Month, solarDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var times = new Dictionary<Prayer, DateTime>();
        for (int i = 0; i < solar.Length; i++)
        {
            if (double.IsNaN(solar[i]))
                continue;
            double utcMinutes = (solar[i] - longitude / 15) * 60 + (p.Offsets?[i] ?? 0);
            var utc = utcMidnight.AddMinutes(Math.Round(utcMinutes, MidpointRounding.AwayFromZero));
            // The zone's own offset at that instant, so DST (and its changeover days) is honoured.
            times[(Prayer)i] = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        }
        return new PrayerDay(date, times);
    }

    /// <summary>
    /// The next prayer strictly after <paramref name="now"/> (Sunrise is skipped: it isn't a prayer); after Isha, tomorrow's Fajr.
    /// An Unspecified <paramref name="now"/> is taken as wall-clock time in <paramref name="zone"/>; Utc or Local is converted.
    /// Null only if no prayer happens within the next two days (polar day or night with <see cref="HighLatitudeRule.None"/>).
    /// </summary>
    public static (Prayer Prayer, DateTime At)? Next(DateTime now, double lat, double lon, TimeZoneInfo zone, PrayerMethod method,
        AsrJuristic asr, HighLatitudeRule rule = HighLatitudeRule.AngleBased)
    {
        if (now.Kind != DateTimeKind.Unspecified)
            now = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(now);
        // From yesterday: in high-latitude summers Isha can fall after midnight.
        for (int d = -1; d <= 2; d++)
        {
            var day = For(today.AddDays(d), lat, lon, zone, method, asr, rule);
            foreach (var prayer in new[] { Prayer.Fajr, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha })
                if (day.Times.TryGetValue(prayer, out var at) && at > now)
                    return (prayer, at);
        }
        return null;
    }

    // Altitude of the sun's upper limb at sunrise and sunset, with refraction, at sea level.
    private const double RiseSetAngle = 0.833;

    private static double Julian(DateOnly date)
    {
        int year = date.Year, month = date.Month;
        if (month <= 2)
        {
            year--;
            month += 12;
        }
        double a = Math.Floor(year / 100.0);
        double b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + date.Day + b - 1524.5;
    }

    /// <summary>Declination (degrees) and equation of time (hours) for a Julian day.</summary>
    private static (double Declination, double Equation) SunPosition(double jd)
    {
        double d = jd - 2451545.0;
        double g = FixAngle(357.529 + 0.98560028 * d);
        double q = FixAngle(280.459 + 0.98564736 * d);
        double l = FixAngle(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g));
        double e = 23.439 - 0.00000036 * d;
        double ra = Atan2(Cos(e) * Sin(l), Cos(l)) / 15;
        return (Asin(Sin(e) * Sin(l)), q / 15 - FixHour(ra));
    }

    private static double MidDay(double jd, double hour) => FixHour(12 - SunPosition(jd + hour / 24).Equation);

    /// <summary>When the sun is <paramref name="angle"/> degrees below the horizon, before (ccw) or after noon. NaN if never.</summary>
    private static double SunAngleTime(double jd, double lat, double angle, double hour, bool ccw)
    {
        double decl = SunPosition(jd + hour / 24).Declination;
        double noon = MidDay(jd, hour);
        double t = Acos((-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat))) / 15;
        return noon + (ccw ? -t : t);
    }

    private static double AsrTime(double jd, double lat, double factor, double hour)
    {
        double decl = SunPosition(jd + hour / 24).Declination;
        double angle = -Acot(factor + Tan(Math.Abs(lat - decl)));
        return SunAngleTime(jd, lat, angle, hour, ccw: false);
    }

    private static double AdjustHighLatitude(double time, double baseTime, double angle, double night, HighLatitudeRule rule, bool ccw)
    {
        double portion = night * rule switch
        {
            HighLatitudeRule.AngleBased => angle / 60,
            HighLatitudeRule.OneSeventh => 1.0 / 7,
            _ => 0.5,
        };
        double diff = ccw ? FixHour(baseTime - time) : FixHour(time - baseTime);
        if (double.IsNaN(time) || diff > portion)
            time = baseTime + (ccw ? -portion : portion);
        return time;
    }

    private static double Sin(double deg) => Math.Sin(deg * Math.PI / 180);
    private static double Cos(double deg) => Math.Cos(deg * Math.PI / 180);
    private static double Tan(double deg) => Math.Tan(deg * Math.PI / 180);
    private static double Asin(double x) => Math.Asin(x) * 180 / Math.PI;
    private static double Acos(double x) => Math.Acos(x) * 180 / Math.PI;
    private static double Atan2(double y, double x) => Math.Atan2(y, x) * 180 / Math.PI;
    private static double Acot(double x) => Math.Atan(1 / x) * 180 / Math.PI;
    private static double FixAngle(double a) => a - 360 * Math.Floor(a / 360);
    private static double FixHour(double h) => h - 24 * Math.Floor(h / 24);
}
