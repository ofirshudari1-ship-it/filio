using System.Windows;
using System.Windows.Threading;

namespace Filio;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetVersion(string version)
    {
        VersionText.Text = $"v{version}";
    }

    // The int progress parameter is kept (but ignored) purely so existing call sites in
    // App.xaml.cs don't need to change - the splash uses a continuous spinner rather than a
    // fake progress bar (STANDARDS.md §19.1: no percentage is ever real work-completion here).
    public void SetStatus(string status, int progress = -1)
    {
        StatusText.Text = status;
    }
}
