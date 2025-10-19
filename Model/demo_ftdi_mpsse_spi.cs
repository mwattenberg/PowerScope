//using System;
//using System.Runtime.InteropServices;
//using System.Text;

//namespace FTDIMPSSEExample
//{
//    public class FTDIMPSSEManager
//    {
//        // FTDI D2XX DLL imports
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_Open(uint deviceNumber, ref IntPtr ftHandle);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_Close(IntPtr ftHandle);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_SetBitMode(IntPtr ftHandle, byte mask, byte mode);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_SetUSBParameters(IntPtr ftHandle, uint inTransferSize, uint outTransferSize);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_SetLatencyTimer(IntPtr ftHandle, byte latency);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_Write(IntPtr ftHandle, byte[] buffer, uint bytesToWrite, ref uint bytesWritten);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_Read(IntPtr ftHandle, byte[] buffer, uint bytesToRead, ref uint bytesRead);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_Purge(IntPtr ftHandle, uint mask);
        
//        [DllImport("ftd2xx.dll")]
//        private static extern uint FT_GetQueueStatus(IntPtr ftHandle, ref uint rxBytes);

//        // Constants
//        private const uint FT_OK = 0;
//        private const byte FT_BITMODE_RESET = 0x00;
//        private const byte FT_BITMODE_MPSSE = 0x02;
//        private const uint FT_PURGE_RX = 1;
//        private const uint FT_PURGE_TX = 2;

//        // MPSSE Commands
//        private const byte MPSSE_WRITE_NEG = 0x01;  // Write on negative clock edge
//        private const byte MPSSE_BITMODE = 0x02;     // Bit mode
//        private const byte MPSSE_READ_NEG = 0x04;    // Read on negative clock edge
//        private const byte MPSSE_LSB_FIRST = 0x08;   // LSB first
//        private const byte MPSSE_DO_WRITE = 0x10;    // Write TDI/DO
//        private const byte MPSSE_DO_READ = 0x20;     // Read TDO/DI
//        private const byte MPSSE_WRITE_TMS = 0x40;   // Write TMS/CS

//        // Common MPSSE commands
//        private const byte MPSSE_CMD_SET_DATA_BITS_LOW = 0x80;
//        private const byte MPSSE_CMD_SET_DATA_BITS_HIGH = 0x82;
//        private const byte MPSSE_CMD_GET_DATA_BITS_LOW = 0x81;
//        private const byte MPSSE_CMD_GET_DATA_BITS_HIGH = 0x83;
//        private const byte MPSSE_CMD_SET_CLOCK_DIVISOR = 0x86;
//        private const byte MPSSE_CMD_DISABLE_CLOCK_DIVIDE_BY_5 = 0x8A;
//        private const byte MPSSE_CMD_ENABLE_3_PHASE_CLOCK = 0x8C;

//        private IntPtr ftHandle = IntPtr.Zero;

//        public bool OpenDevice(uint deviceIndex = 0)
//        {
//            uint status = FT_Open(deviceIndex, ref ftHandle);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to open FTDI device. Error: {status}");
//                return false;
//            }
            
//            Console.WriteLine("FTDI device opened successfully.");
//            return true;
//        }

//        public bool ConfigureMPSSE()
//        {
//            if (ftHandle == IntPtr.Zero)
//            {
//                Console.WriteLine("Device not opened.");
//                return false;
//            }

//            // Reset the device
//            uint status = FT_SetBitMode(ftHandle, 0x00, FT_BITMODE_RESET);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to reset device. Error: {status}");
//                return false;
//            }
//            System.Threading.Thread.Sleep(50);

//            // Set MPSSE mode
//            status = FT_SetBitMode(ftHandle, 0x00, FT_BITMODE_MPSSE);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to set MPSSE mode. Error: {status}");
//                return false;
//            }
//            System.Threading.Thread.Sleep(50);

//            // Configure USB transfer sizes
//            status = FT_SetUSBParameters(ftHandle, 65536, 65536);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to set USB parameters. Error: {status}");
//                return false;
//            }

//            // Set latency timer to 1ms
//            status = FT_SetLatencyTimer(ftHandle, 1);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to set latency timer. Error: {status}");
//                return false;
//            }

//            // Purge buffers
//            status = FT_Purge(ftHandle, FT_PURGE_RX | FT_PURGE_TX);
//            if (status != FT_OK)
//            {
//                Console.WriteLine($"Failed to purge buffers. Error: {status}");
//                return false;
//            }

//            Console.WriteLine("MPSSE mode configured successfully.");
//            return true;
//        }

//        public bool ConfigureSPI(uint clockFrequency = 1000000)
//        {
//            if (ftHandle == IntPtr.Zero)
//            {
//                Console.WriteLine("Device not opened.");
//                return false;
//            }

//            byte[] buffer = new byte[20];
//            int idx = 0;

//            // Disable clock divide-by-5 for higher frequencies
//            buffer[idx++] = MPSSE_CMD_DISABLE_CLOCK_DIVIDE_BY_5;

//            // Disable adaptive clocking
//            buffer[idx++] = 0x97;

//            // Enable 3-phase clocking for better reliability
//            buffer[idx++] = MPSSE_CMD_ENABLE_3_PHASE_CLOCK;

//            // Set clock divisor
//            // Clock = 60MHz / ((1 + divisor) * 2)
//            uint divisor = (30000000 / clockFrequency) - 1;
//            buffer[idx++] = MPSSE_CMD_SET_CLOCK_DIVISOR;
//            buffer[idx++] = (byte)(divisor & 0xFF);
//            buffer[idx++] = (byte)((divisor >> 8) & 0xFF);

