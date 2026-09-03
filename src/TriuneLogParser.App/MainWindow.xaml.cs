using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        _vm.SelectionShouldFollow += FollowSelection;
        Closed += (_, _) => _vm.Dispose();
        Loaded += (_, _) => _vm.OnShellReady();
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
        _vm.BreakdownWidth = Math.Max(120, e.NewSize.Width - 4);
    }
}
