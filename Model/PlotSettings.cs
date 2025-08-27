using System;
using System.ComponentModel;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Model class that holds all plot-related settings and configuration
    /// </summary>
    public class PlotSettings : INotifyPropertyChanged
    {
        private double _plotUpdateRateFPS = 30.0;
        private int _serialPortUpdateRateHz = 1000;
        private int _lineWidth = 1;
        private bool _antiAliasing = false;
        private bool _showRenderTime = false;
        private int _xmin = 0;
        private int _xmax = 3000;
        private int _ymin = -200;
        private int _ymax = 4000;
        private bool _yAutoScale = true;

        /// <summary>
        /// Plot refresh rate in frames per second
        /// </summary>
        public double PlotUpdateRateFPS
        {
            get { return _plotUpdateRateFPS; }
            set
            {
                if (Math.Abs(_plotUpdateRateFPS - value) > 0.001) // Use tolerance for double comparison
                {
                    _plotUpdateRateFPS = Math.Max(0.1, Math.Min(120.0, value)); // Clamp between 0.1-120 FPS
                    OnPropertyChanged(nameof(PlotUpdateRateFPS));
                }
            }
        }

        /// <summary>
        /// Serial port data update rate in Hz (legacy property, may not be used with multiple streams)
        /// </summary>
        public int SerialPortUpdateRateHz
        {
            get { return _serialPortUpdateRateHz; }
            set
            {
                if (_serialPortUpdateRateHz != value)
                {
                    _serialPortUpdateRateHz = Math.Max(1, Math.Min(10000, value)); // Clamp between 1-10000 Hz
                    OnPropertyChanged(nameof(SerialPortUpdateRateHz));
                }
            }
        }

        /// <summary>
        /// Line width for plot signals
        /// </summary>
        public int LineWidth
        {
            get { return _lineWidth; }
            set
            {
                if (_lineWidth != value)
                {
                    _lineWidth = Math.Max(1, Math.Min(10, value)); // Clamp between 1-10
                    OnPropertyChanged(nameof(LineWidth));
                }
            }
        }

        /// <summary>
        /// Enable or disable anti-aliasing for plot rendering
        /// </summary>
        public bool AntiAliasing
        {
            get { return _antiAliasing; }
            set
            {
                if (_antiAliasing != value)
                {
                    _antiAliasing = value;
                    OnPropertyChanged(nameof(AntiAliasing));
                }
            }
        }

        /// <summary>
        /// Show or hide render time benchmark
        /// </summary>
        public bool ShowRenderTime
        {
            get { return _showRenderTime; }
            set
            {
                if (_showRenderTime != value)
                {
                    _showRenderTime = value;
                    OnPropertyChanged(nameof(ShowRenderTime));
                }
            }
        }

        /// <summary>
        /// Minimum X-axis value
        /// </summary>
        public int Xmin
        {
            get { return _xmin; }
            set
            {
                if (_xmin != value)
                {
                    _xmin = value;
                    OnPropertyChanged(nameof(Xmin));
                }
            }
        }

        /// <summary>
        /// Maximum X-axis value
        /// </summary>
        public int Xmax
        {
            get { return _xmax; }
            set
            {
                if (_xmax != value)
                {
                    _xmax = value;
                    OnPropertyChanged(nameof(Xmax));
                }
            }
        }

        /// <summary>
        /// Minimum Y-axis value
        /// </summary>
        public int Ymin
        {
            get { return _ymin; }
            set
            {
                if (_ymin != value)
                {
                    _ymin = value;
                    OnPropertyChanged(nameof(Ymin));
                }
            }
        }

        /// <summary>
        /// Maximum Y-axis value
        /// </summary>
        public int Ymax
        {
            get { return _ymax; }
            set
            {
                if (_ymax != value)
                {
                    _ymax = value;
                    OnPropertyChanged(nameof(Ymax));
                }
            }
        }

        /// <summary>
        /// Enable or disable Y-axis auto-scaling
        /// </summary>
        public bool YAutoScale
        {
            get { return _yAutoScale; }
            set
            {
                if (_yAutoScale != value)
                {
                    _yAutoScale = value;
                    OnPropertyChanged(nameof(YAutoScale));
                }
            }
        }

        /// <summary>
        /// Timer interval in milliseconds (calculated from PlotUpdateRateFPS)
        /// </summary>
        public double TimerInterval => 1000.0 / PlotUpdateRateFPS;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}