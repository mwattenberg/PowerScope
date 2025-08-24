using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialPlotDN_WPF.Model
{
    public interface IDataStream:IDisposable
    {
        //Status message to be displayed in the UI, e.g. "Connected", "Disconnected", "Error: Port not found", etc.
        string StatusMessage { get; set; }
        //Indicates the type of data stream, e.g. "Serial", "USB", "Audio", etc.
        string StreamType { get; }
        //True when connected to the data source
        bool IsConnected { get; set; }
        //True when actively sampling from the data source
        bool IsStreaming { get; set; }
        //Connect to the data source, i.e. open serial port, USB , audio, etc.
        void Connect();
        //disconnect from the data source, i.e. close serial port, USB , audio, etc.
        void Disconnect();
        //Starts sampling from the data source
        void StartStreaming();
        //Stops sampling from the data source
        void StopStreaming();
        //Returns the latest n samples from the specified channel
        int CopyLatestTo(int channel, double[] destination, int n);

    }
}
