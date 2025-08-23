using System.Text;
using System.IO.Ports; // Add for port validation
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

        private readonly System.IO.Ports.SerialPort _port;
        private byte[] _residue;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;
                
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
        public SourceSetting SourceSetting { get; init; }

        public SerialDataStream(SourceSetting source, DataParser dataParser)
        {
            SourceSetting = source;
            Parser = dataParser;

            // Validate that the port exists on the system
            ValidatePortExists(source.PortName);

            try
            {
                _port = new System.IO.Ports.SerialPort(source.PortName, source.BaudRate, source.Parity, source.DataBits);
                _port.Encoding = Encoding.ASCII;
                _port.ReadTimeout = 100;
                _port.WriteTimeout = 100;
                _port.ReadBufferSize = 8192; // Set buffer size directly
                _port.Open();
            }
            catch (UnauthorizedAccessException ex)
            {
                // Port is already in use by another application
                if (_port != null)
                    _port.Dispose();
                throw new PortAlreadyInUseException(source.PortName, ex);
            }
            catch (ArgumentException ex)
            {
                // Invalid port name or parameters
                if (_port != null)
                    _port.Dispose();
                throw new PortNotFoundException(source.PortName, ex);
            }
            catch (InvalidOperationException ex)
            {
                // Port is already open or other operation issue
                if (_port != null)
                    _port.Dispose();
                throw new PortAlreadyInUseException(source.PortName, ex);
            }

            int ringBufferSize = Math.Max(200000, source.BaudRate / 10);
            
            ReceivedData = new RingBuffer<double>[dataParser.NumberOfChannels];
            _lastReadPositions = new int[dataParser.NumberOfChannels];
            for (int i = 0; i < dataParser.NumberOfChannels; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
                _lastReadPositions[i] = 0;
            }
            int MaxReadSize = _port.ReadBufferSize * 2;
            _readBuffer = new byte[MaxReadSize];
            _workingBuffer = new byte[MaxReadSize];
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

            // Get all available serial ports on the system
            string[] availablePorts = SerialPort.GetPortNames();
            
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
            IsRunning = true;
            _readSerialPortThread = new Thread(ReadSerialData) { IsBackground = true };
            _readSerialPortThread.Start();
        }

        public void Stop()
        {
            IsRunning = false;
            if (_readSerialPortThread != null && _readSerialPortThread.IsAlive)
            {
                _readSerialPortThread.Join(500);
            }
        }

        private Thread _readSerialPortThread;

        private void ReadSerialData()
        {
            int MinBytesThreshold = 200;
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
                        Thread.Sleep(1/SerialPortUpdateRateHz);
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Serial read error: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        public IEnumerable<double> GetNewData(int channel)
        {
            if (channel < 0 || channel >= ReceivedData.Length)
            {
                return new List<double>();
            }

            return ReceivedData[channel].GetNewData(ref _lastReadPositions[channel]);
        }

        public IEnumerable<double> GetLatestData(int channel, int sampleCount)
        {
            if (channel < 0 || channel >= ReceivedData.Length)
            {
                return new List<double>();
            }

            return ReceivedData[channel].GetLatest(sampleCount);
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

        private bool _disposed = false;

        public void Dispose()
        {
            Stop();
            try
            {
                if (_port != null && _port.IsOpen == true)
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
        public string? AudioDeviceName { get; init; }

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
            //Parity = Parity.None;
            BaudRate = 0;
            AudioDeviceName = DeviceName;
            Source = DataSource.Audio;
        }
    }
}
