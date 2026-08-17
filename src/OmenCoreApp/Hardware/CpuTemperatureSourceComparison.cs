using System.Threading.Tasks;

namespace OmenCore.Hardware
{
    /// <summary>
    /// One-shot, read-only snapshot of every CPU temperature source OmenCore knows how to read,
    /// for a first-class "does this temperature look right?" diagnostic. Before this existed, a
    /// user with a wrong-looking reading had no way to see WMI BIOS / ACPI Thermal Zone / LHM
    /// fallback side by side without a maintainer reading raw log files by hand — every field
    /// report that led to the ACPI zone-selection fix took several round-trips for exactly that
    /// reason.
    /// </summary>
    public sealed class CpuTemperatureSourceComparison
    {
        public double? WmiBiosTempC { get; init; }
        public double? AcpiTempC { get; init; }
        public string? AcpiZoneName { get; init; }
        public double? LhmTempC { get; init; }
        public string CurrentAuthoritySource { get; init; } = string.Empty;
        public string CurrentAuthorityReason { get; init; } = string.Empty;

        /// <summary>
        /// Intentionally the same threshold WmiBiosMonitor.MaxAcpiDeltaFromWmiC already uses to
        /// reject an implausible live ACPI reading — a spread bigger than this between any two
        /// available sources is the exact signal this check exists to surface, not sensor/timing
        /// noise. Kept as a local constant rather than referencing the private field directly, so
        /// this comparison stays independently testable from WmiBiosMonitor's live state.
        /// </summary>
        public const double DisagreementThresholdC = 18.0;

        /// <summary>
        /// True when at least two available sources disagree by more than a plausible margin.
        /// </summary>
        public bool SourcesDisagreeMaterially
        {
            get
            {
                double? min = null;
                double? max = null;
                foreach (var value in new[] { WmiBiosTempC, AcpiTempC, LhmTempC })
                {
                    if (!value.HasValue)
                    {
                        continue;
                    }

                    if (min == null || value < min) min = value;
                    if (max == null || value > max) max = value;
                }

                return min.HasValue && max.HasValue && (max.Value - min.Value) > DisagreementThresholdC;
            }
        }

        public int AvailableSourceCount =>
            (WmiBiosTempC.HasValue ? 1 : 0) + (AcpiTempC.HasValue ? 1 : 0) + (LhmTempC.HasValue ? 1 : 0);
    }

    /// <summary>
    /// Narrow abstraction over WmiBiosMonitor's temperature-source comparison, so ViewModels that
    /// need it can be unit-tested without depending on the concrete WMI-coupled class directly —
    /// the same shape as IFanVerificationService for the fan diagnostics side.
    /// </summary>
    public interface ICpuTemperatureSourceComparer
    {
        Task<CpuTemperatureSourceComparison> GetCpuTemperatureSourceComparisonAsync();
    }
}
