using System;
using System.Diagnostics;

namespace SelfishNetModern.Services
{
    public class TokenBucketRateLimiter
    {
        private readonly object _lock = new();
        private double _tokens;
        private double _capacity;
        private double _bytesPerSecond;
        private long _lastRefillTimestamp;

        public TokenBucketRateLimiter(long bytesPerSecond)
        {
            UpdateRate(bytesPerSecond);
        }

        public void UpdateRate(long bytesPerSecond)
        {
            lock (_lock)
            {
                _bytesPerSecond = Math.Max(0, bytesPerSecond);
                // Allow a burst of up to 1 second worth of data or min 4KB
                _capacity = Math.Max(4096, _bytesPerSecond);
                _tokens = _capacity;
                _lastRefillTimestamp = Stopwatch.GetTimestamp();
            }
        }

        public bool AllowPacket(int packetSizeBytes)
        {
            // 0 means unlimited
            if (_bytesPerSecond <= 0) return true;

            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                double elapsedSeconds = (double)(now - _lastRefillTimestamp) / Stopwatch.Frequency;
                _lastRefillTimestamp = now;

                _tokens = Math.Min(_capacity, _tokens + (elapsedSeconds * _bytesPerSecond));

                if (_tokens >= packetSizeBytes)
                {
                    _tokens -= packetSizeBytes;
                    return true;
                }

                return false;
            }
        }
    }
}
