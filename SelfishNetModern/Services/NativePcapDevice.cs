using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SelfishNetModern.Services
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PcapHeader
    {
        public uint Seconds;
        public uint Microseconds;
        public uint CapturedLength;
        public uint PacketLength;
    }

    public delegate void RawPacketArrivalHandler(PcapHeader header, byte[] packetData);

    public class NativePcapDevice : IDisposable
    {
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr pcap_open_live(string dev, int snaplen, int promisc, int to_ms, System.Text.StringBuilder errbuf);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void pcap_close(IntPtr p);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pcap_sendpacket(IntPtr p, byte[] buf, int size);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pcap_loop(IntPtr p, int cnt, pcap_handler callback, IntPtr user);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void pcap_breakloop(IntPtr p);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void pcap_handler(IntPtr user, IntPtr h, IntPtr bytes);

        private IntPtr _handle = IntPtr.Zero;
        private Thread? _captureThread;
        private pcap_handler? _handlerDelegate; // Keep delegate alive
        private readonly object _lock = new();
        private bool _disposed;

        public bool IsOpen => _handle != IntPtr.Zero;
        public bool IsCapturing { get; private set; }
        public string DeviceId { get; private set; } = string.Empty;

        public event RawPacketArrivalHandler? OnPacketArrival;
        public event Action<string>? LogMessage;

        public bool Open(string deviceId, int snaplen = 65536, bool promiscuous = true, int timeoutMs = 50)
        {
            lock (_lock)
            {
                if (IsOpen) Close();

                DeviceId = deviceId;
                var errbuf = new System.Text.StringBuilder(256);

                // Try device name variants: GUID directly, or with \Device\NPF_ prefix
                string[] candidates = new[]
                {
                    deviceId.StartsWith("{") ? deviceId : $"{{{deviceId}}}",
                    deviceId.StartsWith(@"\Device\NPF_") ? deviceId : $@"\Device\NPF_{deviceId}",
                    deviceId.StartsWith(@"\Device\NPF_{") ? deviceId : $@"\Device\NPF_{{{deviceId}}}"
                };

                foreach (var candidate in candidates)
                {
                    _handle = pcap_open_live(candidate, snaplen, promiscuous ? 1 : 0, timeoutMs, errbuf);
                    if (_handle != IntPtr.Zero)
                    {
                        LogMessage?.Invoke($"Native pcap device opened: {candidate}");
                        return true;
                    }
                }

                LogMessage?.Invoke($"Failed to open pcap handle for {deviceId}: {errbuf}");
                return false;
            }
        }

        public void StartCapture()
        {
            lock (_lock)
            {
                if (!IsOpen || IsCapturing) return;

                IsCapturing = true;
                _handlerDelegate = PacketCallback;

                _captureThread = new Thread(CaptureWorker)
                {
                    Name = "PcapCaptureWorker",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _captureThread.Start();
            }
        }

        private void CaptureWorker()
        {
            try
            {
                if (_handle != IntPtr.Zero && _handlerDelegate != null)
                {
                    pcap_loop(_handle, 0, _handlerDelegate, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Capture loop exited: {ex.Message}");
            }
            finally
            {
                IsCapturing = false;
            }
        }

        private void PacketCallback(IntPtr user, IntPtr h, IntPtr bytes)
        {
            if (!IsCapturing || h == IntPtr.Zero || bytes == IntPtr.Zero) return;

            try
            {
                var hdr = Marshal.PtrToStructure<PcapHeader>(h);
                if (hdr.CapturedLength > 0 && hdr.CapturedLength <= 65536)
                {
                    byte[] data = new byte[hdr.CapturedLength];
                    Marshal.Copy(bytes, data, 0, (int)hdr.CapturedLength);
                    OnPacketArrival?.Invoke(hdr, data);
                }
            }
            catch { }
        }

        public void StopCapture()
        {
            lock (_lock)
            {
                if (!IsCapturing) return;

                IsCapturing = false;
                if (_handle != IntPtr.Zero)
                {
                    try { pcap_breakloop(_handle); } catch { }
                }

                try
                {
                    _captureThread?.Join(500);
                }
                catch { }
                _captureThread = null;
            }
        }

        public bool SendPacket(byte[] buffer)
        {
            if (!IsOpen || buffer == null || buffer.Length == 0) return false;

            lock (_lock)
            {
                if (_handle == IntPtr.Zero) return false;
                int res = pcap_sendpacket(_handle, buffer, buffer.Length);
                return res == 0;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                StopCapture();

                if (_handle != IntPtr.Zero)
                {
                    try { pcap_close(_handle); } catch { }
                    _handle = IntPtr.Zero;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
