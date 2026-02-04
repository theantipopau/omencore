using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// OpenRGB provider for generic RGB device support on desktop systems.
    /// Connects to OpenRGB SDK server (default: localhost:6742).
    /// Requires OpenRGB to be running with SDK server enabled.
    /// </summary>
    public class OpenRgbProvider : IRgbProvider
    {
        private readonly LoggingService _logging;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly List<OpenRgbDevice> _devices = new();
        private bool _isConnected;
        private readonly string _host;
        private readonly int _port;
        
        // OpenRGB SDK Protocol Constants
        private const string OPENRGB_MAGIC = "ORGB";
        private const int PACKET_ID_REQUEST_CONTROLLER_COUNT = 0;
        private const int PACKET_ID_REQUEST_CONTROLLER_DATA = 1;
        private const int PACKET_ID_SET_CLIENT_NAME = 50;
        private const int PACKET_ID_RGBCONTROLLER_UPDATELEDS = 1050;
        private const int PACKET_ID_RGBCONTROLLER_UPDATEZONELEDS = 1051;
        private const int PACKET_ID_RGBCONTROLLER_UPDATESINGLELED = 1052;
        private const int PACKET_ID_RGBCONTROLLER_SETCUSTOMMODE = 1100;
        
        public string ProviderName => "OpenRGB";
        public string ProviderId => "openrgb";
        public bool IsAvailable => _isConnected && _devices.Count > 0;
        public bool IsConnected => _isConnected;
        public int DeviceCount => _devices.Count;
        
        public IReadOnlyList<RgbEffectType> SupportedEffects => new[]
        {
            RgbEffectType.Static,
            RgbEffectType.Off
        };
        
        public IReadOnlyList<OpenRgbDevice> Devices => _devices;
        
        public OpenRgbProvider(LoggingService logging, string host = "127.0.0.1", int port = 6742)
        {
            _logging = logging;
            _host = host;
            _port = port;
        }
        
        public async Task InitializeAsync()
        {
            try
            {
                await ConnectAsync();
                if (_isConnected)
                {
                    await DiscoverDevicesAsync();
                    _logging.Info($"[OpenRGB] Connected to OpenRGB server, found {_devices.Count} device(s)");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"[OpenRGB] Failed to connect to OpenRGB server: {ex.Message}");
                _logging.Info("[OpenRGB] Make sure OpenRGB is running with SDK Server enabled (SDK Server tab → Start Server)");
                _isConnected = false;
            }
        }
        
        private async Task ConnectAsync()
        {
            try
            {
                _client = new TcpClient();
                _client.ReceiveTimeout = 5000;
                _client.SendTimeout = 5000;
                
                // Try to connect with timeout
                var connectTask = _client.ConnectAsync(_host, _port);
                if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask)
                {
                    _logging.Debug("[OpenRGB] Connection timeout - OpenRGB server not available");
                    return;
                }
                
                await connectTask; // Ensure any exception is thrown
                
                _stream = _client.GetStream();
                _isConnected = true;
                
                // Send client name
                await SendClientNameAsync("OmenCore");
            }
            catch (SocketException)
            {
                _logging.Debug("[OpenRGB] OpenRGB server not running or not accessible");
                _isConnected = false;
            }
        }
        
        private async Task SendClientNameAsync(string name)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name + "\0");
            await SendPacketAsync(PACKET_ID_SET_CLIENT_NAME, nameBytes);
        }
        
        private async Task<int> GetControllerCountAsync()
        {
            await SendPacketAsync(PACKET_ID_REQUEST_CONTROLLER_COUNT, Array.Empty<byte>());
            var response = await ReceivePacketAsync();
            
            if (response.Data.Length >= 4)
            {
                return BitConverter.ToInt32(response.Data, 0);
            }
            return 0;
        }
        
        private async Task DiscoverDevicesAsync()
        {
            _devices.Clear();
            
            int count = await GetControllerCountAsync();
            _logging.Debug($"[OpenRGB] Found {count} controller(s)");
            
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var device = await GetControllerDataAsync(i);
                    if (device != null)
                    {
                        _devices.Add(device);
                        _logging.Debug($"[OpenRGB] Device {i}: {device.Name} ({device.Type}) - {device.LedCount} LEDs");
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"[OpenRGB] Failed to get controller {i} data: {ex.Message}");
                }
            }
        }
        
        private async Task<OpenRgbDevice?> GetControllerDataAsync(int deviceIndex)
        {
            var indexBytes = BitConverter.GetBytes(deviceIndex);
            await SendPacketAsync(PACKET_ID_REQUEST_CONTROLLER_DATA, indexBytes, deviceIndex);
            var response = await ReceivePacketAsync();
            
            if (response.Data.Length < 100)
                return null;
            
            return ParseControllerData(deviceIndex, response.Data);
        }
        
        private OpenRgbDevice ParseControllerData(int index, byte[] data)
        {
            // OpenRGB controller data packet structure (simplified parsing)
            var device = new OpenRgbDevice { Index = index };
            
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            
            try
            {
                // Skip data size (4 bytes)
                reader.ReadInt32();
                
                // Device type (4 bytes)
                device.Type = (OpenRgbDeviceType)reader.ReadInt32();
                
                // Name (null-terminated string with 2-byte length prefix)
                int nameLen = reader.ReadUInt16();
                var nameBytes = reader.ReadBytes(nameLen);
                device.Name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                
                // Vendor (null-terminated string with 2-byte length prefix)
                int vendorLen = reader.ReadUInt16();
                var vendorBytes = reader.ReadBytes(vendorLen);
                device.Vendor = Encoding.ASCII.GetString(vendorBytes).TrimEnd('\0');
                
                // Description (null-terminated string with 2-byte length prefix)
                int descLen = reader.ReadUInt16();
                reader.ReadBytes(descLen); // Skip description
                
                // Version (null-terminated string with 2-byte length prefix)
                int verLen = reader.ReadUInt16();
                reader.ReadBytes(verLen); // Skip version
                
                // Serial (null-terminated string with 2-byte length prefix)
                int serialLen = reader.ReadUInt16();
                reader.ReadBytes(serialLen); // Skip serial
                
                // Location (null-terminated string with 2-byte length prefix)
                int locLen = reader.ReadUInt16();
                reader.ReadBytes(locLen); // Skip location
                
                // Mode count (2 bytes)
                int modeCount = reader.ReadUInt16();
                
                // Active mode (4 bytes)
                reader.ReadInt32();
                
                // Skip mode data for now - we just need LED count
                // This is a simplified parser - modes have complex variable-length data
                // For now, we'll estimate LED count from remaining data
                
                // Skip to zones section - this requires proper mode parsing
                // For simplicity, we'll use a heuristic based on device type
                device.LedCount = EstimateLedCount(device.Type);
            }
            catch (Exception ex)
            {
                _logging.Debug($"[OpenRGB] Error parsing controller data: {ex.Message}");
                device.LedCount = 1; // Minimum
            }
            
            return device;
        }
        
        private int EstimateLedCount(OpenRgbDeviceType type)
        {
            return type switch
            {
                OpenRgbDeviceType.Keyboard => 104,
                OpenRgbDeviceType.Mouse => 2,
                OpenRgbDeviceType.MouseMat => 15,
                OpenRgbDeviceType.Headset => 2,
                OpenRgbDeviceType.HeadsetStand => 4,
                OpenRgbDeviceType.Gamepad => 4,
                OpenRgbDeviceType.Light => 1,
                OpenRgbDeviceType.Speaker => 4,
                OpenRgbDeviceType.LedStrip => 30,
                OpenRgbDeviceType.Motherboard => 8,
                OpenRgbDeviceType.Gpu => 1,
                OpenRgbDeviceType.Cooler => 8,
                OpenRgbDeviceType.Ram => 8,
                _ => 10
            };
        }
        
        public async Task ApplyEffectAsync(string effectId)
        {
            if (!_isConnected) return;
            
            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = effectId.Substring(6);
                var color = ColorTranslator.FromHtml(hex);
                await SetStaticColorAsync(color);
            }
            else if (effectId.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                await TurnOffAsync();
            }
        }
        
        public async Task SetStaticColorAsync(Color color)
        {
            if (!_isConnected) return;
            
            foreach (var device in _devices)
            {
                try
                {
                    await SetDeviceColorAsync(device.Index, color, device.LedCount);
                }
                catch (Exception ex)
                {
                    _logging.Debug($"[OpenRGB] Failed to set color on device {device.Index}: {ex.Message}");
                }
            }
        }
        
        private async Task SetDeviceColorAsync(int deviceIndex, Color color, int ledCount)
        {
            // Set device to direct/custom mode first
            await SendPacketAsync(PACKET_ID_RGBCONTROLLER_SETCUSTOMMODE, Array.Empty<byte>(), deviceIndex);
            await Task.Delay(10); // Small delay for mode switch
            
            // Build LED color array packet
            // Format: data_size (4) + led_count (2) + [r,g,b,padding] * led_count
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Data size (will be calculated)
            int dataSize = 2 + (ledCount * 4);
            writer.Write(dataSize);
            
            // LED count
            writer.Write((ushort)ledCount);
            
            // LED colors (RGBX format - OpenRGB uses R, G, B, 0)
            for (int i = 0; i < ledCount; i++)
            {
                writer.Write(color.R);
                writer.Write(color.G);
                writer.Write(color.B);
                writer.Write((byte)0); // Padding
            }
            
            await SendPacketAsync(PACKET_ID_RGBCONTROLLER_UPDATELEDS, ms.ToArray(), deviceIndex);
        }
        
        public Task SetBreathingEffectAsync(Color color)
        {
            // OpenRGB doesn't have built-in breathing effect via SDK
            // Just apply static color
            return SetStaticColorAsync(color);
        }
        
        public Task SetSpectrumEffectAsync()
        {
            // Spectrum cycling not supported via direct SDK control
            // Would need to implement timer-based color cycling
            return Task.CompletedTask;
        }
        
        public async Task TurnOffAsync()
        {
            await SetStaticColorAsync(Color.Black);
        }
        
        private async Task SendPacketAsync(int packetId, byte[] data, int deviceIndex = 0)
        {
            if (_stream == null) return;
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Header: "ORGB" magic
            writer.Write(Encoding.ASCII.GetBytes(OPENRGB_MAGIC));
            
            // Device index (4 bytes)
            writer.Write(deviceIndex);
            
            // Packet ID (4 bytes)
            writer.Write(packetId);
            
            // Data length (4 bytes)
            writer.Write(data.Length);
            
            // Data
            if (data.Length > 0)
                writer.Write(data);
            
            var packet = ms.ToArray();
            await _stream.WriteAsync(packet, 0, packet.Length);
            await _stream.FlushAsync();
        }
        
        private async Task<OpenRgbPacket> ReceivePacketAsync()
        {
            if (_stream == null)
                return new OpenRgbPacket();
            
            var header = new byte[16];
            int bytesRead = 0;
            while (bytesRead < 16)
            {
                int read = await _stream.ReadAsync(header, bytesRead, 16 - bytesRead);
                if (read == 0) throw new IOException("Connection closed");
                bytesRead += read;
            }
            
            // Parse header
            // Skip magic (4 bytes)
            int deviceIndex = BitConverter.ToInt32(header, 4);
            int packetId = BitConverter.ToInt32(header, 8);
            int dataLength = BitConverter.ToInt32(header, 12);
            
            // Read data
            byte[] data = Array.Empty<byte>();
            if (dataLength > 0)
            {
                data = new byte[dataLength];
                bytesRead = 0;
                while (bytesRead < dataLength)
                {
                    int read = await _stream.ReadAsync(data, bytesRead, dataLength - bytesRead);
                    if (read == 0) throw new IOException("Connection closed");
                    bytesRead += read;
                }
            }
            
            return new OpenRgbPacket
            {
                DeviceIndex = deviceIndex,
                PacketId = packetId,
                Data = data
            };
        }
        
        public void Shutdown()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
            
            _stream = null;
            _client = null;
            _isConnected = false;
            _devices.Clear();
        }
    }
    
    public class OpenRgbDevice
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public OpenRgbDeviceType Type { get; set; }
        public int LedCount { get; set; }
        public Color CurrentColor { get; set; } = Color.Black;
    }
    
    public class OpenRgbPacket
    {
        public int DeviceIndex { get; set; }
        public int PacketId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
    
    public enum OpenRgbDeviceType
    {
        Motherboard = 0,
        Ram = 1,
        Gpu = 2,
        Cooler = 3,
        LedStrip = 4,
        Keyboard = 5,
        Mouse = 6,
        MouseMat = 7,
        Headset = 8,
        HeadsetStand = 9,
        Gamepad = 10,
        Light = 11,
        Speaker = 12,
        Virtual = 13,
        Storage = 14,
        Case = 15,
        Unknown = 16
    }
}
