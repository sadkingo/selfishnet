using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using SelfishNetModern.Models;

namespace SelfishNetModern.Services
{
    public class DeviceProfile
    {
        public string MacAddress { get; set; } = string.Empty;
        public string CustomName { get; set; } = string.Empty;
        public int DownloadLimitKbps { get; set; }
        public int UploadLimitKbps { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsControlled { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class DeviceSettingsService
    {
        public static string SettingsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SelfishNetModern"
        );

        public static string SettingsFilePath => Path.Combine(SettingsDirectory, "device_profiles.json");

        private readonly ConcurrentDictionary<string, DeviceProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _fileLock = new();
        private Timer? _debounceSaveTimer;
        private readonly string _customFilePath;

        public DeviceSettingsService(string? customFilePath = null)
        {
            _customFilePath = customFilePath ?? SettingsFilePath;
            LoadProfiles();
        }

        public void LoadProfiles()
        {
            try
            {
                if (File.Exists(_customFilePath))
                {
                    string json = File.ReadAllText(_customFilePath);
                    var list = JsonSerializer.Deserialize<List<DeviceProfile>>(json);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrWhiteSpace(item.MacAddress))
                            {
                                _profiles[item.MacAddress] = item;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public bool TryGetProfile(string macAddress, out DeviceProfile? profile)
        {
            return _profiles.TryGetValue(macAddress, out profile);
        }

        public void ApplyToDevice(NetworkDevice device)
        {
            if (device.IsGateway || device.IsSelf) return;

            if (_profiles.TryGetValue(device.MacString, out var profile) && profile != null)
            {
                device.DownloadLimitKbps = profile.DownloadLimitKbps;
                device.UploadLimitKbps = profile.UploadLimitKbps;
                device.IsBlocked = profile.IsBlocked;
                device.IsControlled = profile.IsControlled;
                if (!string.IsNullOrWhiteSpace(profile.CustomName))
                {
                    device.CustomName = profile.CustomName;
                }
            }
        }

        public void SaveDevice(NetworkDevice device)
        {
            if (device.IsGateway || device.IsSelf) return;

            var profile = new DeviceProfile
            {
                MacAddress = device.MacString,
                CustomName = device.CustomName,
                DownloadLimitKbps = device.DownloadLimitKbps,
                UploadLimitKbps = device.UploadLimitKbps,
                IsBlocked = device.IsBlocked,
                IsControlled = device.IsControlled,
                LastUpdated = DateTime.Now
            };

            _profiles[device.MacString] = profile;
            ScheduleSave();
        }

        public void Flush()
        {
            lock (_fileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_customFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var list = new List<DeviceProfile>(_profiles.Values);
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(list, options);
                    File.WriteAllText(_customFilePath, json);
                }
                catch { }
            }
        }

        private void ScheduleSave()
        {
            lock (_fileLock)
            {
                _debounceSaveTimer?.Dispose();
                _debounceSaveTimer = new Timer(_ => Flush(), null, 500, Timeout.Infinite);
            }
        }
    }
}
