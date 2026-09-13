using System;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace SelfishNetModern.Models
{
    public class NetworkDevice : INotifyPropertyChanged
    {
        private string _hostname = "Unknown";
        private string _vendor = "Unknown";
        private bool _isControlled;
        private bool _isBlocked;
        private int _downloadLimitKbps; // 0 = Unlimited
        private int _uploadLimitKbps;   // 0 = Unlimited
        private double _currentDownloadKbps;
        private double _currentUploadKbps;
        private DateTime _lastSeen = DateTime.Now;
        private bool _isOnline = true;

        public IPAddress IP { get; set; } = IPAddress.None;
        public PhysicalAddress MAC { get; set; } = PhysicalAddress.None;
        public string MacString => string.Join(":", MAC.GetAddressBytes().Select(b => b.ToString("X2")));

        public bool IsGateway { get; set; }
        public bool IsSelf { get; set; }

        public string Hostname
        {
            get => _hostname;
            set { if (_hostname != value) { _hostname = value; OnPropertyChanged(); } }
        }

        public string Vendor
        {
            get => _vendor;
            set { if (_vendor != value) { _vendor = value; OnPropertyChanged(); } }
        }

        public bool IsControlled
        {
            get => _isControlled;
            set
            {
                if (_isControlled != value)
                {
                    _isControlled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDescription));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public bool IsBlocked
        {
            get => _isBlocked;
            set
            {
                if (_isBlocked != value)
                {
                    _isBlocked = value;
                    if (_isBlocked && !_isControlled)
                    {
                        _isControlled = true;
                        OnPropertyChanged(nameof(IsControlled));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDescription));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public int DownloadLimitKbps
        {
            get => _downloadLimitKbps;
            set
            {
                if (_downloadLimitKbps != value)
                {
                    _downloadLimitKbps = Math.Max(0, value);
                    if (_downloadLimitKbps > 0 && !_isControlled)
                    {
                        _isControlled = true;
                        OnPropertyChanged(nameof(IsControlled));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DownloadLimitDisplay));
                    OnPropertyChanged(nameof(StatusDescription));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public int UploadLimitKbps
        {
            get => _uploadLimitKbps;
            set
            {
                if (_uploadLimitKbps != value)
                {
                    _uploadLimitKbps = Math.Max(0, value);
                    if (_uploadLimitKbps > 0 && !_isControlled)
                    {
                        _isControlled = true;
                        OnPropertyChanged(nameof(IsControlled));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UploadLimitDisplay));
                    OnPropertyChanged(nameof(StatusDescription));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string DownloadLimitDisplay => DownloadLimitKbps == 0 ? "Unlimited" : $"{DownloadLimitKbps} KB/s";
        public string UploadLimitDisplay => UploadLimitKbps == 0 ? "Unlimited" : $"{UploadLimitKbps} KB/s";

        public double CurrentDownloadKbps
        {
            get => _currentDownloadKbps;
            set { if (Math.Abs(_currentDownloadKbps - value) > 0.1) { _currentDownloadKbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadSpeedDisplay)); } }
        }

        public double CurrentUploadKbps
        {
            get => _currentUploadKbps;
            set { if (Math.Abs(_currentUploadKbps - value) > 0.1) { _currentUploadKbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(UploadSpeedDisplay)); } }
        }

        public string DownloadSpeedDisplay => $"{_currentDownloadKbps:F1} KB/s";
        public string UploadSpeedDisplay => $"{_currentUploadKbps:F1} KB/s";

        public DateTime LastSeen
        {
            get => _lastSeen;
            set { _lastSeen = value; OnPropertyChanged(); }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { if (_isOnline != value) { _isOnline = value; OnPropertyChanged(); } }
        }

        public string DeviceTypeIcon
        {
            get
            {
                if (IsGateway) return "🌐";
                if (IsSelf) return "💻";
                var vendorLower = Vendor.ToLowerInvariant();
                if (vendorLower.Contains("apple") || vendorLower.Contains("samsung") || vendorLower.Contains("xiaomi") || vendorLower.Contains("huawei") || vendorLower.Contains("oneplus") || vendorLower.Contains("oppo") || vendorLower.Contains("vivo"))
                    return "📱";
                if (vendorLower.Contains("tv") || vendorLower.Contains("roku") || vendorLower.Contains("lg electron") || vendorLower.Contains("sony"))
                    return "📺";
                if (vendorLower.Contains("espressif") || vendorLower.Contains("tuya") || vendorLower.Contains("raspberry") || vendorLower.Contains("arduino") || vendorLower.Contains("tasmota"))
                    return "🔌";
                return "💻";
            }
        }

        public string StatusDescription
        {
            get
            {
                if (IsGateway) return "Gateway (Router)";
                if (IsSelf) return "Your Computer";
                if (IsBlocked) return "BLOCKED";
                if (IsControlled && (DownloadLimitKbps > 0 || UploadLimitKbps > 0)) return "THROTTLED";
                if (IsControlled) return "REDIRECTED";
                return "NORMAL";
            }
        }

        public string StatusColor
        {
            get
            {
                if (IsGateway) return "#89B4FA"; // Blue
                if (IsSelf) return "#A6E3A1";    // Green
                if (IsBlocked) return "#F38BA8"; // Red
                if (IsControlled && (DownloadLimitKbps > 0 || UploadLimitKbps > 0)) return "#FAB387"; // Orange
                if (IsControlled) return "#CBA6F7"; // Purple
                return "#A6ADC8"; // Muted Gray
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
