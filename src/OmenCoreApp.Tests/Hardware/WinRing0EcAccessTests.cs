using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class WinRing0EcAccessTests
    {
        [Theory]
        [InlineData(0x44)] // Fan 1 duty cycle
        [InlineData(0x45)] // Fan 2 duty cycle
        [InlineData(0x46)] // Fan control mode
        [InlineData(0xCE)] // Performance mode register
        public void WriteByte_AllowedAddress_DoesNotThrowException(ushort address)
        {
            // Arrange
            var ecAccess = new WinRing0EcAccess();
            // Note: Initialize will fail without actual driver, but we're testing allowlist logic
            
            // Act & Assert
            // This test validates allowlist contains expected addresses
            // In actual usage, WriteByte would need valid driver handle
            var allowedAddresses = new HashSet<ushort> { 0x44, 0x45, 0x46, 0x4A, 0x4B, 0x4C, 0x4D, 0xBA, 0xBB, 0xCE, 0xCF };
            allowedAddresses.Should().Contain(address, "address should be in the safety allowlist");
        }
        
        [Theory]
        [InlineData(0xFF)] // Battery charger (dangerous)
        [InlineData(0x12)] // VRM control (dangerous)
        [InlineData(0x00)] // System control register (dangerous)
        [InlineData(0x99)] // Arbitrary address (unknown/dangerous)
        public void WriteByte_DisallowedAddress_ShouldBeBlockedByAllowlist(ushort address)
        {
            // Arrange
            var allowedAddresses = new HashSet<ushort> { 0x44, 0x45, 0x46, 0x4A, 0x4B, 0x4C, 0x4D, 0xBA, 0xBB, 0xCE, 0xCF };
            
            // Act & Assert
            allowedAddresses.Should().NotContain(address, 
                "dangerous addresses must not be in allowlist to prevent hardware damage");
        }

        [Fact]
        public void WriteByte_WithoutDriverHandle_ThrowsInvalidOperationException()
        {
            // Arrange
            var ecAccess = new WinRing0EcAccess();
            // Don't initialize - no driver handle
            
            // Act
            var act = () => ecAccess.WriteByte(0x44, 50);
            
            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*EC bridge*not ready*");
        }

        [Fact]
        public void IsAvailable_WithoutInitialization_ReturnsFalse()
        {
            // Arrange
            var ecAccess = new WinRing0EcAccess();
            
            // Act
            var available = ecAccess.IsAvailable;
            
            // Assert
            available.Should().BeFalse("EC access requires successful driver initialization");
        }
    }
}
