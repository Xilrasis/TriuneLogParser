using System.Windows;
using System.Windows.Controls;
using TriuneLogParser.Core.Config;

namespace TriuneLogParser.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<(bool ok, string message)>? _archiveNow;

    /// <summary>What changed, so the caller can react (e.g. re-parse on a rest-period change).</summary>
    public bool RestPeriodChanged { get; private set; }
    public bool RetroChanged { get; private set; }
    public bool FolderChanged { get; private set; }
    public bool Saved { get; private set; }

    private readonly int _origRest;
    private readonly int _origRetro;
    private readonly string? _origFolder;

    public SettingsWindow(AppSettings settings, string? currentLogPath = null,
        Func<(bool ok, string message)>? archiveNow = null)
    {
        _settings = settings;
        _archiveNow = archiveNow;
        _origRest = settings.RestPeriodSeconds;
        _origRetro = settings.RetroParseMinutes;
        _origFolder = settings.EverQuestFolder;

        InitializeComponent();

        FolderText.Text = settings.EverQuestFolder ?? "(not set)";
        RestSlider.Value = settings.RestPeriodSeconds;
        UpdateRestLabel();
        SelectRetro(settings.RetroParseMinutes);
        SplitCheck.IsChecked = settings.LogSplitEnabled;
        SplitSizeBox.Text = settings.LogSplitSizeMb.ToString();

        bool canSplitNow = archiveNow is not null && !string.IsNullOrEmpty(currentLogPath);
        SplitNowButton.IsEnabled = canSplitNow;
        SplitNowResult.Text = canSplitNow
            ? System.IO.Path.GetFileName(currentLogPath)
            : "Start monitoring a character first.";
    }

    private void SplitNow_Click(object sender, RoutedEventArgs e)
    {
        if (_archiveNow is null)
            return;

        SplitNowButton.IsEnabled = false;
        (bool ok, string message) = _archiveNow();
        SplitNowResult.Text = message;
        SplitNowButton.IsEnabled = !ok; // one archive is enough until there's a fresh log
    }

    private void RestSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateRestLabel();

    private void UpdateRestLabel()
    {
        int s = (int)Math.Round(RestSlider.Value);
        RestLabel.Text = s == 0 ? "0s — per-pull"
            : s < 60 ? $"{s}s"
            : $"{s / 60}m {s % 60:00}s";
    }

    private void SelectRetro(int minutes)
    {
        foreach (ComboBoxItem item in RetroCombo.Items)
        {
            if (item.Tag is string t && int.TryParse(t, out int m) && m == minutes)
            {
                RetroCombo.SelectedItem = item;
                return;
            }
        }

        RetroCombo.SelectedIndex = 1; // 30 min
    }

    private void Retro_Changed(object sender, SelectionChangedEventArgs e) { }
    private void SplitCheck_Click(object sender, RoutedEventArgs e) { }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select your EverQuest folder (the one containing the 'logs' folder)",
            InitialDirectory = _settings.EverQuestFolder ?? "",
        };
        if (dlg.ShowDialog() == true)
        {
            _settings.EverQuestFolder = dlg.FolderName;
            FolderText.Text = dlg.FolderName;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _settings.EverQuestFolder = _origFolder; // revert folder picks
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.RestPeriodSeconds = (int)Math.Round(RestSlider.Value);

        if ((RetroCombo.SelectedItem as ComboBoxItem)?.Tag is string tag && int.TryParse(tag, out int retro))
            _settings.RetroParseMinutes = retro;

        _settings.LogSplitEnabled = SplitCheck.IsChecked == true;
        if (int.TryParse(SplitSizeBox.Text, out int mb))
            _settings.LogSplitSizeMb = Math.Clamp(mb, 5, 4000);

        _settings.Save();

        RestPeriodChanged = _settings.RestPeriodSeconds != _origRest;
        RetroChanged = _settings.RetroParseMinutes != _origRetro;
        FolderChanged = !string.Equals(_settings.EverQuestFolder, _origFolder, StringComparison.OrdinalIgnoreCase);
        Saved = true;

        DialogResult = true;
        Close();
    }
}
