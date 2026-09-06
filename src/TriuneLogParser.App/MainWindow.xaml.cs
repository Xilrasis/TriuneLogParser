using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using TriuneLogParser.App.ViewModels;

namespace TriuneLogParser.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Title = $"TriuneLogParser {AppVersion()}";

        _vm.SelectionShouldFollow += FollowSelection;
        _vm.RequestSavePath = SuggestSavePath;
        _vm.RequestSettingsDialog = ShowSettingsDialog;
        Closed += (_, _) => { UnregisterSplitHotkey(); _vm.Dispose(); };
        Loaded += (_, _) => _vm.OnShellReady();
        SourceInitialized += (_, _) => RegisterSplitHotkey();
    }

    private static string AppVersion()
    {
        string? v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(v))
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        int plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    // ---- global "split fight" hotkey (Ctrl+Alt+S), works while the game has focus ----

    private const int WmHotkey = 0x0312;
    private const int SplitHotkeyId = 0xB01D;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private const uint VkS = 0x53;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;

    private void RegisterSplitHotkey()
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is null)
            return;
        _source.AddHook(WndProc);
        RegisterHotKey(_source.Handle, SplitHotkeyId, ModControl | ModAlt | ModNoRepeat, VkS);
    }

    private void UnregisterSplitHotkey()
    {
        if (_source is null)
            return;
        UnregisterHotKey(_source.Handle, SplitHotkeyId);
        _source.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == SplitHotkeyId)
        {
            _vm.SplitFight();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private (bool, bool, bool, bool) ShowSettingsDialog(TriuneLogParser.Core.Config.AppSettings settings)
    {
        var dlg = new SettingsWindow(settings, _vm.CurrentLogPath, _vm.ArchiveLogNow) { Owner = this };
        dlg.ShowDialog();
        return (dlg.Saved, dlg.RestPeriodChanged, dlg.RetroChanged, dlg.FolderChanged);
    }

    private string? SuggestSavePath(string suggestedName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void EncounterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        var ids = EncounterList.SelectedItems
            .OfType<EncounterRowViewModel>()
            .Select(r => r.Id)
            .ToList();

        _vm.SetSelectedEncounters(ids);
    }

    private void FollowSelection(int id)
    {
        EncounterRowViewModel? row = EncounterList.Items
            .OfType<EncounterRowViewModel>()
            .FirstOrDefault(r => r.Id == id);
        if (row is null)
            return;

        _syncingSelection = true;
        try
        {
            EncounterList.SelectedItems.Clear();
            EncounterList.SelectedItem = row;
            EncounterList.ScrollIntoView(row);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void Metric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse(tag, out Metric m))
            _vm.SetMetric(m);
    }

    private void Node_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BreakdownNode node })
            _vm.ToggleNode(node);
    }

    private void BreakdownScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Give rows a concrete width so right-aligned columns lay out correctly.
        _vm.BreakdownWidth = Math.Max(120, e.NewSize.Width - 16);
    }
}
