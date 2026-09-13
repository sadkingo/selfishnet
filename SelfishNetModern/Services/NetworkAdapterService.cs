using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SelfishNetModern.Models;
using SharpPcap;

namespace SelfishNetModern.Services
{
    public class NetworkAdapterService
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPNETROW
        {
            public int dwIndex;
            public int dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public uint dwAddr;
            public int dwType;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

        public static Dictionary<IPAddress, PhysicalAddress> GetKernelArpCache()
        {
            var result = new Dictionary<IPAddress, PhysicalAddress>();
            int bytesNeeded = 0;
            GetIpNetTable(IntPtr.Zero, ref bytesNeeded, false);
            if (bytesNeeded <= 0) return result;

            IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
            try
            {
                if (GetIpNetTable(buffer, ref bytesNeeded, false) == 0)
                {
                    int numEntries = Marshal.ReadInt32(buffer);
                    IntPtr current = IntPtr.Add(buffer, 4);
                    int rowSize = Marshal.SizeOf<MIB_IPNETROW>();
                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_IPNETROW>(current);
                        current = IntPtr.Add(current, rowSize);
                        if (row.dwType == 3 || row.dwType == 4) // Dynamic or Static
                        {
                            byte[] ipBytes = BitConverter.GetBytes(row.dwAddr);
                            var ip = new IPAddress(ipBytes);
                            if (row.dwPhysAddrLen == 6)
                            {
                                byte[] macBytes = new byte[6];
                                Array.Copy(row.bPhysAddr, macBytes, 6);
                                if (!macBytes.All(b => b == 0) && !macBytes.All(b => b == 0xFF))
                                {
                                    result[ip] = new PhysicalAddress(macBytes);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        public static List<AdapterInfo> GetAvailableAdapters()
        {
            var adapters = new List<AdapterInfo>();
            CaptureDeviceList pcapDevices;
            try
            {
                pcapDevices = CaptureDeviceList.Instance;
                pcapDevices.Refresh();
            }
            catch
            {
                pcapDevices = null!;
            }

            var netInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            foreach (var ni in netInterfaces)
            {
                var ipProps = ni.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4 == null) continue;

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                var localMac = ni.GetPhysicalAddress();
                var localMacBytes = localMac.GetAddressBytes();

                ILiveDevice? matchedPcapDevice = null;
                if (pcapDevices != null)
                {
                    foreach (var dev in pcapDevices)
                    {
                        if (dev.MacAddress != null && dev.MacAddress.Equals(localMac))
                        {
                            matchedPcapDevice = dev;
                            break;
                        }

                        // Fallback match on device name/ID
                        if (!string.IsNullOrEmpty(dev.Name) && dev.Name.Contains(ni.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedPcapDevice = dev;
                            break;
                        }
                    }
                }

                var adapterInfo = new AdapterInfo
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    IpAddress = ipv4.Address,
                    SubnetMask = ipv4.IPv4Mask ?? IPAddress.Parse("255.255.255.0"),
                    MacAddress = localMac,
                    GatewayIp = gateway?.Address,
                    PcapDevice = matchedPcapDevice
                };

                if (adapterInfo.GatewayIp != null)
                {
                    adapterInfo.GatewayMac = ResolveMac(adapterInfo.GatewayIp, adapterInfo.IpAddress);
                }

                adapters.Add(adapterInfo);
            }

            return adapters;
        }

        public static AdapterInfo? GetDefaultAdapter()
        {
            var adapters = GetAvailableAdapters();
            // Prioritize adapter that has a gateway and gateway MAC resolved
            return adapters.FirstOrDefault(a => a.GatewayIp != null && a.GatewayMac != null && !a.GatewayMac.Equals(PhysicalAddress.None))
                ?? adapters.FirstOrDefault(a => a.GatewayIp != null)
                ?? adapters.FirstOrDefault();
        }

        public static PhysicalAddress? ResolveMac(IPAddress targetIp, IPAddress? sourceIp = null)
        {
            try
            {
                byte[] targetBytes = targetIp.GetAddressBytes();
                uint destIp = BitConverter.ToUInt32(targetBytes, 0);

                uint srcIp = 0;
                if (sourceIp != null)
                {
                    byte[] srcBytes = sourceIp.GetAddressBytes();
                    srcIp = BitConverter.ToUInt32(srcBytes, 0);
                }

                byte[] macBytes = new byte[6];
                uint len = (uint)macBytes.Length;

                int result = SendARP(destIp, srcIp, macBytes, ref len);
                if (result == 0 && len == 6)
                {
                    return new PhysicalAddress(macBytes);
                }
            }
            catch
            {
                // Fallback / failed
            }

            return null;
        }
    }
}
