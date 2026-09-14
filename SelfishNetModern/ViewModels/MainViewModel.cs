using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SelfishNetModern.Models;
using SelfishNetModern.Services;
using SelfishNetModern.Views;

namespace SelfishNetModern.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ArpScanner _scanner;
        private readonly ArpSpoofer _spoofer;
        private readonly TrafficController _controller;
        private readonly NetworkResilienceManager _resilience;
        private readonly DeviceSettingsService _settingsService;
        private readonly Dispatcher _dispatcher;

        private AdapterInfo? _selectedAdapter;
        private bool _isScanning;
        private int _scanProgress;
        private bool _isRedirecting;
        private string _statusMessage = "Ready";
        private bool _isHealthy = true;
        private double _totalDownloadKbps;
        private double _totalUploadKbps;
        private string _gatewayDisplay = "None";
        private string _myIpDisplay = "None";

        public ObservableCollection<AdapterInfo> Adapters { get; } = new();
        public ObservableCollection<NetworkDevice> Devices { get; } = new();
        public ObservableCollection<string> EventLogs { get; } = new();

        public AdapterInfo? SelectedAdapter
        {
            get => _selectedAdapter;
            set
            {
                if (_selectedAdapter != value)
                {
                    _selectedAdapter = value;
                    OnPropertyChanged();
                    OnAdapterChanged();
                }
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); }
        }

        public int ScanProgress
        {
            get => _scanProgress;
            set { _scanProgress = value; OnPropertyChanged(); }
        }

        public bool IsRedirecting
        {
            get => _isRedirecting;
            set
            {
                if (_isRedirecting != value)
                {
                    _isRedirecting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RedirectButtonText));
                    OnPropertyChanged(nameof(RedirectButtonColor));
                }
            }
        }

        public string RedirectButtonText => IsRedirecting ? "🛑 Stop Redirecting" : "⚡ Start Redirecting";
        public string RedirectButtonColor => IsRedirecting ? "#F38BA8" : "#A6E3A1";

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsHealthy
        {
            get => _isHealthy;
            set { _isHealthy = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthColor)); }
        }

        public string HealthColor => IsHealthy ? "#A6E3A1" : "#F38BA8";

        public double TotalDownloadKbps
        {
            get => _totalDownloadKbps;
            set { _totalDownloadKbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalDownloadDisplay)); }
        }

        public double TotalUploadKbps
        {
            get => _totalUploadKbps;
            set { _totalUploadKbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalUploadDisplay)); }
        }

        public string TotalDownloadDisplay => TotalDownloadKbps > 1024 
            ? $"{(TotalDownloadKbps / 1024):F2} MB/s" 
            : $"{TotalDownloadKbps:F1} KB/s";

        public string TotalUploadDisplay => TotalUploadKbps > 1024 
            ? $"{(TotalUploadKbps / 1024):F2} MB/s" 
            : $"{TotalUploadKbps:F1} KB/s";

        public string GatewayDisplay
        {
            get => _gatewayDisplay;
            set { _gatewayDisplay = value; OnPropertyChanged(); }
        }

        public string MyIpDisplay
        {
            get => _myIpDisplay;
            set { _myIpDisplay = value; OnPropertyChanged(); }
        }

        public int ControlledDeviceCount => Devices.Count(d => d.IsControlled && !d.IsGateway && !d.IsSelf);
        public int BlockedDeviceCount => Devices.Count(d => d.IsBlocked && !d.IsGateway && !d.IsSelf);
        public int TotalDeviceCount => Devices.Count;

        // Commands
        public ICommand ScanCommand { get; }
        public ICommand ToggleRedirectCommand { get; }
        public ICommand BlockAllCommand { get; }
        public ICommand UnblockAllCommand { get; }
        public ICommand ResetAllLimitsCommand { get; }
        public ICommand ToggleBlockDeviceCommand { get; }
        public ICommand ToggleControlDeviceCommand { get; }
        public ICommand RefreshAdaptersCommand { get; }
        public ICommand ShowTermsCommand { get; }

        public MainViewModel()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            _scanner = new ArpScanner();
            _spoofer = new ArpSpoofer();
            _controller = new TrafficController();
            _settingsService = new DeviceSettingsService();
            _resilience = new NetworkResilienceManager(_spoofer, _controller);

            // Wire scanner events
            _scanner.DeviceFound += OnDeviceFound;
            _scanner.ScanProgressChanged += p => _dispatcher.BeginInvoke(DispatcherPriority.Background, () => ScanProgress = p);
            _scanner.ScanCompleted += () => _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                IsScanning = false;
                StatusMessage = IsRedirecting 
                    ? $"Traffic control ACTIVE ({ControlledDeviceCount} device(s) redirected)."
                    : $"Scan completed. Found {Devices.Count} active device(s).";
                AddLog($"Scan finished. {Devices.Count} devices detected on subnet.");
                UpdateDeviceCounts();
            });

            // Wire spoofer & traffic controller events
            _spoofer.LogMessage += msg => AddLog($"[ARP] {msg}");
            _controller.LogMessage += msg => AddLog($"[Traffic] {msg}");
            _controller.TotalSpeedUpdated += (dl, ul) =>
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    TotalDownloadKbps = dl;
                    TotalUploadKbps = ul;
                });
            };

            // Wire resilience events
            _resilience.ResilienceStateChanged += (msg, healthy) =>
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    StatusMessage = msg;
                    IsHealthy = healthy;
                    AddLog($"[Resilience] {msg}");
                });
            };

            _resilience.AdapterRecovered += recoveredAdapter =>
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    SelectedAdapter = recoveredAdapter;
                    AddLog($"[Resilience] Adapter recovered: {recoveredAdapter.IpAddress}");
                });
            };

            // Commands
            ScanCommand = new RelayCommand(ExecuteScan, () => !IsScanning && SelectedAdapter != null);
            ToggleRedirectCommand = new RelayCommand(ExecuteToggleRedirect, () => SelectedAdapter != null);
            BlockAllCommand = new RelayCommand(ExecuteBlockAll);
            UnblockAllCommand = new RelayCommand(ExecuteUnblockAll);
            ResetAllLimitsCommand = new RelayCommand(ExecuteResetAllLimits);
            ToggleBlockDeviceCommand = new RelayCommand(p => ExecuteToggleBlock(p as NetworkDevice));
            ToggleControlDeviceCommand = new RelayCommand(p => ExecuteToggleControl(p as NetworkDevice));
            RefreshAdaptersCommand = new RelayCommand(LoadAdapters);
            ShowTermsCommand = new RelayCommand(ExecuteShowTerms);

            // Initialize adapters and auto-start redirect by default (without automatic subnet scan)
            LoadAdapters();
            if (SelectedAdapter != null)
            {
                StartRedirecting();
            }
        }

        public void LoadAdapters()
        {
            Adapters.Clear();
            var list = NetworkAdapterService.GetAvailableAdapters();
            foreach (var a in list)
            {
                Adapters.Add(a);
            }

            var def = NetworkAdapterService.GetDefaultAdapter();
            SelectedAdapter = def ?? Adapters.FirstOrDefault();
            AddLog($"Detected {Adapters.Count} active network adapter(s).");
        }

        private void OnAdapterChanged()
        {
            if (SelectedAdapter != null)
            {
                MyIpDisplay = $"{SelectedAdapter.IpAddress} ({SelectedAdapter.MacAddress})";
                GatewayDisplay = SelectedAdapter.GatewayIp != null 
                    ? $"{SelectedAdapter.GatewayIp} ({SelectedAdapter.GatewayMac})" 
                    : "No Gateway Detected";

                _spoofer.SetAdapter(SelectedAdapter);
                _controller.SetAdapter(SelectedAdapter);
                _resilience.Start(SelectedAdapter);

                EnsureDefaultDevices(SelectedAdapter);

                AddLog($"Selected interface: {SelectedAdapter.Name} ({SelectedAdapter.IpAddress})");
            }
        }

        private void EnsureDefaultDevices(AdapterInfo adapter)
        {
            // 1. Default Gateway (Router)
            if (adapter.GatewayIp != null)
            {
                var existingGw = Devices.FirstOrDefault(d => d.IsGateway || d.IP.Equals(adapter.GatewayIp));
                if (existingGw == null)
                {
                    var gwMac = adapter.GatewayMac ?? NetworkAdapterService.ResolveMac(adapter.GatewayIp, adapter.IpAddress);
                    if (gwMac != null)
                    {
                        var gwDevice = new NetworkDevice
                        {
                            IP = adapter.GatewayIp,
                            MAC = gwMac,
                            IsGateway = true,
                            Vendor = MacVendorService.GetVendor(gwMac),
                            Hostname = "Default Gateway (Router)"
                        };
                        Devices.Insert(0, gwDevice);
                    }
                }
            }

            // 2. Your Computer (Host)
            var existingSelf = Devices.FirstOrDefault(d => d.IsSelf || d.IP.Equals(adapter.IpAddress));
            if (existingSelf == null)
            {
                var selfDevice = new NetworkDevice
                {
                    IP = adapter.IpAddress,
                    MAC = adapter.MacAddress,
                    IsSelf = true,
                    Vendor = MacVendorService.GetVendor(adapter.MacAddress),
                    Hostname = Environment.MachineName
                };
                int insertIdx = Devices.Any(d => d.IsGateway) ? 1 : 0;
                Devices.Insert(insertIdx, selfDevice);
                _controller.SelfDevice = selfDevice;
            }
            else
            {
                _controller.SelfDevice = existingSelf;
            }

            UpdateDeviceCounts();
        }

        private void ExecuteScan()
        {
            if (SelectedAdapter == null || IsScanning) return;

            // Only clear devices if we are not actively redirecting, preserving live traffic control & table state
            if (!IsRedirecting)
            {
                Devices.Clear();
                EnsureDefaultDevices(SelectedAdapter);
            }

            IsScanning = true;
            ScanProgress = 0;
            StatusMessage = "Scanning local subnet for active devices...";
            AddLog("Started ARP subnet discovery...");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _scanner.StartScanAsync(SelectedAdapter);
                }
                catch (Exception ex)
                {
                    _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        StatusMessage = $"Scan error: {ex.Message}";
                        AddLog($"Scan error: {ex.Message}");
                        IsScanning = false;
                    });
                }
            });
        }

        private void OnDeviceFound(NetworkDevice device)
        {
            if (device == null) return;
            if (!device.IsGateway && !device.IsSelf && !NetworkAdapterService.IsValidUnicastHost(device.IP, device.MAC, SelectedAdapter))
                return;

            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                var existing = Devices.FirstOrDefault(d => d.MacString.Equals(device.MacString, StringComparison.OrdinalIgnoreCase) ||
                                                           d.IP.Equals(device.IP));
                if (existing != null)
                {
                    if (string.IsNullOrEmpty(existing.Hostname) && !string.IsNullOrEmpty(device.Hostname))
                        existing.Hostname = device.Hostname;
                    if (string.IsNullOrEmpty(existing.Vendor) && !string.IsNullOrEmpty(device.Vendor))
                        existing.Vendor = device.Vendor;
                    existing.LastSeen = DateTime.Now;

                    if (existing.IsSelf)
                    {
                        _controller.SelfDevice = existing;
                    }

                    // If redirecting is currently active, ensure this device is controlled and in the spoofer & controller
                    if (IsRedirecting && !existing.IsGateway && !existing.IsSelf)
                    {
                        existing.IsControlled = true;
                        _spoofer.AddControlledDevice(existing);
                        _controller.RegisterDevice(existing);
                    }
                    return;
                }

                // Apply any remembered profile/settings for this MAC address
                if (_settingsService.TryGetProfile(device.MacString, out var savedProfile) && savedProfile != null)
                {
                    _settingsService.ApplyToDevice(device);
                    AddLog($"Applied remembered settings for [{device.MacString}] ({device.IP}): DL={device.DownloadLimitDisplay}, UL={device.UploadLimitDisplay}, Blocked={device.IsBlocked}");
                }

                // When scanning, ensure all devices are set to controlled by default
                if (!device.IsGateway && !device.IsSelf)
                {
                    device.IsControlled = true;
                }

                device.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NetworkDevice.IsControlled))
                    {
                        if (IsRedirecting && !device.IsGateway && !device.IsSelf)
                        {
                            if (device.IsControlled)
                            {
                                _spoofer.AddControlledDevice(device);
                                _controller.RegisterDevice(device);
                                AddLog($"Started redirecting: {device.IP} ({device.MacString})");
                            }
                            else
                            {
                                _spoofer.RemoveControlledDevice(device);
                                _controller.UnregisterDevice(device);
                                AddLog($"Stopped redirecting: {device.IP} ({device.MacString})");
                            }
                        }
                        _settingsService.SaveDevice(device);
                        UpdateDeviceCounts();
                    }
                    else if (e.PropertyName == nameof(NetworkDevice.IsBlocked))
                    {
                        if (IsRedirecting && !device.IsGateway && !device.IsSelf)
                        {
                            if (device.IsBlocked)
                            {
                                if (!device.IsControlled)
                                {
                                    device.IsControlled = true; // Will trigger IsControlled handler above
                                }
                                else
                                {
                                    _spoofer.AddControlledDevice(device);
                                    _controller.RegisterDevice(device);
                                }
                            }
                        }
                        _settingsService.SaveDevice(device);
                        UpdateDeviceCounts();
                    }
                    else if (e.PropertyName == nameof(NetworkDevice.DownloadLimitKbps) ||
                             e.PropertyName == nameof(NetworkDevice.UploadLimitKbps) ||
                             e.PropertyName == nameof(NetworkDevice.CustomName))
                    {
                        _settingsService.SaveDevice(device);
                    }
                };

                Devices.Add(device);

                if (device.IsSelf)
                {
                    _controller.SelfDevice = device;
                }

                // If redirection is currently active, immediately auto-redirect this new device!
                if (IsRedirecting && device.IsControlled && !device.IsGateway && !device.IsSelf)
                {
                    _spoofer.AddControlledDevice(device);
                    _controller.RegisterDevice(device);
                    AddLog($"Auto-redirected newly discovered device: {device.IP} ({device.MacString})");
                }

                UpdateDeviceCounts();
            });
        }

        private void StartRedirecting()
        {
            if (SelectedAdapter == null) return;

            // Ensure adapters are bound
            _spoofer.SetAdapter(SelectedAdapter);
            _controller.SetAdapter(SelectedAdapter);

            // Auto-control all non-gateway, non-self devices
            foreach (var dev in Devices.Where(d => !d.IsGateway && !d.IsSelf))
            {
                dev.IsControlled = true;
            }

            // Start engines and mark active
            _spoofer.Start();
            _controller.Start();
            IsRedirecting = true;

            // Register all currently controlled devices
            int controlledCount = 0;
            foreach (var dev in Devices.Where(d => d.IsControlled && !d.IsGateway && !d.IsSelf))
            {
                _spoofer.AddControlledDevice(dev);
                _controller.RegisterDevice(dev);
                controlledCount++;
            }

            // Link Host PC (Self) device to controller
            var selfDevice = Devices.FirstOrDefault(d => d.IsSelf);
            if (selfDevice != null)
            {
                _controller.SelfDevice = selfDevice;
            }

            StatusMessage = controlledCount > 0 
                ? $"Traffic control ACTIVE ({controlledCount} device(s) redirected)."
                : "Ready. Click 'Scan Network' to discover local devices.";
            AddLog(controlledCount > 0 
                ? $"Traffic redirection activated for {controlledCount} device(s)."
                : "Traffic redirection engine ready (will auto-redirect scanned devices).");
            UpdateDeviceCounts();
        }

        private void ExecuteToggleRedirect()
        {
            if (SelectedAdapter == null) return;

            if (IsRedirecting)
            {
                // Stop
                _spoofer.Stop();
                _controller.Stop();
                IsRedirecting = false;
                StatusMessage = "Traffic control stopped. All caches restored.";
                AddLog("Traffic redirection deactivated.");
                UpdateDeviceCounts();
            }
            else
            {
                StartRedirecting();
            }
        }

        private void ExecuteToggleControl(NetworkDevice? device)
        {
            if (device == null || device.IsGateway || device.IsSelf) return;

            device.IsControlled = !device.IsControlled;
            if (!device.IsControlled)
            {
                device.IsBlocked = false;
            }

            if (IsRedirecting)
            {
                if (device.IsControlled)
                {
                    _spoofer.AddControlledDevice(device);
                    _controller.RegisterDevice(device);
                }
                else
                {
                    _spoofer.RemoveControlledDevice(device);
                    _controller.UnregisterDevice(device);
                }
            }
            UpdateDeviceCounts();
        }

        private void ExecuteToggleBlock(NetworkDevice? device)
        {
            if (device == null || device.IsGateway || device.IsSelf) return;

            device.IsBlocked = !device.IsBlocked;
            if (device.IsBlocked && !device.IsControlled)
            {
                device.IsControlled = true;
            }

            if (IsRedirecting)
            {
                _spoofer.AddControlledDevice(device);
                _controller.RegisterDevice(device);
            }
            UpdateDeviceCounts();
        }

        private void ExecuteBlockAll()
        {
            foreach (var dev in Devices.Where(d => !d.IsGateway && !d.IsSelf))
            {
                dev.IsControlled = true;
                dev.IsBlocked = true;
                if (IsRedirecting)
                {
                    _spoofer.AddControlledDevice(dev);
                    _controller.RegisterDevice(dev);
                }
            }
            UpdateDeviceCounts();
            AddLog("Blocked all network devices except Gateway and Host.");
        }

        private void ExecuteUnblockAll()
        {
            foreach (var dev in Devices.Where(d => !d.IsGateway && !d.IsSelf))
            {
                dev.IsBlocked = false;
            }
            UpdateDeviceCounts();
            AddLog("Unblocked all devices.");
        }

        private void ExecuteResetAllLimits()
        {
            foreach (var dev in Devices.Where(d => !d.IsGateway && !d.IsSelf))
            {
                dev.DownloadLimitKbps = 0;
                dev.UploadLimitKbps = 0;
                dev.IsBlocked = false;
                dev.IsControlled = false;

                if (IsRedirecting)
                {
                    _spoofer.RemoveControlledDevice(dev);
                    _controller.UnregisterDevice(dev);
                }

                _settingsService.SaveDevice(dev);
            }
            UpdateDeviceCounts();
            AddLog("Reset all limits and released control for all devices.");
        }

        private void ExecuteShowTerms()
        {
            var dialog = new TermsOfUseDialog
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        private void UpdateDeviceCounts()
        {
            OnPropertyChanged(nameof(ControlledDeviceCount));
            OnPropertyChanged(nameof(BlockedDeviceCount));
            OnPropertyChanged(nameof(TotalDeviceCount));
            if (IsRedirecting)
            {
                StatusMessage = $"Traffic control ACTIVE ({ControlledDeviceCount} device(s) redirected).";
            }
        }

        public void AddLog(string message)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                EventLogs.Insert(0, $"[{timestamp}] {message}");
                if (EventLogs.Count > 150)
                {
                    EventLogs.RemoveAt(EventLogs.Count - 1);
                }
            });
        }

        public void OnClosing()
        {
            _resilience.Stop();
            _spoofer.Stop();
            _controller.Stop();
            _scanner.StopScan();
            _settingsService.Flush();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
