using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.SystemOptimizer
{
    /// <summary>
    /// Handles backup and restoration of registry keys modified by optimizations.
    /// Creates Windows restore points before major changes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class RegistryBackupService
    {
        private readonly LoggingService _logger;
        private readonly string _backupPath;
        private readonly Dictionary<string, object?> _backupCache;

        public RegistryBackupService(LoggingService logger)
        {
            _logger = logger;
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OmenCore", "registry_backup");
            _backupCache = new Dictionary<string, object?>();
            
            // Ensure backup directory exists
            Directory.CreateDirectory(_backupPath);
        }

        /// <summary>
        /// Creates a Windows System Restore point before applying optimizations.
        /// </summary>
        public async Task<bool> CreateRestorePointAsync(string description)
        {
            try
            {
                _logger.Info($"Creating system restore point: {description}");
                
                // Use WMI to create restore point
                var result = await Task.Run(() =>
                {
                    try
                    {
                        using var process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = "powershell.exe";
                        process.StartInfo.Arguments = $"-Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction SilentlyContinue\"";
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.Start();
                        process.WaitForExit(60000); // 60 second timeout
                        return process.ExitCode == 0;
                    }
                    catch
                    {
                        return false;
                    }
                });
                
                if (result)
                    _logger.Info("System restore point created successfully");
                else
                    _logger.Warn("Could not create system restore point (System Protection may be disabled)");
                    
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to create restore point: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Backs up a registry value before modification.
        /// </summary>
        public void BackupRegistryValue(string keyPath, string valueName)
        {
            try
            {
                var cacheKey = $"{keyPath}\\{valueName}";
                if (_backupCache.ContainsKey(cacheKey))
                    return; // Already backed up
                    
                using var key = OpenRegistryKey(keyPath, false);
                if (key != null)
                {
                    var value = key.GetValue(valueName);
                    var kind = key.GetValueKind(valueName);
                    _backupCache[cacheKey] = new BackupEntry { Value = value, Kind = kind };
                    _logger.Debug($"Backed up: {cacheKey} = {value}");
                }
                else
                {
                    // Key doesn't exist - mark as null so we know to delete on revert
                    _backupCache[cacheKey] = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Could not backup {keyPath}\\{valueName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores a previously backed up registry value.
        /// </summary>
        public bool RestoreRegistryValue(string keyPath, string valueName)
        {
            try
            {
                var cacheKey = $"{keyPath}\\{valueName}";
                
                if (!_backupCache.TryGetValue(cacheKey, out var backup))
                {
                    _logger.Warn($"No backup found for: {cacheKey}");
                    return false;
                }
                
                if (backup == null)
                {
                    // Value didn't exist before - delete it
                    using var key = OpenRegistryKey(keyPath, true);
                    key?.DeleteValue(valueName, false);
                    _logger.Debug($"Deleted (restored to non-existent): {cacheKey}");
                }
                else if (backup is BackupEntry entry)
                {
                    using var key = OpenRegistryKey(keyPath, true);
                    if (key != null)
                    {
                        key.SetValue(valueName, entry.Value!, entry.Kind);
                        _logger.Debug($"Restored: {cacheKey} = {entry.Value}");
                    }
                }
                
                _backupCache.Remove(cacheKey);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to restore {keyPath}\\{valueName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets a registry value, backing up the original first.
        /// </summary>
        public bool SetRegistryValue(string keyPath, string valueName, object value, RegistryValueKind kind)
        {
            try
            {
                // Backup first
                BackupRegistryValue(keyPath, valueName);
                
                // Set new value
                using var key = OpenRegistryKey(keyPath, true) ?? CreateRegistryKey(keyPath);
                if (key != null)
                {
                    key.SetValue(valueName, value, kind);
                    _logger.Debug($"Set: {keyPath}\\{valueName} = {value}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to set {keyPath}\\{valueName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a registry value.
        /// </summary>
        public object? GetRegistryValue(string keyPath, string valueName, object? defaultValue = null)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                return key?.GetValue(valueName, defaultValue);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a registry value exists.
        /// </summary>
        public bool RegistryValueExists(string keyPath, string valueName)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return false;
                return key.GetValue(valueName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Deletes a registry value, backing up first.
        /// </summary>
        public bool DeleteRegistryValue(string keyPath, string valueName)
        {
            try
            {
                BackupRegistryValue(keyPath, valueName);
                
                using var key = OpenRegistryKey(keyPath, true);
                key?.DeleteValue(valueName, false);
                _logger.Debug($"Deleted: {keyPath}\\{valueName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to delete {keyPath}\\{valueName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Opens a registry key from a path string.
        /// Supports HKLM, HKCU, HKCR prefixes.
        /// </summary>
        private RegistryKey? OpenRegistryKey(string path, bool writable)
        {
            var parts = path.Split('\\', 2);
            if (parts.Length < 2) return null;
            
            RegistryKey? root = parts[0].ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                "HKU" or "HKEY_USERS" => Registry.Users,
                _ => null
            };
            
            return root?.OpenSubKey(parts[1], writable);
        }

        /// <summary>
        /// Creates a registry key from a path string.
        /// </summary>
        private RegistryKey? CreateRegistryKey(string path)
        {
            var parts = path.Split('\\', 2);
            if (parts.Length < 2) return null;
            
            RegistryKey? root = parts[0].ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                "HKU" or "HKEY_USERS" => Registry.Users,
                _ => null
            };
            
            return root?.CreateSubKey(parts[1], true);
        }

        /// <summary>
        /// Saves all backed up values to disk for persistence.
        /// </summary>
        public void SaveBackupToDisk()
        {
            try
            {
                var backupFile = Path.Combine(_backupPath, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(_backupCache);
                File.WriteAllText(backupFile, json);
                _logger.Info($"Backup saved to: {backupFile}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save backup: {ex.Message}");
            }
        }

        private class BackupEntry
        {
            public object? Value { get; set; }
            public RegistryValueKind Kind { get; set; }
        }
    }
}
