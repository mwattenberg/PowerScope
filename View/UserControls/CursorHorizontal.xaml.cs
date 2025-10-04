using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for CursorHorizontal.xaml
    /// Pure view that displays horizontal cursor data from Cursor model
    /// No longer contains embedded calculation logic - follows MVVM properly
    /// </summary>
    public partial class CursorHorizontal : UserControl
    {
        public CursorHorizontal()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets or sets the cursor model for data binding
        /// </summary>
        public Cursor CursorModel
        {
            get { return DataContext as Cursor; }
            set { DataContext = value; }
        }
    }
}