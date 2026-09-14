using System.Net;
using System.Net.NetworkInformation;
using SharpPcap;

namespace SelfishNetModern.Models
{
    public class AdapterInfo
    {
        public string Id { get; set; } = string.Empty;
        public int InterfaceIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IPAddress IpAddress { get; set; } = IPAddress.None;
        public IPAddress SubnetMask { get; set; } = IPAddress.None;
        public PhysicalAddress MacAddress { get; set; } = PhysicalAddress.None;
        public IPAddress? GatewayIp { get; set; }
        public PhysicalAddress? GatewayMac { get; set; }
        public ILiveDevice? PcapDevice { get; set; }
        public SelfishNetModern.Services.NativePcapDevice? NativeDevice { get; set; }
        public HashSet<IPAddress> LocalIps { get; } = new();

        public bool MatchesLocalIp(IPAddress? ip)
        {
            if (ip == null) return false;
            if (ip.Equals(IpAddress)) return true;
            return LocalIps.Contains(ip);
        }

        public string DisplayName => $"{Name} ({IpAddress}) - {Description}";

        public List<IPAddress> GetSubnetIps()
        {
            var ips = new List<IPAddress>();
            if (IpAddress == null || SubnetMask == null || IpAddress.Equals(IPAddress.None))
                return ips;

            byte[] ipBytes = IpAddress.GetAddressBytes();
            byte[] maskBytes = SubnetMask.GetAddressBytes();
            if (ipBytes.Length != 4 || maskBytes.Length != 4)
                return ips;

            byte[] networkBytes = new byte[4];
            byte[] broadcastBytes = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                broadcastBytes[i] = (byte)(networkBytes[i] | ~maskBytes[i]);
            }

            uint net = (uint)((networkBytes[0] << 24) | (networkBytes[1] << 16) | (networkBytes[2] << 8) | networkBytes[3]);
            uint bcast = (uint)((broadcastBytes[0] << 24) | (broadcastBytes[1] << 16) | (broadcastBytes[2] << 8) | broadcastBytes[3]);

            // If subnet is larger than /24 (more than 512 hosts), limit to the local /24 around our IP for speed
            if (bcast - net > 512)
            {
                net = (uint)((ipBytes[0] << 24) | (ipBytes[1] << 16) | (ipBytes[2] << 8) | 0);
                bcast = net + 255;
            }

            for (uint current = net + 1; current < bcast; current++)
            {
                byte[] currentBytes = new byte[4]
                {
                    (byte)((current >> 24) & 0xFF),
                    (byte)((current >> 16) & 0xFF),
                    (byte)((current >> 8) & 0xFF),
                    (byte)(current & 0xFF)
                };
                ips.Add(new IPAddress(currentBytes));
            }

            return ips;
        }
    }
}
