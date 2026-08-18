using System.Reflection;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class OmenKeyServiceTests
    {
        private const uint VkF12 = 0x7B;
        private const uint DedicatedOmenLaunchScan = 0xE045;

        [Fact]
        public void FnF12_WithDedicatedOmenLaunchScan_IsNotMarkedNeverIntercept()
        {
            using var service = CreateService();

            var (neverIntercept, reason) = InvokeTryGetNeverInterceptReason(service, VkF12, DedicatedOmenLaunchScan);

            neverIntercept.Should().BeFalse();
            reason.Should().BeEmpty();
        }

        [Fact]
        public void FnF12_WithDedicatedOmenLaunchScan_IsAcceptedAsOmenKey()
        {
            using var service = CreateService();

            InvokeIsOmenKey(service, VkF12, DedicatedOmenLaunchScan).Should().BeTrue();
        }

        [Fact]
        public void PlainF12_RemainsNeverInterceptFunctionKey()
        {
            using var service = CreateService();

            var (neverIntercept, reason) = InvokeTryGetNeverInterceptReason(service, VkF12, 0x0058);

            neverIntercept.Should().BeTrue();
            reason.Should().Be("never-intercept-function-key");
            InvokeIsOmenKey(service, VkF12, 0x0058).Should().BeFalse();
        }

        [Fact]
        public void GetDiagnosticSnapshot_ReportsDefaultGuardState_WhenNotStarted()
        {
            using var service = CreateService();

            var snapshot = service.GetDiagnosticSnapshot();

            snapshot.Enabled.Should().BeTrue();
            snapshot.Action.Should().Be(OmenKeyAction.ToggleOmenCore);
            snapshot.HookActive.Should().BeFalse();
            snapshot.WmiWatcherActive.Should().BeFalse();
            snapshot.StrictMode.Should().BeTrue();
            snapshot.FirmwareFnPProfileCycleEnabled.Should().BeFalse();
            snapshot.LastNeverInterceptAgeMs.Should().BeNull();
            snapshot.LastCandidateAccepted.Should().BeNull();
            snapshot.LastCandidateReason.Should().BeNull();
        }

        [Fact]
        public void GetDiagnosticSnapshot_RecordsAcceptedCandidate_AfterDedicatedOmenKey()
        {
            using var service = CreateService();

            InvokeIsOmenKey(service, VkF12, DedicatedOmenLaunchScan).Should().BeTrue();

            var snapshot = service.GetDiagnosticSnapshot();
            snapshot.LastCandidateAccepted.Should().BeTrue();
            snapshot.LastCandidateSource.Should().Be("keyboard-hook");
            snapshot.LastCandidateVkCode.Should().Be(VkF12);
            snapshot.LastCandidateScanCode.Should().Be(DedicatedOmenLaunchScan);
            snapshot.LastCandidateReason.Should().Be("f12-dedicated-omen-scan");
            snapshot.LastCandidateAgeMs.Should().NotBeNull();
        }

        [Fact]
        public void GetDiagnosticSnapshot_RecordsRejectedCandidate_ForBrightnessConflictScan()
        {
            using var service = CreateService();
            const uint vkLaunchApp2 = 0xB7;
            const uint brightnessConflictScan = 0x002B;

            InvokeIsOmenKey(service, vkLaunchApp2, brightnessConflictScan).Should().BeFalse();

            var snapshot = service.GetDiagnosticSnapshot();
            snapshot.LastCandidateAccepted.Should().BeFalse();
            snapshot.LastCandidateReason.Should().Be("brightness-key-conflict-launch-app-scan");
        }

        [Fact]
        public void VkOemOmen_WithBrightnessDownScanCode_IsRejectedInStrictMode()
        {
            // Regression test for GitHub #141: on an OMEN 16 (2025, AMD Ryzen AI) board, Fn+F2
            // (brightness down) was reported firing OmenCore's OMEN-key action because the OS
            // delivered it as VK=0xFF (VK_OEM_OMEN) with Scan=0x002B - a real HP-side keyboard
            // ambiguity, not a bug in the reporter's config. Unlike the VK_LAUNCH_APP2/APP1
            // branches, the VK_OEM_OMEN branch does not consult BrightnessConflictScanCodes at
            // all; it only requires the scan code to be one of the recognized dedicated OMEN
            // scan codes under strict mode. Since 0x002B is not in that set, this already
            // resolves correctly today - this test exists so it stays that way.
            using var service = CreateService();
            const uint vkOemOmen = 0xFF;
            const uint brightnessDownScan = 0x002B;

            InvokeIsOmenKey(service, vkOemOmen, brightnessDownScan).Should().BeFalse();

            var snapshot = service.GetDiagnosticSnapshot();
            snapshot.LastCandidateAccepted.Should().BeFalse();
            snapshot.LastCandidateReason.Should().Be("strict-mode-oem-omen-scan-mismatch");
        }

        [Fact]
        public void GetDiagnosticSnapshot_LatestCandidateOverwritesPreviousOne()
        {
            using var service = CreateService();
            const uint vkLaunchApp2 = 0xB7;
            const uint brightnessConflictScan = 0x002B;

            InvokeIsOmenKey(service, VkF12, DedicatedOmenLaunchScan).Should().BeTrue();
            InvokeIsOmenKey(service, vkLaunchApp2, brightnessConflictScan).Should().BeFalse();

            var snapshot = service.GetDiagnosticSnapshot();
            snapshot.LastCandidateAccepted.Should().BeFalse();
            snapshot.LastCandidateVkCode.Should().Be(vkLaunchApp2);
            snapshot.LastCandidateScanCode.Should().Be(brightnessConflictScan);
        }

        private static OmenKeyService CreateService()
        {
            var logging = new LoggingService();
            logging.Initialize();
            return new OmenKeyService(logging);
        }

        private static (bool Result, string Reason) InvokeTryGetNeverInterceptReason(OmenKeyService service, uint vkCode, uint scanCode)
        {
            var method = typeof(OmenKeyService).GetMethod("TryGetNeverInterceptReason", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            var args = new object?[] { vkCode, scanCode, null };
            var result = (bool)method!.Invoke(service, args)!;
            return (result, (string)args[2]!);
        }

        private static bool InvokeIsOmenKey(OmenKeyService service, uint vkCode, uint scanCode)
        {
            var method = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            return (bool)method!.Invoke(service, new object[] { vkCode, scanCode })!;
        }
    }
}
