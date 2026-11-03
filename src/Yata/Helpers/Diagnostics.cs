using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Yata.Services;

namespace Yata.Helpers;

/// <summary>
/// The "Copy diagnostics" text for an issue: versions, displays and which features are on.
/// Nothing personal goes in (no city, account, task or file), and nothing is sent anywhere.
/// </summary>
internal static class Diagnostics
{
    public static string Describe(SettingsData settings, GoogleAccountStatus google) =>
        Describe(settings, google, Monitors.All(), WindowsVersion(), Log.Directory);

    internal static string Describe(SettingsData settings, GoogleAccountStatus google, IReadOnlyList<DisplayInfo> displays,
        string windows, string logDirectory)
    {
        string OnOff(bool on) => on ? "ligado" : "desligado";

        var text = new StringBuilder()
            .AppendLine($"Yata {UpdateChecker.Current}")
            .AppendLine($"{windows}, {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}")
            .AppendLine($"Telas: {displays.Count} ({string.Join(", ", displays.Select(d => $"{d.Width}×{d.Height}{(d.IsPrimary ? " principal" : "")}"))})")
            .AppendLine($"Notch na tela: {(settings.Monitor == null ? "principal" : "escolhida")}")
            .AppendLine($"Idioma: {settings.Language ?? $"do Windows ({Lang.FromWindows})"}")
            .AppendLine($"Letras: {OnOff(settings.LyricsEnabled)} · Tempo: {OnOff(settings.WeatherEnabled)} · " +
                        $"Aviso de versão: {OnOff(settings.UpdateCheckEnabled)} · Tema: {settings.ThemeId}")
            .AppendLine($"Atalhos: {string.Join(", ", Hotkeys.All.Select(a => $"{a}={Hotkeys.For(settings, a)?.ToString() ?? "-"}"))}")
            .AppendLine($"Spotify Web API: {(settings.SpotifyClientId == null ? "sem Client ID" : "com Client ID")}")
            .AppendLine($"Conta Google: {google} · Agenda no notch fechado: {OnOff(settings.CalendarInCompact)} · " +
                        $"Sincronizar tarefas: {OnOff(settings.TasksSyncEnabled)}")
            .AppendLine($"Yata companheiro: {OnOff(settings.CompanionEnabled)} · Esconder em tela cheia: {OnOff(settings.HideInFullscreen)}")
            .AppendLine($"Pasta de dados: {(Environment.GetEnvironmentVariable("YATA_DATA_DIR") is { Length: > 0 } ? "personalizada" : "padrão")}");

        string? crash = System.IO.Directory.Exists(logDirectory)
            ? System.IO.Directory.GetFiles(logDirectory, "crash-*.log").Select(System.IO.Path.GetFileName).Max(StringComparer.Ordinal)
            : null;
        text.AppendLine($"Última queda registrada: {crash ?? "nenhuma"}");
        return text.ToString();
    }

    private static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("CurrentBuild") is string build)
            {
                string release = key.GetValue("DisplayVersion") as string ?? "";
                object? ubr = key.GetValue("UBR");
                return $"Windows {release} (build {build}{(ubr != null ? $".{ubr}" : "")})".Replace("  ", " ");
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return RuntimeInformation.OSDescription;
    }
}
