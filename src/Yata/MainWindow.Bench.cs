using System;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;

namespace Yata;

// Fixed states for tools/measure.ps1 (--bench <state>): the media sessions of the PC are ignored and
// nothing goes to the network, so every run shows the same thing.
public partial class MainWindow
{
    private const string BenchArtist = "Yata";
    private const string BenchTitle = "Medição";
    private static readonly TimeSpan BenchTrackLength = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BenchLineEvery = TimeSpan.FromSeconds(4);

    /// <summary>
    /// "parado" (just the clock), "musica" (a track with synced lyrics, notch closed),
    /// "aberto" (the same track, notch open on the media page), "foco" (Pomodoro running) or
    /// "dormindo" (Yata asleep in the closed notch);
    /// "transicoes" (opens, switches tabs and closes over and over, for tools/measure-frames.ps1);
    /// "vitrine" (every state drawn to a PNG, for tools/make-shots.ps1; see MainWindow.Shots.cs);
    /// "reel" (a scripted tour recorded for the README's animations, for tools/make-reel.ps1; see MainWindow.Reel.cs);
    /// "erro" and "queda" throw on purpose, to check the error handling. Null outside measurements.
    /// </summary>
    internal string? Bench { get; init; }

    /// <summary>"sem-sombra" or "gpu" (the latter applied by App, before any window): what a measurement compares.</summary>
    internal string? BenchVariant { get; init; }

    private void StartBench(string state)
    {
        ApplyBenchVariant(BenchVariant);
        switch (state)
        {
            case "musica":
                SimulateTrack();
                break;
            case "aberto":
                SimulateTrack();
                Dispatcher.InvokeAsync(() => ShowPage("media", 300), DispatcherPriority.ApplicationIdle);
                break;
            case "foco":
                _focus.Start();
                break;
            case "transicoes":
                SimulateTrack();
                After(TimeSpan.FromSeconds(3), () => RunTransitions(1));
                break;
            case "vitrine":
                RunShots(ShotsDir ?? System.IO.Path.GetTempPath());
                break;
            case "reel":
                RunReel(ShotsDir ?? System.IO.Path.GetTempPath());
                break;
            case "dormindo":
                // Nothing playing and the user long gone: Yata dozes in the closed notch.
                Settings.CompanionEnabled = true;
                _presence.Simulate(TimeSpan.FromMinutes(30));
                break;
            // The safety net (App.CatchUnhandledErrors): a UI error must be survived, a background one
            // must leave a crash file.
            case "erro":
                After(TimeSpan.FromSeconds(2), () => throw new InvalidOperationException("Erro de teste na interface (--bench erro)"));
                break;
            case "queda":
                After(TimeSpan.FromSeconds(2), () => new Thread(() =>
                    throw new InvalidOperationException("Erro de teste fora da interface (--bench queda)")).Start());
                break;
        }
    }

    private const int TransitionCycles = 12;
    private static readonly TimeSpan TransitionStep = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// "transicoes" (tools/measure-frames.ps1): the notch opens, goes through three tabs, closes and
    /// shows a notice, over and over, each step given time to settle. The frames go to --frames.
    /// </summary>
    private void RunTransitions(int cycle)
    {
        if (cycle > TransitionCycles)
        {
            FrameTimings.Note("fim");
            return;
        }
        (string Label, Action Run)[] steps =
        [
            ("abrir", () => ShowPage("media", 300)),
            ("aba-agenda", () => TabAgenda.IsChecked = true),
            ("aba-tarefas", () => TabTasks.IsChecked = true),
            ("aba-midia", () => TabMedia.IsChecked = true),
            ("fechar", () => SetExpanded(false)),
            ("hud", () => ShowHud(new HudMessage(Glyphs.Volume2, "Volume", "50", Level: 0.5, DurationMs: 60000))),
            ("hud-sair", HideHud),
        ];
        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            After(TransitionStep * i, () =>
            {
                FrameTimings.Begin(step.Label, cycle);
                step.Run();
            });
        }
        After(TransitionStep * steps.Length, () => RunTransitions(cycle + 1));
    }

    /// <summary>"sem-sombra" (--bench-variant): the notch without its drop shadows, to see what they cost.</summary>
    private void ApplyBenchVariant(string? variant)
    {
        if (variant != "sem-sombra")
            return;
        NotchShadow.Visibility = Visibility.Collapsed;
        BubbleShadow.Visibility = Visibility.Collapsed;
        ActivityPanelShadow.Visibility = Visibility.Collapsed;
    }

    private void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void SimulateTrack()
    {
        var lrc = new StringBuilder();
        int line = 1;
        for (var at = TimeSpan.Zero; at < BenchTrackLength; at += BenchLineEvery)
            lrc.AppendLine($"[{(int)at.TotalMinutes:00}:{at.Seconds:00}.00] Linha {line++} da letra de medição");
        _lyricsService.Seed(BenchArtist, BenchTitle, lrc.ToString());
        _media.Simulate(BenchTitle, BenchArtist, BenchTrackLength);
    }
}
