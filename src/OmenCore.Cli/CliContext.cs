using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Cli;

/// <summary>
/// One-shot hardware bootstrap for the CLI, built the same way MainViewModel's constructor
/// builds it (src/OmenCoreApp/ViewModels/MainViewModel.cs, ~line 2490 onward) - HardwareBringup
/// does the shared NVAPI/PawnIO/WmiBiosMonitor/capability-detection/EC-access/fan-controller
/// sequence, and FanService/PerformanceModeService are constructed on top of it exactly the way
/// the WPF app constructs them, so a CLI command exercises the same validated code paths a GUI
/// action would, not a second implementation of hardware control.
///
/// Deliberately does NOT dispose FanService when a command finishes. FanService.Dispose() resets
/// the EC back to BIOS auto-control - correct for the WPF app quitting, wrong for a CLI: a
/// one-shot "fan --profile quiet" call is supposed to leave the fan on quiet after the process
/// exits, the same way the Linux CLI's writes persist past the invocation that made them.
/// </summary>
public sealed class CliContext
{
    public LoggingService Logging { get; }
    public AppConfig Config { get; }
    public HardwareBringup Bringup { get; }
    public FanService FanService { get; }
    public PerformanceModeService PerformanceModeService { get; }

    private CliContext(
        LoggingService logging,
        AppConfig config,
        HardwareBringup bringup,
        FanService fanService,
        PerformanceModeService performanceModeService)
    {
        Logging = logging;
        Config = config;
        Bringup = bringup;
        FanService = fanService;
        PerformanceModeService = performanceModeService;
    }

    public static CliContext Create()
    {
        var logging = AppHost.Logging;
        logging.Initialize();

        var config = AppHost.Configuration.Load();

        var bringup = new HardwareBringup(logging, config);

        // Toasts are a GUI affordance; a script driving the CLI in a loop shouldn't get spammed.
        var notificationService = new NotificationService(logging) { IsEnabled = false };

        var ecOperationCoordinator = new RuntimeEcOperationCoordinator(logging);
        var resumeRecoveryDiagnostics = new ResumeRecoveryDiagnosticsService();

        var fanService = new FanService(
            bringup.FanController,
            new ThermalSensorProvider(bringup.WmiBiosMonitor),
            logging,
            notificationService,
            config.MonitoringIntervalMs,
            resumeRecoveryDiagnostics,
            ecOperationCoordinator,
            bringup.Capabilities);
        fanService.SetHysteresis(config.FanHysteresis);
        fanService.SetSmoothingSettings(config.FanTransition);

        var powerPlanService = new PowerPlanService(logging);
        PowerLimitController? powerLimitController = null;
        if (bringup.EcAccess is { IsAvailable: true } ec)
        {
            try
            {
                powerLimitController = new PowerLimitController(ec, useSimplifiedMode: true);
            }
            catch (Exception ex)
            {
                logging.Warn($"Power limit controller unavailable: {ex.Message}");
            }
        }

        var performanceModeService = new PerformanceModeService(
            bringup.FanController,
            powerPlanService,
            powerLimitController,
            logging,
            modelCapabilities: bringup.Capabilities.ModelConfig,
            ecOperationCoordinator: ecOperationCoordinator)
        {
            LinkFanToPerformanceMode = config.LinkFanToPerformanceMode
        };

        return new CliContext(logging, config, bringup, fanService, performanceModeService);
    }
}
