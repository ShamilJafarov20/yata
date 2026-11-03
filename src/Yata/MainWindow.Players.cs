using System;
using System.Windows;
using System.Windows.Media;
using Yata.Services;

namespace Yata;

// Several players at once (Spotify and a YouTube tab, say): ⇄ switches the notch to the other one, ✕
// takes the shown one off the notch (the app keeps running; it comes back when it plays again).
public partial class MainWindow
{
    private void InitPlayers() => _media.SourcesChanged += UpdatePlayerButtons;

    /// <summary>⇄ with two or more players to show; ✕ whenever one is shown.</summary>
    private void UpdatePlayerButtons()
    {
        string? next = _media.NextSource;
        SourcesButton.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
        SourcesButton.ToolTip = next != null ? string.Format(Strings.Media_SwitchTo, SourceName(next)) : null;
        RemovePlayerButton.Visibility = _state != null ? Visibility.Visible : Visibility.Collapsed;
        RemovePlayerButton.ToolTip = _state != null ? string.Format(Strings.Media_RemovePlayer, SourceName(_state.SourceApp)) : null;
    }

    private void OnSwitchPlayerClick(object sender, RoutedEventArgs e)
    {
        _media.SwitchToNext();
        UpdatePlayerButtons();
    }

    private void OnRemovePlayerClick(object sender, RoutedEventArgs e)
    {
        _media.RemoveCurrent();
        UpdatePlayerButtons();
    }

    /// <summary>"Spotify", "Opera", "Media Player": the process name when known, else the app id cleaned up.</summary>
    private static string SourceName(string appId) =>
        AppVolume.ProcessFor(appId) is { } process ? AppLabel(process) : AppNames.Friendly(appId);
}
