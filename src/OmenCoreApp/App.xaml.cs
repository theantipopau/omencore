using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OmenCore.Services;

namespace OmenCore
{
    public partial class App : Application
    {
        public static LoggingService Logging { get; } = new();
        public static ConfigurationService Configuration { get; } = new();

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Logging.Initialize();
            Logging.Info("OmenCore starting up");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logging.Info("OmenCore shutting down");
            Logging.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logging.Error("Unhandled UI thread exception", e.Exception);
            e.Handled = true;
            ShowFatalDialog(e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logging.Error("Unhandled AppDomain exception", ex);
                ShowFatalDialog(ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logging.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
            ShowFatalDialog(e.Exception);
        }

        private static void ShowFatalDialog(Exception ex)
        {
            MessageBox.Show($"OmenCore hit an unexpected error:\n{ex.Message}\n\nSee %LOCALAPPDATA%\\OmenCore for full logs.", "OmenCore Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown();
        }
    }
}
