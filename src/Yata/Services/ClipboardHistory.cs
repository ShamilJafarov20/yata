using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>One thing that was copied: text, or an image (a screenshot from Win+Shift+S, say).</summary>
public sealed class ClipboardItem
{
    public string? Text { get; init; }
    public BitmapSource? Image { get; init; }
    public DateTime Copied { get; init; } = DateTime.Now;

    /// <summary>One line for the list: the text's start, or the image's size.</summary>
    public string Preview
    {
        get
        {
            if (Text == null)
                return Image != null ? $"{Image.PixelWidth} × {Image.PixelHeight}" : "";
            string line = string.Join(" ", Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return line.Length > 140 ? line[..140] + "…" : line;
        }
    }

    public bool IsImage => Image != null;
}

/// <summary>
/// The last things copied, in memory only: nothing is written to disk, and it's gone when Yata closes.
/// Windows tells us when the clipboard changes (AddClipboardFormatListener); nothing is polled.
/// Copies that ask not to be kept (password managers mark them, as Windows' own clipboard history
/// respects) are skipped.
/// </summary>
public sealed class ClipboardHistory : IDisposable
{
    public const int MaxItems = 12;
    private const int MaxTextLength = 20_000;
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly Dispatcher _dispatcher;
    private HwndSource? _source;
    private uint _ownSequence;
    private bool _keepHistory;
    private bool _watchImages;

    public ClipboardHistory(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public ObservableCollection<ClipboardItem> Items { get; } = new();

    /// <summary>An image was just copied (most often a screenshot): the notch can offer it.</summary>
    public event Action<ClipboardItem>? ImageCopied;

    /// <summary>
    /// Listens while either feature wants it: the history (text and images kept) or the screenshot
    /// preview (images announced, kept only while the history is on).
    /// </summary>
    public void Configure(IntPtr hwnd, bool keepHistory, bool watchImages)
    {
        _keepHistory = keepHistory;
        _watchImages = watchImages;
        if (!keepHistory)
            Items.Clear();

        bool wanted = keepHistory || watchImages;
        if (wanted && _source == null && hwnd != IntPtr.Zero)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
            if (!AddClipboardFormatListener(hwnd))
                Log.Warn("clipboard", $"AddClipboardFormatListener falhou ({Marshal.GetLastWin32Error()})");
        }
        else if (!wanted && _source != null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            // The app that copied may still hold the clipboard open: read a moment later.
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Read));
        }
        return IntPtr.Zero;
    }

    private void Read()
    {
        uint sequence = GetClipboardSequenceNumber();
        if (sequence == _ownSequence)
            return; // Our own "copy again".
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null || IsPrivate(data))
                return;

            ClipboardItem? item = null;
            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                item = new ClipboardItem { Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text };
            }
            else if (data.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is BitmapSource image)
            {
                var copy = new WriteableBitmap(image); // Detached from the clipboard's memory.
                copy.Freeze();
                item = new ClipboardItem { Image = copy };
            }
            if (item == null)
                return;

            if (_keepHistory)
                Remember(item);
            if (item.IsImage && _watchImages)
                ImageCopied?.Invoke(item);
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or ArgumentException)
        {
            // Another app holds the clipboard, or the data is in a form WPF can't read: skip this copy.
            Log.Warn("clipboard", "Não foi possível ler a área de transferência", ex);
        }
    }

    private void Remember(ClipboardItem item)
    {
        // The same text copied again moves to the top rather than repeating.
        if (item.Text != null && Items.FirstOrDefault(i => i.Text == item.Text) is { } same)
            Items.Remove(same);
        Items.Insert(0, item);
        while (Items.Count > MaxItems)
            Items.RemoveAt(Items.Count - 1);
    }

    /// <summary>
    /// Copies marked as not for history: password managers (KeePass, 1Password, Bitwarden…) set these,
    /// as Windows documents for its own clipboard history.
    /// </summary>
    private static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore"))
            return true;
        foreach (string format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (data.GetDataPresent(format) && data.GetData(format) is System.IO.MemoryStream stream && stream.Length >= 4)
            {
                var bytes = stream.ToArray();
                if (BitConverter.ToInt32(bytes, 0) == 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Puts an item back on the clipboard, without adding it to the history again.</summary>
    public bool CopyAgain(ClipboardItem item)
    {
        try
        {
            if (item.Text != null)
                Clipboard.SetText(item.Text);
            else if (item.Image != null)
                Clipboard.SetImage(item.Image);
            _ownSequence = GetClipboardSequenceNumber();
            if (_keepHistory && Items.Remove(item))
                Items.Insert(0, item);
            return true;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Warn("clipboard", "Não foi possível copiar de novo", ex);
            return false;
        }
    }

    public void Clear() => Items.Clear();

    public void Dispose()
    {
        if (_source != null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
