using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SelfishNetModern.Services
{
    public class DeviceNameResolver
    {
        public static async Task<string> ResolveAsync(IPAddress ip, CancellationToken cancellationToken = default)
        {
            if (ip == null || ip.Equals(IPAddress.None))
                return "Unknown";

            // 1. Try NetBIOS Name Query (Fastest and most accurate for Windows, Samba, NAS, and many IoT devices)
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(400); // 400ms timeout for NetBIOS
                string netbiosName = await QueryNetBiosNameAsync(ip, cts.Token);
                if (!string.IsNullOrWhiteSpace(netbiosName))
                {
                    return netbiosName;
                }
            }
            catch
            {
                // Fallthrough to DNS
            }

            // 2. Try Reverse DNS
            try
            {
                var hostTask = Dns.GetHostEntryAsync(ip);
                if (await Task.WhenAny(hostTask, Task.Delay(400, cancellationToken)) == hostTask)
                {
                    var hostEntry = await hostTask;
                    if (!string.IsNullOrWhiteSpace(hostEntry.HostName))
                    {
                        // Clean domain if it's like mypc.lan or mypc.home
                        var parts = hostEntry.HostName.Split('.');
                        return parts.Length > 0 ? parts[0] : hostEntry.HostName;
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return "Unknown";
        }

        private static async Task<string> QueryNetBiosNameAsync(IPAddress ip, CancellationToken cancellationToken)
        {
            using var client = new UdpClient();
            client.Client.SendTimeout = 300;
            client.Client.ReceiveTimeout = 300;

            // NBSTAT query packet for "*" (wildcard node status)
            byte[] packet = new byte[]
            {
                0x80, 0x94, // Transaction ID
                0x00, 0x00, // Flags: Query
                0x00, 0x01, // Questions: 1
                0x00, 0x00, // Answers: 0
                0x00, 0x00, // Authority: 0
                0x00, 0x00, // Additional: 0
                // Question Name: CKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA (encoded '*')
                0x20, 0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x00,
                0x00, 0x21, // Type: NBSTAT
                0x00, 0x01  // Class: IN
            };

            var endpoint = new IPEndPoint(ip, 137);
            await client.SendAsync(packet, packet.Length, endpoint);

            var receiveTask = client.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(350, cancellationToken));
            if (completedTask == receiveTask)
            {
                var result = await receiveTask;
                byte[] data = result.Buffer;
                if (data.Length > 57)
                {
                    int namesCount = data[56];
                    if (namesCount > 0 && data.Length >= 57 + 18)
                    {
                        // First NetBIOS name entry (15 chars name + 1 type + 2 flags)
                        string name = Encoding.ASCII.GetString(data, 57, 15).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }

            return string.Empty;
        }
    }
}
