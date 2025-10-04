using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorVertical.xaml
    /// Pure view that displays vertical cursor data from Cursor model
    /// No longer contains embedded calculation logic - follows MVVM properly
    /// </summary>
    public partial class CursorVertical : UserControl
    {
        public CursorVertical()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets or sets the cursor model for data binding
        /// </summary>
        public PowerScope.Model.Cursor CursorModel
        {
            get { return DataContext as PowerScope.Model.Cursor; }
            set { DataContext = value; }
        }
    }
}