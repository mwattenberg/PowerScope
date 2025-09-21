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
using System.Windows.Shapes;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for FFT.xaml
    /// FFT Spectrum window for displaying frequency domain analysis
    /// </summary>
    public partial class FFT : Window
    {
        public FFT()
        {
            InitializeComponent();
            // Initialize FFT plot when the window is loaded
            Loaded += FFT_Loaded;
        }

        /// <summary>
        /// Handle window loaded event
        /// </summary>
        private void FFT_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeFFTPlot();
        }

        /// <summary>
        /// Initialize the FFT plot with appropriate settings for spectrum display
        /// </summary>
        public void InitializeFFTPlot()
        {
            // Apply dark theme similar to main plot
            WpfPlotFFT.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            WpfPlotFFT.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
            WpfPlotFFT.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            WpfPlotFFT.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
            WpfPlotFFT.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
            WpfPlotFFT.Plot.Axes.Left.Label.Text = "Magnitude (dB)";
            WpfPlotFFT.Plot.Title("FFT Spectrum");
            
            // Disable auto scaling to prevent automatic axis adjustments
            WpfPlotFFT.Plot.Axes.ContinuouslyAutoscale = false;
            
            // Setup user input for zoom/pan
            SetupPlotUserInput();
            
            WpfPlotFFT.Refresh();
        }

        /// <summary>
        /// Setup user input handlers for the FFT plot
        /// </summary>
        private void SetupPlotUserInput()
        {
            // Clear existing input handlers
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Clear();
            WpfPlotFFT.UserInputProcessor.IsEnabled = true;
            
            // Left-click-drag pan
            var panButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            var panResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(panButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(panResponse);

            // Middle-click-drag zoom rectangle
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Middle;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            // Right-click auto-scale
            var autoscaleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var autoscaleResponse = new ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale(autoscaleButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(autoscaleResponse);

            // Mouse wheel zoom with proper constructor parameters
            var wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(
                ScottPlot.Interactivity.StandardKeys.Shift, 
                ScottPlot.Interactivity.StandardKeys.Control);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }

        /// <summary>
        /// Handle close button click
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Handle title bar drag for window movement
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}
