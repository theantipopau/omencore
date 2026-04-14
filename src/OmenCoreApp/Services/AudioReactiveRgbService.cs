using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OmenCore.Services.Rgb;

namespace OmenCore.Services
{
    /// <summary>
    /// Service that captures system audio and generates RGB effects based on audio analysis.
    /// Supports beat detection, frequency spectrum analysis, and volume-based effects.
    /// </summary>
    public class AudioReactiveRgbService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly List<IRgbProvider> _rgbProviders = new();
        private bool _disposed;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private Task? _processingTask;

        // Audio capture
        private IAudioCapture? _audioCapture;
        private float[] _audioBuffer = new float[2048];
        private float[] _fftBuffer = new float[1024];
        private float[] _frequencyBands = new float[8]; // 8-band spectrum

        // Beat detection
        private float _beatThreshold = 0.3f;
#pragma warning disable CS0414 // Reserved for future beat detection enhancement
        private float _beatEnergy = 0f;
#pragma warning restore CS0414
        private float _beatEnergyHistory = 0f;
        private DateTime _lastBeat = DateTime.MinValue;
        private readonly TimeSpan _beatCooldown = TimeSpan.FromMilliseconds(100);

        // Color generation
        private (byte R, byte G, byte B) _currentColor = (255, 0, 0);
        private float _hue = 0f;
        private float _brightness = 1f;

        #region Properties

        /// <summary>
        /// Gets whether the service is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Gets or sets the visualization mode.
        /// </summary>
        public AudioVisualizationMode Mode { get; set; } = AudioVisualizationMode.Pulse;

        /// <summary>
        /// Gets or sets the sensitivity (0.0 - 1.0).
        /// </summary>
        public float Sensitivity { get; set; } = 0.5f;

        /// <summary>
        /// Gets or sets the color palette for spectrum mode.
        /// </summary>
        public ColorPalette Palette { get; set; } = ColorPalette.Rainbow;

        /// <summary>
        /// Gets or sets the base color for pulse mode.
        /// </summary>
        public (byte R, byte G, byte B) BaseColor { get; set; } = (255, 0, 100);

        /// <summary>
        /// Gets or sets the update rate in milliseconds.
        /// </summary>
        public int UpdateIntervalMs { get; set; } = 33; // ~30 FPS

        /// <summary>
        /// Gets the current audio level (0.0 - 1.0).
        /// </summary>
        public float CurrentLevel { get; private set; }

        /// <summary>
        /// Gets whether a beat was detected in the last frame.
        /// </summary>
        public bool BeatDetected { get; private set; }

        /// <summary>
        /// Gets the 8-band frequency spectrum (bass to treble).
        /// </summary>
        public float[] FrequencySpectrum => _frequencyBands;

        /// <summary>
        /// Event raised when audio data is processed.
        /// </summary>
        public event EventHandler<AudioDataEventArgs>? AudioDataProcessed;

        /// <summary>
        /// Event raised when a beat is detected.
        /// </summary>
        public event EventHandler? BeatOccurred;

        #endregion

        public AudioReactiveRgbService(LoggingService logging, KeyboardLightingService? keyboardLightingService = null)
        {
            _logging = logging;
            _keyboardLightingService = keyboardLightingService;
        }

        /// <summary>
        /// Register an RGB provider to receive color updates.
        /// </summary>
        public void RegisterProvider(IRgbProvider provider)
        {
            if (!_rgbProviders.Contains(provider))
                _rgbProviders.Add(provider);
        }

