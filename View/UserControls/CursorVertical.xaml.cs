using System;
using System.ComponentModel;
using System.Windows.Controls;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorVertical.xaml
    /// Displays vertical cursor measurements like sample position, time, and frequency
    /// </summary>
    public partial class CursorVertical : UserControl, INotifyPropertyChanged
    {
        private double _cursorASamplePosition;
        private double _cursorBSamplePosition;
        private double _sampleRate;
        private double _cursorATime;
        private double _cursorBTime;
        private double _timeDelta;
        private double _frequency;
        private double _sampleDelta;
        private bool _hasValidSampleRate;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Sample position of cursor A
        /// </summary>
        public double CursorASamplePosition
        {
            get 
            { 
                return _cursorASamplePosition; 
            }
            set
            {
                if (_cursorASamplePosition != value)
                {
                    _cursorASamplePosition = value;
                    UpdateCalculations();
                    OnPropertyChanged(nameof(CursorASamplePosition));
                }
            }
        }

        /// <summary>
        /// Sample position of cursor B
        /// </summary>
        public double CursorBSamplePosition
        {
            get 
            { 
                return _cursorBSamplePosition; 
            }
            set
            {
                if (_cursorBSamplePosition != value)
                {
                    _cursorBSamplePosition = value;
                    UpdateCalculations();
                    OnPropertyChanged(nameof(CursorBSamplePosition));
                }
            }
        }

        /// <summary>
        /// Current sample rate in samples per second
        /// </summary>
        public double SampleRate
        {
            get 
            { 
                return _sampleRate; 
            }
            set
            {
                if (_sampleRate != value)
                {
                    _sampleRate = value;
                    UpdateCalculations();
                    OnPropertyChanged(nameof(SampleRate));
                }
            }
        }

        /// <summary>
        /// Indicates whether we have a valid sample rate for time and frequency calculations
        /// </summary>
        public bool HasValidSampleRate
        {
            get 
            { 
                return _hasValidSampleRate; 
            }
            private set
            {
                if (_hasValidSampleRate != value)
                {
                    _hasValidSampleRate = value;
                    OnPropertyChanged(nameof(HasValidSampleRate));
                }
            }
        }

        /// <summary>
        /// Sample difference between cursors (B - A)
        /// </summary>
        public double SampleDelta
        {
            get 
            { 
                return _sampleDelta; 
            }
            private set
            {
                if (_sampleDelta != value)
                {
                    _sampleDelta = value;
                    OnPropertyChanged(nameof(SampleDelta));
                }
            }
        }

        /// <summary>
        /// Time position of cursor A in seconds
        /// </summary>
        public double CursorATime
        {
            get 
            { 
                return _cursorATime; 
            }
            private set
            {
                if (_cursorATime != value)
                {
                    _cursorATime = value;
                    OnPropertyChanged(nameof(CursorATime));
                }
            }
        }

        /// <summary>
        /// Time position of cursor B in seconds
        /// </summary>
        public double CursorBTime
        {
            get 
            { 
                return _cursorBTime; 
            }
            private set
            {
                if (_cursorBTime != value)
                {
                    _cursorBTime = value;
                    OnPropertyChanged(nameof(CursorBTime));
                }
            }
        }

        /// <summary>
        /// Time difference between cursors (period T)
        /// </summary>
        public double TimeDelta
        {
            get 
            { 
                return _timeDelta; 
            }
            private set
            {
                if (_timeDelta != value)
                {
                    _timeDelta = value;
                    OnPropertyChanged(nameof(TimeDelta));
                }
            }
        }

        /// <summary>
        /// Frequency calculated from time delta (1/T)
        /// </summary>
        public double Frequency
        {
            get 
            { 
                return _frequency; 
            }
            private set
            {
                if (_frequency != value)
                {
                    _frequency = value;
                    OnPropertyChanged(nameof(Frequency));
                }
            }
        }

        public CursorVertical()
        {
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Updates cursor data with current positions and sample rate
        /// </summary>
        /// <param name="cursorASample">Cursor A sample position</param>
        /// <param name="cursorBSample">Cursor B sample position</param>
        /// <param name="sampleRate">Current sample rate in Hz</param>
        public void UpdateCursorData(double cursorASample, double cursorBSample, double sampleRate)
        {
            CursorASamplePosition = cursorASample;
            CursorBSamplePosition = cursorBSample;
            SampleRate = sampleRate;
        }

        /// <summary>
        /// Updates cursor sample positions when no sample rate is available
        /// </summary>
        /// <param name="cursorASample">Cursor A sample position</param>
        /// <param name="cursorBSample">Cursor B sample position</param>
        public void UpdateCursorSamplePositions(double cursorASample, double cursorBSample)
        {
            CursorASamplePosition = cursorASample;
            CursorBSamplePosition = cursorBSample;
            
            // Mark sample rate as invalid
            _sampleRate = 0;
            HasValidSampleRate = false;
            
            // Update sample calculations but not time-based ones
            SampleDelta = _cursorBSamplePosition - _cursorASamplePosition;
            
            // Clear time and frequency data
            CursorATime = 0;
            CursorBTime = 0;
            TimeDelta = 0;
            Frequency = 0;
            
            OnPropertyChanged(nameof(SampleRate));
        }

        /// <summary>
        /// Updates all calculations when cursor positions or sample rate change
        /// </summary>
        private void UpdateCalculations()
        {
            // Calculate sample difference
            SampleDelta = _cursorBSamplePosition - _cursorASamplePosition;

            if (_sampleRate <= 0)
            {
                HasValidSampleRate = false;
                CursorATime = 0;
                CursorBTime = 0;
                TimeDelta = 0;
                Frequency = 0;
                return;
            }

            HasValidSampleRate = true;

            // Calculate time positions
            CursorATime = _cursorASamplePosition / _sampleRate;
            CursorBTime = _cursorBSamplePosition / _sampleRate;
            
            // Calculate time difference (period)
            double deltaTime = Math.Abs(_cursorBTime - _cursorATime);
            TimeDelta = deltaTime;
            
            // Calculate frequency (1/T)
            if (deltaTime > 0)
                Frequency = 1.0 / deltaTime;
            else
                Frequency = 0;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}