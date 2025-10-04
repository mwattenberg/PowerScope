using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorChannel.xaml
    /// Now uses MVVM pattern with CursorChannelViewModel for proper data binding
    /// No more imperative UI updates - everything is handled through binding
    /// </summary>
    public partial class CursorChannel : UserControl
    {
        public CursorChannel()
        {
            InitializeComponent();
            // DataContext will be set by the parent control (MeasurementBar)
            // to a CursorChannelViewModel instance
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
