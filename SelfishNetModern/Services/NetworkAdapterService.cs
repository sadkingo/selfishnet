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

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int CreateIpNetEntry(ref MIB_IPNETROW pArpEntry);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int DeleteIpNetEntry(ref MIB_IPNETROW pArpEntry);

        public static bool IsValidUnicastHost(IPAddress? ip, PhysicalAddress? mac, AdapterInfo? adapter = null)
        {
            if (ip == null || mac == null) return false;

            // 1. MAC Validation
            byte[] macBytes = mac.GetAddressBytes();
            if (macBytes.Length != 6) return false;
            // Disallow all 0s and all 0xFFs
            if (macBytes.All(b => b == 0) || macBytes.All(b => b == 0xFF)) return false;
            // Disallow Multicast / Broadcast MAC (IEEE 802 I/G bit: bit 0 of first octet is 1)
            // e.g. 01:00:5E:... (IPv4 multicast), 33:33:... (IPv6 multicast), FF:FF:FF:... (broadcast)
            if ((macBytes[0] & 0x01) != 0) return false;

            // 2. IP Validation
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] ipBytes = ip.GetAddressBytes();
            if (ipBytes.Length != 4) return false;

            // Reject 0.0.0.0/8, 127.0.0.0/8 (Loopback), 169.254.0.0/16 (APIPA)
            if (ipBytes[0] == 0 || ipBytes[0] == 127) return false;
            if (ipBytes[0] == 169 && ipBytes[1] == 254) return false;

            // Reject Class D (Multicast 224.0.0.0 - 239.255.255.255) and Class E / Broadcast (>= 240.0.0.0)
            if (ipBytes[0] >= 224) return false;

            // 3. Adapter Subnet Validation (if provided)
            if (adapter != null && adapter.SubnetMask != null && adapter.IpAddress != null)
            {
                byte[] maskBytes = adapter.SubnetMask.GetAddressBytes();
                byte[] adapterIpBytes = adapter.IpAddress.GetAddressBytes();

                for (int i = 0; i < 4; i++)
                {
                    if ((ipBytes[i] & maskBytes[i]) != (adapterIpBytes[i] & maskBytes[i]))
                        return false;
                }

                // Check for network address (host bits all 0) and broadcast address (host bits all 1)
                bool isNetworkAddr = true;
                bool isBroadcastAddr = true;
                for (int i = 0; i < 4; i++)
                {
                    byte hostBits = (byte)(ipBytes[i] & ~maskBytes[i]);
                    byte maxHostBits = (byte)(~maskBytes[i] & 0xFF);
                    if (hostBits != 0) isNetworkAddr = false;
                    if (hostBits != maxHostBits) isBroadcastAddr = false;
                }
                if (isNetworkAddr || isBroadcastAddr) return false;
            }

            return true;
        }

        public static Dictionary<IPAddress, PhysicalAddress> GetKernelArpCache(AdapterInfo? adapter = null)
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
                                var mac = new PhysicalAddress(macBytes);
                                if (IsValidUnicastHost(ip, mac, adapter))
                                {
                                    result[ip] = mac;
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

                int ifIndex = 0;
                try
                {
                    var ipv4Props = ipProps.GetIPv4Properties();
                    ifIndex = ipv4Props != null ? ipv4Props.Index : 0;
                }
                catch { }

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                var localMac = ni.GetPhysicalAddress();

                NativePcapDevice? nativeDev = new NativePcapDevice();
                if (!nativeDev.Open(ni.Id))
                {
                    nativeDev.Dispose();
                    nativeDev = null;
                }

                var adapterInfo = new AdapterInfo
                {
                    Id = ni.Id,
                    InterfaceIndex = ifIndex,
                    Name = ni.Name,
                    Description = ni.Description,
                    IpAddress = ipv4.Address,
                    SubnetMask = ipv4.IPv4Mask ?? IPAddress.Parse("255.255.255.0"),
                    MacAddress = localMac,
                    GatewayIp = gateway?.Address,
                    NativeDevice = nativeDev
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
            // Prioritize adapter that has an active capture device AND gateway resolved
            return adapters.FirstOrDefault(a => a.NativeDevice != null && a.GatewayIp != null && a.GatewayMac != null && !a.GatewayMac.Equals(PhysicalAddress.None))
                ?? adapters.FirstOrDefault(a => a.NativeDevice != null && a.GatewayIp != null)
                ?? adapters.FirstOrDefault(a => a.NativeDevice != null)
                ?? adapters.FirstOrDefault(a => a.GatewayIp != null && a.GatewayMac != null && !a.GatewayMac.Equals(PhysicalAddress.None))
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

        public static void LockArpEntry(int interfaceIndex, IPAddress? ip, PhysicalAddress? mac)
        {
            if (interfaceIndex <= 0 || ip == null || mac == null) return;
            try
            {
                var row = new MIB_IPNETROW();
                row.dwIndex = interfaceIndex;
                row.dwPhysAddrLen = 6;
                row.bPhysAddr = new byte[8];
                Array.Copy(mac.GetAddressBytes(), row.bPhysAddr, 6);
                row.dwAddr = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                row.dwType = 4; // Static/Permanent in Windows
                DeleteIpNetEntry(ref row);
                CreateIpNetEntry(ref row);
            }
            catch { }
        }

        public static void UnlockArpEntry(int interfaceIndex, IPAddress? ip)
        {
            if (interfaceIndex <= 0 || ip == null) return;
            try
            {
                var row = new MIB_IPNETROW();
                row.dwIndex = interfaceIndex;
                row.dwPhysAddrLen = 0;
                row.bPhysAddr = new byte[8];
                row.dwAddr = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                DeleteIpNetEntry(ref row);
            }
            catch { }
        }
    }
}
