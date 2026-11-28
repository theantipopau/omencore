using System;
using FluentAssertions;
using Microsoft.Win32;
using OmenCore.Services;
using OmenCore.Services.SystemOptimizer;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>Uses a throwaway HKCU key; nothing outside it is touched.</summary>
    public class RegistryBackupServiceTests : IDisposable
    {
        private readonly string _subKey = @"Software\OmenCoreTests\RegistryBackup_" + Guid.NewGuid().ToString("N");
        private string KeyPath => @"HKCU\" + _subKey;

        public RegistryBackupServiceTests() => Registry.CurrentUser.CreateSubKey(_subKey)!.Dispose();

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);

        [Fact]
        public void Revert_DeletesAValueThatDidNotExist_WhenTheKeyDid()
        {
            using var logging = new LoggingService();
            var backup = new RegistryBackupService(logging);

            backup.SetRegistryValue(KeyPath, "TcpAckFrequency", 1, RegistryValueKind.DWord).Should().BeTrue();
            backup.RestoreRegistryValue(KeyPath, "TcpAckFrequency").Should().BeTrue();

            using var key = Registry.CurrentUser.OpenSubKey(_subKey)!;
            key.GetValue("TcpAckFrequency").Should().BeNull();
        }

        [Fact]
        public void Revert_RestoresTheOriginal_WhenTheSameValueIsWrittenUnderTwoSpellings()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(_subKey, writable: true)!)
                key.SetValue("TcpNoDelay", 0, RegistryValueKind.DWord);

            using var logging = new LoggingService();
            var backup = new RegistryBackupService(logging);

            backup.SetRegistryValue(KeyPath, "TcpNoDelay", 1, RegistryValueKind.DWord);
            backup.SetRegistryValue(KeyPath, "TCPNoDelay", 1, RegistryValueKind.DWord);
            backup.RestoreRegistryValue(KeyPath, "TcpNoDelay");
            backup.RestoreRegistryValue(KeyPath, "TCPNoDelay");

            using var after = Registry.CurrentUser.OpenSubKey(_subKey)!;
            after.GetValue("TcpNoDelay").Should().Be(0);
        }
    }
}
