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

    public void SetStatus(string status, int progress = -1)
    {
        StatusText.Text = status;
        if (progress >= 0)
            SplashProgress.Value = progress;
    }
}
