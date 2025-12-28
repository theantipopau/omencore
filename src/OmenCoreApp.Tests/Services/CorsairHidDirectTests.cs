using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Corsair;
using OmenCore.Services.Corsair;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class CorsairHidDirectTests
    {
        private class FlakyWriteCorsairHidDirect : CorsairHidDirect
        {
            private int _attempts = 0;
            private readonly int _failAttempts;
            public FlakyWriteCorsairHidDirect(LoggingService logging, int failAttempts) : base(logging)
            {
                _failAttempts = failAttempts;
            }

            protected override Task<bool> WriteReportAsync(CorsairHidDevice deviceObj, byte[] report)
            {
                _attempts++;
                if (_attempts <= _failAttempts)
                {
                    throw new System.Exception("Simulated HID write failure");
                }
                return Task.FromResult(true);
            }

            // Expose a public wrapper for testing protected SendColorCommandAsync
            public Task CallSendColor(string deviceId, int pid)
            {
                // Add a test device and call protected method
                AddTestHidDevice(deviceId, pid, CorsairDeviceType.Mouse, "Flaky Mouse");
                var dev = default(CorsairDevice);
                // get reference to internal device list via DiscoverDevicesAsync
                var d = DiscoverDevicesAsync().Result;
                foreach (var x in d) if (x.DeviceId == deviceId) dev = x;
                return SendColorForTestAsync(dev, 0x11, 0x22, 0x33);
            }

            // wrapper for protected SendColorCommandAsync
            public Task SendColorForTestAsync(CorsairDevice device, byte r, byte g, byte b)
            {
                // locate the private nested CorsairHidDevice by matching DeviceId and then call SendColorCommandAsync via reflection
                var t = typeof(CorsairHidDirect);
                var method = t.GetMethod("SendColorCommandAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method == null) throw new System.Exception("SendColorCommandAsync method not found");
                return (Task)method.Invoke(this, new object[] { /*CorsairHidDevice*/ GetInternalHidDevice(device.DeviceId), r, g, b })!;
            }

            private object GetInternalHidDevice(string deviceId)
            {
                var t = typeof(CorsairHidDirect);
                var field = t.GetField("_devices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var list = field.GetValue(this) as System.Collections.IEnumerable;
                foreach (var item in list!)
                {
                    var di = item.GetType().GetProperty("DeviceInfo").GetValue(item) as CorsairDevice;
                    if (di.DeviceId == deviceId) return item;
                }
                throw new System.Exception("internal hid device not found");
            }
        }

        [Fact]
        public async Task SendColor_RetriesAndEventuallySucceeds()
        {
            var log = new LoggingService();
            var flaky = new FlakyWriteCorsairHidDirect(log, failAttempts: 2);

            await flaky.CallSendColor("test:1", 0x1B2E);

            // Verify no failed device recorded
            flaky.HidWriteFailedDeviceIds.Should().BeEmpty();
        }

        [Fact]
        public async Task SendColor_FailsAfterRetries_RecordsFailedDevice()
        {
            var log = new LoggingService();
            var flaky = new FlakyWriteCorsairHidDirect(log, failAttempts: 5);

            await flaky.CallSendColor("test:2", 0x1B2E);

            // After repeated failures, device should be recorded in failed set
            flaky.HidWriteFailedDeviceIds.Should().Contain((0x1B2E).ToString()); // decimal of 0x1B2E
        }
    }
}
