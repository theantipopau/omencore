using System;
using System.Threading.Tasks;

namespace OmenCore.Utils
{
    /// <summary>
    /// Lets a handful of Core-layer services (Corsair/Logitech device discovery, hardware
    /// monitoring's sample buffer, thermal sensor reads, notifications) defer UI-thread
    /// marshaling to whatever host is running them, without Core itself referencing WPF.
    ///
    /// Extracted from direct <c>System.Windows.Application.Current?.Dispatcher</c> calls during
    /// the OmenCore.Core split (docs/ROADMAP_v4.2.1.md, "v4.3.0 candidate slate" item 1) - those
    /// five call sites were the only concrete WPF coupling standing between those services and
    /// Core, and the actual logic ("is there a UI thread, and are we already on it") doesn't
    /// care which UI framework answers it.
    ///
    /// Defaults below assume no UI thread exists (correct for tests and a future headless/CLI
    /// host: just run inline). OmenCoreApp's WPF host wires the real Dispatcher-backed behavior
    /// in once, at startup, before any of these services can be touched - see
    /// App.xaml.cs's WireUiThreadMarshaller().
    /// </summary>
    public static class UiThreadMarshaller
    {
        /// <summary>
        /// Marshal to the UI thread and await completion if one exists and we're not already on
        /// it; otherwise run inline. Mirrors the "dispatcher != null &amp;&amp; !CheckAccess()"
        /// guard every original call site used.
        /// </summary>
        public static Func<Action, Task> InvokeAsync = action =>
        {
            action();
            return Task.CompletedTask;
        };

        /// <summary>
        /// Fire-and-forget marshal to the UI thread (or run inline if already there / no UI
        /// thread exists). Mirrors DispatcherHelper.RunOnUiThread's behavior exactly.
        /// </summary>
        public static Action<Action> BeginInvoke = action => action();

        /// <summary>True if the calling thread is a wired-up UI thread. False (the default) is
        /// the safe answer when no UI host has registered one.</summary>
        public static Func<bool> IsOnUiThread = () => false;

        /// <summary>
        /// True if the host wants window activation / activation-triggered actions (e.g. the
        /// OMEN key) suppressed right now - originally OmenCoreApp.App.ShouldSuppressWindowActivation,
        /// which tracks RDP/lock session-switch state via Microsoft.Win32.SystemEvents. False
        /// (the default) is correct for a host with no such window/session concept.
        /// </summary>
        public static Func<bool> ShouldSuppressActivation = () => false;
    }
}
