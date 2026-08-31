using OmenCore.Services;

namespace OmenCore
{
    /// <summary>
    /// Process-wide Logging/Configuration singletons. These used to live directly on WPF's
    /// App class (OmenCoreApp/App.xaml.cs) and get reached via a bare "App.Logging" /
    /// "App.Configuration" from anywhere in the OmenCore.* namespace tree, relying on C#'s
    /// nested-namespace lookup (OmenCore.Services/OmenCore.Hardware are nested under OmenCore).
    /// That trick stopped working the moment those call sites moved into this separate
    /// assembly, since Core can't reference OmenCoreApp at all without a circular reference.
    ///
    /// Moved here instead of introducing a new pattern: OmenCoreApp's App.Logging /
    /// App.Configuration now forward to this class, so every existing call site in the WPF
    /// host keeps working unchanged, and Core-layer code reaches the exact same singleton
    /// instances through OmenCore.AppHost.Logging / .Configuration.
    /// </summary>
    public static class AppHost
    {
        public static LoggingService Logging { get; } = new();
        public static ConfigurationService Configuration { get; } = new();
    }
}
