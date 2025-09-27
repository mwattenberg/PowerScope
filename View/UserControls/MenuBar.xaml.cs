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

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for MenuBar.xaml
    /// </summary>
    public partial class MenuBar : UserControl
    {
        // Events for menu actions
        public event EventHandler LoadSettingsClicked;
        public event EventHandler SaveSettingsClicked;
        public event EventHandler ExportPlotClicked;
        public event EventHandler PreferencesClicked;
        public event EventHandler AboutClicked;
        public event EventHandler ExitClicked;

        public MenuBar()
        {
            InitializeComponent();
        }

        private void MenuItem_LoadSettings_Click(object sender, RoutedEventArgs e)
        {
            LoadSettingsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void MenuItem_SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void MenuItem_ExportPlot_Click(object sender, RoutedEventArgs e)
        {
            ExportPlotClicked?.Invoke(this, EventArgs.Empty);
        }

        private void MenuItem_Preferences_Click(object sender, RoutedEventArgs e)
        {
            PreferencesClicked?.Invoke(this, EventArgs.Empty);
        }

        private void MenuItem_About_Click(object sender, RoutedEventArgs e)
        {
            AboutClicked?.Invoke(this, EventArgs.Empty);
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            ExitClicked?.Invoke(this, EventArgs.Empty);
            // Also close the application
            Application.Current.Shutdown();
        }
    }
}
