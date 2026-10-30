using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorChannelView.xaml
    /// Uses MVVM pattern with CursorChannelModel for proper data binding
    /// No imperative UI updates - everything is handled through binding
    /// </summary>
    public partial class CursorChannelView : UserControl
    {
        public CursorChannelView()
        {
            InitializeComponent();
            // DataContext will be set by the parent control (MeasurementBar)
            // to a CursorChannelModel instance
        }

        /// <summary>
        /// Gets the view model if the DataContext is set correctly
        /// </summary>
        public CursorChannelModel ViewModel
        {
            get { return DataContext as CursorChannelModel; }
        }
    }
}