using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a source for virtual channel operations
    /// Can be either a Channel or a constant numeric value
    /// </summary>
  public interface IOperandSource : INotifyPropertyChanged
    {
        /// <summary>
     /// Gets whether this operand is a constant value
        /// </summary>
    bool IsConstant { get; }

    /// <summary>
/// Gets the constant value if this is a constant operand
   /// Returns 0 if this is a channel operand
        /// </summary>
     double ConstantValue { get; }

        /// <summary>
        /// Gets the channel if this is a channel operand
        /// Returns null if this is a constant operand
        /// </summary>
      Channel Channel { get; }

        /// <summary>
        /// Gets a display string for this operand
        /// </summary>
        string DisplayString { get; }
    }

    /// <summary>
    /// Channel-based operand source
    /// </summary>
    public class ChannelOperand : IOperandSource
    {
   private Channel _channel;

        public bool IsConstant => false;
      public double ConstantValue => 0.0;
        
        public Channel Channel
      {
            get { return _channel; }
      set
      {
                if (_channel != value)
          {
               _channel = value;
           OnPropertyChanged(nameof(Channel));
       OnPropertyChanged(nameof(DisplayString));
      }
          }
   }

        public string DisplayString => _channel?.Label ?? "(null)";

        public event PropertyChangedEventHandler PropertyChanged;

        public ChannelOperand(Channel channel)
  {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

   protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayString;
    }

    /// <summary>
    /// Constant value operand source
    /// </summary>
    public class ConstantOperand : IOperandSource
    {
        private double _value;

        public bool IsConstant => true;
 
        public double ConstantValue
   {
        get { return _value; }
       set
            {
            if (Math.Abs(_value - value) > 1e-15)
        {
               _value = value;
          OnPropertyChanged(nameof(ConstantValue));
         OnPropertyChanged(nameof(DisplayString));
     }
      }
        }

        public Channel Channel => null;

  public string DisplayString => _value.ToString("G");

   public event PropertyChangedEventHandler PropertyChanged;

 public ConstantOperand(double value = 0.0)
 {
         _value = value;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => DisplayString;
 }
}
