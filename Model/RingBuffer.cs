using System;
using System.Collections;
using System.Collections.Generic;

namespace SerialPlotDN_WPF.Model
{
    public class RingBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        private int _head = 0;
        private int _tail = 0;
        private int _count = 0;
        private readonly object _lock = new object();

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be positive", nameof(capacity));

            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public int Capacity => _capacity;
        public int Count => _count;
        public bool IsFull => _count == _capacity;
        public bool IsEmpty => _count == 0;

        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _capacity;

                if (_count < _capacity)
                {
                    _count++;
                }
                else
                {
                    // Buffer is full, move tail forward (overwrite oldest)
                    _tail = (_tail + 1) % _capacity;
                }
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        public IEnumerable<T> GetLatest(int count)
        {
            if (count <= 0) yield break;

            lock (_lock)
            {
                int actualCount = Math.Min(count, _count);
                for (int i = 0; i < actualCount; i++)
                {
                    int index = (_head - actualCount + i + _capacity) % _capacity;
                    yield return _buffer[index];
                }
            }
        }

        public IEnumerable<T> GetNewData(ref int lastReadPosition)
        {
            var result = new List<T>();
            
            lock (_lock)
            {
                if (lastReadPosition == _head) 
                    return result;

                while (lastReadPosition != _head)
                {
                    result.Add(_buffer[lastReadPosition]);
                    lastReadPosition = (lastReadPosition + 1) % _capacity;
                }
            }
            
            return result;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _tail = 0;
                _count = 0;
                Array.Clear(_buffer, 0, _capacity);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                for (int i = 0; i < _count; i++)
                {
                    int index = (_tail + i) % _capacity;
                    yield return _buffer[index];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
