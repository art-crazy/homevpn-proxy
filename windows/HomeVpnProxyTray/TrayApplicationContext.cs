using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HomeVpnProxyTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _toggleMenuItem;

    private MainWindow? _mainWindow;
    private HealthSnapshot _lastSnapshot = new(false, false, Array.Empty<string>(), null);
    private bool _checking;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayApplicationContext()
    {
        _toggleMenuItem = new ToolStripMenuItem("Включить", null, (_, _) => Toggle());
        var openItem = new ToolStripMenuItem("Открыть", null, (_, _) => ShowMainWindow());
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = MakeStatusIcon(TrayState.Disabled),
            Text = "HomeVPN Proxy",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseUp += OnTrayMouseUp;

        _timer = new System.Windows.Forms.Timer { Interval = Constants.HealthCheckIntervalMs };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private void OnTrayMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.ToggleRequested += (_, _) =>
        {
            Toggle();
            _mainWindow?.RefreshToggles();
        };
        _mainWindow.RefreshRequested += async (_, _) => await RefreshAsync();
        _mainWindow.RenderHealth(_lastSnapshot, ProxyToggle.IsEnabled());
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void Toggle()
    {
        if (ProxyToggle.IsEnabled())
        {
            ProxyToggle.Disable();
        }
        else
        {
            ProxyToggle.Enable();
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var enabled = ProxyToggle.IsEnabled();
            _lastSnapshot = enabled
                ? await HealthChecker.RunAsync()
                : new HealthSnapshot(false, SafeCheckPointConnected(), Array.Empty<string>(), null);

            UpdateTrayVisuals(enabled);

            if (_mainWindow is { IsVisible: true })
            {
                _mainWindow.RenderHealth(_lastSnapshot, enabled);
            }
        }
        finally
        {
            _checking = false;
        }
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

    private void UpdateTrayVisuals(bool enabled)
    {
        var state = !enabled
            ? TrayState.Disabled
            : _lastSnapshot.ProxyReachable
                ? TrayState.Healthy
                : TrayState.Unreachable;

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = MakeStatusIcon(state);
        oldIcon?.Dispose();

        _toggleMenuItem.Text = enabled ? "Выключить" : "Включить";

        _notifyIcon.Text = state switch
        {
            TrayState.Healthy => "HomeVPN Proxy - работает",
            TrayState.Unreachable => "HomeVPN Proxy - роутер не отвечает",
            _ => "HomeVPN Proxy - выключен",
        };
    }

    private static Icon MakeStatusIcon(TrayState state)
    {
        var color = state switch
        {
            TrayState.Healthy => Color.SeaGreen,
            TrayState.Unreachable => Color.Goldenrod,
            _ => Color.Gray,
        };

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 3, 3, 26, 26);
            using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1.5f);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }

        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _timer.Stop();
        _timer.Dispose();
        _mainWindow?.Close();
        System.Windows.Forms.Application.Exit();
    }

    private enum TrayState
    {
        Disabled,
        Healthy,
        Unreachable,
    }
}
