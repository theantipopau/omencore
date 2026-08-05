using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The firmware-to-UI GPU mode mapping used by <c>GpuSwitchService.DetectCurrentMode</c>.
    ///
    /// Mode detection used to be inferred entirely from which adapter was painting a display, which
    /// gets the answer wrong on any healthy Optimus laptop: RTD3 idles the dGPU into D3, so
    /// <c>Win32_VideoController</c> reports it Off Line with no resolution, and Hybrid was reported as
    /// Integrated. These tests cover the mapping half - the part that can be exercised without a WMI
    /// round trip or a live display topology.
    /// </summary>
    public class GpuSwitchServiceModeMappingTests
    {
        [Fact]
        public void Discrete_Maps_To_Discrete()
        {
            GpuSwitchService.MapFirmwareGpuMode(HpWmiBios.GpuMode.Discrete)
                .Should().Be(GpuSwitchMode.Discrete);
        }

        [Fact]
        public void Hybrid_Maps_To_Hybrid()
        {
            GpuSwitchService.MapFirmwareGpuMode(HpWmiBios.GpuMode.Hybrid)
                .Should().Be(GpuSwitchMode.Hybrid);
        }

        [Fact]
        public void Optimus_Maps_To_Hybrid_Because_The_Ui_Has_No_Separate_Member()
        {
            // Optimus is a hybrid arrangement with dGPU-direct routing available. GpuSwitchMode
            // declares only Integrated/Discrete/Hybrid, and Hybrid is what the UI means by it.
            GpuSwitchService.MapFirmwareGpuMode(HpWmiBios.GpuMode.Optimus)
                .Should().Be(GpuSwitchMode.Hybrid);
        }

        [Fact]
        public void An_Undeclared_Firmware_Value_Maps_To_Null_Rather_Than_Guessing()
        {
            // A machine routed to iGPU-only - the BIOS calls it UMA on board 8D87 - is a state
            // HpWmiBios.GpuMode does not declare, and no capture has pinned what Legacy 0x52 reads
            // there. Null hands the question back to adapter inference, which is at its most reliable
            // in exactly that case: a machine with no usable dGPU really does have one active adapter.
            GpuSwitchService.MapFirmwareGpuMode((HpWmiBios.GpuMode)0x7F)
                .Should().BeNull("an unmapped firmware byte must not be forced into a UI mode");
        }

        [Fact]
        public void Never_Maps_Any_Firmware_Value_To_Integrated()
        {
            // The firmware enum has no iGPU-only member, so nothing it reports should produce
            // Integrated. This pins the specific defect: Integrated was being concluded from a
            // sleeping dGPU, never from anything the firmware actually said.
            foreach (var mode in new[]
                     {
                         HpWmiBios.GpuMode.Hybrid,
                         HpWmiBios.GpuMode.Discrete,
                         HpWmiBios.GpuMode.Optimus
                     })
            {
                GpuSwitchService.MapFirmwareGpuMode(mode)
                    .Should().NotBe(GpuSwitchMode.Integrated,
                        $"{mode} is not an iGPU-only state");
            }
        }
    }
}
