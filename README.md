# SelfishNet Modern ⚡

A high-performance, stable, and resilient local network bandwidth controller and traffic management utility built in C# (.NET 8) and WPF, utilizing **SharpPcap** and **PacketDotNet**.

---

## ⚠️ Terms of Use & Legal Compliance Notice
This software is designed **exclusively for network administrators and authorized users** to monitor, manage, and benchmark bandwidth allocation on local area networks (LANs) that they own or have received explicit written permission to administer. 

Executing ARP redirection or traffic throttling on third-party networks without authorization is strictly prohibited.

---

## 🌟 Key Features

- **🎨 Modern Dark Dashboard**:
  - Sleek Fluent / Catppuccin dark styling with responsive layout.
  - Automatic device classification with friendly icons (🌐 Router, 💻 PC, 📱 Phone, 📺 Smart TV, 🔌 IoT).
  - Real-time speedometer cards for Total Download and Total Upload throughput (KB/s and MB/s).
  - Live per-device speed tracking.

- **⚡ Stable Control**:
  - **Bidirectional ARP Redirection**: Redirects target traffic through the host machine using continuous background pulses (1.5s interval).
  - **High-Precision Token Bucket Shaper**: Mathematically accurate token bucket rate limiter for upload and download limits.
  - **Instant Blocking**: Discards and drops packets with zero latency.
  - **Graceful Un-Poison / Healing**: Immediately fires 5 restoration ARP frames on stop, disable, or exit so network tables heal reliably.

- **🔄 Network Resilience & Auto-Reconnect**:
  - Monitors `NetworkAddressChanged` and `NetworkAvailabilityChanged` events.
  - 3-second gateway heartbeat watchdog.
  - Automatically recovers from Wi-Fi drops, DHCP renewals, or router reboots without manual intervention.

- **🔍 Smart Network Discovery**:
  - Concurrent multi-threaded ARP subnet sweep (under 3 seconds for `/24`).
  - MAC OUI manufacturer identification (Apple, Samsung, Xiaomi, Huawei, Intel, Realtek, TP-Link, Espressif, etc.).
  - Hostname resolution via NetBIOS (`NBSTAT` UDP 137) and Reverse DNS.

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 or Windows 11 (x64)
- **Npcap** (or WinPcap) installed in WinPcap-compatible mode
- Administrator privileges (required for raw packet capture and ARP manipulation)

### Build & Run
```bash
# Build Release
dotnet build SelfishNetModern\SelfishNetModern.csproj -c Release

# Publish single folder win-x64 app
dotnet publish SelfishNetModern\SelfishNetModern.csproj -c Release -r win-x64 --no-self-contained -o bin\publish

# Run launcher
Run-SelfishNet.bat
```

### Running Automated Tests
```bash
dotnet run --project SelfishNetTests\SelfishNetTests.csproj
```
