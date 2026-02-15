using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;
using OmenCore.Services.Rgb;

namespace OmenCore.Services
{
    /// <summary>
    /// Unified RGB scene service that manages presets, scheduling, and coordinated RGB control.
    /// </summary>
    public class RgbSceneService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly RgbManager _rgbManager;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly ConfigurationService? _configService;
        private readonly List<RgbScene> _scenes = new();
        private readonly Timer? _scheduleTimer;
        private readonly SemaphoreSlim _applyLock = new(1, 1);
        
        private RgbScene? _currentScene;
        private RgbScene? _defaultScene;
        private bool _isDisposed;
        
        /// <summary>
        /// Event fired when a scene is applied.
        /// </summary>
        public event EventHandler<RgbSceneChangedEventArgs>? SceneChanged;
        
        /// <summary>
        /// Event fired when scene list is modified.
        /// </summary>
        public event EventHandler? ScenesListChanged;
        
        /// <summary>
        /// Currently active scene.
        /// </summary>
        public RgbScene? CurrentScene => _currentScene;
        
        /// <summary>
        /// All available scenes.
        /// </summary>
        public IReadOnlyList<RgbScene> Scenes => _scenes;
        
        /// <summary>
        /// Whether scene scheduling is enabled.
        /// </summary>
        public bool IsSchedulingEnabled { get; set; } = true;
        
        /// <summary>
        /// Whether performance mode triggers are enabled.
        /// </summary>
        public bool IsPerformanceTriggerEnabled { get; set; } = true;

