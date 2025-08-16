using SerialPlotDN_WPF.Model;
using System.Collections.ObjectModel;


public class DataStreamManager
{
    private readonly Dictionary<DataStreamViewModel, DataStream> _dataStreams = new();
    public ObservableCollection<DataStreamViewModel> StreamViewModels { get; } = new();

    public DataStreamViewModel AddStream(string port, int baud)
    {
        var vm = new DataStreamViewModel { Port = port, Baud = baud, StatusMessage = "Disconnected" };
        StreamViewModels.Add(vm);
        return vm;
    }

    public void Connect(DataStreamViewModel vm)
    {
        if (_dataStreams.ContainsKey(vm))
            return; //we already have a stream for this VM

        var source = new SourceSetting(vm.Port, vm.Baud, vm.DataBits);

        //var parser = new DataParser(vm.NumberOfChannels,vm.FrameStart);
        //var streamer = new DataStream(source, parser);
        //streamer.Start();
        //_dataStreams[vm] = streamer;
        vm.IsConnected = true;
        vm.StatusMessage = "Connected";
    }

    public void Disconnect(DataStreamViewModel vm)
    {
        if (_dataStreams.TryGetValue(vm, out var streamer))
        {
            streamer.Stop();
            streamer.Dispose();
            _dataStreams.Remove(vm);
        }
        vm.IsConnected = false;
        vm.StatusMessage = "Disconnected";
    }

    public void RemoveStream(DataStreamViewModel vm)
    {
        Disconnect(vm);
        StreamViewModels.Remove(vm);
    }
}