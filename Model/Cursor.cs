using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Unified cursor model that manages all cursor-related state and calculations
    /// Centralizes cursor logic and provides proper MVVM separation
    /// </summary>
    public class Cursor : INotifyPropertyChanged, IDisposable
    {
        private double? _verticalCursorA;
        private double? _verticalCursorB;
        private double? _horizontalCursorA;
        private double? _horizontalCursorB;
        private CursorMode _activeMode;
        private double _sampleRate;
        private bool _hasValidSampleRate;
        private bool _disposed = false;

        // Calculated values for vertical cursors
        private double _verticalSampleDelta;
        private double _verticalTimeA;
        private double _verticalTimeB;
        private double _verticalTimeDelta;
        private double _verticalFrequency;

        // Calculated values for horizontal cursors
        private double _horizontalValueDelta;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Collection of per-channel cursor data
        /// </summary>
        public ObservableCollection<CursorChannelModel> ChannelData { get; private set; }

        /// <summary>
        /// Current cursor mode (Vertical, Horizontal, or None)
        /// </summary>
        public CursorMode ActiveMode
        {
            get { return _activeMode; }
            set
            {
                if (_activeMode != value)
                {
                    _activeMode = value;
                    OnPropertyChanged(nameof(ActiveMode));
                }
            }
        }

        /// <summary>
        /// Position of vertical cursor A (sample index)
        /// </summary>
        public double? VerticalCursorA
        {
            get { return _verticalCursorA; }
            set
            {
                if (_verticalCursorA != value)
                {
                    _verticalCursorA = value;
                    OnPropertyChanged(nameof(VerticalCursorA));
                    UpdateVerticalCalculations();
                }
            }
        }

        /// <summary>
        /// Position of vertical cursor B (sample index)
        /// </summary>
        public double? VerticalCursorB
        {
            get { return _verticalCursorB; }
            set
            {
                if (_verticalCursorB != value)
                {
                    _verticalCursorB = value;
                    OnPropertyChanged(nameof(VerticalCursorB));
                    UpdateVerticalCalculations();
                }
            }
        }

        /// <summary>
        /// Position of horizontal cursor A (Y value)
        /// </summary>
        public double? HorizontalCursorA
        {
            get { return _horizontalCursorA; }
            set
            {
                if (_horizontalCursorA != value)
                {
                    _horizontalCursorA = value;
                    OnPropertyChanged(nameof(HorizontalCursorA));
                    UpdateHorizontalCalculations();
                }
            }
        }

        /// <summary>
        /// Position of horizontal cursor B (Y value)
        /// </summary>
        public double? HorizontalCursorB
        {
            get { return _horizontalCursorB; }
            set
            {
                if (_horizontalCursorB != value)
                {
                    _horizontalCursorB = value;
                    OnPropertyChanged(nameof(HorizontalCursorB));
                    UpdateHorizontalCalculations();
                }
            }
        }

        /// <summary>
        /// Current sample rate for time calculations
        /// </summary>
        public double SampleRate
        {
            get { return _sampleRate; }
            set
            {
                if (_sampleRate != value)
                {
                    _sampleRate = value;
                    _hasValidSampleRate = value > 0;
                    OnPropertyChanged(nameof(SampleRate));
                    OnPropertyChanged(nameof(HasValidSampleRate));
                    UpdateVerticalCalculations();
                }
            }
        }

        /// <summary>
        /// Whether we have a valid sample rate for time calculations
        /// </summary>
        public bool HasValidSampleRate
        {
            get { return _hasValidSampleRate; }
        }

        // Vertical cursor calculated properties
        public double VerticalSampleDelta
        {
            get { return _verticalSampleDelta; }
            private set
            {
                if (_verticalSampleDelta != value)
                {
                    _verticalSampleDelta = value;
                    OnPropertyChanged(nameof(VerticalSampleDelta));
                }
            }
        }

        public double VerticalTimeA
        {
            get { return _verticalTimeA; }
            private set
            {
                if (_verticalTimeA != value)
                {
                    _verticalTimeA = value;
                    OnPropertyChanged(nameof(VerticalTimeA));
                }
            }
        }

        public double VerticalTimeB
        {
            get { return _verticalTimeB; }
            private set
            {
                if (_verticalTimeB != value)
                {
                    _verticalTimeB = value;
                    OnPropertyChanged(nameof(VerticalTimeB));
                }
            }
        }

        public double VerticalTimeDelta
        {
            get { return _verticalTimeDelta; }
            private set
            {
                if (_verticalTimeDelta != value)
                {
                    _verticalTimeDelta = value;
                    OnPropertyChanged(nameof(VerticalTimeDelta));
                }
            }
        }

        public double VerticalFrequency
        {
            get { return _verticalFrequency; }
            private set
            {
                if (_verticalFrequency != value)
                {
                    _verticalFrequency = value;
                    OnPropertyChanged(nameof(VerticalFrequency));
                }
            }
        }

        // Horizontal cursor calculated properties
        public double HorizontalValueDelta
        {
            get { return _horizontalValueDelta; }
            private set
            {
                if (_horizontalValueDelta != value)
                {
                    _horizontalValueDelta = value;
                    OnPropertyChanged(nameof(HorizontalValueDelta));
                }
            }
        }

        public Cursor()
        {
            ChannelData = new ObservableCollection<CursorChannelModel>();
            ActiveMode = CursorMode.None;
        }

        /// <summary>
        /// Updates both vertical cursor positions at once
        /// </summary>
        public void UpdateVerticalCursors(double cursorA, double cursorB, double sampleRate)
        {
            _verticalCursorA = cursorA;
            _verticalCursorB = cursorB;
            _sampleRate = sampleRate;
            _hasValidSampleRate = sampleRate > 0;

            OnPropertyChanged(nameof(VerticalCursorA));
            OnPropertyChanged(nameof(VerticalCursorB));
            OnPropertyChanged(nameof(SampleRate));
            OnPropertyChanged(nameof(HasValidSampleRate));
            
            UpdateVerticalCalculations();
        }

        /// <summary>
        /// Updates both horizontal cursor positions at once
        /// </summary>
        public void UpdateHorizontalCursors(double cursorA, double cursorB)
        {
            _horizontalCursorA = cursorA;
            _horizontalCursorB = cursorB;

            OnPropertyChanged(nameof(HorizontalCursorA));
            OnPropertyChanged(nameof(HorizontalCursorB));
            
            UpdateHorizontalCalculations();
        }

        /// <summary>
        /// Updates the channel data collection based on available channels
        /// </summary>
        public void UpdateChannelData(ObservableCollection<Channel> channels)
        {
            // Clear existing models
            foreach (CursorChannelModel model in ChannelData)
            {
                model.Dispose();
            }
            ChannelData.Clear();

            if (channels == null)
                return;

            // Create new models for all channels
            foreach (Channel channel in channels)
            {
                CursorChannelModel model = new CursorChannelModel(channel);
                ChannelData.Add(model);
            }
        }

        /// <summary>
        /// Updates channel cursor values with data from PlotManager
        /// </summary>
        public void UpdateChannelValues(PlotManager plotManager)
        {
            if (plotManager == null || !_verticalCursorA.HasValue || !_verticalCursorB.HasValue)
                return;

            int sampleIndexA = (int)Math.Round(_verticalCursorA.Value);
            int sampleIndexB = (int)Math.Round(_verticalCursorB.Value);

            foreach (CursorChannelModel model in ChannelData)
            {
                double? valueA = plotManager.GetPlotDataAt(model.Channel, sampleIndexA);
                double? valueB = plotManager.GetPlotDataAt(model.Channel, sampleIndexB);
                
                model.UpdateCursorValues(valueA, valueB);
            }
        }

        private void UpdateVerticalCalculations()
        {
            if (!_verticalCursorA.HasValue || !_verticalCursorB.HasValue)
            {
                VerticalSampleDelta = 0;
                VerticalTimeA = 0;
                VerticalTimeB = 0;
                VerticalTimeDelta = 0;
                VerticalFrequency = 0;
                return;
            }

            // Calculate sample difference
            VerticalSampleDelta = _verticalCursorB.Value - _verticalCursorA.Value;

            if (!_hasValidSampleRate)
            {
                VerticalTimeA = 0;
                VerticalTimeB = 0;
                VerticalTimeDelta = 0;
                VerticalFrequency = 0;
                return;
            }

            // Calculate time positions
            VerticalTimeA = _verticalCursorA.Value / _sampleRate;
            VerticalTimeB = _verticalCursorB.Value / _sampleRate;
            
            // Calculate time difference (period)
            double deltaTime = Math.Abs(VerticalTimeB - VerticalTimeA);
            VerticalTimeDelta = deltaTime;
            
            // Calculate frequency (1/T)
            if (deltaTime > 0)
                VerticalFrequency = 1.0 / deltaTime;
            else
                VerticalFrequency = 0;
        }

        private void UpdateHorizontalCalculations()
        {
            if (!_horizontalCursorA.HasValue || !_horizontalCursorB.HasValue)
            {
                HorizontalValueDelta = 0;
                return;
            }

            HorizontalValueDelta = _horizontalCursorB.Value - _horizontalCursorA.Value;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                foreach (CursorChannelModel model in ChannelData)
                {
                    model.Dispose();
                }
                ChannelData.Clear();
            }
        }
    }

    /// <summary>
    /// Enumeration for cursor modes
    /// </summary>
    public enum CursorMode
    {
        None,
        Vertical,
        Horizontal
    }
}