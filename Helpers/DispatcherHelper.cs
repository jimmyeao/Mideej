using System.Windows;
using System.Windows.Threading;

namespace Mideej.Helpers;

/// <summary>
/// Helper class for working with the UI dispatcher
/// </summary>
public static class DispatcherHelper
{
    /// <summary>
    /// Executes an action on the UI thread
    /// </summary>
    public static void RunOnUIThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Executes an action on the UI thread synchronously
    /// </summary>
    public static void RunOnUIThreadSync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action, DispatcherPriority.Normal);
        }
    }
}
