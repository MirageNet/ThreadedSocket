using System;
using System.Threading;
using Mirage.SocketLayer;
using NetStack.Threading;
using UnityEngine;

namespace Mirage.ThreadedSocket
{
    public class MultiThreadSocket : ISocket
    {
        readonly ISocket _socket;
        readonly int maxPacketSize;
        readonly ConcurrentBuffer<Packet> _receiveBuffer;
        /// <summary>Pool for packets that have been dequeued from receive buffer</summary>
        readonly ConcurrentBuffer<Packet> _bufferPool;
        Thread _receiveThread;
        volatile bool closed;

        Packet next;

        public MultiThreadSocket(ISocket socket, int bufferSize, int maxPacketSize)
        {
            _socket = socket;
            this.maxPacketSize = maxPacketSize;
            _receiveBuffer = new ConcurrentBuffer<Packet>(bufferSize);
            _bufferPool = new ConcurrentBuffer<Packet>(bufferSize);
        }

        public void Bind(IEndPoint endPoint)
        {
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Name = "MultiThreadSocket";
            _receiveThread.Start();
            _socket.Bind(endPoint);
        }

        public void Close()
        {
            closed = true;
        }

        public void Connect(IEndPoint endPoint)
        {
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
            _socket.Connect(endPoint);
        }

        public void Send(IEndPoint endPoint, byte[] packet, int length)
        {
            if (closed) throw new InvalidOperationException("Socket Closed");

            _socket.Send(endPoint, packet, length);
        }

        public bool Poll()
        {
            if (closed) throw new InvalidOperationException("Socket Closed");

#if UNITY_ASSERTIONS
            // todo maybe just return true if next is not null?
            Debug.Assert(next == null, "Should not be calling poll while Next is not null");
#endif
            return _receiveBuffer.TryDequeue(out next);
        }

        public int Receive(byte[] buffer, out IEndPoint endPoint)
        {
            if (closed) throw new InvalidOperationException("Socket Closed");

            endPoint = next.endPoint;
            int size = next.size;
            Buffer.BlockCopy(next.buffer, 0, buffer, 0, size);

            // recycle buffer, if Try fails just leave buffer for GC
            _bufferPool.TryEnqueue(next);

            // clear reference, next will be set again by poll
            next = null;
            return size;
        }

        void ReceiveLoop()
        {
            while (true)
            {
                if (closed)
                {
                    _socket.Close();
                    return;
                }

                // if poll is true, receive next message
                // else sleep for 1ms
                if (_socket.Poll())
                {
                    ReceiveOne();
                }
                else
                {
                    // sleep in else, so that if there are 2 packet it will receive both before sleeping
                    Thread.Sleep(1);
                }
            }
        }

        void ReceiveOne()
        {
            bool hasBuffer = _bufferPool.TryDequeue(out Packet packet);
            if (!hasBuffer)
                packet = new Packet(maxPacketSize);

            packet.size = _socket.Receive(packet.buffer, out IEndPoint endPoint);
#if UNITY_ASSERTIONS
            Debug.Assert(packet.size > 0, "Size must be non-negative");
#endif
            packet.endPoint = endPoint.CreateCopy();

            if (!_receiveBuffer.TryEnqueue(packet))
            {
#if DEBUG
                // todo dont always log here, it will cause performnace problems if buffer max is reached
                Debug.LogWarning("Receive buffer full, increase buffer size to avoid packet loss");
#endif

                // block thread till receive buffer has space
                _receiveBuffer.Enqueue(packet);
            }
        }

        class Packet
        {
            public readonly byte[] buffer;
            public int size;
            public IEndPoint endPoint;

            public Packet(int maxPacketSize)
            {
                buffer = new byte[maxPacketSize];
            }
        }
    }
}
