namespace HomeVpnProxyTray;

/// <summary>
/// Ensures only one process of a given "scope" runs at a time - used
/// separately for the tray supervisor (only one tray icon ever) and for
/// the window process (only one settings window ever), each independent
/// of the other. A second launch in the same scope detects the first
/// instance via a named mutex, signals it to come to front, and exits
/// immediately instead of spawning a duplicate.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    // Fixed GUID so this doesn't collide with anything else on the
    // machine; doesn't need to mean anything beyond being unique to
    // this app. Combined with the caller-supplied scope so the
    // supervisor and window processes don't fight over the same lock.
    private const string GuidSuffix = "9F3D2E7B-6C1A-4B8F-9E2D-3A7C5F1B8D4E";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;

    public bool IsFirstInstance { get; }

    public event EventHandler? ShowRequested;

    public SingleInstanceGuard(string scope)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Global\HomeVpnProxyTray-{scope}-{GuidSuffix}", out var createdNew);
        IsFirstInstance = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Global\HomeVpnProxyTray-{scope}-Show-{GuidSuffix}");
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