        public RgbSceneService(
            LoggingService logging,
            RgbManager rgbManager,
            KeyboardLightingService? keyboardLightingService = null,
            ConfigurationService? configService = null)
        {
            _logging = logging;
            _rgbManager = rgbManager;
            _keyboardLightingService = keyboardLightingService;
            _configService = configService;
            
            // Initialize with built-in scenes
            InitializeBuiltInScenes();
            
            // Load saved scenes
            LoadScenesFromConfig();
            
            // Start schedule timer (checks every minute)
            _scheduleTimer = new Timer(CheckScheduledScenes, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
            
            _logging.Info($"RgbSceneService initialized with {_scenes.Count} scenes");
        }

        private void InitializeBuiltInScenes()
        {
            // OMEN Red - Default
            _scenes.Add(new RgbScene
            {
                Id = "omen-red",
                Name = "OMEN Red",
                Description = "Classic OMEN red theme",
                Icon = "🔴",
                Effect = RgbSceneEffect.Static,
                PrimaryColor = "#E6002E",
                Brightness = 100,
                IsDefault = true
            });
            
            // Gaming - Performance mode trigger
            _scenes.Add(new RgbScene
            {
                Id = "gaming",
                Name = "Gaming",
                Description = "Intense gaming colors",
                Icon = "🎮",
                Effect = RgbSceneEffect.Breathing,
                PrimaryColor = "#FF4500",
                SecondaryColor = "#FF0000",
                Speed = 70,
                Brightness = 100,
                TriggerOnPerformanceMode = "Performance"
            });
            
            // Night Mode
            _scenes.Add(new RgbScene
            {
                Id = "night",
                Name = "Night Mode",
                Description = "Dim, warm lighting for nighttime",
                Icon = "🌙",
                Effect = RgbSceneEffect.Static,
                PrimaryColor = "#331100",
                Brightness = 30,
                ScheduledTime = "22:00"
            });
            
            // Work Mode
            _scenes.Add(new RgbScene
            {
                Id = "work",
                Name = "Work",
                Description = "Clean, professional white lighting",
                Icon = "💼",
                Effect = RgbSceneEffect.Static,
                PrimaryColor = "#FFFFFF",
                Brightness = 80,
                ScheduledTime = "09:00",
                ScheduledDays = new List<int> { 1, 2, 3, 4, 5 } // Mon-Fri
            });
            
            // Rainbow
            _scenes.Add(new RgbScene
            {
                Id = "rainbow",
                Name = "Rainbow",
                Description = "Full spectrum cycling",
                Icon = "🌈",
                Effect = RgbSceneEffect.Spectrum,
                Speed = 50,
                Brightness = 100
            });
            
            // Cool Blue
            _scenes.Add(new RgbScene
            {
                Id = "cool-blue",
                Name = "Cool Blue",
                Description = "Calm blue theme",
                Icon = "💙",
                Effect = RgbSceneEffect.Static,
                PrimaryColor = "#0066FF",
                Brightness = 100
            });
            
            // Ambient
            _scenes.Add(new RgbScene
            {
                Id = "ambient",
                Name = "Ambient",
                Description = "Syncs with screen colors",
                Icon = "✨",
                Effect = RgbSceneEffect.Ambient,
                Brightness = 100
            });
            
            // Off
            _scenes.Add(new RgbScene
            {
                Id = "off",
                Name = "Lights Off",
                Description = "Turn off all RGB",
                Icon = "⬛",
                Effect = RgbSceneEffect.Off,
                Brightness = 0
            });
            
            _defaultScene = _scenes.FirstOrDefault(s => s.IsDefault);
        }

        private void LoadScenesFromConfig()
        {
            try
            {
                var scenesPath = GetScenesFilePath();
                if (File.Exists(scenesPath))
                {
                    var json = File.ReadAllText(scenesPath);
                    var savedScenes = JsonSerializer.Deserialize<List<RgbScene>>(json);
                    
                    if (savedScenes != null)
                    {
                        // Merge saved scenes with built-in (saved takes precedence by ID)
                        foreach (var saved in savedScenes)
                        {
                            var existing = _scenes.FirstOrDefault(s => s.Id == saved.Id);
                            if (existing != null)
                            {
                                _scenes.Remove(existing);
                            }
                            _scenes.Add(saved);
                        }
                        
                        _defaultScene = _scenes.FirstOrDefault(s => s.IsDefault) ?? _scenes.FirstOrDefault();
                        _logging.Info($"Loaded {savedScenes.Count} saved RGB scenes");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load saved scenes: {ex.Message}");
            }
        }

        /// <summary>
        /// Save scenes to config file.
        /// </summary>
        public void SaveScenes()
        {
            try
            {
                var scenesPath = GetScenesFilePath();
                var directory = Path.GetDirectoryName(scenesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonSerializer.Serialize(_scenes, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(scenesPath, json);
                _logging.Info($"Saved {_scenes.Count} RGB scenes");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to save RGB scenes", ex);
            }
        }

        private string GetScenesFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "OmenCore", "rgb_scenes.json");
        }

        /// <summary>
        /// Apply a scene by ID.
        /// </summary>
        public async Task<RgbSceneApplyResult> ApplySceneAsync(string sceneId, string trigger = "manual")
        {
            var scene = _scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene == null)
            {
                return new RgbSceneApplyResult
                {
                    Success = false,
                    SceneId = sceneId,
                    Errors = new List<string> { $"Scene '{sceneId}' not found" }
                };
            }
            
            return await ApplySceneAsync(scene, trigger);
        }

        /// <summary>
        /// Apply a scene.
        /// </summary>
        public async Task<RgbSceneApplyResult> ApplySceneAsync(RgbScene scene, string trigger = "manual")
        {
            var result = new RgbSceneApplyResult
            {
                SceneId = scene.Id,
                SceneName = scene.Name
            };
            
            var sw = Stopwatch.StartNew();
            
            if (!await _applyLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                result.Success = false;
                result.Errors.Add("Timeout waiting for apply lock");
                return result;
            }
            
            try
            {
                var previousScene = _currentScene;
                _currentScene = scene;
                
                var tasks = new List<Task<bool>>();
                
                // Apply to HP OMEN keyboard
                if (scene.ApplyToOmenKeyboard && _keyboardLightingService?.IsAvailable == true)
                {
                    tasks.Add(ApplyToOmenKeyboardAsync(scene));
                }
                
                // Apply to RgbManager providers
                if (scene.ApplyToCorsair || scene.ApplyToLogitech || scene.ApplyToRazer)
                {
                    tasks.Add(ApplyToRgbManagerAsync(scene));
                }
                
                var results = await Task.WhenAll(tasks);
                result.ProvidersApplied = results.Count(r => r);
                result.ProvidersFailed = results.Count(r => !r);
                result.Success = result.ProvidersApplied > 0 || result.ProvidersFailed == 0;
                
                sw.Stop();
                result.ApplyDuration = sw.Elapsed;
                
                // Fire event
                SceneChanged?.Invoke(this, new RgbSceneChangedEventArgs
                {
                    PreviousScene = previousScene,
                    CurrentScene = scene,
                    Trigger = trigger
                });
                
                _logging.Info($"Applied scene '{scene.Name}' ({trigger}) in {sw.ElapsedMilliseconds}ms - {result.ProvidersApplied} providers");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add(ex.Message);
                _logging.Error($"Failed to apply scene '{scene.Name}'", ex);
            }
            finally
            {
                _applyLock.Release();
            }
            
            return result;
        }

        private async Task<bool> ApplyToOmenKeyboardAsync(RgbScene scene)
        {
            if (_keyboardLightingService == null) return false;
            
            try
            {
                return await Task.Run(async () =>
                {
                    if (scene.Effect == RgbSceneEffect.Off)
                    {
                        _keyboardLightingService.SetBrightness(0);
                        return true;
                    }
                    
                    // Apply brightness
                    var brightnessLevel = scene.Brightness switch
                    {
                        0 => 0,
                        <= 33 => 1,
                        <= 66 => 2,
                        _ => 3
                    };
                    _keyboardLightingService.SetBrightness(brightnessLevel);
                    
                    // Apply colors
                    if (scene.ZoneColors.Count > 0)
                    {
                        // Per-zone colors
                        var colors = new Color[4];
                        for (int i = 0; i < 4; i++)
                        {
                            if (scene.ZoneColors.TryGetValue(i, out var hex))
                            {
                                colors[i] = ColorTranslator.FromHtml(hex);
                            }
                            else
                            {
                                colors[i] = ColorTranslator.FromHtml(scene.PrimaryColor);
                            }
                        }
                        await _keyboardLightingService.SetAllZoneColors(colors);
                    }
                    else
                    {
                        // Single color to all zones
                        var color = ColorTranslator.FromHtml(scene.PrimaryColor);
                        await _keyboardLightingService.SetAllZoneColors(new[] { color, color, color, color });
                    }
                    
                    return true;
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to apply scene to OMEN keyboard: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ApplyToRgbManagerAsync(RgbScene scene)
        {
            try
            {
                var color = ColorTranslator.FromHtml(scene.PrimaryColor);
                
                switch (scene.Effect)
                {
                    case RgbSceneEffect.Static:
                    case RgbSceneEffect.Gradient:
                        await _rgbManager.SyncStaticColorAsync(color);
                        break;
                        
                    case RgbSceneEffect.Breathing:
                        await _rgbManager.SyncBreathingEffectAsync(color);
                        break;
                        
                    case RgbSceneEffect.Spectrum:
                    case RgbSceneEffect.Wave:
                        await _rgbManager.SyncSpectrumEffectAsync();
                        break;
                        
                    case RgbSceneEffect.Off:
                        await _rgbManager.TurnOffAllAsync();
                        break;
                        
                    case RgbSceneEffect.Ambient:
                        // Ambient is handled by ScreenSamplingService
                        break;
                        
                    default:
                        await _rgbManager.SyncStaticColorAsync(color);
                        break;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to apply scene to RGB manager: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handle performance mode change to trigger associated scene.
        /// </summary>
        public async Task OnPerformanceModeChangedAsync(string newMode)
        {
            if (!IsPerformanceTriggerEnabled) return;
            
            var triggerScene = _scenes.FirstOrDefault(s => 
                !string.IsNullOrEmpty(s.TriggerOnPerformanceMode) &&
                string.Equals(s.TriggerOnPerformanceMode, newMode, StringComparison.OrdinalIgnoreCase));
            
            if (triggerScene != null)
            {
                _logging.Info($"Performance mode '{newMode}' triggered scene '{triggerScene.Name}'");
                await ApplySceneAsync(triggerScene, "performance");
            }
        }

        private void CheckScheduledScenes(object? state)
        {
            if (!IsSchedulingEnabled) return;
            
            try
            {
                var now = DateTime.Now;
                var currentTime = now.ToString("HH:mm");
                var dayOfWeek = (int)now.DayOfWeek;
                
                foreach (var scene in _scenes.Where(s => !string.IsNullOrEmpty(s.ScheduledTime)))
                {
                    if (scene.ScheduledTime != currentTime) continue;
                    
                    // Check if today is in scheduled days (empty = all days)
                    if (scene.ScheduledDays.Count > 0 && !scene.ScheduledDays.Contains(dayOfWeek))
                        continue;
                    
                    // Avoid re-applying if already current
                    if (_currentScene?.Id == scene.Id) continue;
                    
                    _logging.Info($"Scheduled time triggered scene '{scene.Name}'");
                    _ = ApplySceneAsync(scene, "schedule");
                    break; // Only apply one scene per check
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Error checking scheduled scenes: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new scene.
        /// </summary>
        public void AddScene(RgbScene scene)
        {
            if (_scenes.Any(s => s.Id == scene.Id))
            {
                throw new ArgumentException($"Scene with ID '{scene.Id}' already exists");
            }
            
            _scenes.Add(scene);
            SaveScenes();
            ScenesListChanged?.Invoke(this, EventArgs.Empty);
            _logging.Info($"Added new scene: {scene.Name}");
        }

        /// <summary>
        /// Update an existing scene.
        /// </summary>
        public void UpdateScene(RgbScene scene)
        {
            var existing = _scenes.FirstOrDefault(s => s.Id == scene.Id);
            if (existing == null)
            {
                throw new ArgumentException($"Scene with ID '{scene.Id}' not found");
            }
            
            var index = _scenes.IndexOf(existing);
            _scenes[index] = scene;
            
            if (scene.IsDefault)
            {
                // Ensure only one default
                foreach (var s in _scenes.Where(s => s.Id != scene.Id))
                {
                    s.IsDefault = false;
                }
                _defaultScene = scene;
            }
            
            SaveScenes();
            ScenesListChanged?.Invoke(this, EventArgs.Empty);
            _logging.Info($"Updated scene: {scene.Name}");
        }

        /// <summary>
        /// Remove a scene.
        /// </summary>
        public void RemoveScene(string sceneId)
        {
            var scene = _scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene == null) return;
            
            // Don't remove built-in scenes
            if (IsBuiltInScene(sceneId))
            {
                _logging.Warn($"Cannot remove built-in scene: {scene.Name}");
                return;
            }
            
            _scenes.Remove(scene);
            SaveScenes();
            ScenesListChanged?.Invoke(this, EventArgs.Empty);
            _logging.Info($"Removed scene: {scene.Name}");
        }

        private bool IsBuiltInScene(string sceneId)
        {
            return sceneId is "omen-red" or "gaming" or "night" or "work" or "rainbow" or "cool-blue" or "ambient" or "off";
        }

        /// <summary>
        /// Create a scene from current device colors.
        /// </summary>
        public RgbScene CreateSceneFromCurrent(string name)
        {
            var scene = new RgbScene
            {
                Name = name,
                Description = "Created from current settings",
                Icon = "📷"
            };
            
            // Copy current scene settings if available
            if (_currentScene != null)
            {
                scene.Effect = _currentScene.Effect;
                scene.PrimaryColor = _currentScene.PrimaryColor;
                scene.SecondaryColor = _currentScene.SecondaryColor;
                scene.Speed = _currentScene.Speed;
                scene.Brightness = _currentScene.Brightness;
                scene.ZoneColors = new Dictionary<int, string>(_currentScene.ZoneColors);
            }
            
            return scene;
        }

        /// <summary>
        /// Apply the default scene.
        /// </summary>
        public async Task ApplyDefaultSceneAsync()
        {
            if (_defaultScene != null)
            {
                await ApplySceneAsync(_defaultScene, "startup");
            }
        }

        /// <summary>
        /// Get scene by ID.
        /// </summary>
        public RgbScene? GetScene(string sceneId)
        {
            return _scenes.FirstOrDefault(s => s.Id == sceneId);
        }

        /// <summary>
        /// Get scenes triggered by a specific performance mode.
        /// </summary>
        public IEnumerable<RgbScene> GetScenesForPerformanceMode(string mode)
        {
            return _scenes.Where(s => 
                string.Equals(s.TriggerOnPerformanceMode, mode, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get scenes with scheduled times.
        /// </summary>
        public IEnumerable<RgbScene> GetScheduledScenes()
        {
            return _scenes.Where(s => !string.IsNullOrEmpty(s.ScheduledTime));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            _scheduleTimer?.Dispose();
            _applyLock.Dispose();
            
            _logging.Info("RgbSceneService disposed");
        }
    }
}
