using System.Reflection;
using System.Windows;

namespace TriuneLogParser.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText.Text = $"v{version}";
    }
}
