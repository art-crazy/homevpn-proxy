using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HomeVpnProxyTray;

/// <summary>
/// Runs as its own short-lived process (see Program.RunWindowProcess) -
/// self-sufficient, doesn't need anything from the tray supervisor. Reads
/// live proxy/PAC state directly and applies toggles directly; the
/// supervisor picks up any change on its own next health-check tick (or
/// immediately once this process exits).
/// </summary>
public partial class MainWindow : FluentWindow
{
    private bool _suppressEvents;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);

        HttpProxyValueBox.Text = Constants.HttpProxyUrl;
        HttpsProxyValueBox.Text = Constants.HttpProxyUrl;
        AllProxyValueBox.Text = Constants.SocksProxyUrl;
        PacUrlValueBox.Text = Constants.PacUrl;

        RefreshToggles();
        _ = RefreshHealthAsync();
    }

    private void RefreshToggles()
    {
        _suppressEvents = true;
        try
        {
            var enabled = ProxyToggle.IsEnabled();
            MasterToggle.IsChecked = enabled;
            MasterStatusText.Text = enabled
                ? "Включён - переменные и PAC применены"
                : "Выключен";

            AutoStartToggle.IsChecked = AutoStartManager.IsEnabled();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private async Task RefreshHealthAsync()
    {
        var enabled = ProxyToggle.IsEnabled();
        var snapshot = enabled
            ? await HealthChecker.RunAsync()
            : new HealthSnapshot(false, SafeCheckPointConnected(), Array.Empty<string>(), null);

        RenderHealth(snapshot, enabled);
    }

    private static bool SafeCheckPointConnected()
    {
        try
        {
            return HealthChecker.CheckPointConnected();
        }
        catch
        {
            return false;
        }
    }

    private void RenderHealth(HealthSnapshot snapshot, bool enabled)
    {
        ProxyStatusText.Text = enabled
            ? (snapshot.ProxyReachable ? "Прокси на роутере: доступен" : "Прокси на роутере: НЕ отвечает")
            : "Прокси выключен";
        ProxyStatusDot.Fill = new SolidColorBrush(enabled
            ? (snapshot.ProxyReachable ? Colors.SeaGreen : Colors.Firebrick)
            : Colors.Gray);

        CheckPointStatusText.Text = snapshot.CheckPointConnected
            ? "Check Point: подключён"
            : "Check Point: не подключён";
        CheckPointStatusDot.Fill = new SolidColorBrush(snapshot.CheckPointConnected
            ? Colors.SteelBlue
            : Colors.Gray);

        DomainsItemsControl.ItemsSource = snapshot.TunneledDomains.Count == 0
            ? new[] { "(не удалось прочитать PAC с роутера)" }
            : snapshot.TunneledDomains.Select(d => "*." + d).ToArray();
    }

    private void OnMasterToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        if (ProxyToggle.IsEnabled())
        {
            ProxyToggle.Disable();
        }
        else
        {
            ProxyToggle.Enable();
        }

        RefreshToggles();
        _ = RefreshHealthAsync();
    }

    private void OnAutoStartToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AutoStartManager.SetEnabled(AutoStartToggle.IsChecked == true);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshHealthAsync();
}
