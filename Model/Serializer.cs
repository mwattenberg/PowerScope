using System;
using System.IO;
using System.Xml.Linq;
using SerialPlotDN_WPF.View.UserControls;
using System.Collections.Generic;

namespace SerialPlotDN_WPF.Model
{
    public static class Serializer
    {
        public static void WriteSettingsToXML(string filePath, PlotManager plotManager, SerialDataStream dataStream, ChannelControlBar channelControlBar, VerticalControl verticalControl, IEnumerable<DataStreamViewModel> streamViewModels)
        {
            var settingsXml = new XElement("PlotSettings");
            WritePlotSettings(settingsXml, plotManager, dataStream, verticalControl);
            WriteChannelLabels(settingsXml, channelControlBar);
            WriteStreams(settingsXml, streamViewModels);
            settingsXml.Save(filePath);
        }

        public static List<DataStreamViewModel> ReadSettingsFromXML(string filePath, PlotManager plotManager, SerialDataStream dataStream, ChannelControlBar channelControlBar, VerticalControl verticalControl)
        {
            var streamViewModels = new List<DataStreamViewModel>();
            if (!File.Exists(filePath)) return streamViewModels;
            try
            {
                var settingsXml = XElement.Load(filePath);
                ReadPlotSettings(settingsXml, plotManager, dataStream, verticalControl);
                ReadChannelLabels(settingsXml, channelControlBar);
                streamViewModels = ReadStreams(settingsXml);
            }
            catch { /* Ignore errors and use defaults */ }
            return streamViewModels;
        }

        // --- Private helpers ---
        private static void WritePlotSettings(XElement parent, PlotManager plotManager, SerialDataStream dataStream, VerticalControl verticalControl)
        {
            parent.Add(new XElement("PlotUpdateRateFPS", plotManager.CurrentPlotUpdateRateFPS));
            parent.Add(new XElement("SerialPortUpdateRateHz", dataStream?.SerialPortUpdateRateHz ?? 1000));
            parent.Add(new XElement("LineWidth", plotManager.CurrentLineWidth));
            parent.Add(new XElement("AntiAliasing", plotManager.CurrentAntiAliasing));
            parent.Add(new XElement("ShowRenderTime", plotManager.ShowRenderTime));
            parent.Add(new XElement("AutoScale", verticalControl.IsAutoScale));
        }

        private static void WriteChannelLabels(XElement parent, ChannelControlBar channelControlBar)
        {
            var channelLabels = new XElement("ChannelLabels");
            foreach (ChannelControl channel in channelControlBar.Channels)
            {
                channelLabels.Add(new XElement("Label", channel.Label));
            }
            parent.Add(channelLabels);
        }

        private static void WriteStreams(XElement parent, IEnumerable<DataStreamViewModel> streamViewModels)
        {
            var streamsElement = new XElement("Streams");
            foreach (var vm in streamViewModels)
            {
                var streamElement = new XElement("Stream",
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
                streamsElement.Add(streamElement);
            }
            parent.Add(streamsElement);
        }

        private static void ReadPlotSettings(XElement settingsXml, PlotManager plotManager, SerialDataStream dataStream, VerticalControl verticalControl)
        {
            int plotUpdateRateFPS = int.Parse(settingsXml.Element("PlotUpdateRateFPS")?.Value ?? "30");
            int serialPortUpdateRateHz = int.Parse(settingsXml.Element("SerialPortUpdateRateHz")?.Value ?? "1000");
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
            if (dataStream != null)
                dataStream.SerialPortUpdateRateHz = serialPortUpdateRateHz;
        }

        private static void ReadChannelLabels(XElement settingsXml, ChannelControlBar channelControlBar)
        {
            var channelLabelElement = settingsXml.Element("ChannelLabels");
            if (channelLabelElement != null)
            {
                var labelElements = channelLabelElement.Elements("Label").ToList();
                int i = 0;
                foreach (ChannelControl channel in channelControlBar.Channels)
                {
                    if (i < labelElements.Count)
                    {
                        channel.Label = labelElements[i].Value;
                    }
                    i++;
                }
            }
        }

        private static List<DataStreamViewModel> ReadStreams(XElement settingsXml)
        {
            var streamViewModels = new List<DataStreamViewModel>();
            var streamsElement = settingsXml.Element("Streams");
            if (streamsElement != null)
            {
                foreach (var streamElement in streamsElement.Elements("Stream"))
                {
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
                    streamViewModels.Add(vm);
                }
            }
            return streamViewModels;
        }
    }
}
