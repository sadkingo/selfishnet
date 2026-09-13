using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace SelfishNetModern.Services
{
    public class MacVendorService
    {
        private static readonly Dictionary<string, string> OuiDatabase = new(StringComparer.OrdinalIgnoreCase)
        {
            // Apple
            { "00:03:93", "Apple" }, { "00:05:02", "Apple" }, { "00:0A:27", "Apple" }, { "00:0A:95", "Apple" },
            { "00:0D:93", "Apple" }, { "00:11:24", "Apple" }, { "00:14:51", "Apple" }, { "00:16:CB", "Apple" },
            { "00:17:F2", "Apple" }, { "00:19:E3", "Apple" }, { "00:1B:63", "Apple" }, { "00:1C:B3", "Apple" },
            { "00:1D:4F", "Apple" }, { "00:1E:52", "Apple" }, { "00:1F:5B", "Apple" }, { "00:1F:F3", "Apple" },
            { "00:21:E9", "Apple" }, { "00:22:41", "Apple" }, { "00:23:12", "Apple" }, { "00:23:32", "Apple" },
            { "00:23:6C", "Apple" }, { "00:24:36", "Apple" }, { "00:25:00", "Apple" }, { "00:25:4B", "Apple" },
            { "00:25:BC", "Apple" }, { "00:26:08", "Apple" }, { "00:26:4A", "Apple" }, { "00:26:B0", "Apple" },
            { "00:26:BB", "Apple" }, { "28:CF:E9", "Apple" }, { "3C:07:54", "Apple" }, { "3C:15:C2", "Apple" },
            { "40:6c:8f", "Apple" }, { "48:60:5F", "Apple" }, { "50:BC:96", "Apple" }, { "54:26:96", "Apple" },
            { "58:55:CA", "Apple" }, { "64:20:0C", "Apple" }, { "68:96:7B", "Apple" }, { "70:11:24", "Apple" },
            { "78:31:C1", "Apple" }, { "7C:6D:62", "Apple" }, { "80:E6:50", "Apple" }, { "84:38:35", "Apple" },
            { "88:66:5A", "Apple" }, { "8C:85:90", "Apple" }, { "90:72:40", "Apple" }, { "94:10:3E", "Apple" },
            { "98:01:A7", "Apple" }, { "A4:C3:61", "Apple" }, { "AC:87:A3", "Apple" }, { "B8:78:26", "Apple" },
            { "BC:52:B7", "Apple" }, { "C0:84:7D", "Apple" }, { "C4:2C:03", "Apple" }, { "C8:33:4B", "Apple" },
            { "CC:29:F5", "Apple" }, { "D0:25:98", "Apple" }, { "D4:90:9C", "Apple" }, { "DC:2B:61", "Apple" },
            { "E0:B9:BA", "Apple" }, { "E4:98:D6", "Apple" }, { "E8:80:2E", "Apple" }, { "F0:18:98", "Apple" },
            { "F4:0F:24", "Apple" }, { "F8:1E:DF", "Apple" }, { "FC:FC:48", "Apple" },

            // Samsung
            { "00:07:AB", "Samsung" }, { "00:12:47", "Samsung" }, { "00:15:99", "Samsung" }, { "00:18:AF", "Samsung" },
            { "00:1D:25", "Samsung" }, { "00:21:D1", "Samsung" }, { "00:23:D7", "Samsung" }, { "00:24:54", "Samsung" },
            { "00:26:5D", "Samsung" }, { "14:1F:78", "Samsung" }, { "18:22:7E", "Samsung" }, { "24:4B:81", "Samsung" },
            { "34:23:BA", "Samsung" }, { "38:0B:40", "Samsung" }, { "40:0E:85", "Samsung" }, { "44:F4:59", "Samsung" },
            { "48:44:F7", "Samsung" }, { "50:85:69", "Samsung" }, { "54:92:BE", "Samsung" }, { "5C:A3:9D", "Samsung" },
            { "64:16:7F", "Samsung" }, { "78:47:1D", "Samsung" }, { "84:25:DB", "Samsung" }, { "88:32:9B", "Samsung" },
            { "8C:77:12", "Samsung" }, { "94:63:72", "Samsung" }, { "A8:06:00", "Samsung" }, { "AC:5F:3E", "Samsung" },
            { "B4:F1:DA", "Samsung" }, { "C0:97:27", "Samsung" }, { "CC:07:AB", "Samsung" }, { "D0:17:6A", "Samsung" },
            { "D8:57:EF", "Samsung" }, { "E4:7C:F9", "Samsung" }, { "E8:50:8B", "Samsung" }, { "F4:09:D8", "Samsung" },

            // Xiaomi
            { "00:9E:C8", "Xiaomi" }, { "04:CF:8C", "Xiaomi" }, { "0C:98:38", "Xiaomi" }, { "14:F6:5A", "Xiaomi" },
            { "18:F0:E4", "Xiaomi" }, { "28:6C:07", "Xiaomi" }, { "34:80:B3", "Xiaomi" }, { "38:A4:ED", "Xiaomi" },
            { "40:31:3C", "Xiaomi" }, { "50:64:2B", "Xiaomi" }, { "54:48:E6", "Xiaomi" }, { "58:44:98", "Xiaomi" },
            { "64:CC:2E", "Xiaomi" }, { "74:51:BA", "Xiaomi" }, { "78:02:F8", "Xiaomi" }, { "7C:49:EB", "Xiaomi" },
            { "88:C3:97", "Xiaomi" }, { "98:FA:E3", "Xiaomi" },
            { "AC:C1:EE", "Xiaomi" }, { "B0:4A:FE", "Xiaomi" }, { "C4:0B:CB", "Xiaomi" }, { "D4:97:0B", "Xiaomi" },

            // Huawei & Honor
            { "00:1E:10", "Huawei" }, { "00:25:68", "Huawei" }, { "04:25:28", "Huawei" }, { "0C:96:BF", "Huawei" },
            { "10:47:80", "Huawei" }, { "18:C5:8A", "Huawei" }, { "20:F4:78", "Huawei" }, { "28:31:52", "Huawei" },
            { "34:2E:B6", "Huawei" }, { "3C:CD:36", "Huawei" }, { "40:CB:A8", "Huawei" }, { "48:46:FB", "Huawei" },
            { "50:9F:27", "Huawei" }, { "54:89:98", "Huawei" }, { "58:2A:F7", "Huawei" }, { "60:DE:44", "Huawei" },
            { "70:7B:E8", "Huawei" }, { "80:B6:86", "Huawei" }, { "88:28:B3", "Huawei" }, { "90:4E:91", "Huawei" },

            // TP-Link
            { "00:0A:EB", "TP-Link" }, { "00:14:78", "TP-Link" }, { "00:19:E0", "TP-Link" }, { "00:21:27", "TP-Link" },
            { "00:23:CD", "TP-Link" }, { "00:25:86", "TP-Link" }, { "00:27:19", "TP-Link" }, { "14:CF:92", "TP-Link" },
            { "18:A6:F7", "TP-Link" }, { "1C:3B:F3", "TP-Link" }, { "30:B5:C2", "TP-Link" }, { "30:DE:4B", "TP-Link" },
            { "50:C7:BF", "TP-Link" }, { "54:AF:97", "TP-Link" }, { "5C:63:BF", "TP-Link" }, { "60:32:B1", "TP-Link" },
            { "64:56:01", "TP-Link" }, { "64:70:02", "TP-Link" }, { "70:4F:57", "TP-Link" }, { "74:05:A5", "TP-Link" },
            { "7C:8B:CA", "TP-Link" }, { "84:16:F9", "TP-Link" }, { "90:F6:52", "TP-Link" }, { "98:DA:C4", "TP-Link" },
            { "A0:F3:C1", "TP-Link" }, { "B0:4E:26", "TP-Link" }, { "C0:25:E9", "TP-Link" }, { "C0:4A:00", "TP-Link" },
            { "D8:07:B6", "TP-Link" }, { "E4:C3:2A", "TP-Link" }, { "EC:08:6B", "TP-Link" }, { "F4:EC:38", "TP-Link" },

            // Intel
            { "00:02:B3", "Intel" }, { "00:03:47", "Intel" }, { "00:04:23", "Intel" }, { "00:0E:0C", "Intel" },
            { "00:11:11", "Intel" }, { "00:13:02", "Intel" }, { "00:13:20", "Intel" }, { "00:15:00", "Intel" },
            { "00:16:76", "Intel" }, { "00:19:D1", "Intel" }, { "00:1B:21", "Intel" }, { "00:1D:E0", "Intel" },
            { "00:1E:64", "Intel" }, { "00:21:5C", "Intel" }, { "00:23:14", "Intel" }, { "00:24:D7", "Intel" },
            { "08:11:96", "Intel" }, { "14:4F:8A", "Intel" }, { "28:70:4E", "Intel" }, { "34:13:E8", "Intel" },
            { "3C:F8:62", "Intel" }, { "40:A8:F0", "Intel" }, { "48:51:B7", "Intel" }, { "4C:79:6E", "Intel" },
            { "58:91:CF", "Intel" }, { "64:4E:97", "Intel" }, { "7C:21:4A", "Intel" }, { "80:86:F2", "Intel" },
            { "88:78:73", "Intel" }, { "A4:4E:31", "Intel" }, { "AC:74:B1", "Intel" }, { "C8:5B:76", "Intel" },

            // Realtek
            { "00:00:21", "Realtek" }, { "00:07:70", "Realtek" }, { "00:0B:2F", "Realtek" }, { "00:0E:2E", "Realtek" },
            { "00:13:8F", "Realtek" }, { "00:18:E7", "Realtek" }, { "00:24:2C", "Realtek" }, { "00:26:18", "Realtek" },
            { "00:E0:4C", "Realtek" }, { "20:7C:8F", "Realtek" }, { "48:5D:60", "Realtek" }, { "52:54:4C", "Realtek" },

            // Google
            { "00:1A:11", "Google" }, { "3C:5A:37", "Google" }, { "48:D6:D5", "Google" }, { "54:60:09", "Google" },
            { "64:16:66", "Google" }, { "70:3E:AC", "Google" }, { "94:EB:CD", "Google" }, { "A4:77:33", "Google" },
            { "D8:6C:63", "Google" }, { "F4:03:04", "Google" }, { "F4:F5:DB", "Google" },

            // Espressif (ESP8266 / ESP32 IoT)
            { "18:FE:34", "Espressif (IoT)" }, { "24:0A:C4", "Espressif (IoT)" }, { "24:62:AB", "Espressif (IoT)" },
            { "24:6F:28", "Espressif (IoT)" }, { "24:B2:DE", "Espressif (IoT)" }, { "2C:F4:32", "Espressif (IoT)" },
            { "30:AE:A4", "Espressif (IoT)" }, { "3C:61:05", "Espressif (IoT)" }, { "3C:71:BF", "Espressif (IoT)" },
            { "48:3F:DA", "Espressif (IoT)" }, { "4C:11:AE", "Espressif (IoT)" }, { "54:5A:A6", "Espressif (IoT)" },
            { "5C:CF:7F", "Espressif (IoT)" }, { "60:01:94", "Espressif (IoT)" }, { "68:C6:3A", "Espressif (IoT)" },
            { "84:0D:8E", "Espressif (IoT)" }, { "84:F3:EB", "Espressif (IoT)" }, { "90:97:D5", "Espressif (IoT)" },
            { "A4:CF:12", "Espressif (IoT)" }, { "AC:D0:74", "Espressif (IoT)" }, { "B4:E6:2D", "Espressif (IoT)" },
            { "BC:DD:C2", "Espressif (IoT)" }, { "C4:4F:33", "Espressif (IoT)" }, { "CC:50:E3", "Espressif (IoT)" },
            { "DC:4F:22", "Espressif (IoT)" }, { "EC:FA:BC", "Espressif (IoT)" },

            // Raspberry Pi
            { "B8:27:EB", "Raspberry Pi" }, { "DC:A6:32", "Raspberry Pi" }, { "E4:5F:01", "Raspberry Pi" },
            { "28:CD:C1", "Raspberry Pi" },

            // Microsoft
            { "00:03:FF", "Microsoft" }, { "00:0D:3A", "Microsoft" }, { "00:12:5A", "Microsoft" }, { "00:15:5D", "Microsoft Hyper-V" },
            { "00:17:FA", "Microsoft" }, { "00:1D:D8", "Microsoft" }, { "00:22:48", "Microsoft Xbox" }, { "00:25:AE", "Microsoft" },
            { "00:50:F2", "Microsoft" }, { "28:18:78", "Microsoft Surface" }, { "60:45:BD", "Microsoft" }, { "7C:ED:8D", "Microsoft" },

            // Cisco / Linksys
            { "00:00:0C", "Cisco" }, { "00:01:42", "Cisco" }, { "00:01:43", "Cisco" }, { "00:01:C7", "Cisco" },
            { "00:01:C9", "Cisco" }, { "00:02:16", "Cisco" }, { "00:02:17", "Cisco" }, { "00:02:4A", "Cisco" },
            { "00:04:9B", "Cisco" }, { "00:04:C0", "Cisco" }, { "00:06:53", "Cisco" }, { "00:0E:D7", "Cisco Linksys" },

            // Netgear
            { "00:09:5B", "Netgear" }, { "00:0F:B5", "Netgear" }, { "00:14:6C", "Netgear" }, { "00:18:4D", "Netgear" },
            { "00:1B:2F", "Netgear" }, { "00:1E:2A", "Netgear" }, { "00:1F:33", "Netgear" }, { "00:24:B2", "Netgear" },
            { "08:02:8E", "Netgear" }, { "10:DA:43", "Netgear" }, { "20:4E:7F", "Netgear" }, { "28:80:88", "Netgear" },

            // D-Link
            { "00:05:5D", "D-Link" }, { "00:0D:88", "D-Link" }, { "00:11:95", "D-Link" }, { "00:13:46", "D-Link" },
            { "00:15:E9", "D-Link" }, { "00:17:9A", "D-Link" }, { "00:19:5B", "D-Link" }, { "00:1B:11", "D-Link" },

            // Amazon
            { "00:FC:8B", "Amazon Echo/Fire" }, { "18:74:2E", "Amazon" }, { "38:F7:3D", "Amazon" }, { "40:B4:CD", "Amazon" },
            { "44:65:0D", "Amazon" }, { "50:DC:E7", "Amazon" }, { "68:37:E9", "Amazon" }, { "68:54:5A", "Amazon" },
            { "74:75:48", "Amazon" }, { "AC:63:BE", "Amazon" }, { "FC:A6:67", "Amazon" },

            // Sony
            { "00:01:4A", "Sony" }, { "00:04:1F", "Sony" }, { "00:13:15", "Sony" }, { "00:15:C1", "Sony" },
            { "00:19:C5", "Sony" }, { "00:1D:BA", "Sony PlayStation" }, { "00:24:8D", "Sony PlayStation" },

            // Asus
            { "00:0C:6E", "Asus" }, { "00:0E:A6", "Asus" }, { "00:11:D8", "Asus" }, { "00:15:F2", "Asus" },
            { "00:17:31", "Asus" }, { "00:18:F3", "Asus" }, { "00:1A:92", "Asus" }, { "04:D9:F5", "Asus" },
            { "08:60:6E", "Asus" }, { "10:7B:44", "Asus" }, { "10:BF:48", "Asus" }, { "14:DD:A9", "Asus" },
            { "1C:87:2C", "Asus" }, { "2C:4D:54", "Asus" }, { "30:5A:3A", "Asus" }, { "38:2C:4A", "Asus" }
        };

        public static string GetVendor(PhysicalAddress mac)
        {
            if (mac == null || mac.Equals(PhysicalAddress.None))
                return "Unknown";

            byte[] bytes = mac.GetAddressBytes();
            if (bytes.Length < 3)
                return "Unknown";

            // Check randomized/private MAC address bit (locally administered bit: second least significant bit of first octet)
            bool isLocallyAdministered = (bytes[0] & 0x02) != 0;

            string prefix = $"{bytes[0]:X2}:{bytes[1]:X2}:{bytes[2]:X2}";

            if (OuiDatabase.TryGetValue(prefix, out string? vendor))
            {
                return vendor;
            }

            if (isLocallyAdministered)
            {
                return "Private MAC (Mobile/Randomized)";
            }

            return "Network Device";
        }
    }
}
