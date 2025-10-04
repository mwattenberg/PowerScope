using System;
using System.ComponentModel;
using System.Windows.Controls;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorHorizontal.xaml
    /// Displays horizontal cursor measurements with Y-axis values and delta
    /// </summary>
    public partial class CursorHorizontal : UserControl, INotifyPropertyChanged
    {
        private double _cursorAYValue;
        private double _cursorBYValue;
        private double _yValueDelta;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Y-axis value of cursor A
        /// </summary>
        public double CursorAYValue
        {
            get 
            { 
                return _cursorAYValue; 
            }
            set
            {
                if (_cursorAYValue != value)
                {
                    _cursorAYValue = value;
                    UpdateCalculations();
                    OnPropertyChanged(nameof(CursorAYValue));
                }
            }
        }

        /// <summary>
        /// Y-axis value of cursor B
        /// </summary>
        public double CursorBYValue
        {
            get 
            { 
                return _cursorBYValue; 
            }
            set
            {
                if (_cursorBYValue != value)
                {
                    _cursorBYValue = value;
                    UpdateCalculations();
                    OnPropertyChanged(nameof(CursorBYValue));
                }
            }
        }

        /// <summary>
        /// Y-axis difference between horizontal cursors (B - A)
        /// </summary>
        public double YValueDelta
        {
            get 
            { 
                return _yValueDelta; 
            }
            private set
            {
                if (_yValueDelta != value)
                {
                    _yValueDelta = value;
                    OnPropertyChanged(nameof(YValueDelta));
                }
            }
        }

        public CursorHorizontal()
        {
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Updates cursor Y-axis values for horizontal cursors
        /// </summary>
        /// <param name="cursorAYValue">Cursor A Y-axis value</param>
        /// <param name="cursorBYValue">Cursor B Y-axis value</param>
        public void UpdateCursorData(double cursorAYValue, double cursorBYValue)
        {
            CursorAYValue = cursorAYValue;
            CursorBYValue = cursorBYValue;
        }

        /// <summary>
        /// Updates Y-axis delta calculation
        /// </summary>
        private void UpdateCalculations()
        {
            // Calculate Y-axis difference
            YValueDelta = _cursorBYValue - _cursorAYValue;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}