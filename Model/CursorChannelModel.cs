using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// View model for CursorChannel that handles cursor value display and delta calculations
    /// Follows MVVM pattern for proper data binding without visual tree traversal
    /// </summary>
    public class CursorChannelModel : INotifyPropertyChanged
    {
        private double? _cursorAValue;
        private double? _cursorBValue;
        private double? _delta;

        /// <summary>
        /// The channel this view model represents
        /// </summary>
        public Channel Channel { get; }

        /// <summary>
        /// Channel label for display
        /// </summary>
        public string Label 
        { 
            get { return Channel.Label; } 
        }

        /// <summary>
        /// Channel color for display
        /// </summary>
        public System.Windows.Media.Color Color 
        { 
            get { return Channel.Color; } 
        }

        /// <summary>
        /// Value at cursor A position
        /// </summary>
        public double? CursorAValue
        {
            get { return _cursorAValue; }
            set
            {
                if (_cursorAValue != value)
                {
                    _cursorAValue = value;
                    OnPropertyChanged(nameof(CursorAValue));
                    OnPropertyChanged(nameof(CursorAText));
                    UpdateDelta();
                }
            }
        }

        /// <summary>
        /// Value at cursor B position
        /// </summary>
        public double? CursorBValue
        {
            get { return _cursorBValue; }
            set
            {
                if (_cursorBValue != value)
                {
                    _cursorBValue = value;
                    OnPropertyChanged(nameof(CursorBValue));
                    OnPropertyChanged(nameof(CursorBText));
                    UpdateDelta();
                }
            }
        }

        /// <summary>
        /// Delta (B - A) between cursor values
        /// </summary>
        public double? Delta
        {
            get { return _delta; }
            private set
            {
                if (_delta != value)
                {
                    _delta = value;
                    OnPropertyChanged(nameof(Delta));
                    OnPropertyChanged(nameof(DeltaText));
                }
            }
        }

        /// <summary>
        /// Formatted text for cursor A value
        /// </summary>
        public string CursorAText
        {
            get { return CursorAValue?.ToString("F1") ?? "-"; }
        }

        /// <summary>
        /// Formatted text for cursor B value
        /// </summary>
        public string CursorBText
        {
            get { return CursorBValue?.ToString("F1") ?? "-"; }
        }

        /// <summary>
        /// Formatted text for delta value
        /// </summary>
        public string DeltaText
        {
            get { return Delta?.ToString("F1") ?? "-"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Creates a new cursor channel view model
        /// </summary>
        /// <param name="channel">The channel this view model represents</param>
        public CursorChannelModel(Channel channel)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            
            // Subscribe to channel property changes for label/color updates
            Channel.PropertyChanged += OnChannelPropertyChanged;
        }

        /// <summary>
        /// Updates both cursor values at once (more efficient than setting individually)
        /// </summary>
        /// <param name="valueA">Value at cursor A position</param>
        /// <param name="valueB">Value at cursor B position</param>
        public void UpdateCursorValues(double? valueA, double? valueB)
        {
            bool aChanged = _cursorAValue != valueA;
            bool bChanged = _cursorBValue != valueB;

            if (aChanged || bChanged)
            {
                _cursorAValue = valueA;
                _cursorBValue = valueB;

                if (aChanged)
                {
                    OnPropertyChanged(nameof(CursorAValue));
                    OnPropertyChanged(nameof(CursorAText));
                }

                if (bChanged)
                {
                    OnPropertyChanged(nameof(CursorBValue));
                    OnPropertyChanged(nameof(CursorBText));
                }

                UpdateDelta();
            }
        }

        private void UpdateDelta()
        {
            if (CursorAValue.HasValue && CursorBValue.HasValue)
                Delta = CursorBValue.Value - CursorAValue.Value;
            else
                Delta = null;
        }

        private void OnChannelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward relevant channel property changes
            switch (e.PropertyName)
            {
                case nameof(Channel.Label):
                case "Settings.Label":
                    OnPropertyChanged(nameof(Label));
                    break;
                case nameof(Channel.Color):
                case "Settings.Color":
                    OnPropertyChanged(nameof(Color));
                    break;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Cleanup when view model is no longer needed
        /// </summary>
        public void Dispose()
        {
            if (Channel != null)
            {
                Channel.PropertyChanged -= OnChannelPropertyChanged;
            }
        }
    }
}