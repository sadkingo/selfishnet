using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using SelfishNetModern.Models;

namespace SelfishNetModern.Services
{
    public class NetworkResilienceManager
    {
        private readonly ArpSpoofer _spoofer;
        private readonly TrafficController _controller;
        private AdapterInfo? _adapter;
        private CancellationTokenSource? _cts;
        private Task? _watchdogTask;
        private bool _isPausedForReconnect;

        public event Action<string, bool>? ResilienceStateChanged; // message, isHealthy
        public event Action<AdapterInfo>? AdapterRecovered;

        public NetworkResilienceManager(ArpSpoofer spoofer, TrafficController controller)
        {
            _spoofer = spoofer;
            _controller = controller;

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        public void Start(AdapterInfo adapter)
        {
            _adapter = adapter;
            _cts = new CancellationTokenSource();
            _watchdogTask = Task.Run(() => WatchdogLoopAsync(_cts.Token));
            ResilienceStateChanged?.Invoke("Network Monitor Active (Auto-Reconnect Enabled)", true);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _watchdogTask?.Wait(500); } catch { }
            ResilienceStateChanged?.Invoke("Network Monitor Stopped", true);
        }

        public void UpdateAdapter(AdapterInfo adapter)
        {
            _adapter = adapter;
        }

        private async void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            // Only trigger reconnect if our current adapter has actually lost its IP or disconnected
            if (_adapter != null)
            {
                var available = NetworkAdapterService.GetAvailableAdapters();
                bool stillActive = available.Any(a => a.Id == _adapter.Id && a.IpAddress.Equals(_adapter.IpAddress));
                if (!stillActive && !_isPausedForReconnect)
                {
                    await HandleNetworkInstabilityAsync("Local network IP or adapter changed");
                }
            }
        }

        private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                await HandleNetworkInstabilityAsync("Network link disconnected");
            }
            else
            {
                await HandleNetworkInstabilityAsync("Network link restored");
            }
        }

        private async Task WatchdogLoopAsync(CancellationToken token)
        {
            int failureCount = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(3000, token);

                    if (_adapter?.GatewayIp != null)
                    {
                        var freshMac = NetworkAdapterService.ResolveMac(_adapter.GatewayIp, _adapter.IpAddress);
                        if (freshMac == null || freshMac.Equals(PhysicalAddress.None))
                        {
                            failureCount++;
                            // Only trigger if sustained loss over multiple checks (15+ seconds) and network reports unavailable
                            if (failureCount >= 5 && !NetworkInterface.GetIsNetworkAvailable() && !_isPausedForReconnect)
                            {
                                await HandleNetworkInstabilityAsync("Gateway heartbeat lost (Router unreachable or resetting)");
                            }
                        }
                        else
                        {
                            failureCount = 0;
                            // Check if gateway MAC changed
                            if (_adapter.GatewayMac != null && !freshMac.Equals(_adapter.GatewayMac))
                            {
                                _adapter.GatewayMac = freshMac;
                                ResilienceStateChanged?.Invoke($"Gateway MAC updated to {freshMac}", true);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Ignore watchdog iteration error
                }
            }
        }

        private async Task HandleNetworkInstabilityAsync(string reason)
        {
            if (_isPausedForReconnect) return;
            _isPausedForReconnect = true;

            ResilienceStateChanged?.Invoke($"⚠️ {reason}. Re-connecting automatically...", false);

            bool wasSpoofing = _spoofer.IsRunning;
            bool wasCapturing = _controller.IsCapturing;

            // Pause engines safely
            if (wasSpoofing) _spoofer.Stop();
            if (wasCapturing) _controller.Stop();

            // Retry loop to re-acquire connection
            for (int attempt = 1; attempt <= 15; attempt++)
            {
                await Task.Delay(2000);
                try
                {
                    var newAdapter = NetworkAdapterService.GetDefaultAdapter();
                    if (newAdapter != null && newAdapter.GatewayIp != null)
                    {
                        var gwMac = NetworkAdapterService.ResolveMac(newAdapter.GatewayIp, newAdapter.IpAddress);
                        if (gwMac != null && !gwMac.Equals(PhysicalAddress.None))
                        {
                            newAdapter.GatewayMac = gwMac;
                            _adapter = newAdapter;

                            _spoofer.SetAdapter(newAdapter);
                            _controller.SetAdapter(newAdapter);

                            // Restart capture and spoofer if they were running
                            if (wasCapturing) _controller.Start();
                            if (wasSpoofing) _spoofer.Start();

                            _isPausedForReconnect = false;
                            AdapterRecovered?.Invoke(newAdapter);
                            ResilienceStateChanged?.Invoke("✅ Network re-connected and stabilized! Control restored.", true);
                            return;
                        }
                    }
                }
                catch
                {
                    // Wait next retry
                }

                ResilienceStateChanged?.Invoke($"Re-connecting attempt ({attempt}/15)...", false);
            }

            _isPausedForReconnect = false;
            ResilienceStateChanged?.Invoke("❌ Failed to reconnect after 15 attempts. Please check physical cable or Wi-Fi.", false);
        }
    }
}
