using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services;

/// <summary>
/// Integration with RivaTuner Statistics Server (RTSS) for FPS monitoring.
/// 
/// RTSS exposes frame data via shared memory (memory-mapped file).
/// This allows OmenCore to display accurate FPS without hooking into games.
/// 
/// Prerequisites:
/// - RTSS must be running
/// - "Enable shared memory support" must be enabled in RTSS settings
/// 
/// If RTSS is not available, the service gracefully returns null/0 values.
/// </summary>
public class RtssIntegrationService : IDisposable
{
    private const string RTSS_SHARED_MEMORY_NAME = "RTSSSharedMemoryV2";
    
    // RTSS Shared Memory Header (simplified)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RTSS_SHARED_MEMORY_HEADER
    {
        public uint dwSignature;           // 'RTSS' = 0x53535452
        public uint dwVersion;             // Version
        public uint dwAppEntrySize;        // Size of each app entry
        public uint dwAppArrOffset;        // Offset to app entries array
        public uint dwAppArrSize;          // Number of app entries
        public uint dwOSDEntrySize;        // Size of OSD entry
        public uint dwOSDArrOffset;        // Offset to OSD array
        public uint dwOSDArrSize;          // Number of OSD entries
        public uint dwOSDFrame;            // Global OSD frame counter
    }
    
    // RTSS App Entry (simplified - we only need framerate data)
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct RTSS_SHARED_MEMORY_APP_ENTRY
    {
        // Process info
        public uint dwProcessID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szName;
        public uint dwFlags;
        
        // Performance metrics
        public uint dwInstantFrameTime;    // Instant frame time in μs
        public uint dwTime0;               // Start timestamp
        public uint dwTime1;               // End timestamp
        public uint dwFrames;              // Frame count
        public uint dwOSDFrameId;          // OSD frame ID
        
        // Statistics
        public float fStatFramerateAvg;    // Average framerate
        public float fStatFramerateMin;    // Minimum framerate
        public float fStatFramerateMax;    // Maximum framerate
        public float fStatFramerate1Dot0Pct; // 1% low FPS
        public float fStatFramerate0Dot1Pct; // 0.1% low FPS
        
        // Frametime stats
        public float fStatFrametimeAvg;    // Average frametime (ms)
        public float fStatFrametimeMin;    // Minimum frametime
        public float fStatFrametimeMax;    // Maximum frametime
        public float fStatFrametime1Dot0Pct; // 1% high frametime
        public float fStatFrametime0Dot1Pct; // 0.1% high frametime
    }
    
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private readonly LoggingService _logging;
    private bool _isAvailable;
    private Timer? _pollTimer;
    
    public bool IsAvailable => _isAvailable;
    public float CurrentFps { get; private set; }
    public float AverageFps { get; private set; }
    public float MinFps { get; private set; }
    public float MaxFps { get; private set; }
    public float OnePercentLow { get; private set; }
    public float FrametimeMs { get; private set; }
    public string CurrentProcess { get; private set; } = "";
    
    public event Action<RtssFrameData>? OnFrameDataUpdated;
    
    public RtssIntegrationService(LoggingService logging)
    {
        _logging = logging;
    }
    
    /// <summary>
    /// Initialize RTSS shared memory connection.
    /// </summary>
    public bool Initialize()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(RTSS_SHARED_MEMORY_NAME, MemoryMappedFileRights.Read);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            
            // Verify signature
            _accessor.Read(0, out RTSS_SHARED_MEMORY_HEADER header);
            if (header.dwSignature != 0x53535452) // 'RTSS'
            {
                _logging.Warn("RTSS: Invalid shared memory signature");
                Cleanup();
                return false;
            }
            
