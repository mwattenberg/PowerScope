using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.Model
{
  /// <summary>
    /// View model for virtual channel configuration
    /// Manages the state and operations for creating/editing virtual channels
    /// Stores configuration data to be used later for actual data computation
    /// Supports both Channel and constant operands
    /// </summary>
    public class VirtualChannelConfig : INotifyPropertyChanged
    {
     private IOperandSource _inputA;
        private IOperandSource _inputB;
  private VirtualChannelOperationType _operation;
        private string _label;
        private ChannelSettings _targetChannelSettings;
        private List<Channel> _availableChannels;

        public IOperandSource InputA
   {
    get { return _inputA; }
          set
        {
       if (_inputA != value)
{
        _inputA = value;
      OnPropertyChanged(nameof(InputA));
           }
  }
        }

    public IOperandSource InputB
        {
          get { return _inputB; }
          set
         {
          if (_inputB != value)
       {
     _inputB = value;
    OnPropertyChanged(nameof(InputB));
      }
          }
     }

        public VirtualChannelOperationType Operation
        {
            get { return _operation; }
            set
            {
     if (_operation != value)
       {
         _operation = value;
          OnPropertyChanged(nameof(Operation));
          }
      }
        }

     public string Label
   {
        get { return _label; }
    set
            {
        if (_label != value)
          {
_label = value;
   OnPropertyChanged(nameof(Label));
        }
        }
        }

        /// <summary>
        /// Reference to the ChannelSettings this virtual channel will update
      /// </summary>
        public ChannelSettings TargetChannelSettings
      {
            get { return _targetChannelSettings; }
     set
    {
  if (_targetChannelSettings != value)
        {
        _targetChannelSettings = value;
                    OnPropertyChanged(nameof(TargetChannelSettings));
             }
            }
     }

        /// <summary>
        /// List of available channels for input selection
        /// </summary>
        public List<Channel> AvailableChannels
      {
 get { return _availableChannels; }
          set
    {
                if (_availableChannels != value)
            {
    _availableChannels = value;
          OnPropertyChanged(nameof(AvailableChannels));
            }
            }
        }

        /// <summary>
        /// Gets the available operations for virtual channels
        /// </summary>
        public static IReadOnlyList<VirtualChannelOperationType> AvailableOperations
        {
            get
            {
                return new List<VirtualChannelOperationType>
  {
     VirtualChannelOperationType.Add,
 VirtualChannelOperationType.Subtract,
       VirtualChannelOperationType.Multiply,
     VirtualChannelOperationType.Divide
              }.AsReadOnly();
  }
        }

     public VirtualChannelConfig(List<Channel> availableChannels = null, ChannelSettings targetChannelSettings = null)
  {
 Operation = VirtualChannelOperationType.Add;
    Label = "Virtual Channel";
         TargetChannelSettings = targetChannelSettings;
   AvailableChannels = availableChannels ?? new List<Channel>();
        }

   /// <summary>
    /// Validates the current configuration
      /// </summary>
        /// <returns>Validation error message, or empty string if valid</returns>
        public string Validate()
        {
        if (InputA == null)
   return "Please select Input A channel or enter a constant.";

          if (InputB == null)
    return "Please select Input B channel or enter a constant.";

            // Check that we're not using the same channel twice (but constants are allowed to be the same)
          if (!InputA.IsConstant && !InputB.IsConstant && InputA == InputB)
   return "Input A and Input B cannot be the same channel.";

     if (string.IsNullOrWhiteSpace(Label))
                return "Please enter a channel label.";

          return string.Empty; // Valid
        }

        public event PropertyChangedEventHandler PropertyChanged;

   protected virtual void OnPropertyChanged(string propertyName)
  {
       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
 }
}
