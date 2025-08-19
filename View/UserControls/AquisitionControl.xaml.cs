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
    /// Interaction logic for AquisitionControl.xaml
    /// </summary>
    public partial class AquisitionControl : UserControl
    {
        public AquisitionControl(int baudrate)
        {
            InitializeComponent();
        }

        public AquisitionControl()
        {
            InitializeComponent();
        }

        public int Baudrate { private get; set; }

        public long BitsPerSecond
        {
            set
            {
                double usage = Baudrate > 0 ? (double)value / Baudrate * 100.0 : 0.0;
                BitsPerSecondTextBox.Text = $"{usage:F0}% ({value})";
            }
        }

        public long SamplesPerSecond
        {
            set => SamplesPerSecondTextBox.Text = value.ToString();
        }

        public long TotalMemorySize
        {
            set => TotalMemorySizeTextBox.Text = value.ToString();
        }

        public double CPULoad
        {
            set => CPULoadTextBox.Text = value.ToString("F1");
        }
    }
}
