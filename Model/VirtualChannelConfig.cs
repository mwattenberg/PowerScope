using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace PowerScope.Model
{
    public enum VirtualChannelOperationType
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    /// <summary>
    /// View model for virtual channel configuration
    /// Manages the state and operations for creating/editing virtual channels
    /// Stores configuration data to be used later for actual data computation
    /// InputA and InputB are now direct Channel references (constants wrapped in ConstantDataStream)
    /// </summary>
    public class VirtualChannelConfig : INotifyPropertyChanged
    {
        private Channel _inputA;
        private Channel _inputB;
        private VirtualChannelOperationType _operation;
        private string _label;
        private ChannelSettings _targetChannelSettings;
        private List<Channel> _availableChannels;

        public Channel InputA
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

        public Channel InputB
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

        public VirtualChannelConfig(List<Channel> availableChannels = null, ChannelSettings targetChannelSettings = null)
        {
            Operation = VirtualChannelOperationType.Add;
            Label = "Virtual Channel";
            TargetChannelSettings = targetChannelSettings;
            AvailableChannels = availableChannels ?? new List<Channel>();
        }

        public string Validate()
        {
            if (InputA == null)
                return "Please select Input A channel or enter a constant.";

            if (InputB == null)
                return "Please select Input B channel or enter a constant.";

            if (InputA != null && InputB != null)
            {
                if (InputA.OwnerStream is ConstantDataStream && InputB.OwnerStream is ConstantDataStream)
                {
                }
                else if (InputA == InputB)
                {
                    return "Input A and Input B cannot be the same channel.";
                }
            }

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
