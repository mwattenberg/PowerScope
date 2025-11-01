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

            XElement settingsXml = XElement.Load(filePath);
            ReadPlotSettings(settingsXml, plotManager);
            ReadDataStreamsWithChannels(settingsXml, dataStreamBar);
        }

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

            Dictionary<IDataStream, List<Channel>> streamGroups = new Dictionary<IDataStream, List<Channel>>();
            foreach (Channel channel in dataStreamBar.Channels)
            {
                if (!streamGroups.ContainsKey(channel.OwnerStream))
                    streamGroups[channel.OwnerStream] = new List<Channel>();
                streamGroups[channel.OwnerStream].Add(channel);
            }

            foreach (KeyValuePair<IDataStream, List<Channel>> streamGroup in streamGroups)
            {
                IDataStream stream = streamGroup.Key;
                List<Channel> channels = streamGroup.Value;

                XElement streamElement = new XElement("Stream");

                int upDownSamplingFactor = 0;
                if (stream is IUpDownSampling upDownSamplingStream)
                {
                    upDownSamplingFactor = upDownSamplingStream.UpDownSamplingFactor;
                }

                if (stream is DemoDataStream demoStream)
                {
                    streamElement.Add(
                         new XElement("StreamSource", "Demo"),
                    new XElement("NumberOfChannels", demoStream.ChannelCount),
                    new XElement("DemoSampleRate", demoStream.DemoSettings.SampleRate),
                      new XElement("DemoSignalType", demoStream.DemoSettings.SignalType.ToString()),
                               new XElement("UpDownSampling", upDownSamplingFactor)
                      );
                }
                else if (stream is AudioDataStream audioStream)
                {
                    string deviceName = audioStream.DeviceName;
                    if (deviceName == null)
                        deviceName = "Default";

                    streamElement.Add(
               new XElement("StreamSource", "AudioInput"),
                     new XElement("NumberOfChannels", audioStream.ChannelCount),
                new XElement("AudioDevice", deviceName),
                          new XElement("AudioSampleRate", audioStream.SampleRate),
                           new XElement("UpDownSampling", upDownSamplingFactor)
                  );
                }
                else if (stream is SerialDataStream serialStream)
                {
                    string dataFormatStr = serialStream.Parser.Mode == DataParser.ParserMode.ASCII ? "ASCII" : "RawBinary";

                    string delimiterStr = ",";
                    if (serialStream.Parser.Mode == DataParser.ParserMode.ASCII)
                    {
                        char sep = serialStream.Parser.Separator;
                        delimiterStr = ConvertSeparatorToString(sep);
                    }

                    string frameStartStr = "0xAA,0xAA";
                    if (serialStream.Parser.Mode == DataParser.ParserMode.Binary && serialStream.Parser.FrameStart != null)
                    {
                        frameStartStr = ConvertFrameStartToString(serialStream.Parser.FrameStart);
                    }

                    string binaryFormatStr = "uint16_t";
                    if (serialStream.Parser.Mode == DataParser.ParserMode.Binary)
                    {
                        binaryFormatStr = serialStream.Parser.Format.ToString();
                    }

                    string numberTypeStr = ConvertBinaryFormatToNumberType(binaryFormatStr);

                    streamElement.Add(
                          new XElement("StreamSource", "SerialPort"),
                     new XElement("NumberOfChannels", serialStream.ChannelCount),
                        new XElement("Port", serialStream.SourceSetting.PortName),
                           new XElement("Baud", serialStream.SourceSetting.BaudRate),
                     new XElement("DataBits", serialStream.SourceSetting.DataBits),
                new XElement("StopBits", serialStream.SourceSetting.StopBits),
                 new XElement("Parity", serialStream.SourceSetting.Parity.ToString()),
                    new XElement("DataFormat", dataFormatStr),
                     new XElement("NumberType", numberTypeStr),
                      new XElement("Delimiter", delimiterStr),
                  new XElement("FrameStart", frameStartStr),
                  new XElement("UpDownSampling", upDownSamplingFactor)
                 );
                }

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

                    if (channel.Settings.Filter != null)
                    {
                        XElement filterElement = new XElement("Filter",
                                 new XElement("Type", channel.Settings.Filter.GetFilterType())
                       );

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

        private static string GetElementValue(XElement parent, string elementName, string defaultValue)
        {
            XElement element = parent.Element(elementName);
            if (element != null)
                return element.Value;
            else
                return defaultValue;
        }

        private static int GetElementValueInt(XElement parent, string elementName, int defaultValue)
        {
            string value = GetElementValue(parent, elementName, defaultValue.ToString());
            return int.Parse(value);
        }

        private static double GetElementValueDouble(XElement parent, string elementName, double defaultValue)
        {
            string value = GetElementValue(parent, elementName, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool GetElementValueBool(XElement parent, string elementName, bool defaultValue)
        {
            string value = GetElementValue(parent, elementName, defaultValue.ToString());
            return bool.Parse(value);
        }

        private static void ReadPlotSettings(XElement settingsXml, PlotManager plotManager)
        {
            double plotUpdateRateFPS = GetElementValueDouble(settingsXml, "PlotUpdateRateFPS", 30.0);
            int lineWidth = GetElementValueInt(settingsXml, "LineWidth", 1);
            bool antiAliasing = GetElementValueBool(settingsXml, "AntiAliasing", false);
            bool showRenderTime = GetElementValueBool(settingsXml, "ShowRenderTime", false);
            bool yAutoScale = GetElementValueBool(settingsXml, "YAutoScale", true);
            int bufferSize = GetElementValueInt(settingsXml, "BufferSize", 500000);
            int xMin = GetElementValueInt(settingsXml, "Xmin", 0);
            int xMax = GetElementValueInt(settingsXml, "Xmax", 3000);
            int yMin = GetElementValueInt(settingsXml, "Ymin", -200);
            int yMax = GetElementValueInt(settingsXml, "Ymax", 4000);

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

            plotManager.Plot.Plot.Axes.SetLimitsY(yMin, yMax);
            plotManager.Plot.Refresh();
        }

        private static void ReadDataStreamsWithChannels(XElement settingsXml, DataStreamBar dataStreamBar)
        {
            XElement dataStreamsElement = settingsXml.Element("DataStreams");
            if (dataStreamsElement == null)
                return;

            foreach (XElement streamElement in dataStreamsElement.Elements("Stream"))
            {
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

                        XElement dataFormatElement = streamElement.Element("DataFormat");
                        if (dataFormatElement != null && Enum.TryParse<DataFormatType>(dataFormatElement.Value, out DataFormatType dataFormat))
                        {
                            streamSettings.DataFormat = dataFormat;
                        }
                        else
                        {
                            streamSettings.DataFormat = DataFormatType.ASCII;
                        }

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

                XElement upDownSamplingElement = streamElement.Element("UpDownSampling");
                if (upDownSamplingElement != null && int.TryParse(upDownSamplingElement.Value, out int upDownSamplingFactor))
                {
                    streamSettings.UpDownSampling = upDownSamplingFactor;
                }

                IDataStream dataStream = dataStreamBar.CreateDataStreamFromUserInput(streamSettings);

                if (dataStream is IUpDownSampling upDownSamplingStream)
                {
                    upDownSamplingStream.UpDownSamplingFactor = streamSettings.UpDownSampling;
                }

                dataStream.Connect();
                dataStream.StartStreaming();

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
                        if (colorElement != null)
                            setting.Color = ParseColor(colorElement.Value);
                        else
                            setting.Color = ParseColor(null);

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
                                        XAttribute nameAttr = paramElement.Attribute("Name");
                                        XAttribute valueAttr = paramElement.Attribute("Value");

                                        if (nameAttr != null && valueAttr != null)
                                        {
                                            string paramName = nameAttr.Value;
                                            string paramValue = valueAttr.Value;

                                            if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(paramValue) &&
                                               double.TryParse(paramValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                                            {
                                                parameters[paramName] = value;
                                            }
                                        }
                                    }

                                    setting.Filter = CreateFilterFromTypeAndParameters(filterType, parameters);
                                }
                            }
                        }

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

                    Color[] channelColors = new Color[channelSettingsList.Count];
                    for (int i = 0; i < channelSettingsList.Count; i++)
                    {
                        channelColors[i] = channelSettingsList[i].Color;
                    }
                    dataStreamBar.AddChannelsForStream(dataStream, channelColors);

                    List<Channel> streamChannels = new List<Channel>();
                    foreach (Channel ch in dataStreamBar.GetChannelsForStream(dataStream))
                    {
                        streamChannels.Add(ch);
                    }

                    for (int i = 0; i < Math.Min(channelSettingsList.Count, streamChannels.Count); i++)
                    {
                        Channel channel = streamChannels[i];
                        ChannelSettings settings = channelSettingsList[i];

                        channel.Settings.Label = settings.Label;
                        channel.Settings.Color = settings.Color;
                        channel.Settings.IsEnabled = settings.IsEnabled;
                        channel.Settings.Gain = settings.Gain;
                        channel.Settings.Offset = settings.Offset;
                        channel.Settings.Filter = settings.Filter;

                        if (i < channelMeasurementsList.Count)
                        {
                            List<MeasurementType> measurements = channelMeasurementsList[i];
                            foreach (MeasurementType measurementType in measurements)
                            {
                                channel.AddMeasurement(measurementType);
                            }
                        }
                    }

                    dataStreamBar.AddStreamInfoPanel(streamSettings, dataStream);
                }
            }
        }

        private static string ConvertSeparatorToString(char separator)
        {
            switch (separator)
            {
                case ',':
                    return ",";
                case ' ':
                    return "Space";
                case '\t':
                    return "Tab";
                case ';':
                    return ";";
                default:
                    return separator.ToString();
            }
        }

        private static string ConvertFrameStartToString(byte[] frameStart)
        {
            string[] hexStrings = new string[frameStart.Length];
            for (int i = 0; i < frameStart.Length; i++)
            {
                hexStrings[i] = $"0x{frameStart[i]:X2}";
            }
            return string.Join(",", hexStrings);
        }

        private static string ConvertBinaryFormatToNumberType(string binaryFormat)
        {
            string result = binaryFormat.Replace("_t", "");
            result = result.Replace("uint", "Uint");
            result = result.Replace("int", "Int");
            result = result.Replace("float", "Float");
            return result;
        }

        private static Color ParseColor(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return Colors.Blue;

            return (Color)ColorConverter.ConvertFromString(colorString);
        }

        private static IDigitalFilter CreateFilterFromTypeAndParameters(string filterType, Dictionary<string, double> parameters)
        {
            switch (filterType)
            {
                case "Exponential Low Pass":
                    return CreateExponentialLowPassFilter(parameters);
                case "Exponential High Pass":
                    return CreateExponentialHighPassFilter(parameters);
                case "Moving Average":
                    return CreateMovingAverageFilter(parameters);
                case "Median":
                    return CreateMedianFilter(parameters);
                case "Notch":
                    return CreateNotchFilter(parameters);
                case "Absolute":
                    return new AbsoluteFilter();
                case "Squared":
                    return new SquaredFilter();
                default:
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
