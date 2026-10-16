using System.Windows.Threading;
using Microsoft.Win32;

namespace ERP.MES.Desktop.Session;

/// <summary>
/// Adapter from the Windows session events (<see cref="SystemEvents.SessionSwitch"/>) to the application. Windows raises them on its own thread, so each one is
/// handed to the UI thread. Disposing unsubscribes: the event is static and would otherwise keep the old session alive.
/// </summary>
public sealed class WindowsSessionMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<SessionSwitchReason> _onEvent;

    public WindowsSessionMonitor(Dispatcher dispatcher, Action<SessionSwitchReason> onEvent)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var reason = e.Reason;
        _dispatcher.BeginInvoke(() => _onEvent(reason));
    }
}
