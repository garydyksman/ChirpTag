using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class TxQueue
    {
        private readonly byte[][] _buffer;
        private readonly object _lock = new object();
        private int _head = 0;
        private int _tail = 0;
        private int _count = 0;

        public TxQueue(int capacity = 8)
        {
            _buffer = new byte[capacity][];
        }

        /// <summary>
        /// Enqueues a packet for TX. Safe to call from any thread.
        /// Returns false if the queue is full — packet is dropped.
        /// </summary>
        public bool Enqueue(byte[] packet)
        {
            lock (_lock)
            {
                if (_count == _buffer.Length)
                {
                    DebugLog.Write("TxQueue full — packet dropped");
                    return false;
                }

                _buffer[_tail] = packet;
                _tail = (_tail + 1) % _buffer.Length;
                _count++;
                return true;
            }
        }

        /// <summary>
        /// Dequeues the next packet. Returns null if empty.
        /// Safe to call from the heartbeat thread only.
        /// </summary>
        public byte[] Dequeue()
        {
            lock (_lock)
            {
                if (_count == 0) return null;

                byte[] packet = _buffer[_head];
                _buffer[_head] = null; // release reference
                _head = (_head + 1) % _buffer.Length;
                _count--;
                return packet;
            }
        }

        public bool IsEmpty
        {
            get { lock (_lock) { return _count == 0; } }
        }

        public int Count
        {
            get { lock (_lock) { return _count; } }
        }
    }
}
