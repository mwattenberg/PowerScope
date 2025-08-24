using System.Text;
using RJCP.IO.Ports; // Changed from System.IO.Ports to RJCP.IO.Ports
using System.Linq; // Add for LINQ methods

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Exception thrown when a serial port is not found on the system
    /// </summary>
    public class PortNotFoundException : Exception
    {
        public string PortName { get; }

        public PortNotFoundException(string portName) 
            : base($"Port '{portName}' was not found on this system")
        {
            PortName = portName;
        }

        public PortNotFoundException(string portName, Exception innerException) 
            : base($"Port '{portName}' was not found on this system", innerException)
        {
            PortName = portName;
        }
    }

    /// <summary>
    /// Exception thrown when a serial port is already in use by another application
    /// </summary>
    public class PortAlreadyInUseException : Exception
    {
        public string PortName { get; }

        public PortAlreadyInUseException(string portName) 
            : base($"Port '{portName}' is already in use by another application")
        {
            PortName = portName;
        }

        public PortAlreadyInUseException(string portName, Exception innerException) 
            : base($"Port '{portName}' is already in use by another application", innerException)
        {
            PortName = portName;
        }
    }

    public class SerialDataStream : IDisposable
    {
        private readonly SerialPortStream _port; // Changed from System.IO.Ports.SerialPort to RJCP SerialPortStream
        private byte[] _residue;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;
        private Thread _readSerialPortThread;
        private bool _disposed = false;
        public SourceSetting SourceSetting { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private int[] _lastReadPositions;
        public bool IsRunning { get; private set; }
        public DataParser Parser { get; init; }
        public int SerialPortUpdateRateHz { get; set; } = 200; // Default update rate in Hz
        public int NumberOfChannels 
        { 
            get 
            { 
                return Parser.NumberOfChannels; 
            } 
        }


        public SerialDataStream(SourceSetting source, DataParser dataParser)
        {
            SourceSetting = source;
            Parser = dataParser;

            // Validate that the port exists on the system
            ValidatePortExists(source.PortName);

            // Create RJCP SerialPortStream with enhanced configuration
            _port = new SerialPortStream(source.PortName)
            {
                BaudRate = source.BaudRate,
                DataBits = source.DataBits,
                Parity = source.Parity,
                StopBits = ConvertStopBits(1), // Default stop bits, could be made configurable
                Encoding = Encoding.ASCII,
                ReadTimeout = 1000, // Increased timeout for better reliability
                WriteTimeout = 1000,
                // Enhanced buffering for better performance
                ReadBufferSize = 2048,//small buffer to reduce latency
                WriteBufferSize = 8192,
                // Flow control settings
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            // Larger ring buffer for better performance with high-speed data
            int ringBufferSize = Math.Max(500000, source.BaudRate / 5); // Increased buffer size
            
            ReceivedData = new RingBuffer<double>[dataParser.NumberOfChannels];
            _lastReadPositions = new int[dataParser.NumberOfChannels];
            for (int i = 0; i < dataParser.NumberOfChannels; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
                _lastReadPositions[i] = 0;
            }
            
            // Larger read buffers for better performance
            int MaxReadSize = _port.ReadBufferSize / 2; // Use half of the port buffer size
            _readBuffer = new byte[MaxReadSize];
            _workingBuffer = new byte[MaxReadSize * 2]; // Working buffer can be larger

            _residue = new byte[20];
            _readSerialPortThread = new Thread(ReadSerialData) { IsBackground = true };
        }

        /// <summary>
        /// Converts integer stop bits to RJCP StopBits enum
        /// </summary>
        private StopBits ConvertStopBits(int stopBits)
        {
            if (stopBits == 1)
                return StopBits.One;
            else if (stopBits == 2)
                return StopBits.Two;
            else
                return StopBits.One;
        }

        /// <summary>
        /// Validates that the specified port exists on the system
        /// </summary>
        /// <param name="portName">The port name to validate (e.g., "COM1")</param>
        /// <exception cref="PortNotFoundException">Thrown when the port is not found</exception>
        private static void ValidatePortExists(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                string actualPortName = portName;
                if (actualPortName == null)
                    actualPortName = "null";
                throw new PortNotFoundException(actualPortName);
            }

            // Get all available serial ports on the system - use System.IO.Ports for enumeration
            string[] availablePorts = System.IO.Ports.SerialPort.GetPortNames();
            
            // Check if the requested port exists (case-insensitive comparison)
            bool portExists = false;
            foreach (string port in availablePorts)
            {
                if (string.Equals(port, portName, StringComparison.OrdinalIgnoreCase))
                {
                    portExists = true;
                    break;
                }
            }

            if (!portExists)
            {
                throw new PortNotFoundException(portName);
            }
        }

        public void Start()
        {
            try
            {
                _port.Open();
            }
            catch (UnauthorizedAccessException ex)
            {
                // Port is already in use by another application
                if (_port != null)
                    _port.Dispose();
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (ArgumentException ex)
            {
                // Invalid port name or parameters
                if (_port != null)
                    _port.Dispose();
                throw new PortNotFoundException(SourceSetting.PortName, ex);
            }
            catch (InvalidOperationException ex)
            {
                // Port is already open or other operation issue
                if (_port != null)
                    _port.Dispose();
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (System.IO.IOException ex)
            {
                // RJCP specific IO exceptions (port access issues)
                if (_port != null)
                    _port.Dispose();
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }

            IsRunning = true;
            
            _readSerialPortThread.Start();
        }

        public void Stop()
        {
            IsRunning = false;
            if (_readSerialPortThread != null && _readSerialPortThread.IsAlive)
            {
                _readSerialPortThread.Join(1000); // Increased timeout for cleaner shutdown
            }
        }

        private void ReadSerialData()
        {
            int MinBytesThreshold = 100; // Reduced threshold for better responsiveness
            int MaxReadSize = _readBuffer.Length;
            
            while (IsRunning && _port.IsOpen)
            {
                try
                {
                    int bytesAvailable = _port.BytesToRead;
                    if (bytesAvailable >= MinBytesThreshold)
                    {
                        int bytesToRead = Math.Min(bytesAvailable, MaxReadSize);
                        int bytesRead = _port.Read(_readBuffer, 0, bytesToRead);
                        TotalBits = TotalBits + bytesRead * 8;
                        if (bytesRead > 0)
                        {
                            ProcessReceivedData(_readBuffer, bytesRead, _workingBuffer);
                        }
                    }
                    else
                    {
                        // Use more efficient timing based on expected data rate
                        int sleepTime = Math.Max(1, 1000 / SerialPortUpdateRateHz);
                        Thread.Sleep(sleepTime);
                    }
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (System.IO.IOException ex)
                {
                    // RJCP specific IO exceptions
                    System.Diagnostics.Debug.WriteLine($"Serial IO error: {ex.Message}");
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Serial read error: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        /// <summary>
        /// Efficiently copies the latest data to a pre-allocated array for the specified channel.
        /// This method is optimized for plotting scenarios to avoid memory allocations.
        /// </summary>
        /// <param name="channel">Channel number</param>
        /// <param name="destination">Pre-allocated array to copy data into</param>
        /// <param name="sampleCount">Number of latest samples to copy</param>
        /// <returns>Actual number of samples copied</returns>
        public int CopyLatestDataTo(int channel, double[] destination, int sampleCount)
        {
            if (channel < 0 || channel >= ReceivedData.Length)
                return 0;

            return ReceivedData[channel].CopyLatestTo(destination, sampleCount);
        }
        
        private void ProcessReceivedData(byte[] readBuffer, int bytesRead, byte[] workingBuffer)
        {
            int totalDataLength = bytesRead;
            int workingBufferOffset = 0;

            // Handle residue from previous parsing
            if (_residue != null && _residue.Length > 0)
            {
                // Copy residue to working buffer first
                _residue.CopyTo(workingBuffer, 0);
                workingBufferOffset = _residue.Length;
                totalDataLength += _residue.Length;
            }

            // Copy new data after residue
            Array.Copy(readBuffer, 0, workingBuffer, workingBufferOffset, bytesRead);

            try
            {
                // Parse the combined data
                ParsedData parsedData = Parser.ParseData(workingBuffer.AsSpan(0, totalDataLength));

                // Update residue for next iteration
                _residue = parsedData.Residue;

                // Add parsed data to ring buffers
                if (parsedData.Data != null)
                {
                    AddDataToRingBuffers(parsedData.Data);
                }
            }
            catch (Exception ex)
            {
                // Log parsing errors but continue
                System.Diagnostics.Debug.WriteLine($"Data parsing error: {ex.Message}");
                // Clear residue on parsing error to prevent corruption propagation
                _residue = null;
            }
        }

        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = Math.Min(parsedData.Length, Parser.NumberOfChannels);

            for (int i = 0; i < channelsToProcess; i++)
            {
                if (parsedData[i] != null && parsedData[i].Length > 0)
                {
                    ReceivedData[i].AddRange(parsedData[i]);
                }
            }

            // Update total samples count (using first channel as reference)
            if (channelsToProcess > 0)
            {
                TotalSamples = TotalSamples + parsedData[0].Length;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
                
            Stop();
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.Close();
                }
                if (_port != null)
                {
                    _port.Dispose();
                }
            }
            catch (Exception) { }
            
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                    {
                        ringBuffer.Clear();
                    }
                }
            }
            
            _lastReadPositions = null;
            _residue = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public class SourceSetting
    {
        public enum DataSource { Serial, Audio };
        public string PortName { get; init; }
        public int BaudRate { get; init; }
        public int DataBits { get; init; }
        public Parity Parity { get; init; }
        public DataSource Source { get; init; }
        public string AudioDeviceName { get; init; }

        public SourceSetting(string portName, int baudRate, int dataBits, Parity parity)
        {
            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            AudioDeviceName = null;
            Source = DataSource.Serial;
        }

        public SourceSetting(string DeviceName)
        {
            PortName = "";
            BaudRate = 0;
            DataBits = 8;
            AudioDeviceName = DeviceName;
            Source = DataSource.Audio;
        }
    }
}
