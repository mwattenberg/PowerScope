using System.Text;

namespace SerialPlotDN_WPF.Model
{
    public class DataStream : IDisposable
    {

        private readonly System.IO.Ports.SerialPort _port;

        private byte[] _residue;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;

        public enum Baudrates { Baud_9600 = 9600, Baud_19200 = 19200, Baud_57600 = 57600, Baud_115200 = 115200, Baud_256000 = 256000 };
        public int TotalSamples { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private int[] _lastReadPositions;
        public bool IsRunning { get; private set; }
        public DataParser Parser { get; init; }
        public int SerialPortUpdateRateHz { get; set; } = 1000; // Default update rate in Hz

        public DataStream(SourceSetting source, DataParser dataParser)
        {
            Parser = dataParser;
            _port = new System.IO.Ports.SerialPort(source.PortName, source.BaudRate, System.IO.Ports.Parity.None, 8);
            _port.Encoding = Encoding.ASCII;
            _port.ReadTimeout = 100;
            _port.WriteTimeout = 100;
            _port.ReadBufferSize = 8192; // Set buffer size directly
            _port.Open();
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
                return Enumerable.Empty<double>();

            return ReceivedData[channel].GetNewData(ref _lastReadPositions[channel]);
        }

        public IEnumerable<double> GetLatestData(int channel, int sampleCount)
        {
            if (channel < 0 || channel >= ReceivedData.Length)
                return Enumerable.Empty<double>();

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
                TotalSamples = ReceivedData[0].Count;
            }
        }

        private bool _disposed = false;

        public void Dispose()
        {
            Stop();
            try
            {
                if (_port?.IsOpen == true)
                {
                    _port.Close();
                }
                _port?.Dispose();
            }
            catch (Exception) { }
            if (ReceivedData != null)
            {
                foreach (var ringBuffer in ReceivedData)
                {
                    ringBuffer?.Clear();
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
        //public Parity Parity { get; init; }
        public DataSource Source { get; init; }
        public string? AudioDeviceName { get; init; }

        public SourceSetting(string portName, int baudRate, int dataBits)
        {

            PortName = portName;
            BaudRate = baudRate;
            //Parity = parity;
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
