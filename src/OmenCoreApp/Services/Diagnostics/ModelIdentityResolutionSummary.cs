using System;
using System.Text;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.KeyboardLighting;

namespace OmenCore.Services.Diagnostics
{
    public sealed class ModelIdentityResolutionSummary
    {
        public string RawManufacturer { get; set; } = "Unknown";
        public string RawWmiModel { get; set; } = "Unknown";
        public string RawBaseboardProduct { get; set; } = "Unknown";
        public string RawSystemSku { get; set; } = "Unknown";
        public string RawSystemProductIdentifyingNumber { get; set; } = "Unknown";
        public string HpSupportProductNumber { get; set; } = "Unknown";
        public string CapabilityProductId { get; set; } = "Unknown";
        public string CapabilityModelFamily { get; set; } = "Unknown";
        public string ResolvedModel { get; set; } = "Unknown";
        public string ResolutionSource { get; set; } = "Unknown";
        public string Confidence { get; set; } = "Low";
        public string BadgeText { get; set; } = "Unknown";
        public string BadgeTone { get; set; } = "warning";
        public string WarningText { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string NamePatternCandidate { get; set; } = "none";
        public string ProductIdCandidate { get; set; } = "none";
        public bool IsKnownModel { get; set; }
        public bool IsUserVerified { get; set; }
        public bool IsHpSystem { get; set; }
        public bool HasOmenVictusHint { get; set; }
        public string KeyboardProductIdCandidate { get; set; } = "Unknown";
        public bool KeyboardProductIdAmbiguous { get; set; }
        public string KeyboardModel { get; set; } = "Unknown";
        public string KeyboardResolutionSource { get; set; } = "Unknown";
        public string KeyboardConfidence { get; set; } = "Low";
        public string KeyboardWarningText { get; set; } = string.Empty;
        public string KeyboardNotes { get; set; } = string.Empty;
        public bool KeyboardUserVerified { get; set; }
        public string KeyboardDisambiguationPattern { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string RawIdentitySummary { get; set; } = string.Empty;
        public string ClipboardSummary { get; set; } = string.Empty;
        public string TraceText { get; set; } = string.Empty;
    }

    public static class ModelIdentityResolutionService
    {
        public static ModelIdentityResolutionSummary Build(SystemInfo systemInfo, DeviceCapabilities? capabilities = null, LoggingService? logging = null)
        {
            var effectiveCapabilities = capabilities;
            if (effectiveCapabilities == null)
            {
                using var capabilityService = new CapabilityDetectionService(logging);
                effectiveCapabilities = capabilityService.DetectCapabilities();
            }

            var summary = new ModelIdentityResolutionSummary
            {
                RawManufacturer = Clean(systemInfo.Manufacturer),
                RawWmiModel = Clean(systemInfo.Model),
                RawBaseboardProduct = Clean(systemInfo.ProductName),
                RawSystemSku = Clean(systemInfo.SystemSku),
                RawSystemProductIdentifyingNumber = Clean(systemInfo.SystemProductIdentifyingNumber),
                HpSupportProductNumber = ExtractHpSupportProductNumber(systemInfo.SystemSku),
                CapabilityProductId = Clean(effectiveCapabilities.ProductId),
                CapabilityModelFamily = effectiveCapabilities.ModelFamily.ToString(),
                IsKnownModel = effectiveCapabilities.IsKnownModel
            };

            summary.IsHpSystem = IsHpManufacturer(summary.RawManufacturer);
            summary.HasOmenVictusHint = ContainsOmenVictusHint(summary.RawWmiModel)
                || ContainsOmenVictusHint(summary.RawSystemSku)
                || ContainsOmenVictusHint(summary.HpSupportProductNumber)
                || ContainsOmenVictusHint(summary.RawBaseboardProduct)
                || ContainsOmenVictusHint(effectiveCapabilities.ModelName);

            PopulateCapabilityResolution(summary, effectiveCapabilities);
            PopulateKeyboardResolution(summary, systemInfo);

            summary.RawIdentitySummary = $"WMI Model: {summary.RawWmiModel} | Baseboard ProductId: {summary.RawBaseboardProduct} | System SKU: {summary.RawSystemSku} | System product identifying number: {summary.RawSystemProductIdentifyingNumber} | HP support product: {summary.HpSupportProductNumber}";
            summary.Summary = BuildShortSummary(summary);
            summary.ClipboardSummary = BuildClipboardSummary(summary);
            summary.TraceText = BuildTraceText(summary, systemInfo);

            return summary;
        }

        private static void PopulateCapabilityResolution(ModelIdentityResolutionSummary summary, DeviceCapabilities capabilities)
        {
            var modelNameMatch = ModelCapabilityDatabase.GetCapabilitiesByModelName(capabilities.ModelName);
            var productIdMatch = ModelCapabilityDatabase.IsKnownModel(capabilities.ProductId)
                ? ModelCapabilityDatabase.GetCapabilities(capabilities.ProductId)
                : null;
            var resolvedModel = capabilities.ModelConfig;

            summary.NamePatternCandidate = modelNameMatch != null
                ? $"{modelNameMatch.ModelName} ({modelNameMatch.ProductId})"
                : "none";
            summary.ProductIdCandidate = productIdMatch != null
                ? $"{productIdMatch.ModelName} ({productIdMatch.ProductId})"
                : "none";

            if (resolvedModel == null)
            {
                summary.ResolvedModel = "Unknown";
                summary.ResolutionSource = "No resolved capability profile";
                summary.Confidence = "Low";
                summary.BadgeText = "Unknown";
                summary.BadgeTone = "error";
                summary.WarningText = "Capability detection did not resolve a model profile.";
                return;
            }

            summary.ResolvedModel = $"{resolvedModel.ModelName} ({resolvedModel.ProductId})";
            summary.IsUserVerified = resolvedModel.UserVerified;
            summary.Notes = resolvedModel.Notes ?? string.Empty;

            var productIdIsAmbiguous = ModelCapabilityDatabase.IsAmbiguousProductId(capabilities.ProductId);
            if (!productIdIsAmbiguous &&
                productIdMatch != null &&
                string.Equals(resolvedModel.ProductId, productIdMatch.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                summary.ResolutionSource = "Exact ProductId";
                summary.Confidence = resolvedModel.UserVerified ? "High" : "Medium";
                summary.BadgeText = resolvedModel.UserVerified ? "Exact match" : "Unverified profile";
                summary.BadgeTone = resolvedModel.UserVerified ? "success" : "warning";
                if (!resolvedModel.UserVerified)
                {
                    summary.WarningText = "Capability profile matched by ProductId, but the entry is not user-verified yet.";
                }
                return;
            }

            if (modelNameMatch != null && string.Equals(resolvedModel.ProductId, modelNameMatch.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                summary.ResolutionSource = productIdIsAmbiguous
                    ? "Ambiguous ProductId + model-name disambiguation"
                    : "Model-name pattern";
                summary.Confidence = resolvedModel.UserVerified ? "Medium" : "Low";
                summary.BadgeText = "Inferred match";
                summary.BadgeTone = "warning";
                summary.WarningText = productIdIsAmbiguous
                    ? "Capability profile used the WMI model name to disambiguate a shared ProductId."
                    : "Capability profile was inferred from the WMI model name instead of an exact ProductId entry.";
                return;
            }

            if (resolvedModel.ProductId.StartsWith("FAMILY_", StringComparison.OrdinalIgnoreCase))
            {
                summary.ResolutionSource = "Family fallback";
                summary.Confidence = "Low";
                summary.BadgeText = "Family fallback";
                summary.BadgeTone = "error";
                summary.WarningText = BuildFallbackWarning(
                    summary,
                    "No exact model entry matched; capability defaults were inferred from the broader model family.",
                    "HP system detected, but no validated OMEN/Victus model mapping was found; capability defaults were inferred from a broad family match.",
                    "OMEN/Victus hints were detected, but the specific model could not be verified; capability defaults were inferred from a broad family match.");
                return;
            }

            if (string.Equals(resolvedModel.ProductId, ModelCapabilityDatabase.DefaultCapabilities.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                summary.ResolutionSource = "Default fallback";
                summary.Confidence = "Low";
                summary.BadgeText = "Default fallback";
                summary.BadgeTone = "error";
                summary.WarningText = BuildFallbackWarning(
                    summary,
                    "No exact model or family entry matched; default capabilities are in use.",
                    "HP system detected, but no validated OMEN/Victus mapping was found; generic default capabilities are in use.",
                    "OMEN/Victus hints were detected, but model identity confidence is low; generic default capabilities are in use.");
                return;
            }

            summary.ResolutionSource = "Runtime fallback";
            summary.Confidence = "Low";
            summary.BadgeText = "Fallback";
            summary.BadgeTone = "error";
            summary.WarningText = BuildFallbackWarning(
                summary,
                "Capability profile came from a runtime fallback path and should be treated as provisional.",
                "HP system detected, but capability profile came from a runtime fallback path; treat this mapping as provisional.",
                "OMEN/Victus hints were detected, but capability profile came from a runtime fallback path; treat this mapping as provisional.");
        }

        private static void PopulateKeyboardResolution(ModelIdentityResolutionSummary summary, SystemInfo systemInfo)
        {
            var keyboardProductIdCandidate = !string.IsNullOrWhiteSpace(systemInfo.ProductName)
                ? systemInfo.ProductName.Trim()
                : systemInfo.SystemSku?.Trim() ?? string.Empty;

            var directKeyboardMatch = KeyboardModelDatabase.GetConfig(keyboardProductIdCandidate);
            var keyboardModelNameMatch = KeyboardModelDatabase.GetConfigByModelName(systemInfo.Model);
            var effectiveKeyboardConfig = KeyboardModelDatabase.GetConfig(keyboardProductIdCandidate, systemInfo.Model) ?? keyboardModelNameMatch;
            var keyboardAmbiguous = KeyboardModelDatabase.IsAmbiguousProductId(keyboardProductIdCandidate);

            summary.KeyboardProductIdCandidate = string.IsNullOrWhiteSpace(keyboardProductIdCandidate)
                ? "Unknown"
                : keyboardProductIdCandidate;
            summary.KeyboardProductIdAmbiguous = keyboardAmbiguous;

            if (effectiveKeyboardConfig == null)
            {
                summary.KeyboardModel = "Unknown";
                summary.KeyboardResolutionSource = "No database match";
                summary.KeyboardConfidence = "Low";
                summary.KeyboardWarningText = "Keyboard profile could not be matched from the current ProductId or WMI model.";
                return;
            }

            summary.KeyboardModel = $"{effectiveKeyboardConfig.ModelName} ({effectiveKeyboardConfig.ProductId})";
            summary.KeyboardUserVerified = effectiveKeyboardConfig.UserVerified;
            summary.KeyboardNotes = effectiveKeyboardConfig.Notes ?? string.Empty;
            summary.KeyboardDisambiguationPattern = effectiveKeyboardConfig.ModelNamePattern ?? string.Empty;

            if (keyboardAmbiguous && !string.Equals(effectiveKeyboardConfig.ProductId, keyboardProductIdCandidate, StringComparison.OrdinalIgnoreCase))
            {
                summary.KeyboardResolutionSource = "Ambiguous ProductId + model-name disambiguation";
                summary.KeyboardConfidence = effectiveKeyboardConfig.UserVerified ? "Medium" : "Low";
                summary.KeyboardWarningText = "Keyboard ProductId is shared across models; the profile was selected using the WMI model context.";
                return;
            }

            if (directKeyboardMatch != null && string.Equals(directKeyboardMatch.ProductId, keyboardProductIdCandidate, StringComparison.OrdinalIgnoreCase))
            {
                summary.KeyboardResolutionSource = "Exact ProductId";
                summary.KeyboardConfidence = effectiveKeyboardConfig.UserVerified ? "High" : "Medium";
                if (!effectiveKeyboardConfig.UserVerified)
                {
                    summary.KeyboardWarningText = "Keyboard profile matched by ProductId, but the entry is not user-verified yet.";
                }
                return;
            }

            if (keyboardModelNameMatch != null && string.Equals(keyboardModelNameMatch.ProductId, effectiveKeyboardConfig.ProductId, StringComparison.OrdinalIgnoreCase))
            {
                summary.KeyboardResolutionSource = "Model-name series match";
                summary.KeyboardConfidence = effectiveKeyboardConfig.UserVerified ? "Medium" : "Low";
                summary.KeyboardWarningText = "Keyboard profile was inferred from the model series instead of an exact ProductId match.";
                return;
            }

            if (directKeyboardMatch != null)
            {
                summary.KeyboardResolutionSource = "Partial ProductId match";
                summary.KeyboardConfidence = effectiveKeyboardConfig.UserVerified ? "Medium" : "Low";
                summary.KeyboardWarningText = "Keyboard profile was selected from a partial ProductId match and should be treated as provisional.";
                return;
            }

            summary.KeyboardResolutionSource = "Heuristic match";
            summary.KeyboardConfidence = "Low";
            summary.KeyboardWarningText = "Keyboard profile came from a heuristic fallback path and should be validated before relying on it.";
        }

        private static string BuildShortSummary(ModelIdentityResolutionSummary summary)
        {
            var platformQualifier = summary.IsHpSystem
                    ? (summary.HasOmenVictusHint ? "HP system (OMEN/Victus detected)" : "HP system")
                : "Non-HP or unknown manufacturer";

            return $"{summary.ResolvedModel} via {summary.ResolutionSource} ({summary.Confidence} confidence) on {platformQualifier}. Keyboard: {summary.KeyboardModel} via {summary.KeyboardResolutionSource} ({summary.KeyboardConfidence} confidence).";
        }

        private static string BuildClipboardSummary(ModelIdentityResolutionSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OmenCore Model Identity Summary");
            sb.AppendLine($"Resolved model: {summary.ResolvedModel}");
            sb.AppendLine($"Resolution source: {summary.ResolutionSource}");
            sb.AppendLine($"Confidence: {summary.Confidence}");
            sb.AppendLine($"Capability ProductId: {summary.CapabilityProductId}");
            sb.AppendLine($"WMI model: {summary.RawWmiModel}");
            sb.AppendLine($"Baseboard ProductId: {summary.RawBaseboardProduct}");
            sb.AppendLine($"System SKU: {summary.RawSystemSku}");
            sb.AppendLine($"System product identifying number: {summary.RawSystemProductIdentifyingNumber}");
            sb.AppendLine($"HP support product number: {summary.HpSupportProductNumber}");
            sb.AppendLine($"Keyboard model: {summary.KeyboardModel}");
            sb.AppendLine($"Keyboard source: {summary.KeyboardResolutionSource}");
            sb.AppendLine($"Keyboard confidence: {summary.KeyboardConfidence}");

            if (!string.IsNullOrWhiteSpace(summary.WarningText))
            {
                sb.AppendLine($"Capability warning: {summary.WarningText}");
            }

            if (!string.IsNullOrWhiteSpace(summary.KeyboardWarningText))
            {
                sb.AppendLine($"Keyboard warning: {summary.KeyboardWarningText}");
            }

            if (!string.IsNullOrWhiteSpace(summary.Notes))
            {
                sb.AppendLine($"Capability notes: {summary.Notes}");
            }

            if (!string.IsNullOrWhiteSpace(summary.KeyboardNotes))
            {
                sb.AppendLine($"Keyboard notes: {summary.KeyboardNotes}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildTraceText(ModelIdentityResolutionSummary summary, SystemInfo systemInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MODEL IDENTITY RESOLUTION TRACE ===");
            sb.AppendLine($"Captured: {DateTime.Now:O}");
            sb.AppendLine();

            sb.AppendLine("Raw System Identity Inputs:");
            sb.AppendLine($"  Manufacturer: {Clean(systemInfo.Manufacturer)}");
            sb.AppendLine($"  WMI Model: {summary.RawWmiModel}");
            sb.AppendLine($"  Baseboard ProductId: {summary.RawBaseboardProduct}");
            sb.AppendLine($"  System SKU: {summary.RawSystemSku}");
            sb.AppendLine($"  System product identifying number: {summary.RawSystemProductIdentifyingNumber}");
            sb.AppendLine($"  HP support product number: {summary.HpSupportProductNumber}");
            sb.AppendLine($"  BIOS Version: {Clean(systemInfo.BiosVersion)}");
            sb.AppendLine("  Note: Baseboard ProductId drives OmenCore capability lookup; System SKU is the public support/catalog value. System product identifying number is reported separately because it may be an asset/serial-like identifier.");
            sb.AppendLine();

            sb.AppendLine("Capability Detection Output:");
            sb.AppendLine($"  ProductId: {summary.CapabilityProductId}");
            sb.AppendLine($"  ModelName: {summary.RawWmiModel}");
            sb.AppendLine($"  ModelFamily: {summary.CapabilityModelFamily}");
            sb.AppendLine($"  IsKnownModel: {(summary.IsKnownModel ? "yes" : "no")}");
            sb.AppendLine();

            sb.AppendLine("Model Capability Database Resolution:");
            sb.AppendLine($"  Name-pattern candidate: {summary.NamePatternCandidate}");
            sb.AppendLine($"  ProductId candidate: {summary.ProductIdCandidate}");
            sb.AppendLine($"  Effective resolved model: {summary.ResolvedModel}");
            sb.AppendLine($"  Resolution path: {summary.ResolutionSource}");
            sb.AppendLine($"  Confidence: {summary.Confidence}");
            sb.AppendLine($"  User-verified profile: {(summary.IsUserVerified ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(summary.WarningText))
            {
                sb.AppendLine($"  Attention: {summary.WarningText}");
            }
            if (!string.IsNullOrWhiteSpace(summary.Notes))
            {
                sb.AppendLine($"  Profile notes: {summary.Notes}");
            }

            sb.AppendLine();
            sb.AppendLine("Keyboard Model Resolution:");
            sb.AppendLine($"  ProductId candidate: {summary.KeyboardProductIdCandidate}");
            sb.AppendLine($"  ProductId ambiguous: {(summary.KeyboardProductIdAmbiguous ? "yes" : "no")}");
            sb.AppendLine($"  WMI Model context: {summary.RawWmiModel}");
            sb.AppendLine($"  Effective keyboard model: {summary.KeyboardModel}");
            sb.AppendLine($"  Resolution path: {summary.KeyboardResolutionSource}");
            sb.AppendLine($"  Confidence: {summary.KeyboardConfidence}");
            sb.AppendLine($"  User-verified profile: {(summary.KeyboardUserVerified ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(summary.KeyboardDisambiguationPattern))
            {
                sb.AppendLine($"  Disambiguation pattern: {summary.KeyboardDisambiguationPattern}");
            }
            if (!string.IsNullOrWhiteSpace(summary.KeyboardWarningText))
            {
                sb.AppendLine($"  Attention: {summary.KeyboardWarningText}");
            }
            if (!string.IsNullOrWhiteSpace(summary.KeyboardNotes))
            {
                sb.AppendLine($"  Notes: {summary.KeyboardNotes}");
            }

            return sb.ToString();
        }

        private static string Clean(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        }

        private static string ExtractHpSupportProductNumber(string? systemSku)
        {
            if (string.IsNullOrWhiteSpace(systemSku))
                return "Unknown";

            var trimmed = systemSku.Trim();
            var hashIndex = trimmed.IndexOf('#');
            return hashIndex > 0 ? trimmed[..hashIndex] : trimmed;
        }

        private static string BuildFallbackWarning(ModelIdentityResolutionSummary summary, string generic, string hpSpecific, string omenHint)
        {
            if (summary.IsHpSystem)
            {
                    if (summary.HasOmenVictusHint)
                    {
                        return $"{omenHint} Model name contains OMEN/Victus branding, but exact model is not in database.";
                    }
                    return hpSpecific;
            }

            return generic;
        }

        private static bool IsHpManufacturer(string manufacturer)
        {
            return manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase)
                || manufacturer.Contains("HEWLETT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsOmenVictusHint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("OMEN", StringComparison.OrdinalIgnoreCase)
                || value.Contains("VICTUS", StringComparison.OrdinalIgnoreCase)
                || value.Contains("THE TIGER", StringComparison.OrdinalIgnoreCase)
                || value.Contains("THETIGER", StringComparison.OrdinalIgnoreCase);
        }
    }
}