            _isAvailable = true;
            _logging.Info($"RTSS: Connected (version {header.dwVersion}, {header.dwAppArrSize} app slots)");
            return true;
        }
        catch (FileNotFoundException)
        {
            _logging.Info("RTSS: Not running or shared memory disabled");
            return false;
        }
        catch (Exception ex)
        {
            _logging.Warn($"RTSS: Failed to connect: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Start polling for frame data.
    /// </summary>
    public void StartPolling(int intervalMs = 500)
    {
        if (_pollTimer != null) return;
        
        _pollTimer = new Timer(_ => PollFrameData(), null, 0, intervalMs);
        BackgroundTimerRegistry.Register(
            "RtssFrameDataPoll",
            "RtssIntegrationService",
            "Polls RTSS shared memory for frame time and FPS data",
            intervalMs,
            BackgroundTimerTier.VisibleOnly);
    }
    
    /// <summary>
    /// Stop polling for frame data.
    /// </summary>
    public void StopPolling()
    {
        BackgroundTimerRegistry.Unregister("RtssFrameDataPoll");
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
    
    /// <summary>
    /// Get current frame data for the active game/app.
    /// </summary>
    public RtssFrameData? GetCurrentFrameData()
    {
        if (!_isAvailable || _accessor == null) return null;
        
        try
        {
            // Read header
            _accessor.Read(0, out RTSS_SHARED_MEMORY_HEADER header);
            
            // Find active application (the one with recent frames)
            var appOffset = (int)header.dwAppArrOffset;
            var appSize = (int)header.dwAppEntrySize;
            
            for (int i = 0; i < header.dwAppArrSize; i++)
            {
                var entryOffset = appOffset + (i * appSize);
                _accessor.Read(entryOffset, out RTSS_SHARED_MEMORY_APP_ENTRY entry);
                
                // Skip empty entries
                if (entry.dwProcessID == 0) continue;
                
                // Skip entries with no frames
                if (entry.dwFrames == 0) continue;
                
                // Calculate instant FPS from frame time
                float instantFps = 0;
                if (entry.dwInstantFrameTime > 0)
                {
                    instantFps = 1000000.0f / entry.dwInstantFrameTime;
                }
                
                return new RtssFrameData
                {
                    ProcessId = (int)entry.dwProcessID,
                    ProcessName = entry.szName,
                    InstantFps = instantFps,
                    AverageFps = entry.fStatFramerateAvg,
                    MinFps = entry.fStatFramerateMin,
                    MaxFps = entry.fStatFramerateMax,
                    OnePercentLow = entry.fStatFramerate1Dot0Pct,
                    PointOnePercentLow = entry.fStatFramerate0Dot1Pct,
                    FrametimeMs = entry.fStatFrametimeAvg,
                    FrametimeMax = entry.fStatFrametimeMax,
                    FrameCount = (int)entry.dwFrames
                };
            }
        }
        catch (Exception ex)
        {
            _logging.Warn($"RTSS: Failed to read frame data: {ex.Message}");
        }
        
        return null;
    }
    
    private void PollFrameData()
    {
        if (!_isAvailable)
        {
            // Try to reconnect
            Initialize();
            return;
        }
        
        var data = GetCurrentFrameData();
        if (data != null)
        {
            CurrentFps = data.InstantFps;
            AverageFps = data.AverageFps;
            MinFps = data.MinFps;
            MaxFps = data.MaxFps;
            OnePercentLow = data.OnePercentLow;
            FrametimeMs = data.FrametimeMs;
            CurrentProcess = data.ProcessName;
            
            OnFrameDataUpdated?.Invoke(data);
        }
    }
    
    private void Cleanup()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
        _isAvailable = false;
    }
    
    public void Dispose()
    {
        StopPolling();
        Cleanup();
    }
}

/// <summary>
/// Frame data from RTSS.
/// </summary>
public class RtssFrameData
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public float InstantFps { get; init; }
    public float AverageFps { get; init; }
    public float MinFps { get; init; }
    public float MaxFps { get; init; }
    public float OnePercentLow { get; init; }
    public float PointOnePercentLow { get; init; }
    public float FrametimeMs { get; init; }
    public float FrametimeMax { get; init; }
    public int FrameCount { get; init; }
}
