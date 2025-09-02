using System.Text;
using RJCP.IO.Ports; // Changed from System.IO.Ports to RJCP.IO.Ports
using System.Linq;
using System.Diagnostics; // Add for LINQ methods

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

    public class SerialDataStream : IDataStream, IChannelConfigurable
    {
        private readonly SerialPortStream _port;
        private byte[] _residue;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;
        private Thread _readSerialPortThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage;
        public SourceSetting SourceSetting { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private int[] _lastReadPositions;
        public DataParser Parser { get; init; }
        public int SerialPortUpdateRateHz { get; set; } = 200;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        public int ChannelCount 
        { 
            get 
            { 
                return Parser.NumberOfChannels; 
            } 
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { _statusMessage = value; }
        }

        public string StreamType
        {
            get { return "Serial"; }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            set { _isConnected = value; }
        }

        public bool IsStreaming
        {
            get { return _isStreaming; }
            set { _isStreaming = value; }
        }

        public SerialDataStream(SourceSetting source, DataParser dataParser)
        {
            SourceSetting = source;
            Parser = dataParser;
            ValidatePortExists(source.PortName);
            _port = new SerialPortStream(source.PortName)
            {
                BaudRate = source.BaudRate,
                DataBits = source.DataBits,
                Parity = source.Parity,
                Encoding = Encoding.ASCII,
                ReadTimeout = 100, // Reduced for responsiveness
                WriteTimeout = 1000,
                ReadBufferSize = 65536, // Fixed 64KB - good for most use cases
                WriteBufferSize = 16384, // Fixed 16KB
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            if(source.StopBits == 1)
                _port.StopBits = StopBits.One;
            else if(source.StopBits == 2)
                _port.StopBits = StopBits.Two;
            else
                _port.StopBits = StopBits.One; // Force stop bits to 1 to avoid compatibility issues

            int ringBufferSize = Math.Max(500000, source.BaudRate / 5);
            ReceivedData = new RingBuffer<double>[dataParser.NumberOfChannels];

            _lastReadPositions = new int[dataParser.NumberOfChannels];
            for (int i = 0; i < dataParser.NumberOfChannels; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
                _lastReadPositions[i] = 0;
            }

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[dataParser.NumberOfChannels];
            _channelFilters = new IDigitalFilter[dataParser.NumberOfChannels];

            // Larger buffers for better performance
            _readBuffer = new byte[16384]; // Fixed 16KB chunks
            _workingBuffer = new byte[32768]; // Fixed 32KB working space
            _residue = new byte[1024]; // Fixed 1KB residue
            _isConnected = false;
            _isStreaming = false;
            _statusMessage = "Disconnected";
        }

        #region IChannelConfigurable Implementation

        public void SetChannelSetting(int channelIndex, ChannelSettings settings)
        {
            if (channelIndex < 0 || channelIndex >= ChannelCount)
                return;

            lock (_channelConfigLock)
            {
                _channelSettings[channelIndex] = settings;
                
                // Update filter reference and reset if filter type changed
                var newFilter = settings?.Filter;
                if (_channelFilters[channelIndex] != newFilter)
                {
                    _channelFilters[channelIndex] = newFilter;
                    _channelFilters[channelIndex]?.Reset();
                }
            }
        }

        public void UpdateChannelSettings(IReadOnlyList<ChannelSettings> channelSettings)
        {
            if (channelSettings == null)
                return;

            lock (_channelConfigLock)
            {
                for (int i = 0; i < Math.Min(ChannelCount, channelSettings.Count); i++)
                {
                    SetChannelSetting(i, channelSettings[i]);
                }
            }
        }

        public void ResetChannelFilters()
        {
            lock (_channelConfigLock)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    _channelFilters[i]?.Reset();
                }
            }
        }

        #endregion

        #region Data Processing

        private double ApplyChannelProcessing(int channel, double rawSample)
        {
            // Get channel settings safely
            ChannelSettings settings;
            IDigitalFilter filter;
            
            lock (_channelConfigLock)
            {
                settings = _channelSettings[channel];
                filter = _channelFilters[channel];
            }
            
            if (settings == null)
                return rawSample;
            
            // Apply gain and offset
            double processed = settings.Gain * (rawSample + settings.Offset);
            
            // Apply filter if configured
            if (filter != null)
            {
                processed = filter.Filter(processed);
            }
            
            return processed;
        }

        #endregion

        private static void ValidatePortExists(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                string actualPortName = portName;
                if (actualPortName == null)
                    actualPortName = "null";
                throw new PortNotFoundException(actualPortName);
            }
            string[] availablePorts = System.IO.Ports.SerialPort.GetPortNames();
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

        public void Connect()
        {
            try
            {
                _port.Open();
                _isConnected = true;
                _statusMessage = "Connected";
            }
            catch (UnauthorizedAccessException ex)
            {
                if (_port != null)
                    _port.Dispose();
                _statusMessage = "Port already in use";
                _isConnected = false;
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (ArgumentException ex)
            {
                if (_port != null)
                    _port.Dispose();
                _statusMessage = "Port not found";
                _isConnected = false;
                throw new PortNotFoundException(SourceSetting.PortName, ex);
            }
            catch (InvalidOperationException ex)
            {
                if (_port != null)
                    _port.Dispose();
                _statusMessage = "Port already in use";
                _isConnected = false;
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (System.IO.IOException ex)
            {
                if (_port != null)
                    _port.Dispose();
                _statusMessage = "Port already in use";
                _isConnected = false;
                throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            if (_port != null && _port.IsOpen)
                _port.Close();
            _isConnected = false;
            _statusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
                return;
           
            _isStreaming = true;
            
            // Reset filters when starting streaming
            ResetChannelFilters();
            
            //Not sure if I need this, but just in case
            if ( _readSerialPortThread != null && _readSerialPortThread.IsAlive)
            {
                _isStreaming = false;
                _readSerialPortThread.Join(1000);
            }

            _readSerialPortThread = new Thread(ReadSerialData) { IsBackground = true };
            _readSerialPortThread.Start();

            _statusMessage = "Streaming";
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;
            _isStreaming = false;
            if (_readSerialPortThread != null && _readSerialPortThread.IsAlive)
                _readSerialPortThread.Join(1000);
            _statusMessage = "Stopped";
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0)
                return 0;
            if (channel >= ReceivedData.Length)
                return 0;
            return ReceivedData[channel].CopyLatestTo(destination, n);
        }

        void clearData()
        {
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                        ringBuffer.Clear();
                }
            }
            TotalSamples = 0;
            TotalBits = 0;
            _lastReadPositions = new int[Parser.NumberOfChannels];
            
            // Reset filters when clearing data
            ResetChannelFilters();
        }

        private void ReadSerialData()
        {
            int minBytesThreshold = 64;
            int maxReadSize = _readBuffer.Length;

            while (_isStreaming && _port.IsOpen)
            {
                try
                {
                    int bytesAvailable = _port.BytesToRead;
                    if (bytesAvailable >= minBytesThreshold)
                    {
                        int bytesToRead = Math.Min(bytesAvailable, maxReadSize);
                        int bytesRead = _port.Read(_readBuffer, 0, bytesToRead);

                        if (bytesRead > 0)
                        {
                            TotalBits += bytesRead * 8;
                            ProcessReceivedData(_readBuffer, bytesRead, _workingBuffer);
                        }
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, 1000 / SerialPortUpdateRateHz));
                    }
                }
                catch (TimeoutException) { continue; }
                catch (InvalidOperationException) { break; }
                catch (System.IO.IOException) { Thread.Sleep(10); }
                catch (Exception) { Thread.Sleep(10); }
            }
        }

        private void ProcessReceivedData(byte[] readBuffer, int bytesRead, byte[] workingBuffer)
        {
            int totalDataLength = bytesRead;
            int workingBufferOffset = 0;
            if (_residue != null && _residue.Length > 0)
            {
                _residue.CopyTo(workingBuffer, 0);
                workingBufferOffset = _residue.Length;
                totalDataLength += _residue.Length;
            }
            Array.Copy(readBuffer, 0, workingBuffer, workingBufferOffset, bytesRead);
            try
            {
                ParsedData parsedData = Parser.ParseData(workingBuffer.AsSpan(0, totalDataLength));
                _residue = parsedData.Residue;
                if (parsedData.Data != null)
                {
                    AddDataToRingBuffers(parsedData.Data);
                }
            }
            catch (Exception)
            {
                _residue = null;
            }
        }

        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = parsedData.Length;
            if (channelsToProcess > Parser.NumberOfChannels)
                channelsToProcess = Parser.NumberOfChannels;
            
            for (int channel = 0; channel < channelsToProcess; channel++)
            {
                if (parsedData[channel] != null && parsedData[channel].Length > 0)
                {
                    // Apply channel processing (gain, offset, filtering) to each sample
                    double[] processedSamples = new double[parsedData[channel].Length];
                    for (int sample = 0; sample < parsedData[channel].Length; sample++)
                    {
                        processedSamples[sample] = ApplyChannelProcessing(channel, parsedData[channel][sample]);
                    }
                    
                    // Add processed data to ring buffer
                    ReceivedData[channel].AddRange(processedSamples);
                }
            }
            
            if (channelsToProcess > 0)
            {
                TotalSamples = TotalSamples + parsedData[0].Length;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if(_isStreaming)
                StopStreaming();
            if (_port != null && _port.IsOpen)
                _port.Close();
            if (_port != null)
                _port.Dispose();
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                        ringBuffer.Clear();
                }
            }
            _lastReadPositions = null;
            _residue = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        void IDataStream.clearData()
        {
            clearData();
        }
    }

    public class SourceSetting
    {
        public enum DataSource { Serial, Audio };
        public string PortName { get; init; }
        public int BaudRate { get; init; }
        public int DataBits { get; init; }
        public int StopBits { get; init; }
        public Parity Parity { get; init; }
        public DataSource Source { get; init; }
        public string AudioDeviceName { get; init; }

        public SourceSetting(string portName, int baudRate, int dataBits, int stopBits, Parity parity)
        {
            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
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
