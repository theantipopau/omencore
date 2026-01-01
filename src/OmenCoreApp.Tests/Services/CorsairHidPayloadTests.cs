using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services.Corsair;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class CorsairHidPayloadTests
    {
        private class TestCorsairHidDirect : CorsairHidDirect
        {
            public TestCorsairHidDirect(LoggingService logging) : base(logging) { }

            public byte[] CallBuildSetColorReport(int pid, byte r, byte g, byte b)
            {
                // Determine a reasonable device type for the test based on known PIDs
                var mousePids = new[] { 0x1B2E, 0x1B4B, 0x1B34 };
                var keyboardPids = new[] { 0x1B11, 0x1B2D, 0x1B17, 0x1B60, 0x1B38, 0x1B39 };
                var type = OmenCore.Corsair.CorsairDeviceType.Accessory;
                if (System.Array.Exists(mousePids, p => p == pid)) type = OmenCore.Corsair.CorsairDeviceType.Mouse;
                else if (System.Array.Exists(keyboardPids, p => p == pid)) type = OmenCore.Corsair.CorsairDeviceType.Keyboard;

                // Add a test device to ensure device.ProductId and DeviceType are set
                AddTestHidDevice("test", pid, type, "Test");
                var list = DiscoverDevicesAsync().Result;
                OmenCore.Corsair.CorsairDevice? dev = null;
                foreach (var d in list) if (d.DeviceId == "test") dev = d;
                if (dev == null) throw new System.Exception("test device not found");

                // locate internal CorsairHidDevice
                var hidDevice = GetInternalHidDevice("test");
                var method = typeof(CorsairHidDirect).GetMethod("BuildSetColorReport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method == null) throw new System.Exception("BuildSetColorReport method not found");
                var res = method.Invoke(this, new object[] { hidDevice, r, g, b });
                if (res is byte[] arr) return arr;
                throw new System.Exception("BuildSetColorReport did not return a byte[]");
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
                    if (prop.GetValue(item) is not OmenCore.Corsair.CorsairDevice di) continue;
                    if (di.DeviceId == deviceId) return item;
                }
                throw new System.Exception($"internal hid device '{deviceId}' not found");
            }
        }

        [Theory]
        [InlineData(0x1B11, 0x09)] // K70
        [InlineData(0x1B2D, 0x09)] // K95
        [InlineData(0x1B60, 0x09)] // K100
        [InlineData(0x1B2E, 0x05)] // Dark Core Mouse
        [InlineData(0x1B4B, 0x05)] // Dark Core PRO
        [InlineData(0x1B34, 0x05)] // Ironclaw
        [InlineData(0xFFFF, 0x07)] // Unknown product -> default
        public void BuildSetColorReport_SelectsExpectedCommand(int pid, int expectedCmd)
        {
            var log = new LoggingService();
            var t = new TestCorsairHidDirect(log);

            var report = t.CallBuildSetColorReport(pid, 0x11, 0x22, 0x33);

            report[1].Should().Be((byte)expectedCmd);
            report[4].Should().Be(0x11);
            report[5].Should().Be(0x22);
            report[6].Should().Be(0x33);
        }

        [Theory]
        [InlineData(0x1B11)] // K70
        [InlineData(0x1B2D)] // K95
        [InlineData(0x1B60)] // K100
        public void BuildSetColorReport_KeyboardsProduceFullZone(int pid)
        {
            var log = new LoggingService();
            var t = new TestCorsairHidDirect(log);

            var report = t.CallBuildSetColorReport(pid, 0x11, 0x22, 0x33);

            // Full-device marker
            report[3].Should().Be(0xFF);
            // Marker: 0x01 for regular keyboards, 0x02 for K100 special
            var expectedMarker = pid == 0x1B60 ? (byte)0x02 : (byte)0x01;
            report[7].Should().Be(expectedMarker);

            // Zone payload should carry the same color
            report[8].Should().Be(0x11);
            report[9].Should().Be(0x22);
            report[10].Should().Be(0x33);
        }

        [Fact]
        public void BuildSetColorReport_K100IncludesPerKeyStub()
        {
            var log = new LoggingService();
            var t = new TestCorsairHidDirect(log);

            var report = t.CallBuildSetColorReport(0x1B60, 0x11, 0x22, 0x33);

            // Marker for K100 should be 0x02
            report[7].Should().Be(0x02);
            // After zones we expect the 'P' marker (0x50) somewhere after offset 8
            bool found = false;
            for (int i = 11; i < report.Length - 3; i++)
            {
                if (report[i] == 0x50)
                {
                    found = true;
                    // count byte should be non-zero
                    report[i+1].Should().BeGreaterThan((byte)0);
                    break;
                }
            }
            found.Should().BeTrue();
        }
    }
}
