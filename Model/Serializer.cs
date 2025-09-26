using System;
using System.IO;
using System.Xml.Linq;
using PowerScope.View.UserControls;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.IO.Ports;

namespace PowerScope.Model
{
    public static class Serializer
    {
        public static void WriteSettingsToXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar)
        {
            XElement settingsXml = new XElement("PlotSettings");
            WritePlotSettings(settingsXml, plotManager);
            WriteDataStreamsWithChannels(settingsXml, dataStreamBar);
            settingsXml.Save(filePath);
        }

        public static void ReadSettingsFromXML(string filePath, PlotManager plotManager, DataStreamBar dataStreamBar)
        {
            if (!File.Exists(filePath)) 
                return;
            try
            {
                XElement settingsXml = XElement.Load(filePath);
                ReadPlotSettings(settingsXml, plotManager);
                ReadDataStreamsWithChannels(settingsXml, dataStreamBar);
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
            parent.Add(new XElement("BufferSize", plotManager.Settings.BufferSize));
            parent.Add(new XElement("Xmin", plotManager.Settings.Xmin));
            parent.Add(new XElement("Xmax", plotManager.Settings.Xmax));
            parent.Add(new XElement("Ymin", plotManager.Settings.Ymin));
            parent.Add(new XElement("Ymax", plotManager.Settings.Ymax));
        }

        private static void WriteDataStreamsWithChannels(XElement parent, DataStreamBar dataStreamBar)
        {
            XElement dataStreamsElement = new XElement("DataStreams");
            
            // Group channels by their owner stream to save stream configurations
            var streamGroups = dataStreamBar.Channels
                .GroupBy(channel => channel.OwnerStream)
                .ToList();

            foreach (var streamGroup in streamGroups)
            {
                IDataStream stream = streamGroup.Key;
                List<Channel> channels = streamGroup.ToList();

                // Create stream element with configuration
                XElement streamElement = new XElement("Stream");

                // Save stream configuration - we need to reverse-engineer this from the stream
                if (stream is DemoDataStream demoStream)
                {
                    streamElement.Add(
                        new XElement("StreamSource", "Demo"),
                        new XElement("NumberOfChannels", demoStream.ChannelCount),
                        new XElement("DemoSampleRate", demoStream.DemoSettings.SampleRate),
                        new XElement("DemoSignalType", demoStream.DemoSettings.SignalType.ToString())
                    );
                }
                else if (stream is AudioDataStream audioStream)
                {
                    streamElement.Add(
                        new XElement("StreamSource", "AudioInput"),
                        new XElement("NumberOfChannels", audioStream.ChannelCount),
                        new XElement("AudioDevice", audioStream.DeviceName ?? "Default"),
                        new XElement("AudioSampleRate", audioStream.SampleRate)
                    );
                }
                else if (stream is SerialDataStream serialStream)
                {
                    // Determine data format from the parser mode - back to the simple approach
                    string dataFormatStr = serialStream.Parser.Mode == DataParser.ParserMode.ASCII ? "ASCII" : "RawBinary";
                    
                    // Get delimiter from parser if it's ASCII mode
                    string delimiterStr = ","; // Default
                    if (serialStream.Parser.Mode == DataParser.ParserMode.ASCII)
                    {
                        char sep = serialStream.Parser.Separator;
                        delimiterStr = sep switch
                        {
                            ',' => ",",
                            ' ' => "Space",
                            '\t' => "Tab",
                            ';' => ";",
                            _ => sep.ToString()
                        };
                    }
                    
                    // Get frame start from parser if it's binary mode with frame start
                    string frameStartStr = "0xAA,0xAA"; // Default
                    if (serialStream.Parser.Mode == DataParser.ParserMode.Binary && serialStream.Parser.FrameStart != null)
                    {
                        frameStartStr = string.Join(",", serialStream.Parser.FrameStart.Select(b => $"0x{b:X2}"));
                    }
                    
                    // Get binary format if it's binary mode
                    string binaryFormatStr = "uint16_t"; // Default
                    if (serialStream.Parser.Mode == DataParser.ParserMode.Binary)
                    {
                        binaryFormatStr = serialStream.Parser.format.ToString();
                    }

                    streamElement.Add(
                        new XElement("StreamSource", "SerialPort"),
                        new XElement("NumberOfChannels", serialStream.ChannelCount),
                        new XElement("Port", serialStream.SourceSetting.PortName),
                        new XElement("Baud", serialStream.SourceSetting.BaudRate),
                        new XElement("DataBits", serialStream.SourceSetting.DataBits),
                        new XElement("StopBits", serialStream.SourceSetting.StopBits),
                        new XElement("Parity", serialStream.SourceSetting.Parity.ToString()),
                        new XElement("DataFormat", dataFormatStr), // Back to simple DataFormat
                        new XElement("NumberType", binaryFormatStr.Replace("_t", "").Replace("uint", "Uint").Replace("int", "Int").Replace("float", "Float")), // Map to NumberType
                        new XElement("Delimiter", delimiterStr),
                        new XElement("FrameStart", frameStartStr)
                    );
                }

                // Save channels for this stream - all data comes directly from DataStreamBar.Channels
                XElement channelsElement = new XElement("Channels");
                foreach (Channel channel in channels)
                {
                    XElement channelElement = new XElement("Channel",
                        new XElement("LocalIndex", channel.LocalChannelIndex),
                        new XElement("Label", channel.Settings.Label),
                        new XElement("Color", channel.Settings.Color.ToString()),
                        new XElement("IsEnabled", channel.Settings.IsEnabled),
                        new XElement("Gain", channel.Settings.Gain.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new XElement("Offset", channel.Settings.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    );

                    // Add filter information if a filter is configured
                    if (channel.Settings.Filter != null)
                    {
                        XElement filterElement = new XElement("Filter",
                            new XElement("Type", channel.Settings.Filter.GetFilterType())
                        );

                        // Add filter parameters
                        Dictionary<string, double> parameters = channel.Settings.Filter.GetFilterParameters();
                        XElement parametersElement = new XElement("Parameters");
                        foreach (KeyValuePair<string, double> parameter in parameters)
                        {
                            parametersElement.Add(new XElement("Parameter",
                                new XAttribute("Name", parameter.Key),
                                new XAttribute("Value", parameter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            ));
                        }
                        filterElement.Add(parametersElement);
                        channelElement.Add(filterElement);
                    }

                    // Save measurements for this channel
                    if (channel.Measurements.Count > 0)
                    {
                        XElement measurementsElement = new XElement("Measurements");
                        foreach (Measurement measurement in channel.Measurements)
                        {
                            XElement measurementElement = new XElement("Measurement",
                                new XElement("Type", measurement.Type.ToString())
                            );
                            measurementsElement.Add(measurementElement);
                        }
                        channelElement.Add(measurementsElement);
                    }

                    channelsElement.Add(channelElement);
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

            XElement bufferSizeElement = settingsXml.Element("BufferSize");
            string bufferSizeValue;
            if (bufferSizeElement != null)
                bufferSizeValue = bufferSizeElement.Value;
            else
                bufferSizeValue = "500000"; // Default buffer size
            int bufferSize = int.Parse(bufferSizeValue);

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
            plotManager.Settings.BufferSize = bufferSize;
            plotManager.Settings.Xmin = xMin;
            plotManager.Settings.Xmax = xMax;
            plotManager.Settings.Ymin = yMin;
            plotManager.Settings.Ymax = yMax;
            
            // Settings are automatically applied via PropertyChanged events in PlotManager
            plotManager.Plot.Plot.Axes.SetLimitsY(yMin, yMax);
            plotManager.Plot.Refresh();
        }

        private static void ReadDataStreamsWithChannels(XElement settingsXml, DataStreamBar dataStreamBar)
        {
            XElement dataStreamsElement = settingsXml.Element("DataStreams");
            if (dataStreamsElement == null) 
                return;

            // Load and recreate streams with their channels
            foreach (XElement streamElement in dataStreamsElement.Elements("Stream"))
            {
                // Load stream configuration
                StreamSettings streamSettings = new StreamSettings();
                
                XElement streamSourceElement = streamElement.Element("StreamSource");
                if (streamSourceElement != null && Enum.TryParse<StreamSource>(streamSourceElement.Value, out StreamSource streamSource))
                {
                    streamSettings.StreamSource = streamSource;
                }

                XElement numberOfChannelsElement = streamElement.Element("NumberOfChannels");
                if (numberOfChannelsElement != null && int.TryParse(numberOfChannelsElement.Value, out int numberOfChannels))
                {
                    streamSettings.NumberOfChannels = numberOfChannels;
                }

                // Load stream-specific settings
                switch (streamSettings.StreamSource)
                {
                    case StreamSource.Demo:
                        XElement demoSampleRateElement = streamElement.Element("DemoSampleRate");
                        if (demoSampleRateElement != null && int.TryParse(demoSampleRateElement.Value, out int demoSampleRate))
                            streamSettings.DemoSampleRate = demoSampleRate;

                        XElement demoSignalTypeElement = streamElement.Element("DemoSignalType");
                        if (demoSignalTypeElement != null)
                            streamSettings.DemoSignalType = demoSignalTypeElement.Value;
                        break;

                    case StreamSource.AudioInput:
                        XElement audioDeviceElement = streamElement.Element("AudioDevice");
                        if (audioDeviceElement != null)
                            streamSettings.AudioDevice = audioDeviceElement.Value;

                        XElement audioSampleRateElement = streamElement.Element("AudioSampleRate");
                        if (audioSampleRateElement != null && int.TryParse(audioSampleRateElement.Value, out int audioSampleRate))
                            streamSettings.AudioSampleRate = audioSampleRate;
                        break;

                    case StreamSource.SerialPort:
                        XElement portElement = streamElement.Element("Port");
                        if (portElement != null)
                            streamSettings.Port = portElement.Value;

                        XElement baudElement = streamElement.Element("Baud");
                        if (baudElement != null && int.TryParse(baudElement.Value, out int baud))
                            streamSettings.Baud = baud;

                        XElement dataBitsElement = streamElement.Element("DataBits");
                        if (dataBitsElement != null && int.TryParse(dataBitsElement.Value, out int dataBits))
                            streamSettings.DataBits = dataBits;

                        XElement stopBitsElement = streamElement.Element("StopBits");
                        if (stopBitsElement != null && int.TryParse(stopBitsElement.Value, out int stopBits))
                            streamSettings.StopBits = stopBits;

                        XElement parityElement = streamElement.Element("Parity");
                        if (parityElement != null && Enum.TryParse<Parity>(parityElement.Value, out Parity parity))
                            streamSettings.Parity = parity;

                        // Simplified: Back to DataFormat only, with ASCII as default fallback
                        XElement dataFormatElement = streamElement.Element("DataFormat");
                        if (dataFormatElement != null && Enum.TryParse<DataFormatType>(dataFormatElement.Value, out DataFormatType dataFormat))
                        {
                            streamSettings.DataFormat = dataFormat;
                        }
                        else
                        {
                            streamSettings.DataFormat = DataFormatType.ASCII; // Default fallback as requested
                        }

                        // Get number type 
                        XElement numberTypeElement = streamElement.Element("NumberType");
                        if (numberTypeElement != null && Enum.TryParse<NumberTypeEnum>(numberTypeElement.Value, out NumberTypeEnum numberType))
                        {
                            streamSettings.NumberType = numberType;
                        }

                        XElement delimiterElement = streamElement.Element("Delimiter");
                        if (delimiterElement != null)
                            streamSettings.Delimiter = delimiterElement.Value;

                        XElement frameStartElement = streamElement.Element("FrameStart");
                        if (frameStartElement != null)
                            streamSettings.FrameStart = frameStartElement.Value;
                        break;
                }

                // Create the data stream
                IDataStream dataStream = dataStreamBar.CreateDataStreamFromUserInput(streamSettings);
                dataStream.Connect();
                dataStream.StartStreaming();

                // Load channel settings and measurements directly to channels
                XElement channelsElement = streamElement.Element("Channels");
                if (channelsElement != null)
                {
                    List<ChannelSettings> channelSettingsList = new List<ChannelSettings>();
                    List<List<MeasurementType>> channelMeasurementsList = new List<List<MeasurementType>>();

                    foreach (XElement channelElement in channelsElement.Elements("Channel"))
                    {
                        ChannelSettings setting = new ChannelSettings();
                        
                        XElement labelElement = channelElement.Element("Label");
                        if (labelElement != null)
                            setting.Label = labelElement.Value;
                        else
                            setting.Label = "";

                        XElement colorElement = channelElement.Element("Color");
                        setting.Color = ParseColor(colorElement?.Value);

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
                                    Dictionary<string, double> parameters = new Dictionary<string, double>();
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
                                    
                                    setting.Filter = CreateFilterFromTypeAndParameters(filterType, parameters);
                                }
                            }
                        }

                        // Load measurements for this channel
                        List<MeasurementType> measurements = new List<MeasurementType>();
                        XElement measurementsElement = channelElement.Element("Measurements");
                        if (measurementsElement != null)
                        {
                            foreach (XElement measurementElement in measurementsElement.Elements("Measurement"))
                            {
                                XElement typeElement = measurementElement.Element("Type");
                                if (typeElement != null && Enum.TryParse<MeasurementType>(typeElement.Value, out MeasurementType measurementType))
                                {
                                    measurements.Add(measurementType);
                                }
                            }
                        }

                        channelSettingsList.Add(setting);
                        channelMeasurementsList.Add(measurements);
                    }

                    // Create channels for this stream with loaded settings
                    Color[] channelColors = channelSettingsList.Select(cs => cs.Color).ToArray();
                    dataStreamBar.AddChannelsForStream(dataStream, channelColors);

                    // Apply loaded settings and measurements directly to the created channels
                    var streamChannels = dataStreamBar.GetChannelsForStream(dataStream).ToList();
                    for (int i = 0; i < Math.Min(channelSettingsList.Count, streamChannels.Count); i++)
                    {
                        Channel channel = streamChannels[i];
                        ChannelSettings settings = channelSettingsList[i];

                        // Update channel settings directly
                        channel.Settings.Label = settings.Label;
                        channel.Settings.Color = settings.Color;
                        channel.Settings.IsEnabled = settings.IsEnabled;
                        channel.Settings.Gain = settings.Gain;
                        channel.Settings.Offset = settings.Offset;
                        channel.Settings.Filter = settings.Filter;

                        // Add measurements to channel
                        if (i < channelMeasurementsList.Count)
                        {
                            List<MeasurementType> measurements = channelMeasurementsList[i];
                            foreach (MeasurementType measurementType in measurements)
                            {
                                channel.AddMeasurement(measurementType);
                            }
                        }
                    }

                    // Add stream info panel to UI
                    dataStreamBar.AddStreamInfoPanel(streamSettings, dataStream);
                }
            }
            
            // No need to update ChannelControlBar - MainWindow handles this automatically via CollectionChanged events
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
