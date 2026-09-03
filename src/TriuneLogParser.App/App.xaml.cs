using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace TriuneLogParser.App;

public partial class App : Application
{
    static App()
    {
        // WPF's text layout and culture-aware bindings need a real, specific culture.
        // On some machines the process starts with an unusable culture, which makes
        // right-aligned / culture-formatted text silently fail to render.
        var ci = CultureInfo.CreateSpecificCulture("en-US");
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = ci;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(ci.IetfLanguageTag)));

        FrameworkElement.FlowDirectionProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Environment.GetEnvironmentVariable("TLP_TRACE") == "1")
        {
            string path = Path.Combine(Path.GetTempPath(), "tlp-binding-trace.log");
            var listener = new TextWriterTraceListener(path);
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            Trace.AutoFlush = true;
        }
    }
}
