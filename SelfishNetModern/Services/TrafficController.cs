using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using PacketDotNet;
using PacketDotNet.Utils;
using SelfishNetModern.Models;

namespace SelfishNetModern.Services
{
    public class TrafficController
    {
        private AdapterInfo? _adapter;
        private readonly ConcurrentDictionary<string, NetworkDevice> _controlledDevices = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (TokenBucketRateLimiter dl, TokenBucketRateLimiter ul)> _limiters = new(StringComparer.OrdinalIgnoreCase);
        
        // Speed measurement counters
        private readonly ConcurrentDictionary<string, (long dlBytes, long ulBytes)> _intervalBytes = new(StringComparer.OrdinalIgnoreCase);
        private Timer? _speedMeterTimer;
        private long _lastSpeedMeterTick;

        public bool IsCapturing { get; private set; }
        public double TotalDownloadKbps { get; private set; }
        public double TotalUploadKbps { get; private set; }

        public event Action<string>? LogMessage;
        public event Action<double, double>? TotalSpeedUpdated;

        public void SetAdapter(AdapterInfo adapter)
        {
            _adapter = adapter;
        }

        public void RegisterDevice(NetworkDevice device)
        {
            if (device.IsGateway || device.IsSelf) return;
            if (!NetworkAdapterService.IsValidUnicastHost(device.IP, device.MAC, _adapter)) return;

            _controlledDevices[device.IP.ToString()] = device;
            _controlledDevices[device.MacString] = device;

            var dlLimiter = new TokenBucketRateLimiter(device.DownloadLimitKbps * 1024L);
            var ulLimiter = new TokenBucketRateLimiter(device.UploadLimitKbps * 1024L);
            _limiters[device.MacString] = (dlLimiter, ulLimiter);

            // Subscribe to property changes to update limiters dynamically
            device.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NetworkDevice.DownloadLimitKbps))
                {
                    dlLimiter.UpdateRate(device.DownloadLimitKbps * 1024L);
                }
                else if (e.PropertyName == nameof(NetworkDevice.UploadLimitKbps))
                {
                    ulLimiter.UpdateRate(device.UploadLimitKbps * 1024L);
                }
            };
        }

        public void UnregisterDevice(NetworkDevice device)
        {
            _controlledDevices.TryRemove(device.IP.ToString(), out _);
            _controlledDevices.TryRemove(device.MacString, out _);
            _limiters.TryRemove(device.MacString, out _);
            _intervalBytes.TryRemove(device.MacString, out _);
            device.CurrentDownloadKbps = 0;
            device.CurrentUploadKbps = 0;
        }

        public void Start()
        {
            if (IsCapturing) return;
            if (_adapter?.NativeDevice == null || !_adapter.NativeDevice.IsOpen)
            {
                LogMessage?.Invoke("Traffic controller cannot start: No active pcap device.");
                return;
            }

            try
            {
                var dev = _adapter.NativeDevice;
                dev.OnPacketArrival += OnPacketArrival;
                dev.StartCapture();

                _lastSpeedMeterTick = Stopwatch.GetTimestamp();
                _speedMeterTimer = new Timer(OnSpeedMeterTick, null, 1000, 1000);

                IsCapturing = true;
                LogMessage?.Invoke("Packet interception & bandwidth shaping engine active.");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to start packet capture: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!IsCapturing) return;

            try
            {
                _speedMeterTimer?.Dispose();
                _speedMeterTimer = null;

                if (_adapter?.NativeDevice != null)
                {
                    _adapter.NativeDevice.OnPacketArrival -= OnPacketArrival;
                    _adapter.NativeDevice.StopCapture();
                }

                TotalDownloadKbps = 0;
                TotalUploadKbps = 0;
                TotalSpeedUpdated?.Invoke(0, 0);

                foreach (var dev in _controlledDevices.Values)
                {
                    dev.CurrentDownloadKbps = 0;
                    dev.CurrentUploadKbps = 0;
                }

                IsCapturing = false;
                LogMessage?.Invoke("Traffic controller stopped.");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error stopping traffic controller: {ex.Message}");
            }
        }

        private void OnPacketArrival(PcapHeader header, byte[] rawBytes)
        {
            if (_adapter == null || _adapter.GatewayMac == null) return;
            if (rawBytes.Length < 14) return;

            try
            {
                var ethernetPacket = new EthernetPacket(new ByteArraySegment(rawBytes));
                var srcMac = ethernetPacket.SourceHardwareAddress;
                var dstMac = ethernetPacket.DestinationHardwareAddress;

                // Only inspect frames destined for our MAC (promiscuous MITM capture)
                if (!dstMac.Equals(_adapter.MacAddress)) return;

                // Ignore multicast / broadcast frames
                byte[] dstMacBytes = dstMac.GetAddressBytes();
                byte[] srcMacBytes = srcMac.GetAddressBytes();
                if (dstMacBytes.Length > 0 && (dstMacBytes[0] & 0x01) != 0) return;
                if (srcMacBytes.Length > 0 && (srcMacBytes[0] & 0x01) != 0) return;

                var ipPacket = ethernetPacket.PayloadPacket as IPv4Packet;
                if (ipPacket == null) return;

                var srcIp = ipPacket.SourceAddress;
                var dstIp = ipPacket.DestinationAddress;

                // Ignore multicast (224.0.0.0/4), broadcast, or reserved/loopback IPs
                byte[] dstIpBytes = dstIp.GetAddressBytes();
                byte[] srcIpBytes = srcIp.GetAddressBytes();
                if (dstIpBytes.Length == 4 && (dstIpBytes[0] >= 224 || dstIpBytes[0] == 0 || dstIpBytes[0] == 127))
                    return;
                if (srcIpBytes.Length == 4 && (srcIpBytes[0] >= 224 || srcIpBytes[0] == 0 || srcIpBytes[0] == 127))
                    return;

                // Ignore packets genuinely destined for or originating from our own PC IP
                if (dstIp.Equals(_adapter.IpAddress) || srcIp.Equals(_adapter.IpAddress))
                    return;

                // Case 1: Downlink (Gateway -> Target Device)
                // Traffic coming from Gateway destined for one of our controlled devices
                if (_controlledDevices.TryGetValue(dstIp.ToString(), out var targetDevice) && targetDevice.IsControlled)
                {
                    targetDevice.LastSeen = DateTime.Now;

                    // If blocked, drop immediately
                    if (targetDevice.IsBlocked) return;

                    // Bandwidth Limiter Check
                    if (_limiters.TryGetValue(targetDevice.MacString, out var limiters))
                    {
                        if (!limiters.dl.AllowPacket(rawBytes.Length))
                        {
                            return; // Dropped due to rate limit!
                        }
                    }

                    // Record download bytes
                    RecordBytes(targetDevice.MacString, rawBytes.Length, 0);

                    // Forward to real target device
                    // Rewrite Destination MAC to Target's real MAC
                    // Rewrite Source MAC to Our MAC
                    ethernetPacket.DestinationHardwareAddress = targetDevice.MAC;
                    ethernetPacket.SourceHardwareAddress = _adapter.MacAddress;

                    _adapter.NativeDevice?.SendPacket(ethernetPacket.Bytes);
                    return;
                }

                // Case 2: Uplink (Target Device -> Gateway / Internet)
                // Traffic coming from a controlled target device destined for outside
                string srcMacStr = string.Join(":", srcMac.GetAddressBytes().Select(b => b.ToString("X2")));
                if ((_controlledDevices.TryGetValue(srcIp.ToString(), out var srcDevice) ||
                     _controlledDevices.TryGetValue(srcMacStr, out srcDevice)) && srcDevice.IsControlled)
                {
                    srcDevice.LastSeen = DateTime.Now;

                    // If blocked, drop immediately
                    if (srcDevice.IsBlocked) return;

                    // Bandwidth Limiter Check
                    if (_limiters.TryGetValue(srcDevice.MacString, out var limiters))
                    {
                        if (!limiters.ul.AllowPacket(rawBytes.Length))
                        {
                            return; // Dropped due to rate limit!
                        }
                    }

                    // Record upload bytes
                    RecordBytes(srcDevice.MacString, 0, rawBytes.Length);

                    // Forward to Gateway
                    // Rewrite Destination MAC to Gateway MAC
                    // Rewrite Source MAC to Our MAC
                    ethernetPacket.DestinationHardwareAddress = _adapter.GatewayMac;
                    ethernetPacket.SourceHardwareAddress = _adapter.MacAddress;

                    _adapter.NativeDevice?.SendPacket(ethernetPacket.Bytes);
                }
            }
            catch
            {
                // Ignore transient packet parse errors to keep capture loop ultra fast
            }
        }

        private void RecordBytes(string macStr, long dl, long ul)
        {
            _intervalBytes.AddOrUpdate(macStr,
                (dl, ul),
                (k, old) => (old.dlBytes + dl, old.ulBytes + ul));
        }

        private void OnSpeedMeterTick(object? state)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsedSec = (double)(now - _lastSpeedMeterTick) / Stopwatch.Frequency;
            if (elapsedSec <= 0) elapsedSec = 1.0;
            _lastSpeedMeterTick = now;

            double totalDl = 0;
            double totalUl = 0;

            // Use distinct devices to avoid double-counting due to IP & MAC dictionary keys
            foreach (var device in _controlledDevices.Values.Distinct())
            {
                if (_intervalBytes.TryRemove(device.MacString, out var bytes))
                {
                    double dlKbps = (bytes.dlBytes / 1024.0) / elapsedSec;
                    double ulKbps = (bytes.ulBytes / 1024.0) / elapsedSec;

                    device.CurrentDownloadKbps = dlKbps;
                    device.CurrentUploadKbps = ulKbps;

                    totalDl += dlKbps;
                    totalUl += ulKbps;
                }
                else
                {
                    device.CurrentDownloadKbps = 0;
                    device.CurrentUploadKbps = 0;
                }
            }

            TotalDownloadKbps = totalDl;
            TotalUploadKbps = totalUl;
            TotalSpeedUpdated?.Invoke(totalDl, totalUl);
        }
    }
}
