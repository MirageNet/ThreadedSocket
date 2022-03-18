// MIT License
// Copyright (c) 2018 Alexander Nikitin, Stanislav Denisov
// original source: https://github.com/nxrighthere/NetStack/blob/9a57bfd567e589426eb8d8efd9bccccb13a3958f/Source/NetStack.Threading/ConcurrentBuffer.cs

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetStack.Threading
{
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public sealed class ConcurrentBuffer<T> where T : class // where class so that size of T will always be 8 bytes (pointer to class)
    {
        [FieldOffset(0)]
        private readonly Cell[] _buffer;
        [FieldOffset(8)]
        private readonly int _bufferMask;
        [FieldOffset(64)]
        private int _enqueuePosition;
        [FieldOffset(128)]
        private int _dequeuePosition;

        public int Count
        {
            get
            {
                return _enqueuePosition - _dequeuePosition;
            }
        }

        public ConcurrentBuffer(int bufferSize)
        {
            if (bufferSize < 2)
                throw new ArgumentException("Buffer size should be greater than or equal to two");

            if ((bufferSize & (bufferSize - 1)) != 0)
                throw new ArgumentException("Buffer size should be a power of two");

            _bufferMask = bufferSize - 1;
            _buffer = new Cell[bufferSize];

            for (int i = 0; i < bufferSize; i++)
            {
                _buffer[i] = new Cell(i, null);
            }

            _enqueuePosition = 0;
            _dequeuePosition = 0;
        }

        public void Enqueue(T item)
        {
            while (true)
            {
                if (TryEnqueue(item))
                    break;

                Thread.SpinWait(1);
            }
        }

        public bool TryEnqueue(T item)
        {
            do
            {
                Cell[] buffer = _buffer;
                int position = _enqueuePosition;
                int index = position & _bufferMask;
                Cell cell = buffer[index];

                if (cell.Sequence == position && Interlocked.CompareExchange(ref _enqueuePosition, position + 1, position) == position)
                {
                    buffer[index].Element = item;

#if NET_4_6 || NET_STANDARD_2_0
                    Volatile.Write(ref buffer[index].Sequence, position + 1);
#else
                        Thread.MemoryBarrier();
                        buffer[index].Sequence = position + 1;
#endif

                    return true;
                }

                if (cell.Sequence < position)
                    return false;
            }

            while (true);
        }

        public T Dequeue()
        {
            while (true)
            {
                T element;

                if (TryDequeue(out element))
                    return element;
            }
        }

        public bool TryDequeue(out T result)
        {
            do
            {
                Cell[] buffer = _buffer;
                int bufferMask = _bufferMask;
                int position = _dequeuePosition;
                int index = position & bufferMask;
                Cell cell = buffer[index];

                if (cell.Sequence == position + 1 && Interlocked.CompareExchange(ref _dequeuePosition, position + 1, position) == position)
                {
                    result = cell.Element;
                    buffer[index].Element = null;

#if NET_4_6 || NET_STANDARD_2_0
                    Volatile.Write(ref buffer[index].Sequence, position + bufferMask + 1);
#else
                        Thread.MemoryBarrier();
                        buffer[index].Sequence = position + bufferMask + 1;
#endif

                    return true;
                }

                if (cell.Sequence < position + 1)
                {
                    result = null;

                    return false;
                }
            }

            while (true);
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct Cell
        {
            [FieldOffset(0)]
            public int Sequence;
            [FieldOffset(8)]
            public T Element;

            public Cell(int sequence, T element)
            {
                Sequence = sequence;
                Element = element;
            }
        }
    }
}
