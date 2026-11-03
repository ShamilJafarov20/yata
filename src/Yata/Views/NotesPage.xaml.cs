using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Yata.Services;

namespace Yata.Views;

/// <summary>A scratchpad for a phone number or a link: kept in data.json, saved as you type.</summary>
public partial class NotesPage : UserControl
{
    private AppStore? _store;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _loading;

    public event Action? KeyboardRequested;
    public event Action? KeyboardReleased;
    public event Action<string, string>? Said;

    public NotesPage()
    {
        InitializeComponent();
        _save.Tick += (_, _) => Save();
    }

    public bool IsTyping => Input.IsKeyboardFocused;

    public void Attach(AppStore store)
    {
        _store = store;
        _loading = true;
        Input.Text = store.Data.Notes;
        _loading = false;
        ShowPlaceholder();
    }

    public void EndTyping()
    {
        if (IsTyping)
            Keyboard.ClearFocus();
        Save();
    }

    private void OnInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTyping)
            return;
        KeyboardRequested?.Invoke();
        Input.Focus();
        // Let the click also place the caret.
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndTyping();
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        ShowPlaceholder();
        if (_loading)
            return;
        _save.Stop();
        _save.Start();
    }

    private void ShowPlaceholder() =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Save();
        KeyboardReleased?.Invoke();
    }

    private void Save()
    {
        _save.Stop();
        if (_store == null || _store.Data.Notes == Input.Text)
            return;
        _store.Data.Notes = Input.Text;
        _store.SaveSoon();
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (Input.Text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(Input.Text);
            Said?.Invoke("", Strings.Clip_Copied);
        }
        catch (COMException)
        {
            // The clipboard is busy in another app.
        }
    }
}
