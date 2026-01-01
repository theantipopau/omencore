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
                CorsairDevice? dev = null;
                // get reference to internal device list via DiscoverDevicesAsync
                var d = DiscoverDevicesAsync().Result;
                foreach (var x in d) if (x.DeviceId == deviceId) dev = x;
                if (dev == null) throw new System.Exception($"Test device '{deviceId}' not found");
                return SendColorForTestAsync(dev, 0x11, 0x22, 0x33);
            }

            // wrapper for protected SendColorCommandAsync
            public Task SendColorForTestAsync(CorsairDevice device, byte r, byte g, byte b)
            {
                if (device == null) throw new System.ArgumentNullException(nameof(device));
                // locate the private nested CorsairHidDevice by matching DeviceId and then call SendColorCommandAsync via reflection
                var t = typeof(CorsairHidDirect);
                var method = t.GetMethod("SendColorCommandAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method == null) throw new System.Exception("SendColorCommandAsync method not found");
                var hid = GetInternalHidDevice(device.DeviceId);
                var res = method.Invoke(this, new object[] { /*CorsairHidDevice*/ hid, r, g, b });
                if (res is Task task) return task;
                throw new System.Exception("SendColorCommandAsync did not return a Task");
            }

            private object GetInternalHidDevice(string deviceId)
            {
                var t = typeof(CorsairHidDirect);
                var field = t.GetField("_devices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null) throw new System.Exception("_devices field not found");
                if (field.GetValue(this) is not System.Collections.IEnumerable list) throw new System.Exception("_devices list is null");
                foreach (var item in list)
                {
                    var prop = item.GetType().GetProperty("DeviceInfo");
                    if (prop == null) continue;
                    if (prop.GetValue(item) is not CorsairDevice di) continue;
                    if (di.DeviceId == deviceId) return item;
                }
                throw new System.Exception($"internal hid device '{deviceId}' not found");
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
