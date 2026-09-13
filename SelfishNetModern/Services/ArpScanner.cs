using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using SelfishNetModern.Models;

namespace SelfishNetModern.Services
{
    public class ArpScanner
    {
        public event Action<NetworkDevice>? DeviceFound;
        public event Action<int>? ScanProgressChanged; // 0-100%
        public event Action? ScanCompleted;

        private CancellationTokenSource? _scanCts;
        public bool IsScanning { get; private set; }

        public async Task StartScanAsync(AdapterInfo adapter)
        {
            if (IsScanning) return;

            IsScanning = true;
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            try
            {
                var ips = adapter.GetSubnetIps();
                if (ips.Count == 0)
                {
                    ScanCompleted?.Invoke();
                    IsScanning = false;
                    return;
                }

                // Make sure Gateway is always listed first if known
                if (adapter.GatewayIp != null)
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
                        DeviceFound?.Invoke(gwDevice);
                    }
                }

                // Add Self
                var selfDevice = new NetworkDevice
                {
                    IP = adapter.IpAddress,
                    MAC = adapter.MacAddress,
                    IsSelf = true,
                    Vendor = MacVendorService.GetVendor(adapter.MacAddress),
                    Hostname = Environment.MachineName
                };
                DeviceFound?.Invoke(selfDevice);

                int total = ips.Count;
                int processed = 0;
                var discoveredDevices = new ConcurrentDictionary<string, NetworkDevice>(StringComparer.OrdinalIgnoreCase);

                using var semaphore = new SemaphoreSlim(32); // 32 concurrent ARP probes

                var tasks = ips.Select(async ip =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        // Skip self and gateway if already handled
                        if (ip.Equals(adapter.IpAddress) || (adapter.GatewayIp != null && ip.Equals(adapter.GatewayIp)))
                            return;

                        var mac = NetworkAdapterService.ResolveMac(ip, adapter.IpAddress);
                        if (mac != null && !mac.Equals(PhysicalAddress.None))
                        {
                            string macStr = mac.ToString();
                            var device = new NetworkDevice
                            {
                                IP = ip,
                                MAC = mac,
                                Vendor = MacVendorService.GetVendor(mac),
                                Hostname = "Resolving..."
                            };

                            if (discoveredDevices.TryAdd(macStr, device))
                            {
                                DeviceFound?.Invoke(device);

                                // Asynchronously resolve hostname
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        string name = await DeviceNameResolver.ResolveAsync(device.IP, token);
                                        if (!string.IsNullOrWhiteSpace(name) && name != "Unknown")
                                        {
                                            device.Hostname = name;
                                        }
                                        else
                                        {
                                            device.Hostname = device.Vendor != "Unknown" ? $"{device.Vendor} Device" : "Host";
                                        }
                                    }
                                    catch
                                    {
                                        device.Hostname = "Host";
                                    }
                                }, token);
                            }
                        }
                    }
                    finally
                    {
                        int done = Interlocked.Increment(ref processed);
                        ScanProgressChanged?.Invoke((int)((done / (double)total) * 100));
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Scan canceled
            }
            finally
            {
                IsScanning = false;
                ScanCompleted?.Invoke();
            }
        }

        public void StopScan()
        {
            _scanCts?.Cancel();
            IsScanning = false;
        }
    }
}
