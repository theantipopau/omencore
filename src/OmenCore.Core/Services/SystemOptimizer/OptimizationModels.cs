using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Services.SystemOptimizer
{
    /// <summary>
    /// Represents the current state of all optimizations.
    /// </summary>
    public class OptimizationState
    {
        public PowerOptimizationState Power { get; set; } = new();
        public ServiceOptimizationState Services { get; set; } = new();
        public NetworkOptimizationState Network { get; set; } = new();
        public InputOptimizationState Input { get; set; } = new();
        public VisualOptimizationState Visual { get; set; } = new();
        public StorageOptimizationState Storage { get; set; } = new();
        
        public DateTime LastChecked { get; set; }
        
        public int ActiveCount => 
            Power.ActiveCount + Services.ActiveCount + Network.ActiveCount + 
            Input.ActiveCount + Visual.ActiveCount + Storage.ActiveCount;
            
        public int TotalCount =>
            Power.TotalCount + Services.TotalCount + Network.TotalCount +
            Input.TotalCount + Visual.TotalCount + Storage.TotalCount;
    }

    public class PowerOptimizationState
    {
        public bool UltimatePerformancePlan { get; set; }
        public bool HardwareGpuScheduling { get; set; }
        public bool GameModeEnabled { get; set; }
        public bool ForegroundPriority { get; set; }
        
        public int ActiveCount => (UltimatePerformancePlan ? 1 : 0) + (HardwareGpuScheduling ? 1 : 0) + 
            (GameModeEnabled ? 1 : 0) + (ForegroundPriority ? 1 : 0);
        public int TotalCount => 4;
    }

    public class ServiceOptimizationState
    {
        public bool TelemetryDisabled { get; set; }
        public bool SysMainDisabled { get; set; }      // Superfetch
        public bool SearchIndexingDisabled { get; set; }
        public bool DiagTrackDisabled { get; set; }    // Connected User Experiences
        
        public int ActiveCount => (TelemetryDisabled ? 1 : 0) + (SysMainDisabled ? 1 : 0) + 
            (SearchIndexingDisabled ? 1 : 0) + (DiagTrackDisabled ? 1 : 0);
        public int TotalCount => 4;
    }

    public class NetworkOptimizationState
    {
        public bool TcpNoDelay { get; set; }
        public bool TcpAckFrequency { get; set; }
        public bool DeliveryOptimizationDisabled { get; set; }
        public bool NagleDisabled { get; set; }
        
        public int ActiveCount => (TcpNoDelay ? 1 : 0) + (TcpAckFrequency ? 1 : 0) + 
            (DeliveryOptimizationDisabled ? 1 : 0) + (NagleDisabled ? 1 : 0);
        public int TotalCount => 4;
    }

    public class InputOptimizationState
    {
        public bool MouseAccelerationDisabled { get; set; }
        public bool GameDvrDisabled { get; set; }
        public bool GameBarDisabled { get; set; }
        public bool FullscreenOptimizationsDisabled { get; set; }
        
        public int ActiveCount => (MouseAccelerationDisabled ? 1 : 0) + (GameDvrDisabled ? 1 : 0) + 
            (GameBarDisabled ? 1 : 0) + (FullscreenOptimizationsDisabled ? 1 : 0);
        public int TotalCount => 4;
    }

    public class VisualOptimizationState
    {
        public string Mode { get; set; } = "Default"; // Default, Balanced, Minimal
        public bool AnimationsDisabled { get; set; }
        public bool TransparencyDisabled { get; set; }
        
        public int ActiveCount => (AnimationsDisabled ? 1 : 0) + (TransparencyDisabled ? 1 : 0);
        public int TotalCount => 2;
    }

    public class StorageOptimizationState
    {
        public bool IsSsd { get; set; }
        public bool TrimEnabled { get; set; }
        public bool DefragDisabled { get; set; }
        public bool ShortNamesDisabled { get; set; }   // 8.3 filename creation
        public bool LastAccessDisabled { get; set; }
        
        public int ActiveCount => (TrimEnabled && IsSsd ? 1 : 0) + (DefragDisabled && IsSsd ? 1 : 0) + 
            (ShortNamesDisabled ? 1 : 0) + (LastAccessDisabled ? 1 : 0);
        public int TotalCount => 4;
    }

    /// <summary>
    /// Result of applying or reverting an optimization.
    /// </summary>
    public class OptimizationResult
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool RequiresReboot { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Defines an individual optimization setting.
    /// </summary>
    public class OptimizationDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public OptimizationRisk Risk { get; set; } = OptimizationRisk.Low;
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresReboot { get; set; }
        public bool IsRecommended { get; set; }
        public string? Warning { get; set; }
    }

    public enum OptimizationRisk
    {
        Low,        // Safe, no side effects
        Medium,     // May affect some functionality
        High        // Aggressive, may cause issues
    }

    /// <summary>
    /// A single operation entry in a pre-apply preflight report.
    /// </summary>
    public class PreflightItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public OptimizationRisk Risk { get; set; }
        public bool RequiresReboot { get; set; }
        public bool IsRecommended { get; set; }
        public string? Warning { get; set; }
    }

    /// <summary>
    /// Pre-apply preflight report produced by SystemOptimizerService.GeneratePreflightReportAsync.
    /// Lists every planned operation with its risk tier and any advisory warnings before any changes
    /// are made, so callers can present a summary or gate high-risk operations.
    /// </summary>
    public class PreflightReport
    {
        public IReadOnlyList<PreflightItem> Items { get; set; } = Array.Empty<PreflightItem>();
        public int LowRiskCount => Items.Count(i => i.Risk == OptimizationRisk.Low);
        public int MediumRiskCount => Items.Count(i => i.Risk == OptimizationRisk.Medium);
        public int HighRiskCount => Items.Count(i => i.Risk == OptimizationRisk.High);
        public bool HasHighRisk => HighRiskCount > 0;
        public bool RequiresReboot => Items.Any(i => i.RequiresReboot);
        public IReadOnlyList<PreflightItem> HighRiskItems => Items.Where(i => i.Risk == OptimizationRisk.High).ToList();
        public IReadOnlyList<string> Warnings => Items
            .Where(i => i.Warning != null)
            .Select(i => i.Warning!)
            .Distinct()
            .ToList();
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Describes a single optimization setting that has drifted away from the expected state.
    /// Used by SystemOptimizerService.GetDriftExplanations to surface human-readable drift messages
    /// such as "Windows Update restored SysMain (Superfetch)".
    /// </summary>
    public class OptimizationDriftItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        /// <summary>Human-readable explanation of what changed and what likely caused it.</summary>
        public string Explanation { get; set; } = "";
        /// <summary>Short suggestion for the user: "Re-apply Balanced/Gaming profile to restore this setting."</summary>
        public string Suggestion { get; set; } = "Re-apply the optimizer profile to restore this setting.";
    }

    /// <summary>
    /// Summary of optimizer state drift between an expected baseline and the current live state.
    /// </summary>
    public class OptimizationDriftSummary
    {
        public IReadOnlyList<OptimizationDriftItem> DriftedItems { get; set; } = Array.Empty<OptimizationDriftItem>();
        public bool HasDrift => DriftedItems.Count > 0;
        public int DriftCount => DriftedItems.Count;
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// One-line summary suitable for a status bar or notification.
        /// Returns empty string when there is no drift.
        /// </summary>
        public string OneLinerSummary => HasDrift
            ? $"{DriftCount} optimization{(DriftCount == 1 ? "" : "s")} drifted from expected state"
            : string.Empty;
    }
}
