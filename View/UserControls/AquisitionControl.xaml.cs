using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for AquisitionControl.xaml
    /// </summary>
    public partial class AquisitionControl : UserControl
    {

        private readonly System.Timers.Timer _updateAquisitionTimer = new() { Interval = 1000, Enabled = true, AutoReset = true };
        private TimeSpan _prevCpuTime = TimeSpan.Zero;
        private readonly Stopwatch _cpuStopwatch = Stopwatch.StartNew();
        private long _prevCpuStopwatchMs = 0;
        private long _prevSampleCount = 0;
        private long _totalBits = 0;
        private SerialDataStream _dataStream;
        public AquisitionControl(SerialDataStream datastream)
        {
            _dataStream = datastream;
            Baudrate = datastream.SourceSetting.BaudRate;
            startAutoUpdate();
            InitializeComponent();
        }

        public AquisitionControl()
        {
            InitializeComponent();
        }

        private void AquisitionControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _updateAquisitionTimer?.Stop();
            _updateAquisitionTimer?.Dispose();
        }

        public int Baudrate { private get; set; }

        public long BitsPerSecond
        {
            set
            {
                double usage = Baudrate > 0 ? (double)value / Baudrate * 100.0 : 0.0;
                BitsPerSecondTextBox.Text = $"{usage:F0}% ({value})";
            }
        }

        public long SamplesPerSecond
        {
            set => SamplesPerSecondTextBox.Text = value.ToString();
        }

        public long TotalMemorySize
        {
            set => TotalMemorySizeTextBox.Text = value.ToString();
        }

        public double CPULoad
        {
            set => CPULoadTextBox.Text = value.ToString("F1");
        }

        private void startAutoUpdate()
        {
            _updateAquisitionTimer.Elapsed += UpdateAquisition;
        }

        private void UpdateAquisition(object sender, ElapsedEventArgs e)
        {
            // Ensure up-to-date data
            var process = Process.GetCurrentProcess();
            process.Refresh();

            //Update CPU usage
            TimeSpan cpuTime = process.TotalProcessorTime;
            long currentMs = _cpuStopwatch.ElapsedMilliseconds;

            double cpuUsedMs = (cpuTime - _prevCpuTime).TotalMilliseconds;
            long elapsedMs = currentMs - _prevCpuStopwatchMs;

            _prevCpuStopwatchMs = currentMs;
            _prevCpuTime = cpuTime;

            if (elapsedMs < _updateAquisitionTimer.Interval)
                return; // Avoid division by zero if timer interval is too short

            double cpuUsagePercent = (cpuUsedMs / (elapsedMs * Environment.ProcessorCount)) * 1000;

            //Update memory usage
            long memoryBytes = Process.GetCurrentProcess().WorkingSet64;
            double memoryMB = memoryBytes / (1024.0 * 1024.0);

            //Serial port samples
            long samplesPerSecond = (_dataStream.TotalSamples - _prevSampleCount) / (elapsedMs / 1000);
            _prevSampleCount = _dataStream.TotalSamples;

            //Bits per second
            long bitsPerSecond = (_dataStream.TotalBits - _totalBits) / (elapsedMs / 1000);
            _totalBits = _dataStream.TotalBits;

            // Update AquisitionControl
            TotalMemorySize = (long)memoryMB;
            CPULoad = cpuUsagePercent;
            SamplesPerSecond = samplesPerSecond;
            BitsPerSecond = bitsPerSecond;
        }
    }
}