//            // Configure GPIO pins (lower byte)
//            // Set initial states: SCK=0, MOSI=0, CS=1 (active low)
//            // Direction: SCK=out, MOSI=out, MISO=in, CS=out
//            // AD0=SCK, AD1=MOSI, AD2=MISO, AD3=CS
//            byte initialState = 0x08;  // CS high (bit 3)
//            byte direction = 0x0B;     // SCK, MOSI, CS as outputs (bits 0,1,3)
            
//            buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
//            buffer[idx++] = initialState;
//            buffer[idx++] = direction;

//            // Configure upper GPIO pins (if needed)
//            buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_HIGH;
//            buffer[idx++] = 0x00;
//            buffer[idx++] = 0x00;

//            uint bytesWritten = 0;
//            uint status = FT_Write(ftHandle, buffer, (uint)idx, ref bytesWritten);
            
//            if (status != FT_OK || bytesWritten != idx)
//            {
//                Console.WriteLine($"Failed to configure SPI. Error: {status}");
//                return false;
//            }

//            Console.WriteLine($"SPI configured successfully at {clockFrequency}Hz.");
//            return true;
//        }

//        public bool SPIWrite(byte[] data, bool assertCS = true, bool releaseCS = true)
//        {
//            if (ftHandle == IntPtr.Zero || data == null || data.Length == 0)
//                return false;

//            int bufferSize = data.Length + 20;
//            byte[] buffer = new byte[bufferSize];
//            int idx = 0;

//            // Assert CS (pull low)
//            if (assertCS)
//            {
//                buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
//                buffer[idx++] = 0x00;  // CS low
//                buffer[idx++] = 0x0B;  // Direction
//            }

//            // Write data (clock data out on falling edge, MSB first)
//            buffer[idx++] = (byte)(MPSSE_DO_WRITE | MPSSE_WRITE_NEG);
//            buffer[idx++] = (byte)((data.Length - 1) & 0xFF);       // Length low byte
//            buffer[idx++] = (byte)(((data.Length - 1) >> 8) & 0xFF); // Length high byte
            
//            Array.Copy(data, 0, buffer, idx, data.Length);
//            idx += data.Length;

//            // Release CS (pull high)
//            if (releaseCS)
//            {
//                buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
//                buffer[idx++] = 0x08;  // CS high
//                buffer[idx++] = 0x0B;  // Direction
//            }

//            uint bytesWritten = 0;
//            uint status = FT_Write(ftHandle, buffer, (uint)idx, ref bytesWritten);
            
//            return status == FT_OK && bytesWritten == idx;
//        }

//        public byte[] SPIRead(int numBytes)
//        {
//            if (ftHandle == IntPtr.Zero || numBytes <= 0)
//                return null;

//            byte[] cmdBuffer = new byte[10];
//            int idx = 0;

//            // Assert CS
//            cmdBuffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
//            cmdBuffer[idx++] = 0x00;
//            cmdBuffer[idx++] = 0x0B;

//            // Read data command
//            cmdBuffer[idx++] = (byte)(MPSSE_DO_READ | MPSSE_READ_NEG);
//            cmdBuffer[idx++] = (byte)((numBytes - 1) & 0xFF);
//            cmdBuffer[idx++] = (byte)(((numBytes - 1) >> 8) & 0xFF);

//            // Release CS
//            cmdBuffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
//            cmdBuffer[idx++] = 0x08;
//            cmdBuffer[idx++] = 0x0B;

//            uint bytesWritten = 0;
//            FT_Write(ftHandle, cmdBuffer, (uint)idx, ref bytesWritten);

//            // Wait for data
//            System.Threading.Thread.Sleep(10);

//            byte[] readBuffer = new byte[numBytes];
//            uint bytesRead = 0;
//            FT_Read(ftHandle, readBuffer, (uint)numBytes, ref bytesRead);

//            if (bytesRead == numBytes)
//                return readBuffer;

//            return null;
//        }

//        public void Close()
//        {
//            if (ftHandle != IntPtr.Zero)
//            {
//                FT_Close(ftHandle);
//                ftHandle = IntPtr.Zero;
//                Console.WriteLine("FTDI device closed.");
//            }
//        }

//        // Example usage
//        public static void Main(string[] args)
//        {
//            FTDIMPSSEManager ftdi = new FTDIMPSSEManager();

//            try
//            {
//                // Open device
//                if (!ftdi.OpenDevice(0))
//                    return;

//                // Configure MPSSE mode
//                if (!ftdi.ConfigureMPSSE())
//                    return;

//                // Configure SPI at 1MHz
//                if (!ftdi.ConfigureSPI(1000000))
//                    return;

//                // Example: Write some data via SPI
//                byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
//                Console.WriteLine("Writing test data...");
//                if (ftdi.SPIWrite(testData))
//                {
//                    Console.WriteLine("Data written successfully.");
//                }

//                // Example: Read data
//                Console.WriteLine("Reading data...");
//                byte[] readData = ftdi.SPIRead(4);
//                if (readData != null)
//                {
//                    Console.WriteLine($"Read {readData.Length} bytes: " + 
//                        BitConverter.ToString(readData));
//                }
//            }
//            finally
//            {
//                ftdi.Close();
//            }

//            Console.WriteLine("Press any key to exit...");
//            Console.ReadKey();
//        }
//    }
//}
