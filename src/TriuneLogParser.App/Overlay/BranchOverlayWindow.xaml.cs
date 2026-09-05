using System.Windows;
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
        Left = s.BranchLeft;
        Top = s.BranchTop;
        Width = Math.Clamp(s.BranchWidth, 160, 1200);
        MaxHeight = SystemParameters.WorkArea.Height * 0.92; // scroll the bar list past this

        Loaded += (_, _) => _ready = true;
        LocationChanged += (_, _) => Save();
        SizeChanged += (_, _) => Save();
    }

    private void Root_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore rapid clicks */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Save()
    {
        if (!_ready || WindowState != WindowState.Normal)
            return;

        _vm.Settings.BranchLeft = Left;
        _vm.Settings.BranchTop = Top;
        _vm.Settings.BranchWidth = Width;
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
