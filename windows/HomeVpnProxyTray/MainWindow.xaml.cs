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
    private readonly List<string> _diagnosticsLog = new();
    private RouterConnectionSettings? _lastCheckedSettings;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);

        HttpProxyValueBox.Text = Constants.HttpProxyUrl;
        HttpsProxyValueBox.Text = Constants.HttpProxyUrl;
        AllProxyValueBox.Text = Constants.SocksProxyUrl;
        PacUrlValueBox.Text = Constants.PacUrl;

        DomainsItemsControl.ItemsSource = new[] { "Загрузка..." };

        RefreshToggles();
        LoadRouterSettings();

        // Not called directly here: the constructor runs before WPF's
        // Dispatcher message loop starts, so SynchronizationContext.Current
        // isn't set up yet and the continuation after the await below would
        // resume on a thread-pool thread instead of the UI thread - any UI
        // update then throws a cross-thread exception that a fire-and-forget
        // call swallows silently, leaving the "Загрузка..." placeholder
        // stuck forever. Loaded fires once the window is part of the
        // running dispatcher loop, so the continuation resumes correctly.
        Loaded += (_, _) => _ = RefreshAmbientInfoAsync();
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
        }
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

        RepairStatusText.Text = "Сохранено.";
    }

    // ---- Diagnostics: "Проверить" (read-only) and "Починить" (restarts the
    // service) are separate actions, sharing the same log below so nothing
    // either of them does happens silently. ----

    private void ClearLog()
    {
        DiagnosticsEmptyState.Visibility = Visibility.Collapsed;
        DiagnosticsScrollViewer.Visibility = Visibility.Visible;
        _diagnosticsLog.Clear();
        DiagnosticsLogText.Text = "";
    }

    private void Log(string line = "")
    {
        _diagnosticsLog.Add(line);
        DiagnosticsLogText.Text = string.Join("\n", _diagnosticsLog);
        DiagnosticsScrollViewer.ScrollToBottom();
    }

    private void SetOverallStatus(RouterProxyStatus status, string text)
    {
        OverallStatusText.Text = text;
        OverallStatusDot.Fill = new SolidColorBrush(status switch
        {
            RouterProxyStatus.Healthy => Colors.SeaGreen,
            RouterProxyStatus.Unhealthy => Colors.Firebrick,
            _ => Colors.Gray,
        });
    }

    private async void OnRunCheckClick(object sender, RoutedEventArgs e)
    {
        RunCheckButton.IsEnabled = false;
        RunFixButton.IsEnabled = false;
        _lastCheckedSettings = null;
        ClearLog();

        try
        {
            await RunCheckAsync();
        }
        finally
        {
            RunCheckButton.IsEnabled = true;
        }

        _ = RefreshAmbientInfoAsync();
    }

    private async Task RunCheckAsync()
    {
        if (!ProxyToggle.IsEnabled())
        {
            SetOverallStatus(RouterProxyStatus.Unknown, "Прокси выключен");
            Log("Прокси сейчас выключен (тумблер вверху) - включите его, чтобы проверить.");
            return;
        }

        SetOverallStatus(RouterProxyStatus.Unknown, "Проверяю...");

        Log("Локальная проверка (с этого ПК):");
        Log("$ " + HealthChecker.DescribeLocalCheckCommand());
        var localOk = await HealthChecker.ProbeProxyAsync();
        Log(localOk ? "→ OK" : "→ не отвечает");

        if (localOk)
        {
            SetOverallStatus(RouterProxyStatus.Healthy, "Прокси: работает");
            Log();
            Log("Всё в порядке.");
            return;
        }

        var settings = RouterSettingsStore.Load();
        if (settings is null)
        {
            SetOverallStatus(RouterProxyStatus.Unhealthy, "Прокси: не отвечает с этого ПК");
            Log();
            Log("Настройте подключение к роутеру ниже, чтобы диагностировать и починить.");
            return;
        }

        Log();
        Log("Подключаюсь к роутеру по SSH...");
        var (session, connectError) = await RouterSshSession.ConnectAsync(settings);
        if (session is null)
        {
            SetOverallStatus(RouterProxyStatus.Unknown, "Не удалось подключиться к роутеру");
            Log("→ " + connectError);
            return;
        }

        using (session)
        {
            Log("Проверка прокси на роутере:");
            Log("$ " + RouterSshSession.Describe(settings, RouterSshSession.CheckCommand));
            var code = await session.RunAsync(RouterSshSession.CheckCommand);
            var routerHealthy = RouterSshSession.IsHealthyHttpCode(code);
            Log("→ " + (routerHealthy ? $"HTTP {code}" : "нет ответа"));

            if (routerHealthy)
            {
                SetOverallStatus(RouterProxyStatus.Unhealthy, "Прокси: работает на роутере, но недоступен с этого ПК");
                Log();
                Log("Прокси на роутере в порядке - проблема, похоже, в сети между компьютером и роутером (Wi-Fi, Check Point и т.п.), а не в самом прокси. Перезапуск сервиса это не починит.");
                return;
            }

            SetOverallStatus(RouterProxyStatus.Unhealthy, "Прокси: не отвечает на роутере");
            Log();
            Log("Диагностика состояния сервиса:");
            await LogStep(session, "статус сервиса", RouterSshSession.ProcdStatusCommand, settings);
            await LogStep(session, "процесс sing-box", RouterSshSession.ProcessCheckCommand, settings);
            await LogStep(session, "network namespace", RouterSshSession.NetnsCheckCommand, settings);
            await LogStep(session, "veth в мосту br-lan", RouterSshSession.VethCheckCommand, settings);
            var arp = await LogStep(session, "ARP для 192.168.2.250", RouterSshSession.ArpCheckCommand, settings);
            if (string.IsNullOrEmpty(arp))
            {
                Log("  (нет записи)");
            }

            Log();
            Log("Нажмите «Починить», чтобы перезапустить сервис на роутере.");

            _lastCheckedSettings = settings;
            RunFixButton.IsEnabled = true;
        }
    }

    private async void OnRunFixClick(object sender, RoutedEventArgs e)
    {
        var settings = _lastCheckedSettings ?? RouterSettingsStore.Load();
        if (settings is null) return;

        RunCheckButton.IsEnabled = false;
        RunFixButton.IsEnabled = false;
        ClearLog();

        try
        {
            await RunFixAsync(settings);
        }
        finally
        {
            RunCheckButton.IsEnabled = true;
        }

        _ = RefreshAmbientInfoAsync();
    }

    private async Task RunFixAsync(RouterConnectionSettings settings)
    {
        SetOverallStatus(RouterProxyStatus.Unknown, "Чиню...");
        Log("Подключаюсь к роутеру по SSH...");
        var (session, connectError) = await RouterSshSession.ConnectAsync(settings);
        if (session is null)
        {
            SetOverallStatus(RouterProxyStatus.Unknown, "Не удалось подключиться к роутеру");
            Log("→ " + connectError);
            RunFixButton.IsEnabled = true;
            return;
        }

        using (session)
        {
            Log("Перезапуск сервиса:");
            Log("$ " + RouterSshSession.Describe(settings, RouterSshSession.RestartCommand));
            await session.RunAsync(RouterSshSession.RestartCommand);
            Log("→ выполнено, жду 3 секунды...");
            await Task.Delay(3000);

            Log();
            Log("Повторная проверка на роутере:");
            Log("$ " + RouterSshSession.Describe(settings, RouterSshSession.CheckCommand));
            var code = await session.RunAsync(RouterSshSession.CheckCommand);
            var routerHealthyAfter = RouterSshSession.IsHealthyHttpCode(code);
            Log("→ " + (routerHealthyAfter ? $"HTTP {code}" : "нет ответа"));

            Log();
            Log("Повторная локальная проверка (с этого ПК):");
            Log("$ " + HealthChecker.DescribeLocalCheckCommand());
            var localOk = await HealthChecker.ProbeProxyAsync();
            Log("→ " + (localOk ? "OK" : "не отвечает"));

            Log();
            if (localOk)
            {
                SetOverallStatus(RouterProxyStatus.Healthy, "Прокси: починен, работает");
                Log("Проблема устранена.");
            }
            else if (routerHealthyAfter)
            {
                SetOverallStatus(RouterProxyStatus.Unhealthy, "Прокси: работает на роутере, но всё ещё недоступен с этого ПК");
                Log("Сервис на роутере починен, но с этого ПК всё равно не отвечает - проверьте сеть/Check Point.");
            }
            else
            {
                SetOverallStatus(RouterProxyStatus.Unhealthy, "Прокси: не удалось починить");
                Log("Перезапуск не помог - нужна ручная диагностика на роутере.");
                RunFixButton.IsEnabled = true;
            }
        }
    }

    private async Task<string> LogStep(RouterSshSession session, string label, string command, RouterConnectionSettings settings)
    {
        Log($"$ {RouterSshSession.Describe(settings, command)}");
        var result = await session.RunAsync(command);
        Log($"→ {label}: {result}");
        return result;
    }

    // ---- Ambient info: Check Point + tunneled domain list, always current, not part of the diagnostic run ----

    private async Task RefreshAmbientInfoAsync()
    {
        var enabled = ProxyToggle.IsEnabled();
        var (checkPointConnected, domains) = enabled
            ? await HealthChecker.GetAmbientInfoAsync()
            : (SafeCheckPointConnected(), Array.Empty<string>());

        CheckPointStatusText.Text = checkPointConnected
            ? "Check Point: подключён"
            : "Check Point: не подключён";
        CheckPointStatusDot.Fill = new SolidColorBrush(checkPointConnected
            ? Colors.SteelBlue
            : Colors.Gray);

        DomainsItemsControl.ItemsSource = !enabled
            ? new[] { "(прокси выключен)" }
            : domains.Count == 0
                ? new[] { "(не удалось прочитать PAC с роутера)" }
                : domains.Select(d => "*." + d).ToArray();
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
        _ = RefreshAmbientInfoAsync();
    }

    private void OnAutoStartToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AutoStartManager.SetEnabled(AutoStartToggle.IsChecked == true);
    }
}
