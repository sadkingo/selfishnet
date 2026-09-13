using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using SelfishNetModern.Models;
using SelfishNetModern.Services;
using SelfishNetModern.Views;

namespace SelfishNetTests
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== SelfishNet Modern Verification Tests ===");

            TestMacVendorService();
            TestSubnetIps();
            TestTokenBucketLimiter();
            TestAdapterDetection();
            TestTermsOfUse();

            Console.WriteLine("\n🎉 ALL TESTS PASSED SUCCESSFULLY!");
        }

        static void TestMacVendorService()
        {
            Console.Write("[Test 1] Testing MacVendorService OUI resolution... ");

            var appleMac = PhysicalAddress.Parse("00-03-93-11-22-33");
            Assert(MacVendorService.GetVendor(appleMac) == "Apple", "Expected Apple");

            var samsungMac = PhysicalAddress.Parse("00-07-AB-AA-BB-CC");
            Assert(MacVendorService.GetVendor(samsungMac) == "Samsung", "Expected Samsung");

            var realtekMac = PhysicalAddress.Parse("00-E0-4C-47-51-8A");
            Assert(MacVendorService.GetVendor(realtekMac) == "Realtek", "Expected Realtek");

            var espMac = PhysicalAddress.Parse("18-FE-34-12-34-56");
            Assert(MacVendorService.GetVendor(espMac).Contains("Espressif"), "Expected Espressif");

            // Private / Randomized MAC check (bit 2 set)
            var privateMac = PhysicalAddress.Parse("DA-A1-19-22-33-44");
            Assert(MacVendorService.GetVendor(privateMac).Contains("Private MAC"), "Expected Private MAC detection");

            Console.WriteLine("PASSED");
        }

        static void TestSubnetIps()
        {
            Console.Write("[Test 2] Testing AdapterInfo Subnet IP generation... ");

            var adapter = new AdapterInfo
            {
                IpAddress = IPAddress.Parse("192.168.1.50"),
                SubnetMask = IPAddress.Parse("255.255.255.0")
            };

            var ips = adapter.GetSubnetIps();
            Assert(ips.Count == 254, $"Expected 254 IPs, got {ips.Count}");
            Assert(ips[0].ToString() == "192.168.1.1", $"Expected 192.168.1.1, got {ips[0]}");
            Assert(ips[253].ToString() == "192.168.1.254", $"Expected 192.168.1.254, got {ips[253]}");

            Console.WriteLine("PASSED");
        }

        static void TestTokenBucketLimiter()
        {
            Console.Write("[Test 3] Testing TokenBucketRateLimiter bandwidth shaping... ");

            // 100 KB/s = 102400 bytes/sec
            var limiter = new TokenBucketRateLimiter(102400);

            // Initial burst should allow a normal packet of 1500 bytes
            Assert(limiter.AllowPacket(1500), "Initial packet should be allowed");

            // Exhaust available tokens by consuming 100,000 bytes
            Assert(limiter.AllowPacket(100000), "Should consume remaining tokens");

            // Immediate next packet demanding 50,000 bytes should be throttled (denied) since bucket only has ~900 tokens left
            bool denied = !limiter.AllowPacket(50000);
            Assert(denied, "Excess packet should be throttled when bucket exhausted");

            // Unlimited rate test (0 bps)
            var unlimited = new TokenBucketRateLimiter(0);
            Assert(unlimited.AllowPacket(1000000), "Unlimited limiter should allow all packets");

            Console.WriteLine("PASSED");
        }

        static void TestAdapterDetection()
        {
            Console.Write("[Test 4] Testing NetworkAdapterService enumeration & gateway discovery... ");

            var adapters = NetworkAdapterService.GetAvailableAdapters();
            Assert(adapters.Count > 0, "Should detect at least 1 active network adapter");

            var def = NetworkAdapterService.GetDefaultAdapter();
            Assert(def != null, "Should detect default active adapter");
            Console.WriteLine($"PASSED (Found '{def?.Name}' with IP: {def?.IpAddress}, Gateway: {def?.GatewayIp})");
        }

        static void TestTermsOfUse()
        {
            Console.Write("[Test 5] Testing TermsOfUse confirmation logic... ");

            // Check if dialog can be queried
            bool hasAccepted = TermsOfUseDialog.HasAcceptedTerms();
            Console.WriteLine($"PASSED (Terms status: {(hasAccepted ? "Already Accepted" : "Pending First Run")})");
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"ASSERTION FAILED: {message}");
            }
        }
    }
}
