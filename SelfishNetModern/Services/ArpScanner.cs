using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using PacketDotNet;
using SelfishNetModern.Models;
using SharpPcap;

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

            // Ensure we are running on a background thread pool thread
            await Task.Yield();

            try
            {
                var discoveredDevices = new ConcurrentDictionary<string, NetworkDevice>(StringComparer.OrdinalIgnoreCase);

                // 1. Add Gateway immediately
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
                        if (discoveredDevices.TryAdd(gwMac.ToString(), gwDevice))
                        {
                            DeviceFound?.Invoke(gwDevice);
                        }
                    }
                }

                // 2. Add Self immediately
                var selfDevice = new NetworkDevice
                {
                    IP = adapter.IpAddress,
                    MAC = adapter.MacAddress,
                    IsSelf = true,
                    Vendor = MacVendorService.GetVendor(adapter.MacAddress),
                    Hostname = Environment.MachineName
                };
                if (discoveredDevices.TryAdd(adapter.MacAddress.ToString(), selfDevice))
                {
                    DeviceFound?.Invoke(selfDevice);
                }

                ScanProgressChanged?.Invoke(10);

                // 3. Instant Phase: Read Windows Kernel ARP Cache (< 5ms)
                // This instantly brings in active devices already on the network without any blocking!
                try
                {
                    var kernelCache = NetworkAdapterService.GetKernelArpCache();
                    foreach (var kvp in kernelCache)
                    {
                        var ip = kvp.Key;
                        var mac = kvp.Value;
                        if (ip.Equals(adapter.IpAddress) || (adapter.GatewayIp != null && ip.Equals(adapter.GatewayIp)))
                            continue;

                        string macStr = mac.ToString();
                        if (!discoveredDevices.ContainsKey(macStr))
                        {
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
                                QueueHostnameResolution(device, token);
                            }
                        }
                    }
                }
                catch { }

                ScanProgressChanged?.Invoke(30);

                // 4. Subnet Sweep Phase
                var ips = adapter.GetSubnetIps();
                if (ips.Count == 0 || token.IsCancellationRequested)
                {
                    ScanProgressChanged?.Invoke(100);
                    return;
                }

                // Fast broadcast ARP pings using NativeDevice if opened
                if (adapter.NativeDevice != null)
                {
                    try
                    {
                        var broadcastMac = PhysicalAddress.Parse("FF-FF-FF-FF-FF-FF");
                        var zeroMac = PhysicalAddress.Parse("00-00-00-00-00-00");

                        foreach (var ip in ips)
                        {
                            if (token.IsCancellationRequested) break;
                            if (ip.Equals(adapter.IpAddress)) continue;

                            var arp = new ArpPacket(
                                ArpOperation.Request,
                                zeroMac,
                                ip,
                                adapter.MacAddress,
                                adapter.IpAddress
                            );
                            var eth = new EthernetPacket(
                                adapter.MacAddress,
                                broadcastMac,
                                EthernetType.Arp
                            )
                            {
                                PayloadPacket = arp
                            };

                            try
                            {
                                adapter.NativeDevice.SendPacket(eth.Bytes);
                            }
                            catch { }

                            // Small delay between pings to prevent packet queue saturation
                            await Task.Delay(2, token);
                        }
                    }
                    catch { }
                }

                ScanProgressChanged?.Invoke(60);

                // 5. Gentle background verification with controlled low concurrency (6 workers max)
                // This prevents ThreadPool exhaustion and keeps UI 100% responsive
                int total = ips.Count;
                int processed = 0;
                using var semaphore = new SemaphoreSlim(6);

                var tasks = ips.Select(async ip =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        if (ip.Equals(adapter.IpAddress) || (adapter.GatewayIp != null && ip.Equals(adapter.GatewayIp)))
                            return;

                        // Check if already discovered
                        if (discoveredDevices.Values.Any(d => d.IP.Equals(ip)))
                            return;

                        // Non-blocking yield
                        await Task.Yield();

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
                                QueueHostnameResolution(device, token);
                            }
                        }
                    }
                    finally
                    {
                        int done = Interlocked.Increment(ref processed);
                        int progress = 60 + (int)((done / (double)total) * 40);
                        ScanProgressChanged?.Invoke(Math.Min(100, progress));
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
                ScanProgressChanged?.Invoke(100);
            }
            catch (OperationCanceledException)
            {
                // Canceled cleanly
            }
            finally
            {
                IsScanning = false;
                ScanCompleted?.Invoke();
            }
        }

        private void QueueHostnameResolution(NetworkDevice device, CancellationToken token)
        {
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

        public void StopScan()
        {
            _scanCts?.Cancel();
            IsScanning = false;
        }
    }
}
