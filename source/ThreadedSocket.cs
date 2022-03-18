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
        readonly ConcurrentBuffer _receiveBuffer;
        readonly ConcurrentBuffer _bufferPool;
        Thread _receiveThread;
        volatile bool closed;

        Packet next;

        public MultiThreadSocket(ISocket socket, int bufferSize)
        {
            _socket = socket;
            _receiveBuffer = new ConcurrentBuffer(bufferSize);
            _bufferPool = new ConcurrentBuffer(bufferSize);
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

            bool hasPacket = _receiveBuffer.TryDequeue(out object nextObj);
            if (hasPacket)
                next = (Packet)nextObj;
            return hasPacket;
        }

        public int Receive(byte[] buffer, out IEndPoint endPoint)
        {
            if (closed) throw new InvalidOperationException("Socket Closed");

            endPoint = next.endPoint;
            int size = next.size;
            Buffer.BlockCopy(next.buffer, 0, buffer, 0, size);

            // recycle buffer, if Try fails just leave buffer for GC
            _bufferPool.TryEnqueue(next.buffer);

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
            bool hasBuffer = _bufferPool.TryDequeue(out object objBuffer);
            byte[] buffer;
            if (hasBuffer)
                buffer = (byte[])objBuffer;
            else
                // todo get real MTU size here
                buffer = new byte[1300];

            int size = _socket.Receive(buffer, out IEndPoint endPoint);
#if UNITY_ASSERTIONS
            Debug.Assert(size > 0, "Size must be non-negative");
#endif
            var packet = new Packet
            {
                buffer = buffer,
                size = size,
                // have to create copy so that endPoint returned from will be reused
                // todo stop allocation
                endPoint = endPoint.CreateCopy(),
            };

            if (!_receiveBuffer.TryEnqueue(packet))
            {
                Debug.LogWarning("Receive buffer full, increase buffer size to avoid packet loss");
                // block thread till receive buffer has space
                _receiveBuffer.Enqueue(packet);
            }
        }

        class Packet
        {
            public byte[] buffer;
            public int size;
            public IEndPoint endPoint;
        }
    }
}
