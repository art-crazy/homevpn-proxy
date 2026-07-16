using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace HomeVpnProxyTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.IsFirstInstance)
        {
            // Another instance already owns the tray icon - ask it to
            // show its window and quit instead of spawning a second one.
            singleInstance.NotifyExistingInstance();
            return;
        }

        // The tray icon (System.Windows.Forms.NotifyIcon has no WPF
        // equivalent) drives a WinForms message loop, but the popup and
        // settings windows are WPF/WPF-UI for the Fluent look. A live
        // System.Windows.Application is needed for WPF resources/dispatcher
        // even though WinForms owns Application.Run() below - the two
        // share the same UI thread without conflict.
        var appTheme = ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;

        var wpfApp = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        wpfApp.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = appTheme });
        wpfApp.Resources.MergedDictionaries.Add(new ControlsDictionary());
        ApplicationThemeManager.Apply(appTheme);

        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();

        var context = new TrayApplicationContext();
        singleInstance.ShowRequested += (_, _) =>
            wpfApp.Dispatcher.Invoke(context.ShowMainWindow);
        singleInstance.StartListening();

        System.Windows.Forms.Application.Run(context);
    }
}
