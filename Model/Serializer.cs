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
            XElement settingsXml = new XElement("PlotSettings");
            WritePlotSettings(settingsXml, plotManager, verticalControl);
            WriteDataStreamsWithChannels(settingsXml, dataStreamBar, channelControlBar);
            settingsXml.Save(filePath);
        }

        public static void ReadSettingsFromXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar, VerticalControl verticalControl)
        {
            if (!File.Exists(filePath)) 
                return;
            try
            {
                XElement settingsXml = XElement.Load(filePath);
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
            XElement dataStreamsElement = new XElement("DataStreams");
            int globalChannelIndex = 0;

            foreach (DataStreamViewModel vm in dataStreamBar.DataStreams)
            {
                XElement streamElement = new XElement("DataStream",
                    // Stream configuration
                    new XElement("Port", vm.Port),
                    new XElement("Baud", vm.Baud),
                    new XElement("DataBits", vm.DataBits),
                    new XElement("StopBits", vm.StopBits),
                    new XElement("Parity", vm.Parity.ToString()),
                    new XElement("AudioDevice", vm.AudioDevice),
                    new XElement("AudioDeviceIndex", vm.AudioDeviceIndex),
                    new XElement("SampleRate", vm.AudioSampleRate),
                    new XElement("EnableChecksum", vm.EnableChecksum),
                    new XElement("DataFormat", vm.DataFormat.ToString()),
                    new XElement("NumberOfChannels", vm.NumberOfChannels),
                    new XElement("NumberType", vm.NumberType),
                    new XElement("Endianness", vm.Endianness),
                    new XElement("Delimiter", vm.Delimiter),
                    new XElement("FrameStart", vm.FrameStart)
                );

                // Add channel-specific settings for this stream
                XElement channelsElement = new XElement("Channels");
                for (int i = 0; i < vm.NumberOfChannels; i++)
                {
                    if (globalChannelIndex < channelControlBar.Channels.Count)
                    {
                        ChannelControl channelControl = channelControlBar.Channels[globalChannelIndex];
                        XElement channelElement = new XElement("Channel",
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
            XElement plotUpdateElement = settingsXml.Element("PlotUpdateRateFPS");
            string plotUpdateValue;
            if (plotUpdateElement != null)
                plotUpdateValue = plotUpdateElement.Value;
            else
                plotUpdateValue = "30";
            int plotUpdateRateFPS = int.Parse(plotUpdateValue);

            XElement lineWidthElement = settingsXml.Element("LineWidth");
            string lineWidthValue;
            if (lineWidthElement != null)
                lineWidthValue = lineWidthElement.Value;
            else
                lineWidthValue = "1";
            int lineWidth = int.Parse(lineWidthValue);

            XElement antiAliasingElement = settingsXml.Element("AntiAliasing");
            string antiAliasingValue;
            if (antiAliasingElement != null)
                antiAliasingValue = antiAliasingElement.Value;
            else
                antiAliasingValue = "false";
            bool antiAliasing = bool.Parse(antiAliasingValue);

            XElement showRenderTimeElement = settingsXml.Element("ShowRenderTime");
            string showRenderTimeValue;
            if (showRenderTimeElement != null)
                showRenderTimeValue = showRenderTimeElement.Value;
            else
                showRenderTimeValue = "false";
            bool showRenderTime = bool.Parse(showRenderTimeValue);

            XElement yMinElement = settingsXml.Element("Ymin");
            string yMinValue;
            if (yMinElement != null)
                yMinValue = yMinElement.Value;
            else
                yMinValue = "-200";
            int yMin = int.Parse(yMinValue);

            XElement yMaxElement = settingsXml.Element("Ymax");
            string yMaxValue;
            if (yMaxElement != null)
                yMaxValue = yMaxElement.Value;
            else
                yMaxValue = "4000";
            int yMax = int.Parse(yMaxValue);

            XElement autoScaleElement = settingsXml.Element("AutoScale");
            string autoScaleValue;
            if (autoScaleElement != null)
                autoScaleValue = autoScaleElement.Value;
            else
                autoScaleValue = "true";
            bool autoScale = bool.Parse(autoScaleValue);
            
            verticalControl.Min = yMin;
            verticalControl.Max = yMax;
            verticalControl.IsAutoScale = autoScale;
            plotManager.ApplyPlotSettings(plotUpdateRateFPS, lineWidth, antiAliasing, showRenderTime);
            plotManager.SetYLimits(yMin, yMax);
        }

        private static void ReadDataStreamsWithChannels(XElement settingsXml, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            XElement dataStreamsElement = settingsXml.Element("DataStreams");
            if (dataStreamsElement == null) 
                return;

            List<DataStreamViewModel> loadedStreams = new List<DataStreamViewModel>();
            List<ChannelSettings> channelSettings = new List<ChannelSettings>();

            foreach (XElement streamElement in dataStreamsElement.Elements("DataStream"))
            {
                // Load stream configuration
                DataStreamViewModel vm = new DataStreamViewModel();
                
                XElement portElement = streamElement.Element("Port");
                if (portElement != null)
                    vm.Port = portElement.Value;
                else
                    vm.Port = "";

                XElement baudElement = streamElement.Element("Baud");
                if (baudElement != null && int.TryParse(baudElement.Value, out int baud))
                    vm.Baud = baud;
                else
                    vm.Baud = 0;

                XElement dataBitsElement = streamElement.Element("DataBits");
                if (dataBitsElement != null && int.TryParse(dataBitsElement.Value, out int dataBits))
                    vm.DataBits = dataBits;
                else
                    vm.DataBits = 8;

                XElement stopBitsElement = streamElement.Element("StopBits");
                if (stopBitsElement != null && int.TryParse(stopBitsElement.Value, out int stopBits))
                    vm.StopBits = stopBits;
                else
                    vm.StopBits = 1;

                XElement parityElement = streamElement.Element("Parity");
                if (parityElement != null && Enum.TryParse<System.IO.Ports.Parity>(parityElement.Value, out System.IO.Ports.Parity parity))
                    vm.Parity = parity;
                else
                    vm.Parity = System.IO.Ports.Parity.None;

                XElement audioDeviceElement = streamElement.Element("AudioDevice");
                if (audioDeviceElement != null)
                    vm.AudioDevice = audioDeviceElement.Value;
                else
                    vm.AudioDevice = "";

                XElement audioDeviceIndexElement = streamElement.Element("AudioDeviceIndex");
                if (audioDeviceIndexElement != null && int.TryParse(audioDeviceIndexElement.Value, out int audioDeviceIndex))
                    vm.AudioDeviceIndex = audioDeviceIndex;
                else
                    vm.AudioDeviceIndex = 0;

                XElement sampleRateElement = streamElement.Element("SampleRate");
                if (sampleRateElement != null && int.TryParse(sampleRateElement.Value, out int sampleRate))
                    vm.AudioSampleRate = sampleRate;
                else
                    vm.AudioSampleRate = 0;

                XElement enableChecksumElement = streamElement.Element("EnableChecksum");
                if (enableChecksumElement != null && bool.TryParse(enableChecksumElement.Value, out bool enableChecksum))
                    vm.EnableChecksum = enableChecksum;
                else
                    vm.EnableChecksum = false;

                XElement dataFormatElement = streamElement.Element("DataFormat");
                if (dataFormatElement != null && Enum.TryParse<DataFormatType>(dataFormatElement.Value, out DataFormatType dataFormat))
                    vm.DataFormat = dataFormat;
                else
                    vm.DataFormat = DataFormatType.ASCII;

                XElement numberOfChannelsElement = streamElement.Element("NumberOfChannels");
                if (numberOfChannelsElement != null && int.TryParse(numberOfChannelsElement.Value, out int numberOfChannels))
                    vm.NumberOfChannels = numberOfChannels;
                else
                    vm.NumberOfChannels = 1;

                XElement numberTypeElement = streamElement.Element("NumberType");
                if (numberTypeElement != null && Enum.TryParse<NumberTypeEnum>(numberTypeElement.Value, out NumberTypeEnum numberType))
                    vm.NumberType = numberType;
                else
                    vm.NumberType = NumberTypeEnum.Uint8;

                XElement endiannessElement = streamElement.Element("Endianness");
                if (endiannessElement != null)
                    vm.Endianness = endiannessElement.Value;
                else
                    vm.Endianness = "";

                XElement delimiterElement = streamElement.Element("Delimiter");
                if (delimiterElement != null)
                    vm.Delimiter = delimiterElement.Value;
                else
                    vm.Delimiter = "";

                XElement frameStartElement = streamElement.Element("FrameStart");
                if (frameStartElement != null)
                    vm.FrameStart = frameStartElement.Value;
                else
                    vm.FrameStart = "";

                loadedStreams.Add(vm);

                // Load channel settings for this stream
                XElement channelsElement = streamElement.Element("Channels");
                if (channelsElement != null)
                {
                    foreach (XElement channelElement in channelsElement.Elements("Channel"))
                    {
                        ChannelSettings setting = new ChannelSettings();
                        
                        XElement labelElement = channelElement.Element("Label");
                        if (labelElement != null)
                            setting.Label = labelElement.Value;
                        else
                            setting.Label = "";

                        XElement colorElement = channelElement.Element("Color");
                        string colorValue;
                        if (colorElement != null)
                            colorValue = colorElement.Value;
                        else
                            colorValue = null;
                        setting.Color = ParseColor(colorValue);

                        XElement isEnabledElement = channelElement.Element("IsEnabled");
                        if (isEnabledElement != null && bool.TryParse(isEnabledElement.Value, out bool enabled))
                            setting.IsEnabled = enabled;
                        else
                            setting.IsEnabled = true;

                        XElement gainElement = channelElement.Element("Gain");
                        if (gainElement != null && double.TryParse(gainElement.Value, out double gain))
                            setting.Gain = gain;
                        else
                            setting.Gain = 1.0;

                        XElement offsetElement = channelElement.Element("Offset");
                        if (offsetElement != null && double.TryParse(offsetElement.Value, out double offset))
                            setting.Offset = offset;
                        else
                            setting.Offset = 0.0;

                        XElement couplingElement = channelElement.Element("Coupling");
                        if (couplingElement != null && Enum.TryParse<ChannelControl.CouplingMode>(couplingElement.Value, out ChannelControl.CouplingMode coupling))
                            setting.Coupling = coupling;
                        else
                            setting.Coupling = ChannelControl.CouplingMode.DC;

                        XElement filterElement = channelElement.Element("Filter");
                        if (filterElement != null && Enum.TryParse<ChannelControl.FilterMode>(filterElement.Value, out ChannelControl.FilterMode filter))
                            setting.Filter = filter;
                        else
                            setting.Filter = ChannelControl.FilterMode.None;

                        channelSettings.Add(setting);
                    }
                }
            }

            // Add streams to DataStreamBar
            foreach (DataStreamViewModel stream in loadedStreams)
            {
                dataStreamBar.AddStreamFromSettings(stream);
            }

            // Apply channel settings once all channels are created
            ApplyChannelSettings(channelControlBar, channelSettings);
        }

        private static Color ParseColor(string colorString)
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
                ChannelSettings setting = channelSettings[i];
                ChannelControl channel = channelControlBar.Channels[i];
                
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
