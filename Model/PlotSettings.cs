using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Predefined FPS values for plot update rate
    /// </summary>
    public enum PlotFpsOption
    {
        [Description("120")]
        Fps120 = 120,
        
        [Description("60")]
        Fps60 = 60,
        
        [Description("30")]
        Fps30 = 30,
        
        [Description("15")]
        Fps15 = 15,
        
        [Description("5")]
        Fps5 = 5,
        
        [Description("2")]
        Fps2 = 2,
        
        [Description("1")]
        Fps1 = 1
    }

    /// <summary>
    /// Extension methods for PlotFpsOption enum
    /// </summary>
    public static class PlotFpsOptionExtensions
    {
        /// <summary>
        /// Converts PlotFpsOption to actual double FPS value
        /// </summary>
        public static double ToFpsValue(this PlotFpsOption option)
        {
            return option switch
            {
                PlotFpsOption.Fps120 => 120.0,
                PlotFpsOption.Fps60 => 60.0,
                PlotFpsOption.Fps30 => 30.0,
                PlotFpsOption.Fps15 => 15.0,
                PlotFpsOption.Fps5 => 5.0,
                PlotFpsOption.Fps2 => 2.0,
                PlotFpsOption.Fps1 => 1.0,
                _ => 30.0 // Default fallback
            };
        }

        /// <summary>
        /// Gets the display text for the FPS option
        /// </summary>
        public static string GetDisplayText(this PlotFpsOption option)
        {
            var field = option.GetType().GetField(option.ToString());
            var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
            return attribute?.Description ?? option.ToString();
        }

        /// <summary>
        /// Converts a double FPS value to the closest PlotFpsOption
        /// </summary>
        public static PlotFpsOption FromFpsValue(double fpsValue)
        {
            // Find the closest enum value
            var options = Enum.GetValues<PlotFpsOption>();
            PlotFpsOption closest = PlotFpsOption.Fps30;
            double minDifference = double.MaxValue;

            foreach (var option in options)
            {
                double difference = Math.Abs(option.ToFpsValue() - fpsValue);
                if (difference < minDifference)
                {
                    minDifference = difference;
                    closest = option;
                }
            }

            return closest;
        }
    }

    /// <summary>
    /// Trigger edge type for edge trigger mode
    /// </summary>
    public enum TriggerEdgeType
    {
        Rising,
        Falling
    }

    /// <summary>
    /// Model class that holds all plot-related settings and configuration
    /// </summary>
    public class PlotSettings : INotifyPropertyChanged
    {
        private PlotFpsOption _plotUpdateRateFps = PlotFpsOption.Fps30;
        private int _lineWidth = 1;
        private bool _antiAliasing = false;
        private bool _showRenderTime = false;
        private int _xmin = 0;
        private int _xmax = 3000;
        private int _ymin = -200;
        private int _ymax = 4000;
        private bool _yAutoScale = true;
        private int _bufferSize = 50000;
        private bool _enableEdgeTrigger = false;
        private Channel _triggerSourceChannel = null;
        private TriggerEdgeType _triggerEdge = TriggerEdgeType.Rising;
        private bool _singleShotMode = false;
        private double _triggerLevel = 0.0;
        private int _triggerPosition = 100;

        /// <summary>
        /// Plot refresh rate option (enum-based)
        /// </summary>
        public PlotFpsOption PlotUpdateRateFpsOption
        {
            get { return _plotUpdateRateFps; }
            set
            {
                if (_plotUpdateRateFps != value)
                {
                    _plotUpdateRateFps = value;
                    OnPropertyChanged(nameof(PlotUpdateRateFpsOption));
                    OnPropertyChanged(nameof(PlotUpdateRateFPS)); // Notify computed property
                }
            }
        }

        /// <summary>
        /// Plot refresh rate in frames per second (computed from enum)
        /// </summary>
        public double PlotUpdateRateFPS
        {
            get { return _plotUpdateRateFps.ToFpsValue(); }
            set
            {
                var newOption = PlotFpsOptionExtensions.FromFpsValue(value);
                if (_plotUpdateRateFps != newOption)
                {
                    _plotUpdateRateFps = newOption;
                    OnPropertyChanged(nameof(PlotUpdateRateFPS));
                    OnPropertyChanged(nameof(PlotUpdateRateFpsOption));
                }
            }
        }

        /// <summary>
        /// Serial port data update rate in Hz (fixed value)
        /// </summary>
        public int SerialPortUpdateRateHz => 300;

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
        /// Buffer size for the plot
        /// </summary>
        public int BufferSize
        {
            get { return _bufferSize; }
            set
            {
                if (_bufferSize != value)
                {
                    _bufferSize = value;
                    OnPropertyChanged(nameof(BufferSize));
                }
            }
        }

        /// <summary>
        /// Enable or disable edge trigger mode for data acquisition
        /// When false (default), roll trigger is active - plot continuously updates
        /// When true, plot updates only when trigger condition is met
        /// </summary>
        public bool EnableEdgeTrigger
        {
            get { return _enableEdgeTrigger; }
            set
            {
                if (_enableEdgeTrigger != value)
                {
                    _enableEdgeTrigger = value;
                    OnPropertyChanged(nameof(EnableEdgeTrigger));
                    // Notify button color properties
                    OnPropertyChanged(nameof(NormalModeButtonBackground));
                    OnPropertyChanged(nameof(SingleModeButtonBackground));
                }
            }
        }

        /// <summary>
        /// The channel to use as trigger source
        /// Null means use first enabled channel (default behavior)
        /// </summary>
        public Channel TriggerSourceChannel
        {
            get { return _triggerSourceChannel; }
            set
            {
                if (_triggerSourceChannel != value)
                {
                    _triggerSourceChannel = value;
                    OnPropertyChanged(nameof(TriggerSourceChannel));
                }
            }
        }

        /// <summary>
        /// Trigger edge type (Rising or Falling)
        /// </summary>
        public TriggerEdgeType TriggerEdge
        {
            get { return _triggerEdge; }
            set
            {
                if (_triggerEdge != value)
                {
                    _triggerEdge = value;
                    OnPropertyChanged(nameof(TriggerEdge));
                    OnPropertyChanged(nameof(TriggerOnRisingEdge)); // Notify backward-compatible property
                }
            }
        }

        /// <summary>
        /// Backward-compatible boolean property for rising edge trigger
        /// True = Rising edge, False = Falling edge
        /// </summary>
        public bool TriggerOnRisingEdge
        {
            get { return _triggerEdge == TriggerEdgeType.Rising; }
            set
            {
                TriggerEdge = value ? TriggerEdgeType.Rising : TriggerEdgeType.Falling;
            }
        }

        /// <summary>
        /// Single-shot trigger mode - when true, trigger fires once then requires re-arm
        /// </summary>
        public bool SingleShotMode
        {
            get { return _singleShotMode; }
            set
            {
                if (_singleShotMode != value)
                {
                    _singleShotMode = value;
                    OnPropertyChanged(nameof(SingleShotMode));
                    // Notify button color properties
                    OnPropertyChanged(nameof(NormalModeButtonBackground));
                    OnPropertyChanged(nameof(SingleModeButtonBackground));
                }
            }
        }

        /// <summary>
        /// Trigger level (Y-axis value) for edge detection
        /// </summary>
        public double TriggerLevel
        {
            get { return _triggerLevel; }
            set
            {
                if (_triggerLevel != value)
                {
                    _triggerLevel = value;
                    OnPropertyChanged(nameof(TriggerLevel));
                }
            }
        }

        /// <summary>
        /// Trigger position (X-axis sample index) where trigger point appears on display
        /// </summary>
        public int TriggerPosition
        {
            get { return _triggerPosition; }
            set
            {
                if (_triggerPosition != value)
                {
                    _triggerPosition = value;
                    OnPropertyChanged(nameof(TriggerPosition));
                }
            }
        }

        /// <summary>
        /// Timer interval in milliseconds (calculated from PlotUpdateRateFPS)
        /// </summary>
        public double TimerInterval => 1000.0 / PlotUpdateRateFPS;

        #region Trigger Mode Button Colors (Computed Properties for MVVM)

        /// <summary>
        /// Gets the background brush for the Normal trigger mode button.
        /// LimeGreen when NOT single-shot AND trigger enabled, DarkGray otherwise.
        /// </summary>
        public System.Windows.Media.Brush NormalModeButtonBackground
        {
            get
            {
                if (!_enableEdgeTrigger)
                {
                    // Return the default button background from global style
                    return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PlotSettings_TitleBarBrush"] 
                        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray);
                }

                return !_singleShotMode
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen)
                    : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PlotSettings_TitleBarBrush"]
                        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray);
            }
        }

        /// <summary>
        /// Gets the background brush for the Single trigger mode button.
        /// LimeGreen when single-shot AND trigger enabled, DarkGray otherwise.
        /// </summary>
        public System.Windows.Media.Brush SingleModeButtonBackground
        {
            get
            {
                if (!_enableEdgeTrigger)
                {
                    // Return the default button background from global style
                    return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PlotSettings_TitleBarBrush"]
                        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray);
                }

                return _singleShotMode
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen)
                    : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["PlotSettings_TitleBarBrush"]
                        ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray);
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}