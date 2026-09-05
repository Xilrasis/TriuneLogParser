using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TriuneLogParser.App.ViewModels;

namespace TriuneLogParser.App.Overlay;

/// <summary>Experimental per-player source breakdown, styled like the main overlay.</summary>
public partial class BranchOverlayWindow : Window
{
    private readonly BranchOverlayViewModel _vm;
    private readonly Action _persist;
    private bool _ready;
    private bool _forceClose;

    public BranchOverlayWindow(BranchOverlayViewModel vm, Action persist)
    {
        _vm = vm;
        _persist = persist;
        InitializeComponent();
        DataContext = _vm;

        var s = _vm.Settings;
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        Left = s.BranchLeft;
        Top = s.BranchTop;
        Width = Math.Clamp(s.BranchWidth, MinWidth, MaxWidth);
        Height = Math.Clamp(s.BranchHeight, MinHeight, MaxHeight);

        ApplyLock(s.Locked);
        SyncHeaderColumns();

        Loaded += (_, _) => _ready = true;
        LocationChanged += (_, _) => Save();
        SizeChanged += (_, _) => Save();
    }

    /// <summary>Follows the main overlay's lock state (no button of its own).</summary>
    public void ApplyLock(bool locked)
    {
        if ((ResizeMode == ResizeMode.NoResize) == locked)
            return; // no change
        ResizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResize;
        _vm.NotifyLockChanged();
    }

    private void SyncHeaderColumns()
    {
        var s = _vm.Settings;
        double mid = Math.Max(0.1, 1.0 - s.NameColFraction - s.RateColFraction);
        HdrName.Width = new GridLength(s.NameColFraction, GridUnitType.Star);
        HdrMid.Width = new GridLength(mid, GridUnitType.Star);
        HdrRate.Width = new GridLength(s.RateColFraction, GridUnitType.Star);
    }

    private void ColumnSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        double total = HdrName.ActualWidth + HdrMid.ActualWidth + HdrRate.ActualWidth;
        if (total <= 1)
            return;
        _vm.SetColumnFractions(HdrName.ActualWidth / total, HdrRate.ActualWidth / total);
        SyncHeaderColumns();
        _persist();
    }

    private void Root_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_vm.Settings.Locked)
        {
            try { DragMove(); } catch { /* ignore rapid clicks */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.Settings.Locked)
            return;
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, MaxHeight);
    }

    private void Save()
    {
        if (!_ready || WindowState != WindowState.Normal)
            return;

        _vm.Settings.BranchLeft = Left;
        _vm.Settings.BranchTop = Top;
        _vm.Settings.BranchWidth = Width;
        if (!double.IsNaN(Height))
            _vm.Settings.BranchHeight = Height;
        _persist();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
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
        Topmost = true;
    }
}
