using System;
using System.Threading.Tasks;
using System.Windows;

namespace OmenCore.Utils
{
    /// <summary>
    /// Thread-safe utility methods for WPF dispatcher operations.
    /// Provides consistent patterns for UI thread marshaling.
    /// </summary>
    public static class DispatcherHelper
    {
        /// <summary>
        /// Execute an action on the UI thread asynchronously (non-blocking).
        /// Safe to call even during application shutdown.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public static void RunOnUiThread(Action action)
        {
            if (action == null) return;

            var app = Application.Current;
            if (app == null) return;

            var dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess())
            {
                // Already on UI thread
                action();
            }
            else
            {
                // Marshal to UI thread - use BeginInvoke to avoid deadlocks
                dispatcher.BeginInvoke(action);
            }
        }

        /// <summary>
        /// Execute an action on the UI thread synchronously (blocking).
        /// Use sparingly - prefer RunOnUiThread for non-blocking operations.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public static void RunOnUiThreadSync(Action action)
        {
            if (action == null) return;

            var app = Application.Current;
            if (app == null) return;

            var dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Execute an async action on the UI thread.
        /// </summary>
        /// <param name="asyncAction">The async action to execute.</param>
        public static async Task RunOnUiThreadAsync(Func<Task> asyncAction)
        {
            if (asyncAction == null) return;

            var app = Application.Current;
            if (app == null) return;

            var dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess())
            {
                await asyncAction().ConfigureAwait(false);
            }
            else
            {
                // InvokeAsync returns DispatcherOperation<Task>; await twice to wait for
                // the async action itself to complete, not just to be dispatched.
                await await dispatcher.InvokeAsync(asyncAction);
            }
        }

        /// <summary>
        /// Check if currently on the UI thread.
        /// </summary>
        public static bool IsOnUiThread()
        {
            var dispatcher = Application.Current?.Dispatcher;
            return dispatcher?.CheckAccess() ?? false;
        }
    }
}
