namespace PowerScope.Model
{
    public class RingBuffer<T>
    {
        private T[] _buffer;
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

        public int Capacity
        {
            get { return _capacity; }
        }

        public int Count
        {
            get { return _count; }
        }

        public bool IsFull
        {
            get { return _count == _capacity; }
        }

        public bool IsEmpty
        {
            get { return _count == 0; }
        }

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
            lock (_lock)
            {
                foreach (T item in items)
                {
                    _buffer[_head] = item;
                    _head = (_head + 1) % _capacity;

                    if (_count < _capacity)
                    {
                        _count++;
                    }
                    else
                    {
                        _tail = (_tail + 1) % _capacity;
                    }
                }
            }
        }

        /// <summary>
        /// Adds the first <paramref name="count"/> elements of <paramref name="source"/> without
        /// allocating an enumerator or a right-sized copy. Used by the zero-allocation acquisition
        /// path, where the producer fills a reusable scratch buffer larger than the live sample run.
        /// </summary>
        /// <param name="source">Backing array holding the samples to add.</param>
        /// <param name="count">Number of leading elements of <paramref name="source"/> to add.</param>
        public void AddRange(T[] source, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _buffer[_head] = source[i];
                    _head = (_head + 1) % _capacity;

                    if (_count < _capacity)
                    {
                        _count++;
                    }
                    else
                    {
                        _tail = (_tail + 1) % _capacity;
                    }
                }
            }
        }

        /// <summary>
        /// Efficiently copies the latest data to a pre-allocated destination array.
        /// This method avoids memory allocations and is optimized for plotting scenarios.
        /// </summary>
        /// <param name="destination">Pre-allocated array to copy data into</param>
        /// <param name="requestedCount">Number of latest elements to copy</param>
        /// <returns>Actual number of elements copied</returns>
        public int CopyLatestTo(T[] destination, int requestedCount)
        {
            lock (_lock)
            {
                int actualCount = Math.Min(Math.Min(requestedCount, _count), destination.Length);
                
                if (actualCount == 0)
                    return 0;

                // Calculate starting position for the latest data
                int startIndex = (_head - actualCount + _capacity) % _capacity;
                
                // Handle the case where data wraps around the circular buffer
                if (startIndex + actualCount <= _capacity)
                {
                    // Data is contiguous, single copy operation
                    Array.Copy(_buffer, startIndex, destination, 0, actualCount);
                }
                else
                {
                    // Data wraps around, need two copy operations
                    int firstPartLength = _capacity - startIndex;
                    int secondPartLength = actualCount - firstPartLength;
                    
                    // Copy first part (from startIndex to end of buffer)
                    Array.Copy(_buffer, startIndex, destination, 0, firstPartLength);
                    
                    // Copy second part (from beginning of buffer)
                    Array.Copy(_buffer, 0, destination, firstPartLength, secondPartLength);
                }
                
                return actualCount;
            }
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

    }
}
