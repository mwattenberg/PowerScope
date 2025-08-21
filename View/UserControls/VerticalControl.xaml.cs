using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for VerticalControl.xaml
    /// </summary>
    public partial class VerticalControl : UserControl
    {
        private int _max = 1000;
        public int Max
        {
            get => _max;
            set
            {
                if (_max != value)
                {
                    _max = value;
                    if (MaxTextBox != null)
                        MaxTextBox.Text = _max.ToString();
                }
            }
        }

        private int _min = -1000;
        public int Min
        {
            get => _min;
            set
            {
                if (_min != value)
                {
                    _min = value;
                    if (MinTextBox != null)
                        MinTextBox.Text = _min.ToString();
                }
            }
        }

        private bool _isAutoScale;
        public bool IsAutoScale
        {
            get => _isAutoScale;
            set
            {
                if (_isAutoScale != value)
                {
                    _isAutoScale = value;
                    if (AutoScaleCheckBox != null)
                        AutoScaleCheckBox.IsChecked = _isAutoScale;
                }
            }
        }

        public event EventHandler<int>? MaxValueChanged;
        public event EventHandler<int>? MinValueChanged;
        public event EventHandler<bool>? AutoScaleChanged;
        public VerticalControl()
        {
            InitializeComponent();
        }

        private void MinTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MinTextBox.Text, out int minValue))
            {
                if (minValue != Min)
                {
                    Min = Math.Min(minValue, Max - 1); // Ensure Min is less than Max
                    MinValueChanged?.Invoke(this, Min);
                }
            }
        }

        private void MaxTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MaxTextBox.Text, out int maxValue))
            {
                if (maxValue != Max)
                {
                    Max = Math.Max(maxValue, Min + 1); // Ensure Max is greater than Min
                    MaxValueChanged?.Invoke(this, Max);
                }
            }
        }

        private void AutoScaleCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        { 
            bool? temp = AutoScaleCheckBox.IsChecked.Value;
            if (temp == null)
            {
                IsAutoScale = false; // Default to false if unchecked
            }
            else
            {
                IsAutoScale = AutoScaleCheckBox.IsChecked.Value;
                AutoScaleChanged?.Invoke(this, IsAutoScale);
            }
            
        }
    }
}
