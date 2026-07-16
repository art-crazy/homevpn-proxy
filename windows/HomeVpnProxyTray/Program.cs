namespace HomeVpnProxyTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--window"))
        {
            RunWindowProcess();
        }
        else
        {
            RunSupervisorProcess();
        }
    }

    /// <summary>
    /// Always-on tray icon process. Deliberately never touches any
    /// System.Windows.* (WPF) type - the JIT resolves/loads an assembly
    /// the first time a method that references it actually runs, so
    /// keeping this whole call path WPF-free means PresentationCore/
    /// PresentationFramework etc. never get loaded here at all. This is
    /// most of why the idle footprint is small: no WPF, no WPF-UI
    /// resource dictionaries, just WinForms for the NotifyIcon.
    /// </summary>
    private static void RunSupervisorProcess()
    {
        using var singleInstance = new SingleInstanceGuard("Supervisor");
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.NotifyExistingInstance();
            return;
        }

        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();

        var context = new TrayApplicationContext();
        singleInstance.ShowRequested += (_, _) => context.RequestWindow();
        singleInstance.StartListening();

        System.Windows.Forms.Application.Run(context);
    }

    /// <summary>
    /// Launched on demand (see TrayApplicationContext.RequestWindow) with
    /// "--window" whenever the user actually wants to see the settings
    /// window. This is the only place that bootstraps WPF/WPF-UI, and the
    /// whole process exits the moment the window closes - all of that
    /// memory goes back to the OS instead of sitting around in an
    /// always-running tray process.
    /// </summary>
    private static void RunWindowProcess()
    {
        using var singleInstance = new SingleInstanceGuard("Window");
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.NotifyExistingInstance();
            return;
        }

        RunWindowProcessInner(singleInstance);
    }

    // Split into its own method so RunWindowProcess() above never forces
    // the JIT to resolve WPF types on the "another window is already
    // open" fast-exit path.
    private static void RunWindowProcessInner(SingleInstanceGuard singleInstance)
    {
        var appTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetSystemTheme() == Wpf.Ui.Appearance.SystemTheme.Dark
            ? Wpf.Ui.Appearance.ApplicationTheme.Dark
            : Wpf.Ui.Appearance.ApplicationTheme.Light;

        var wpfApp = new System.Windows.Application();
        wpfApp.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = appTheme });
        wpfApp.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(appTheme);

        var window = new MainWindow();
        singleInstance.ShowRequested += (_, _) => wpfApp.Dispatcher.Invoke(() =>
        {
            window.WindowState = System.Windows.WindowState.Normal;
            window.Activate();
        });
        singleInstance.StartListening();

        wpfApp.Run(window);
    }
}
