using System;
using System.IO;
using System.Xml.Linq;
using SerialPlotDN_WPF.View.UserControls;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace SerialPlotDN_WPF.Model
{
    public static class Serializer
    {
        public static void WriteSettingsToXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar, VerticalControl verticalControl)
        {
            var settingsXml = new XElement("PlotSettings");
            WritePlotSettings(settingsXml, plotManager, verticalControl);
            WriteDataStreamsWithChannels(settingsXml, dataStreamBar, channelControlBar);
            settingsXml.Save(filePath);
        }

        public static void ReadSettingsFromXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar, VerticalControl verticalControl)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                var settingsXml = XElement.Load(filePath);
                ReadPlotSettings(settingsXml, plotManager, verticalControl);
                ReadDataStreamsWithChannels(settingsXml, dataStreamBar, channelControlBar);
            }
            catch { /* Ignore errors and use defaults */ }
        }

        // --- Private helpers ---
        private static void WritePlotSettings(XElement parent, PlotManager plotManager, VerticalControl verticalControl)
        {
            parent.Add(new XElement("PlotUpdateRateFPS", plotManager.CurrentPlotUpdateRateFPS));
            parent.Add(new XElement("LineWidth", plotManager.CurrentLineWidth));
            parent.Add(new XElement("AntiAliasing", plotManager.CurrentAntiAliasing));
            parent.Add(new XElement("ShowRenderTime", plotManager.ShowRenderTime));
            parent.Add(new XElement("AutoScale", verticalControl.IsAutoScale));
            parent.Add(new XElement("Ymin", plotManager.Ymin));
            parent.Add(new XElement("Ymax", plotManager.Ymax));
        }

        private static void WriteDataStreamsWithChannels(XElement parent, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            var dataStreamsElement = new XElement("DataStreams");
            int globalChannelIndex = 0;

            foreach (var vm in dataStreamBar.DataStreams)
            {
                var streamElement = new XElement("DataStream",
                    // Stream configuration
                    new XElement("Port", vm.Port),
                    new XElement("Baud", vm.Baud),
                    new XElement("DataBits", vm.DataBits),
                    new XElement("StopBits", vm.StopBits),
                    new XElement("Parity", vm.Parity.ToString()),
                    new XElement("AudioDevice", vm.AudioDevice),
                    new XElement("AudioDeviceIndex", vm.AudioDeviceIndex),
                    new XElement("SampleRate", vm.SampleRate),
                    new XElement("EnableChecksum", vm.EnableChecksum),
                    new XElement("DataFormat", vm.DataFormat.ToString()),
                    new XElement("NumberOfChannels", vm.NumberOfChannels),
                    new XElement("NumberType", vm.NumberType),
                    new XElement("Endianness", vm.Endianness),
                    new XElement("Delimiter", vm.Delimiter),
                    new XElement("FrameStart", vm.FrameStart)
                );

                // Add channel-specific settings for this stream
                var channelsElement = new XElement("Channels");
                for (int i = 0; i < vm.NumberOfChannels; i++)
                {
                    if (globalChannelIndex < channelControlBar.Channels.Count)
                    {
                        var channelControl = channelControlBar.Channels[globalChannelIndex];
                        var channelElement = new XElement("Channel",
                            new XElement("Index", i),
                            new XElement("Label", channelControl.Label),
                            new XElement("Color", channelControl.Color.ToString()),
                            new XElement("IsEnabled", channelControl.IsEnabled),
                            new XElement("Gain", channelControl.Gain),
                            new XElement("Offset", channelControl.Offset),
                            new XElement("Coupling", channelControl.Coupling.ToString()),
                            new XElement("Filter", channelControl.Filter.ToString())
                        );
                        channelsElement.Add(channelElement);
                    }
                    globalChannelIndex++;
                }
                streamElement.Add(channelsElement);
                dataStreamsElement.Add(streamElement);
            }
            parent.Add(dataStreamsElement);
        }

        private static void ReadPlotSettings(XElement settingsXml, PlotManager plotManager, VerticalControl verticalControl)
        {
            int plotUpdateRateFPS = int.Parse(settingsXml.Element("PlotUpdateRateFPS")?.Value ?? "30");
            int lineWidth = int.Parse(settingsXml.Element("LineWidth")?.Value ?? "1");
            bool antiAliasing = bool.Parse(settingsXml.Element("AntiAliasing")?.Value ?? "false");
            bool showRenderTime = bool.Parse(settingsXml.Element("ShowRenderTime")?.Value ?? "false");
            int yMin = int.Parse(settingsXml.Element("Ymin")?.Value ?? "-200");
            int yMax = int.Parse(settingsXml.Element("Ymax")?.Value ?? "4000");
            bool autoScale = bool.Parse(settingsXml.Element("AutoScale")?.Value ?? "true");
            
            verticalControl.Min = yMin;
            verticalControl.Max = yMax;
            verticalControl.IsAutoScale = autoScale;
            plotManager.ApplyPlotSettings(plotUpdateRateFPS, lineWidth, antiAliasing, showRenderTime);
            plotManager.SetYLimits(yMin, yMax);
        }

        private static void ReadDataStreamsWithChannels(XElement settingsXml, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            var dataStreamsElement = settingsXml.Element("DataStreams");
            if (dataStreamsElement == null) return;

            var loadedStreams = new List<DataStreamViewModel>();
            var channelSettings = new List<ChannelSettings>();

            foreach (var streamElement in dataStreamsElement.Elements("DataStream"))
            {
                // Load stream configuration
                var vm = new DataStreamViewModel
                {
                    Port = streamElement.Element("Port")?.Value ?? "",
                    Baud = int.TryParse(streamElement.Element("Baud")?.Value, out var baud) ? baud : 0,
                    DataBits = int.TryParse(streamElement.Element("DataBits")?.Value, out var dataBits) ? dataBits : 8,
                    StopBits = int.TryParse(streamElement.Element("StopBits")?.Value, out var stopBits) ? stopBits : 1,
                    Parity = Enum.TryParse<System.IO.Ports.Parity>(streamElement.Element("Parity")?.Value, out var parity) ? parity : System.IO.Ports.Parity.None,
                    AudioDevice = streamElement.Element("AudioDevice")?.Value ?? "",
                    AudioDeviceIndex = int.TryParse(streamElement.Element("AudioDeviceIndex")?.Value, out var audioDeviceIndex) ? audioDeviceIndex : 0,
                    SampleRate = int.TryParse(streamElement.Element("SampleRate")?.Value, out var sampleRate) ? sampleRate : 0,
                    EnableChecksum = bool.TryParse(streamElement.Element("EnableChecksum")?.Value, out var enableChecksum) ? enableChecksum : false,
                    DataFormat = Enum.TryParse<DataFormatType>(streamElement.Element("DataFormat")?.Value, out var dataFormat) ? dataFormat : DataFormatType.ASCII,
                    NumberOfChannels = int.TryParse(streamElement.Element("NumberOfChannels")?.Value, out var numberOfChannels) ? numberOfChannels : 1,
                    NumberType = streamElement.Element("NumberType")?.Value ?? "",
                    Endianness = streamElement.Element("Endianness")?.Value ?? "",
                    Delimiter = streamElement.Element("Delimiter")?.Value ?? "",
                    FrameStart = streamElement.Element("FrameStart")?.Value ?? ""
                };

                loadedStreams.Add(vm);

                // Load channel settings for this stream
                var channelsElement = streamElement.Element("Channels");
                if (channelsElement != null)
                {
                    foreach (var channelElement in channelsElement.Elements("Channel"))
                    {
                        var setting = new ChannelSettings
                        {
                            Label = channelElement.Element("Label")?.Value ?? "",
                            Color = ParseColor(channelElement.Element("Color")?.Value),
                            IsEnabled = bool.TryParse(channelElement.Element("IsEnabled")?.Value, out var enabled) ? enabled : true,
                            Gain = double.TryParse(channelElement.Element("Gain")?.Value, out var gain) ? gain : 1.0,
                            Offset = double.TryParse(channelElement.Element("Offset")?.Value, out var offset) ? offset : 0.0,
                            Coupling = Enum.TryParse<ChannelControl.CouplingMode>(channelElement.Element("Coupling")?.Value, out var coupling) ? coupling : ChannelControl.CouplingMode.DC,
                            Filter = Enum.TryParse<ChannelControl.FilterMode>(channelElement.Element("Filter")?.Value, out var filter) ? filter : ChannelControl.FilterMode.None
                        };
                        channelSettings.Add(setting);
                    }
                }
            }

            // Add streams to DataStreamBar
            foreach (var stream in loadedStreams)
            {
                dataStreamBar.AddStreamFromSettings(stream);
            }

            // Apply channel settings once all channels are created
            ApplyChannelSettings(channelControlBar, channelSettings);
        }

        private static Color ParseColor(string? colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return Colors.Blue; // Default color

            try
            {
                return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                return Colors.Blue; // Default color if parsing fails
            }
        }

        private static void ApplyChannelSettings(ChannelControlBar channelControlBar, List<ChannelSettings> channelSettings)
        {
            for (int i = 0; i < Math.Min(channelControlBar.Channels.Count, channelSettings.Count); i++)
            {
                var setting = channelSettings[i];
                var channel = channelControlBar.Channels[i];
                
                channel.Label = setting.Label;
                channel.Color = setting.Color;
                channel.IsEnabled = setting.IsEnabled;
                channel.Gain = setting.Gain;
                channel.Offset = setting.Offset;
                channel.Coupling = setting.Coupling;
                channel.Filter = setting.Filter;
            }
        }

        // Helper class to store channel settings during deserialization
        private class ChannelSettings
        {
            public string Label { get; set; } = "";
            public Color Color { get; set; } = Colors.Blue;
            public bool IsEnabled { get; set; } = true;
            public double Gain { get; set; } = 1.0;
            public double Offset { get; set; } = 0.0;
            public ChannelControl.CouplingMode Coupling { get; set; } = ChannelControl.CouplingMode.DC;
            public ChannelControl.FilterMode Filter { get; set; } = ChannelControl.FilterMode.None;
        }
    }
}
