using System;
using System.IO;
using System.Xml.Linq;
using SerialPlotDN_WPF.View.UserControls;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.IO.Ports;

namespace SerialPlotDN_WPF.Model
{
    public static class Serializer
    {
        public static void WriteSettingsToXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            XElement settingsXml = new XElement("PlotSettings");
            WritePlotSettings(settingsXml, plotManager);
            WriteDataStreamsWithChannels(settingsXml, dataStreamBar, channelControlBar);
            settingsXml.Save(filePath);
        }

        public static void ReadSettingsFromXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            if (!File.Exists(filePath)) 
                return;
            try
            {
                XElement settingsXml = XElement.Load(filePath);
                ReadPlotSettings(settingsXml, plotManager);
                ReadDataStreamsWithChannels(settingsXml, dataStreamBar, channelControlBar);
            }
            catch { /* Ignore errors and use defaults */ }
        }

        // --- Private helpers ---
        private static void WritePlotSettings(XElement parent, PlotManager plotManager)
        {
            parent.Add(new XElement("PlotUpdateRateFPS", plotManager.Settings.PlotUpdateRateFPS.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            parent.Add(new XElement("LineWidth", plotManager.Settings.LineWidth));
            parent.Add(new XElement("AntiAliasing", plotManager.Settings.AntiAliasing));
            parent.Add(new XElement("ShowRenderTime", plotManager.Settings.ShowRenderTime));
            parent.Add(new XElement("YAutoScale", plotManager.Settings.YAutoScale));
            parent.Add(new XElement("Xmin", plotManager.Settings.Xmin));
            parent.Add(new XElement("Xmax", plotManager.Settings.Xmax));
            parent.Add(new XElement("Ymin", plotManager.Settings.Ymin));
            parent.Add(new XElement("Ymax", plotManager.Settings.Ymax));
        }

        private static void WriteDataStreamsWithChannels(XElement parent, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            XElement dataStreamsElement = new XElement("DataStreams");
            int globalChannelIndex = 0;

            foreach (StreamSettings vm in dataStreamBar.ConfiguredDataStreams)
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
                    if (globalChannelIndex < channelControlBar.ChannelSettings.Count)
                    {
                        ChannelSettings channelSettings = channelControlBar.ChannelSettings[globalChannelIndex];
                        XElement channelElement = new XElement("Channel",
                            new XElement("Index", i),
                            new XElement("Label", channelSettings.Label),
                            new XElement("Color", channelSettings.Color.ToString()),
                            new XElement("IsEnabled", channelSettings.IsEnabled),
                            new XElement("Gain", channelSettings.Gain.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                            new XElement("Offset", channelSettings.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        );

                        // Add filter information if a filter is configured
                        if (channelSettings.Filter != null)
                        {
                            XElement filterElement = new XElement("Filter",
                                new XElement("Type", channelSettings.Filter.GetFilterType())
                            );

                            // Add filter parameters
                            var parameters = channelSettings.Filter.GetFilterParameters();
                            XElement parametersElement = new XElement("Parameters");
                            foreach (var parameter in parameters)
                            {
                                parametersElement.Add(new XElement("Parameter",
                                    new XAttribute("Name", parameter.Key),
                                    new XAttribute("Value", parameter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                ));
                            }
                            filterElement.Add(parametersElement);
                            channelElement.Add(filterElement);
                        }

                        channelsElement.Add(channelElement);
                    }
                    globalChannelIndex++;
                }
                streamElement.Add(channelsElement);
                dataStreamsElement.Add(streamElement);
            }
            parent.Add(dataStreamsElement);
        }

        private static void ReadPlotSettings(XElement settingsXml, PlotManager plotManager)
        {
            XElement plotUpdateElement = settingsXml.Element("PlotUpdateRateFPS");
            string plotUpdateValue;
            if (plotUpdateElement != null)
                plotUpdateValue = plotUpdateElement.Value;
            else
                plotUpdateValue = "30.0";
            double plotUpdateRateFPS = double.Parse(plotUpdateValue, System.Globalization.CultureInfo.InvariantCulture);

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

            XElement yAutoScaleElement = settingsXml.Element("YAutoScale");
            string yAutoScaleValue;
            if (yAutoScaleElement != null)
                yAutoScaleValue = yAutoScaleElement.Value;
            else
                yAutoScaleValue = "true";
            bool yAutoScale = bool.Parse(yAutoScaleValue);

            XElement xMinElement = settingsXml.Element("Xmin");
            string xMinValue;
            if (xMinElement != null)
                xMinValue = xMinElement.Value;
            else
                xMinValue = "0";
            int xMin = int.Parse(xMinValue);

            XElement xMaxElement = settingsXml.Element("Xmax");
            string xMaxValue;
            if (xMaxElement != null)
                xMaxValue = xMaxElement.Value;
            else
                xMaxValue = "3000";
            int xMax = int.Parse(xMaxValue);

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
            
            // Apply settings to PlotManager.Settings (this will automatically update via DataBinding)
            plotManager.Settings.PlotUpdateRateFPS = plotUpdateRateFPS;
            plotManager.Settings.LineWidth = lineWidth;
            plotManager.Settings.AntiAliasing = antiAliasing;
            plotManager.Settings.ShowRenderTime = showRenderTime;
            plotManager.Settings.YAutoScale = yAutoScale;
            plotManager.Settings.Xmin = xMin;
            plotManager.Settings.Xmax = xMax;
            plotManager.Settings.Ymin = yMin;
            plotManager.Settings.Ymax = yMax;
            
            // Settings are automatically applied via PropertyChanged events in PlotManager
            plotManager.Plot.Plot.Axes.SetLimitsY(yMin, yMax);
            plotManager.Plot.Refresh();
        }

        private static void ReadDataStreamsWithChannels(XElement settingsXml, DataStreamBar dataStreamBar, ChannelControlBar channelControlBar)
        {
            XElement dataStreamsElement = settingsXml.Element("DataStreams");
            if (dataStreamsElement == null) 
                return;

            List<StreamSettings> loadedStreams = new List<StreamSettings>();
            List<ChannelSettings> channelSettings = new List<ChannelSettings>();

            foreach (XElement streamElement in dataStreamsElement.Elements("DataStream"))
            {
                // Load stream configuration
                StreamSettings vm = new StreamSettings();
                
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
                if (parityElement != null && Enum.TryParse<Parity>(parityElement.Value, out Parity parity))
                    vm.Parity = parity;
                else
                    vm.Parity = Parity.None; // Parity.None

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
                        if (gainElement != null && double.TryParse(gainElement.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double gain))
                            setting.Gain = gain;
                        else
                            setting.Gain = 1.0;

                        XElement offsetElement = channelElement.Element("Offset");
                        if (offsetElement != null && double.TryParse(offsetElement.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double offset))
                            setting.Offset = offset;
                        else
                            setting.Offset = 0.0;

                        // Load filter information
                        XElement filterElement = channelElement.Element("Filter");
                        if (filterElement != null)
                        {
                            XElement typeElement = filterElement.Element("Type");
                            if (typeElement != null)
                            {
                                string filterType = typeElement.Value;
                                XElement parametersElement = filterElement.Element("Parameters");
                                
                                if (parametersElement != null)
                                {
                                    var parameters = new Dictionary<string, double>();
                                    foreach (XElement paramElement in parametersElement.Elements("Parameter"))
                                    {
                                        string paramName = paramElement.Attribute("Name")?.Value;
                                        string paramValue = paramElement.Attribute("Value")?.Value;
                                        
                                        if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(paramValue) &&
                                            double.TryParse(paramValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                                        {
                                            parameters[paramName] = value;
                                        }
                                    }
                                    
                                    // Create the appropriate filter based on type and parameters
                                    setting.Filter = CreateFilterFromTypeAndParameters(filterType, parameters);
                                }
                            }
                        }

                        channelSettings.Add(setting);
                    }
                }
            }

            // Add streams to DataStreamBar
            foreach (StreamSettings stream in loadedStreams)
            {
                var dataStream = dataStreamBar.CreateDataStreamFromUserInput(stream);
                dataStreamBar.ConfiguredDataStreams.Add(stream);
                dataStreamBar.ConnectedDataStreams.Add(dataStream);
                //dataStream.Connect();
                //dataStream.StartStreaming();
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
            // Clear existing settings and add loaded ones
            channelControlBar.ChannelSettings.Clear();
            foreach (ChannelSettings setting in channelSettings)
            {
                channelControlBar.ChannelSettings.Add(setting);
            }
        }

        /// <summary>
        /// Creates a filter instance from the saved filter type and parameters
        /// </summary>
        /// <param name="filterType">The type of filter to create</param>
        /// <param name="parameters">Dictionary of parameter names and values</param>
        /// <returns>IDigitalFilter instance or null if creation fails</returns>
        private static IDigitalFilter CreateFilterFromTypeAndParameters(string filterType, Dictionary<string, double> parameters)
        {
            try
            {
                return filterType switch
                {
                    "Exponential Low Pass" => CreateExponentialLowPassFilter(parameters),
                    "Exponential High Pass" => CreateExponentialHighPassFilter(parameters),
                    "Moving Average" => CreateMovingAverageFilter(parameters),
                    "Median" => CreateMedianFilter(parameters),
                    "Notch" => CreateNotchFilter(parameters),
                    "Absolute" => new AbsoluteFilter(),
                    "Squared" => new SquaredFilter(),
                    _ => null
                };
            }
            catch
            {
                // If filter creation fails, return null (no filtering)
                return null;
            }
        }

        private static ExponentialLowPassFilter CreateExponentialLowPassFilter(Dictionary<string, double> parameters)
        {
            double alpha = parameters.ContainsKey("Alpha") ? parameters["Alpha"] : 0.1;
            return new ExponentialLowPassFilter(alpha);
        }

        private static ExponentialHighPassFilter CreateExponentialHighPassFilter(Dictionary<string, double> parameters)
        {
            double alpha = parameters.ContainsKey("Alpha") ? parameters["Alpha"] : 0.1;
            return new ExponentialHighPassFilter(alpha);
        }

        private static MovingAverageFilter CreateMovingAverageFilter(Dictionary<string, double> parameters)
        {
            int windowSize = parameters.ContainsKey("WindowSize") ? (int)parameters["WindowSize"] : 5;
            return new MovingAverageFilter(windowSize);
        }

        private static MedianFilter CreateMedianFilter(Dictionary<string, double> parameters)
        {
            int windowSize = parameters.ContainsKey("WindowSize") ? (int)parameters["WindowSize"] : 5;
            return new MedianFilter(windowSize);
        }

        private static NotchFilter CreateNotchFilter(Dictionary<string, double> parameters)
        {
            double notchFreq = parameters.ContainsKey("NotchFreq") ? parameters["NotchFreq"] : 50.0;
            double sampleRate = parameters.ContainsKey("SampleRate") ? parameters["SampleRate"] : 1000.0;
            double bandwidth = parameters.ContainsKey("Bandwidth") ? parameters["Bandwidth"] : 2.0;
            return new NotchFilter(notchFreq, sampleRate, bandwidth);
        }
    }
}
