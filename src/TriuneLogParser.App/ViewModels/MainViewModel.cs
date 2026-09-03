using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using TriuneLogParser.App.Services;
using TriuneLogParser.Core.Aggregation;
using TriuneLogParser.Core.Config;
using TriuneLogParser.Core.Encounters;

namespace TriuneLogParser.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly LiveLogService _service = new();
    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;

    private LogFileInfo? _selectedCharacter;
    private string _statusText = "";
    private string _zoneText = "";
    private bool _autoFollow = true;
    private Metric _metric = Metric.DamageDone;
    private IReadOnlyList<int> _selectedIds = Array.Empty<int>();
    private string _breakdownHeader = "Select an encounter";

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _service.Configure(new EncounterOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(Math.Clamp(_settings.IdleTimeoutSeconds, 5, 600)),
        });

        ChangeFolderCommand = new RelayCommand(ChangeFolder);
        FollowLiveCommand = new RelayCommand(() => { AutoFollow = true; Refresh(); });
        ReloadCommand = new RelayCommand(ReloadCurrent, () => SelectedCharacter is not null);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(750) };
        _timer.Tick += (_, _) => { _service.Tick(DateTime.Now); Refresh(); };
        _timer.Start();

        RefreshCharacters();
    }

    // ---- folder / character selection -----------------------------------------

    public string EverQuestFolder => _settings.EverQuestFolder ?? "(not set)";
    public bool NeedsSetup => !_settings.LooksValid();

    public ObservableCollection<LogFileInfo> Characters { get; } = new();

    public LogFileInfo? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (!Set(ref _selectedCharacter, value))
                return;
            Encounters.Clear();
            _selectedIds = Array.Empty<int>();
            ClearBreakdown();
            if (value is not null)
            {
                _autoFollow = true;
                Raise(nameof(AutoFollow));
                _service.Start(value.Path, value.Character);
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand ChangeFolderCommand { get; }
    public ICommand FollowLiveCommand { get; }
    public ICommand ReloadCommand { get; }

    public void ChangeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select your EverQuest folder (the one containing the 'logs' folder)",
            InitialDirectory = _settings.EverQuestFolder ?? "",
        };

        if (dialog.ShowDialog() != true)
            return;

        SetFolder(dialog.FolderName);
    }

    public void SetFolder(string folder)
    {
        _settings.EverQuestFolder = folder;
        _settings.Save();
        Raise(nameof(EverQuestFolder));
        Raise(nameof(NeedsSetup));
        RefreshCharacters();
    }

    private void RefreshCharacters()
    {
        IReadOnlyList<LogFileInfo> found = LogDiscovery.Find(_settings.EverQuestFolder);
        Characters.Clear();
        foreach (LogFileInfo f in found)
            Characters.Add(f);

        if (SelectedCharacter is null && Characters.Count > 0)
            SelectedCharacter = Characters[0];
    }

    private void ReloadCurrent()
    {
        if (SelectedCharacter is { } c)
            _service.Start(c.Path, c.Character);
    }

    // ---- encounter list ------------------------------------------------------

    public ObservableCollection<EncounterRowViewModel> Encounters { get; } = new();

    public bool AutoFollow
    {
        get => _autoFollow;
        set { if (Set(ref _autoFollow, value) && value) Refresh(); }
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ZoneText { get => _zoneText; private set => Set(ref _zoneText, value); }

    /// <summary>Called by the view when the encounter selection changes.</summary>
    public void SetSelectedEncounters(IReadOnlyList<int> ids)
    {
        _selectedIds = ids;
        if (ids.Count > 0)
            _autoFollow = false;
        Raise(nameof(AutoFollow));
        RefreshBreakdown();
    }

    private void Refresh()
    {
        LiveSnapshot snap = _service.GetSnapshot();

        StatusText = snap.Loading
            ? $"Loading {snap.Character}…"
            : snap.Character is null
                ? "No character selected"
                : $"{snap.Character} · {snap.Encounters.Count} encounters · grammar {snap.Coverage:P1}";

        ZoneText = snap.Zone ?? "";

        ReconcileEncounters(snap.Encounters);

        if (_autoFollow)
        {
            EncounterRowViewModel? active = Encounters.FirstOrDefault(e => e.IsActive)
                                            ?? Encounters.FirstOrDefault();
            if (active is not null && (_selectedIds.Count != 1 || _selectedIds[0] != active.Id))
            {
                _selectedIds = new[] { active.Id };
                SelectionShouldFollow?.Invoke(active.Id);
            }
        }

        RefreshBreakdown();
    }

    /// <summary>Raised when auto-follow wants the view to select an encounter row.</summary>
    public event Action<int>? SelectionShouldFollow;

    private void ReconcileEncounters(IReadOnlyList<EncounterSummary> summaries)
    {
        var byId = Encounters.ToDictionary(e => e.Id);

        // Update / add (summaries are newest-first).
        for (int i = 0; i < summaries.Count; i++)
        {
            EncounterSummary s = summaries[i];
            if (byId.TryGetValue(s.Id, out EncounterRowViewModel? row))
            {
                row.Update(s);
            }
            else
            {
                Encounters.Insert(Math.Min(i, Encounters.Count), new EncounterRowViewModel(s));
            }
        }

        // Remove any that disappeared (e.g. after a reload).
        var live = summaries.Select(s => s.Id).ToHashSet();
        for (int i = Encounters.Count - 1; i >= 0; i--)
        {
            if (!live.Contains(Encounters[i].Id))
                Encounters.RemoveAt(i);
        }
    }

    // ---- breakdown ----------------------------------------------------------

    /// <summary>Flattened, expand-aware view of the breakdown tree.</summary>
    public ObservableCollection<BreakdownNode> Nodes { get; } = new();

    private List<BreakdownNode> _tree = new();
    private readonly HashSet<string> _expandedPaths = new();

    public string BreakdownHeader { get => _breakdownHeader; private set => Set(ref _breakdownHeader, value); }

    private double _breakdownWidth = 600;

    /// <summary>Usable width for a breakdown row, pushed from the view on resize.</summary>
    public double BreakdownWidth
    {
        get => _breakdownWidth;
        set => Set(ref _breakdownWidth, Math.Max(120, value));
    }

    public Metric Metric
    {
        get => _metric;
        set { if (Set(ref _metric, value)) RefreshBreakdown(); }
    }

    public void SetMetric(Metric m) => Metric = m;

    public void ToggleNode(BreakdownNode node)
    {
        if (!node.HasChildren)
            return;
        node.IsExpanded = !node.IsExpanded;
        if (node.IsExpanded) _expandedPaths.Add(node.Label); else _expandedPaths.Remove(node.Label);
        Flatten();
    }

    private void ClearBreakdown()
    {
        Nodes.Clear();
        _tree = new();
        BreakdownHeader = "Select an encounter";
    }

    private void RefreshBreakdown()
    {
        EncounterReport? report = _service.Report(_selectedIds);
        if (report is null)
        {
            ClearBreakdown();
            return;
        }

        _tree = BreakdownTreeBuilder.Build(report, Metric, report.DurationSeconds);

        // First time we show a breakdown, open the top fighter so the split is visible.
        if (_expandedPaths.Count == 0 && _tree.Count > 0 && _tree[0].HasChildren)
            _expandedPaths.Add(_tree[0].Label);

        RestoreExpansion(_tree);
        Flatten();

        string what = Metric switch
        {
            Metric.DamageDone => "Damage done",
            Metric.DamageTaken => "Damage taken",
            _ => "Healing",
        };
        string span = _selectedIds.Count > 1 ? $"{_selectedIds.Count} encounters" : "encounter";
        BreakdownHeader = $"{what} · {span} · {report.DurationSeconds:N0}s · {string.Join(" + ", report.Titles.Distinct().Take(3))}";
    }

    private void RestoreExpansion(IEnumerable<BreakdownNode> nodes)
    {
        foreach (BreakdownNode n in nodes)
        {
            if (n.HasChildren && _expandedPaths.Contains(n.Label))
                n.IsExpanded = true;
            RestoreExpansion(n.Children);
        }
    }

    private void Flatten()
    {
        var flat = new List<BreakdownNode>();
        void Walk(IEnumerable<BreakdownNode> nodes)
        {
            foreach (BreakdownNode n in nodes)
            {
                flat.Add(n);
                if (n.IsExpanded)
                    Walk(n.Children);
            }
        }
        Walk(_tree);

        // Reconcile in place so scroll position / selection survive ticks.
        for (int i = 0; i < flat.Count; i++)
        {
            if (i < Nodes.Count && ReferenceEquals(Nodes[i], flat[i]))
                continue;
            if (i < Nodes.Count)
                Nodes[i] = flat[i];
            else
                Nodes.Add(flat[i]);
        }
        while (Nodes.Count > flat.Count)
            Nodes.RemoveAt(Nodes.Count - 1);
    }

    public void Dispose()
    {
        _timer.Stop();
        _service.Dispose();
    }
}
