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
    // A placeholder Password value shown when a password is already saved,
    // so the field visually reads as "filled in" (PasswordBox always masks
    // whatever's in it with dots) without ever redisplaying the real
    // password. Tracked separately from _routerPasswordTouched so Save
    // knows whether the user actually typed something new or just left
    // this placeholder alone.
    private const string RouterPasswordPlaceholder = "••••••••••";

    private bool _suppressEvents;
    private bool _routerPasswordTouched;

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
        LoadRouterSettings();
    }

    private void LoadRouterSettings()
    {
        var settings = RouterSettingsStore.Load();
        if (settings is not null)
        {
            RouterHostBox.Text = settings.Host;
            RouterUsernameBox.Text = settings.Username;

            _suppressEvents = true;
            RouterPasswordBox.Password = RouterPasswordPlaceholder;
            _suppressEvents = false;
            _routerPasswordTouched = false;

            RepairStatusText.Text = "Данные для входа сохранены. Оставьте пароль как есть, если не хотите его менять.";
        }

        CheckStatusButton.IsEnabled = RouterSettingsStore.IsConfigured();
        RenderRouterStatus(RouterProxyStatus.Unknown, "Статус не проверен");
    }

    private void OnRouterPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _routerPasswordTouched = true;
    }

    private void OnSaveRouterSettingsClick(object sender, RoutedEventArgs e)
    {
        var host = RouterHostBox.Text.Trim();
        var username = RouterUsernameBox.Text.Trim();

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
        {
            RepairStatusText.Text = "Укажите IP и логин.";
            return;
        }

        string password;
        if (_routerPasswordTouched)
        {
            password = RouterPasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                RepairStatusText.Text = "Пароль не может быть пустым.";
                return;
            }
        }
        else
        {
            // Field still shows our placeholder (or is genuinely empty on
            // first-ever setup) - keep whatever was already saved.
            var existing = RouterSettingsStore.Load();
            if (existing is null)
            {
                RepairStatusText.Text = "Укажите пароль.";
                return;
            }
            password = existing.Password;
        }

        RouterSettingsStore.Save(host, username, password);

        _suppressEvents = true;
        RouterPasswordBox.Password = RouterPasswordPlaceholder;
        _suppressEvents = false;
        _routerPasswordTouched = false;

        CheckStatusButton.IsEnabled = true;
        RepairStatusText.Text = "Сохранено.";
    }

    private void RenderRouterStatus(RouterProxyStatus status, string message)
    {
        RouterCheckStatusText.Text = message;
        RouterCheckDot.Fill = new SolidColorBrush(status switch
        {
            RouterProxyStatus.Healthy => Colors.SeaGreen,
            RouterProxyStatus.Unhealthy => Colors.Firebrick,
            _ => Colors.Gray,
        });
        FixButton.IsEnabled = status == RouterProxyStatus.Unhealthy;
    }

    private async void OnCheckStatusClick(object sender, RoutedEventArgs e)
    {
        var settings = RouterSettingsStore.Load();
        if (settings is null)
        {
            RepairStatusText.Text = "Сначала сохраните данные для входа.";
            return;
        }

        CheckStatusButton.IsEnabled = false;
        RenderRouterStatus(RouterProxyStatus.Unknown, "Проверяю...");
        try
        {
            var result = await RouterRepair.CheckAsync(settings);
            RenderRouterStatus(result.Status, result.Message);
        }
        finally
        {
            CheckStatusButton.IsEnabled = true;
        }
    }

    private async void OnFixClick(object sender, RoutedEventArgs e)
    {
        var settings = RouterSettingsStore.Load();
        if (settings is null) return;

        CheckStatusButton.IsEnabled = false;
        FixButton.IsEnabled = false;
        RenderRouterStatus(RouterProxyStatus.Unknown, "Чиню...");
        try
        {
            var result = await RouterRepair.FixAsync(settings);
            RenderRouterStatus(result.Status, result.Message);
        }
        finally
        {
            CheckStatusButton.IsEnabled = true;
        }

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
            ? (snapshot.ProxyReachable ? "Прокси: доступен с этого ПК" : "Прокси: НЕ отвечает с этого ПК")
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
