using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace PrinterDesk.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        VersionText.Text = "Version " + DisplayVersion(assembly);
        CopyrightText.Text = $"{assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright}. " +
            "Free software under the MIT License.";
    }

    /// <summary>"1.1.0+78fcba9d9b…" (informational version with commit) → "1.1.0 (78fcba9)".</summary>
    private static string DisplayVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : $"{version[..plus]} ({version[(plus + 1)..Math.Min(version.Length, plus + 8)]})";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Notices_Click(object sender, RoutedEventArgs e)
    {
        if (NoticesBox.Visibility == Visibility.Visible)
        {
            NoticesBox.Visibility = Visibility.Collapsed;
            return;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("THIRD-PARTY-NOTICES.txt");
        NoticesBox.Text = stream is null ? "Notices not found." : new StreamReader(stream).ReadToEnd();
        NoticesBox.Visibility = Visibility.Visible;
    }
}
