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

    public class SerialDataStream : IDataStream
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
                DriverInQueue = 4096,
                BaudRate = source.BaudRate,
                DataBits = source.DataBits,
                Parity = source.Parity,
                Encoding = Encoding.ASCII,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                ReadBufferSize = 8 * 2048,
                WriteBufferSize = 8192,
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
            int MaxReadSize = _port.ReadBufferSize / 2;
            _readBuffer = new byte[MaxReadSize];
            _workingBuffer = new byte[MaxReadSize * 2];
            _residue = new byte[20];
            _isConnected = false;
            _isStreaming = false;
            _statusMessage = "Disconnected";
        }

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
        }

        private void ReadSerialData()
        {
            int MinBytesThreshold = 100;
            int MaxReadSize = _readBuffer.Length;
            while (_isStreaming && _port.IsOpen)
            {
                try
                {
                    int bytesAvailable = _port.BytesToRead;
                    if (bytesAvailable >= MinBytesThreshold)
                    {
                        int bytesToRead = MaxReadSize;
                        if (bytesAvailable < MaxReadSize)
                            bytesToRead = bytesAvailable;
                        int bytesRead = _port.Read(_readBuffer, 0, bytesToRead);
                        TotalBits = TotalBits + bytesRead * 8;
                        if (bytesRead > 0)
                        {
                            ProcessReceivedData(_readBuffer, bytesRead, _workingBuffer);
                        }
                    }
                    //else
                    //{
                        int sleepTime = 1000 / SerialPortUpdateRateHz;
                        if (sleepTime < 1)
                            sleepTime = 1;
                        Thread.Sleep(sleepTime);
                    //}
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (System.IO.IOException)
                {
                    Thread.Sleep(10);
                }
                catch (Exception)
                {
                    Thread.Sleep(10);
                }
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
            for (int i = 0; i < channelsToProcess; i++)
            {
                if (parsedData[i] != null)
                {
                    if (parsedData[i].Length > 0)
                    {
                        ReceivedData[i].AddRange(parsedData[i]);
                    }
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