        /// <summary>
        /// Start audio capture and RGB processing.
        /// </summary>
        public Task<bool> StartAsync()
        {
            if (_isRunning) return Task.FromResult(true);

            try
            {
                _logging.Info("AudioReactiveRGB: Starting audio capture...");

                // Initialize audio capture (WASAPI loopback)
                _audioCapture = CreateAudioCapture();
                if (_audioCapture == null || !_audioCapture.Initialize())
                {
                    _logging.Warn("AudioReactiveRGB: Failed to initialize audio capture");
                    return Task.FromResult(false);
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;

                // Start capture
                _audioCapture.DataAvailable += OnAudioDataAvailable;
                _audioCapture.Start();

                // Start processing loop
                _processingTask = Task.Run(() => ProcessingLoop(_cts.Token), _cts.Token);

                _logging.Info("AudioReactiveRGB: Started successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"AudioReactiveRGB: Start failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Stop audio capture and RGB processing.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _logging.Info("AudioReactiveRGB: Stopping...");

            _cts?.Cancel();

            if (_audioCapture != null)
            {
                _audioCapture.DataAvailable -= OnAudioDataAvailable;
                _audioCapture.Stop();
                _audioCapture.Dispose();
                _audioCapture = null;
            }

            if (_processingTask != null)
            {
                try
                {
                    await _processingTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }

            _isRunning = false;
            _logging.Info("AudioReactiveRGB: Stopped");
        }

        /// <summary>
        /// Handle incoming audio data.
        /// </summary>
        private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
        {
            if (!_isRunning) return;

            // Copy to buffer
            var copyLength = Math.Min(e.Samples.Length, _audioBuffer.Length);
            Array.Copy(e.Samples, _audioBuffer, copyLength);
        }

        /// <summary>
        /// Main processing loop.
        /// </summary>
        private async Task ProcessingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Analyze audio
                    AnalyzeAudio();

                    // Generate RGB based on mode
                    GenerateRgb();

                    // Apply to lighting
                    if (_rgbProviders.Count > 0)
                    {
                        await ApplyToLightingAsync();
                    }

                    // Raise event
                    AudioDataProcessed?.Invoke(this, new AudioDataEventArgs(_audioBuffer, CurrentLevel, _frequencyBands));

                    // Wait for next frame
                    await Task.Delay(UpdateIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error($"AudioReactiveRGB: Processing error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        /// <summary>
        /// Analyze audio buffer for levels, beats, and spectrum.
        /// </summary>
        private void AnalyzeAudio()
        {
            // Calculate RMS level
            float sum = 0f;
            for (int i = 0; i < _audioBuffer.Length; i++)
            {
                sum += _audioBuffer[i] * _audioBuffer[i];
            }
            float rms = (float)Math.Sqrt(sum / _audioBuffer.Length);
            
            // Apply sensitivity
            CurrentLevel = Math.Clamp(rms * (Sensitivity * 4 + 1), 0f, 1f);

            // Simple FFT approximation (8-band spectrum)
            CalculateSpectrumBands();

            // Beat detection (focus on bass frequencies)
            float bassEnergy = (_frequencyBands[0] + _frequencyBands[1]) / 2f;
            
            // Running average for beat threshold
            _beatEnergyHistory = _beatEnergyHistory * 0.95f + bassEnergy * 0.05f;
            _beatThreshold = _beatEnergyHistory * 1.5f + 0.1f;

            // Detect beat
            var now = DateTime.UtcNow;
            if (bassEnergy > _beatThreshold && (now - _lastBeat) > _beatCooldown)
            {
                BeatDetected = true;
                _lastBeat = now;
                BeatOccurred?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                BeatDetected = false;
            }
        }

        /// <summary>
        /// Calculate 8-band frequency spectrum from audio buffer.
        /// </summary>
        private void CalculateSpectrumBands()
        {
            // Simple band-pass approximation without full FFT
            // Bands: Sub-bass, Bass, Low-mid, Mid, High-mid, Presence, Brilliance, Air
            int[] bandBoundaries = { 0, 32, 64, 128, 256, 512, 768, 896, 1024 };
            
            // Simple energy calculation per band
            int samplesPerBand = _audioBuffer.Length / 8;
            
            for (int band = 0; band < 8; band++)
            {
                float energy = 0f;
                int start = band * samplesPerBand;
                int end = start + samplesPerBand;
                
                for (int i = start; i < end && i < _audioBuffer.Length; i++)
                {
                    energy += Math.Abs(_audioBuffer[i]);
                }
                
                energy /= samplesPerBand;
                
                // Apply frequency weighting (bass needs more boost)
                float weight = 1f + (7 - band) * 0.2f;
                _frequencyBands[band] = Math.Clamp(energy * weight * (Sensitivity * 4 + 1), 0f, 1f);
            }
        }

        /// <summary>
        /// Generate RGB color based on current mode and audio data.
        /// </summary>
        private void GenerateRgb()
        {
            switch (Mode)
            {
                case AudioVisualizationMode.Pulse:
                    GeneratePulseColor();
                    break;
                case AudioVisualizationMode.Spectrum:
                    GenerateSpectrumColor();
                    break;
                case AudioVisualizationMode.VuMeter:
                    GenerateVuMeterColor();
                    break;
                case AudioVisualizationMode.Wave:
                    GenerateWaveColor();
                    break;
                case AudioVisualizationMode.BeatSync:
                    GenerateBeatSyncColor();
                    break;
            }
        }

        /// <summary>
        /// Pulse mode: Flash on beat with base color.
        /// </summary>
        private void GeneratePulseColor()
        {
            if (BeatDetected)
            {
                _brightness = 1f;
            }
            else
            {
                _brightness = Math.Max(0.1f, _brightness * 0.9f); // Decay
            }

            _currentColor = (
                (byte)(BaseColor.R * _brightness),
                (byte)(BaseColor.G * _brightness),
                (byte)(BaseColor.B * _brightness)
            );
        }

        /// <summary>
        /// Spectrum mode: Color based on dominant frequency.
        /// </summary>
        private void GenerateSpectrumColor()
        {
            // Find dominant frequency band
            int maxBand = 0;
            float maxValue = 0f;
            for (int i = 0; i < _frequencyBands.Length; i++)
            {
                if (_frequencyBands[i] > maxValue)
                {
                    maxValue = _frequencyBands[i];
                    maxBand = i;
                }
            }

            // Map band to hue (bass = red, treble = violet)
            _hue = maxBand / 8f;
            _brightness = maxValue;

            _currentColor = HsvToRgb(_hue, 1f, _brightness);
        }

        /// <summary>
        /// VU Meter mode: Brightness based on volume level.
        /// </summary>
        private void GenerateVuMeterColor()
        {
            // Green to yellow to red based on level
            float level = CurrentLevel;
            
            byte r, g, b;
            if (level < 0.5f)
            {
                // Green to yellow
                r = (byte)(level * 2 * 255);
                g = 255;
                b = 0;
            }
            else
            {
                // Yellow to red
                r = 255;
                g = (byte)((1 - (level - 0.5f) * 2) * 255);
                b = 0;
            }

            _currentColor = (r, g, b);
        }

        /// <summary>
        /// Wave mode: Rainbow wave that moves with beats.
        /// </summary>
        private void GenerateWaveColor()
        {
            // Slowly rotate hue, jump forward on beat
            if (BeatDetected)
            {
                _hue += 0.1f;
            }
            else
            {
                _hue += 0.005f;
            }
            
            if (_hue >= 1f) _hue = 0f;

            _brightness = 0.5f + CurrentLevel * 0.5f;
            _currentColor = HsvToRgb(_hue, 1f, _brightness);
        }

        /// <summary>
        /// Beat sync mode: Change color on each beat.
        /// </summary>
        private void GenerateBeatSyncColor()
        {
            if (BeatDetected)
            {
                _hue += 0.15f;
                if (_hue >= 1f) _hue = 0f;
                _brightness = 1f;
            }
            else
            {
                _brightness = Math.Max(0.3f, _brightness * 0.95f);
            }

            _currentColor = HsvToRgb(_hue, 1f, _brightness);
        }

        /// <summary>
        /// Convert HSV to RGB.
        /// </summary>
        private (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
        {
            float r, g, b;
            
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        /// <summary>
        /// Apply generated color to registered RGB providers.
        /// </summary>
        private async Task ApplyToLightingAsync()
        {
            if (_rgbProviders.Count == 0 && _keyboardLightingService?.IsAvailable != true) return;

            try
            {
                var color = System.Drawing.Color.FromArgb(_currentColor.R, _currentColor.G, _currentColor.B);
                foreach (var provider in _rgbProviders)
                {
                    if (provider.IsAvailable)
                    {
                        await provider.SetStaticColorAsync(color);
                    }
                }

                if (_keyboardLightingService?.IsAvailable == true)
                {
                    await _keyboardLightingService.SetAllZoneColors(new[] { color, color, color, color });
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"AudioReactiveRGB: Failed to apply lighting: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the current generated color.
        /// </summary>
        public (byte R, byte G, byte B) GetCurrentColor() => _currentColor;

        /// <summary>
        /// Create audio capture implementation.
        /// </summary>
        private IAudioCapture? CreateAudioCapture()
        {
            try
            {
                return new WasapiLoopbackCapture(_logging);
            }
            catch (Exception ex)
            {
                _logging.Error($"AudioReactiveRGB: Failed to create audio capture: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopAsync().Wait(TimeSpan.FromSeconds(2));
            _cts?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Audio visualization modes.
    /// </summary>
    public enum AudioVisualizationMode
    {
        /// <summary>Flash on beat with base color.</summary>
        Pulse,
        /// <summary>Color based on dominant frequency.</summary>
        Spectrum,
        /// <summary>Brightness based on volume level (green-yellow-red).</summary>
        VuMeter,
        /// <summary>Rainbow wave that moves with beats.</summary>
        Wave,
        /// <summary>Change color on each beat.</summary>
        BeatSync
    }

    /// <summary>
    /// Color palette options.
    /// </summary>
    public enum ColorPalette
    {
        Rainbow,
        Fire,
        Ocean,
        Forest,
        Neon,
        Custom
    }

    /// <summary>
    /// Event args for audio data events.
    /// </summary>
    public class AudioDataEventArgs : EventArgs
    {
        public float[] Samples { get; }
        public float Level { get; }
        public float[] Spectrum { get; }

        public AudioDataEventArgs(float[] samples, float level, float[] spectrum)
        {
            Samples = samples;
            Level = level;
            Spectrum = spectrum;
        }
    }

    /// <summary>
    /// Interface for audio capture implementations.
    /// </summary>
    public interface IAudioCapture : IDisposable
    {
        bool Initialize();
        void Start();
        void Stop();
        event EventHandler<AudioDataEventArgs>? DataAvailable;
    }

    /// <summary>
    /// WASAPI loopback audio capture for Windows.
    /// </summary>
    public class WasapiLoopbackCapture : IAudioCapture
    {
        private readonly LoggingService _logging;
        private NAudio.Wave.WasapiLoopbackCapture? _capture;
        private BufferedWaveProvider? _bufferedProvider;
        private ISampleProvider? _sampleProvider;
        private System.Threading.Timer? _readTimer;
        private readonly float[] _sampleReadBuffer = new float[2048];
        private bool _isCapturing;

        public event EventHandler<AudioDataEventArgs>? DataAvailable;

        public WasapiLoopbackCapture(LoggingService logging)
        {
            _logging = logging;
        }

        public bool Initialize()
        {
            try
            {
                _capture = new NAudio.Wave.WasapiLoopbackCapture();
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(2)
                };
                _sampleProvider = _bufferedProvider.ToSampleProvider();

                _capture.DataAvailable += OnCaptureDataAvailable;
                _capture.RecordingStopped += (_, _) => _isCapturing = false;

                _logging.Info($"WasapiLoopbackCapture: Initialized ({_capture.WaveFormat.SampleRate} Hz, {_capture.WaveFormat.Channels} ch)");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"WasapiLoopbackCapture: Init failed: {ex.Message}");
                return false;
            }
        }

        public void Start()
        {
            if (_isCapturing || _capture == null) return;

            _isCapturing = true;
            _capture.StartRecording();
            _readTimer = new System.Threading.Timer(ReadBufferedSamples, null, 0, 33);
        }

        public void Stop()
        {
            _isCapturing = false;

            _readTimer?.Dispose();
            _readTimer = null;

            try
            {
                _capture?.StopRecording();
            }
            catch
            {
                // Best effort stop.
            }
        }

        private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isCapturing || _bufferedProvider == null || e.BytesRecorded <= 0)
                return;

            _bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void ReadBufferedSamples(object? state)
        {
            if (!_isCapturing || _sampleProvider == null)
                return;

            try
            {
                var read = _sampleProvider.Read(_sampleReadBuffer, 0, _sampleReadBuffer.Length);
                if (read <= 0)
                {
                    return;
                }

                if (read < _sampleReadBuffer.Length)
                {
                    Array.Clear(_sampleReadBuffer, read, _sampleReadBuffer.Length - read);
                }

                var snapshot = new float[_sampleReadBuffer.Length];
                Array.Copy(_sampleReadBuffer, snapshot, _sampleReadBuffer.Length);
                DataAvailable?.Invoke(this, new AudioDataEventArgs(snapshot, 0f, Array.Empty<float>()));
            }
            catch (Exception ex)
            {
                _logging.Error($"WasapiLoopbackCapture: Read error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            if (_capture != null)
            {
                _capture.DataAvailable -= OnCaptureDataAvailable;
                _capture.Dispose();
                _capture = null;
            }

            _bufferedProvider = null;
            _sampleProvider = null;
        }
    }
}
