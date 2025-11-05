using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Defines the types of operations that can be performed on virtual channels
    /// </summary>
    public enum VirtualChannelOperationType
    {
        Add,
        Subtract,
    Multiply,
        Divide
    }

    /// <summary>
    /// Configuration for a virtual channel that combines two input channels
    /// </summary>
    public class VirtualChannelOperation : INotifyPropertyChanged
    {
   private Channel _inputA;
  private Channel _inputB;
private VirtualChannelOperationType _operation;
        private string _label = "Virtual Channel";

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

        public VirtualChannelOperation()
   {
        Operation = VirtualChannelOperationType.Add;
        }

   /// <summary>
        /// Performs the configured operation on the two input channels at the given sample index
        /// </summary>
  /// <param name="sampleIndexA">Sample index for input A</param>
     /// <param name="sampleIndexB">Sample index for input B</param>
        /// <returns>Result of the operation</returns>
    public double PerformOperation(double valueA, double valueB)
        {
            return Operation switch
     {
           VirtualChannelOperationType.Add => valueA + valueB,
     VirtualChannelOperationType.Subtract => valueA - valueB,
      VirtualChannelOperationType.Multiply => valueA * valueB,
      VirtualChannelOperationType.Divide => valueB != 0 ? valueA / valueB : 0,
        _ => 0
   };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
    }
}
