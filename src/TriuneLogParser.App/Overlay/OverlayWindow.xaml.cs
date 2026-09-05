using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TriuneLogParser.App.ViewModels;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App.Overlay;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _vm;
    private readonly Action _persist;
    private readonly Action _onSplit;
    private readonly Action<string> _onBranch;
    private bool _loading = true;
    private bool _ready;
    private bool _forceClose;

    public OverlayWindow(OverlayViewModel vm, Action persist, Action onSplit, Action<string> onBranch)
    {
        _vm = vm;
        _persist = persist;
        _onSplit = onSplit;
        _onBranch = onBranch;
        InitializeComponent();
        DataContext = _vm;

        OverlaySettings s = _vm.Settings.Clamp();
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        Left = s.Left;
        Top = s.Top;
        Width = s.Width;
        Height = s.Height; // user-resizable; the bar list scrolls when it doesn't fit
        Root.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb((byte)(s.Opacity * 255), 0x1b, 0x1c, 0x1f));

        OpacitySlider.Value = s.Opacity;
        ScaleSlider.Value = s.Scale;
        RowsSlider.Value = s.MaxRows;
        ClickThroughCheck.IsChecked = s.ClickThrough;

        SourceInitialized += (_, _) => ApplyClickThrough(_vm.Settings.ClickThrough);
        Loaded += (_, _) => { _ready = true; };
        LocationChanged += (_, _) => Save();
        SizeChanged += (_, _) => Save();
        _loading = false;
    }

    public void SetClickThrough(bool on)
    {
        _vm.Settings.ClickThrough = on;
        ClickThroughCheck.IsChecked = on;
        ApplyClickThrough(on);
        _persist();
    }

    // ---- interactions ---------------------------------------------------------

    private void Root_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_vm.Settings.ClickThrough)
        {
            try { DragMove(); } catch { /* ignore rapid clicks */ }
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, MaxHeight);
        // SizeChanged -> Save() persists it.
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Split_Click(object sender, RoutedEventArgs e) => _onSplit();

    // A bar row is a click target for the branch breakdown; swallow the down so it
    // doesn't start a window drag, act on the up.
    private void Bar_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Bar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayBar bar })
            _onBranch(bar.Name);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string ok;
        try
        {
            System.Windows.Clipboard.SetText(_vm.BuildChatSummary());
            ok = "✓";
        }
        catch
        {
            // Another app can transiently hold the clipboard open; nothing to do but let the user retry.
            ok = "✕";
        }

        FlashButton(CopyButton, ok);
    }

    private static void FlashButton(Button button, string glyph)
    {
        object original = button.Content;
        button.Content = glyph;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) => { button.Content = original; timer.Stop(); };
        timer.Start();
    }

    private void MetricPrev_Click(object sender, RoutedEventArgs e) { _vm.CycleMetric(-1); _persist(); }
    private void MetricNext_Click(object sender, RoutedEventArgs e) { _vm.CycleMetric(1); _persist(); }

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _vm.Settings.Opacity = e.NewValue;
        Root.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb((byte)(e.NewValue * 255), 0x1b, 0x1c, 0x1f));
        _persist();
    }

    private void ScaleSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _vm.Settings.Scale = e.NewValue;
        _vm.RaiseSettings();
        _persist();
    }

    private void RowsSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _vm.Settings.MaxRows = (int)Math.Round(e.NewValue);
        _persist();
    }

    private void ClickThrough_Click(object sender, RoutedEventArgs e)
    {
        bool on = ClickThroughCheck.IsChecked == true;
        _vm.Settings.ClickThrough = on;
        ApplyClickThrough(on);
        _persist();
    }

    private void Save()
    {
        if (_loading || !_ready)
            return;

        OverlaySettings s = _vm.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.Left = Left;
            s.Top = Top;
            s.Width = Width;
            if (!double.IsNaN(Height))
                s.Height = Height;
        }

        s.Shown = IsVisible;
        _persist();
    }

    public void PersistNow() => Save();

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The main app owns lifetime; a user close just hides the overlay.
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Topmost = false;
        Topmost = true; // keep above other topmost windows (e.g. the game)
    }

    // ---- click-through interop ----------------------------------------------

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int newStyle);

    private void ApplyClickThrough(bool on)
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
            return;

        int style = GetWindowLong(helper.Handle, GwlExStyle);
        style = on
            ? style | WsExTransparent | WsExLayered
            : style & ~WsExTransparent;
        SetWindowLong(helper.Handle, GwlExStyle, style);
    }
}
