namespace HomeVpnProxyTray;

/// <summary>
/// Ensures only one HomeVpnProxyTray process (and tray icon) runs at a
/// time. A second launch (e.g. double-clicking the shortcut again, or an
/// autostart racing a manual launch) detects the first instance via a
/// named mutex, signals it to bring its window to front, and exits
/// immediately instead of spawning a second tray icon.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    // Fixed GUID so this doesn't collide with anything else on the
    // machine; doesn't need to mean anything beyond being unique to
    // this app.
    private const string MutexName = @"Global\HomeVpnProxyTray-9F3D2E7B-6C1A-4B8F-9E2D-3A7C5F1B8D4E";
    private const string ShowEventName = @"Global\HomeVpnProxyTray-Show-9F3D2E7B-6C1A-4B8F-9E2D-3A7C5F1B8D4E";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;

    public bool IsFirstInstance { get; }

    public event EventHandler? ShowRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    /// <summary>Called by a second instance to ask the first one to show itself, then this process should just exit.</summary>
    public void NotifyExistingInstance() => _showEvent.Set();

    /// <summary>Called by the first instance to react whenever another launch attempt happens.</summary>
    public void StartListening()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _showEvent.Dispose();
    }
}
