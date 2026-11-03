using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Services;

/// <summary>A look for the clock's numbers, from fonts every Windows 10/11 has.</summary>
public sealed record ClockStyle(string Id, Func<string> Name, string Family, FontWeight Weight, string Fallback = "Segoe UI");

public static class ClockStyles
{
    /// <summary>The ids are stored in data.json: don't rename them.</summary>
    public static readonly IReadOnlyList<ClockStyle> All = new[]
    {
        new ClockStyle("default", () => Strings.ClockFont_Default, "Segoe UI Variable Display", FontWeights.SemiBold),
        new ClockStyle("thin", () => Strings.ClockFont_Thin, "Segoe UI Variable Display", FontWeights.Light, "Segoe UI Light"),
        new ClockStyle("bold", () => Strings.ClockFont_Bold, "Segoe UI Black", FontWeights.Black),
        new ClockStyle("condensed", () => Strings.ClockFont_Condensed, "Bahnschrift SemiBold Condensed", FontWeights.Normal, "Bahnschrift"),
        new ClockStyle("wide", () => Strings.ClockFont_Wide, "Bahnschrift", FontWeights.SemiBold),
        new ClockStyle("digital", () => Strings.ClockFont_Digital, "Cascadia Mono", FontWeights.SemiBold, "Consolas"),
        new ClockStyle("serif", () => Strings.ClockFont_Serif, "Sitka Display", FontWeights.SemiBold, "Georgia"),
        new ClockStyle("handwritten", () => Strings.ClockFont_Handwritten, "Ink Free", FontWeights.Normal, "Segoe Print"),
    };

    public static ClockStyle Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    /// <summary>The font, falling back when the first one isn't installed (older Windows 10 lacks Cascadia).</summary>
    public static FontFamily Family(ClockStyle style) => new($"{style.Family}, {style.Fallback}, Segoe UI");

    /// <summary>The idle clock's size for 0 normal, 1 large, 2 huge.</summary>
    public static double IdleSize(int size) => size switch { 0 => 36, 2 => 64, _ => 50 };

    /// <summary>The closed notch's clock: it has 32 px of height, so it grows only a little.</summary>
    public static double CompactSize(int size) => size switch { 0 => 13, 2 => 16, _ => 15 };

    public static string SizeName(int size) => size switch
    {
        0 => Strings.ClockSize_Normal,
        2 => Strings.ClockSize_Huge,
        _ => Strings.ClockSize_Large,
    };
}
