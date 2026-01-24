using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for TriggerControl.xaml
    /// </summary>
    public partial class TriggerControl : UserControl
    {
        private ObservableCollection<Channel> _availableChannels;
        private PlotManager _plotManager;

        /// <summary>
        /// PlotSettings instance used as DataContext
        /// </summary>
        public PlotSettings Settings
        {
            get { return DataContext as PlotSettings; }
            set { DataContext = value; }
        }

        /// <summary>
        /// Reference to PlotManager for mode control
        /// </summary>
        public PlotManager PlotManager
        {
            get { return _plotManager; }
            set { _plotManager = value; }
        }

        /// <summary>
        /// Collection of available channels for trigger selection
        /// This needs to be set from MainWindow when channels are loaded
        /// </summary>
        public ObservableCollection<Channel> AvailableChannels
        {
            get { return _availableChannels; }
            set
            {
                _availableChannels = value;
                UpdateTriggerChannelOptions();
            }
        }

        public TriggerControl()
        {
            InitializeComponent();

            // Subscribe to DataContext changes
            DataContextChanged += TriggerControl_DataContextChanged;
            Loaded += TriggerControl_Loaded;
        }

        private void TriggerControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTriggerEdgeSelection();
        }

        private void TriggerControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old settings
            if (e.OldValue is PlotSettings oldSettings)
            {
                oldSettings.PropertyChanged -= Settings_PropertyChanged;
            }

            // Subscribe to new settings
            if (e.NewValue is PlotSettings newSettings)
            {
                newSettings.PropertyChanged += Settings_PropertyChanged;
                UpdateTriggerEdgeSelection();
                UpdateTriggerChannelSelection();
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.TriggerEdge))
            {
                UpdateTriggerEdgeSelection();
            }
            else if (e.PropertyName == nameof(PlotSettings.TriggerSourceChannel))
            {
                UpdateTriggerChannelSelection();
            }
        }

        /// <summary>
        /// Updates the trigger channel ComboBox options
        /// Directly binds to available channels without wrapper class
        /// </summary>
        private void UpdateTriggerChannelOptions()
        {
            if (TriggerChannelComboBox == null || _availableChannels == null)
                return;

            // Bind directly to available channels collection
            TriggerChannelComboBox.ItemsSource = _availableChannels;
            TriggerChannelComboBox.DisplayMemberPath = "Label";
            TriggerChannelComboBox.SelectedValuePath = nameof(Channel);

            // Select current trigger channel (or leave empty if null)
            UpdateTriggerChannelSelection();
        }

        /// <summary>
        /// Updates the selected item in the trigger channel ComboBox
        /// Shows the currently selected channel or empty if none selected
        /// </summary>
        private void UpdateTriggerChannelSelection()
        {
            if (Settings == null || TriggerChannelComboBox == null)
                return;

            // Directly set selected item to the trigger source channel (may be null)
            TriggerChannelComboBox.SelectedItem = Settings.TriggerSourceChannel;
        }

        /// <summary>
        /// Updates the button selection based on trigger edge type
        /// </summary>
        private void UpdateTriggerEdgeSelection()
        {
            if (Settings != null && RisingEdgeButton != null && FallingEdgeButton != null)
            {
                if (Settings.TriggerEdge == TriggerEdgeType.Rising)
                {
                    RisingEdgeButton.Background = (System.Windows.Media.Brush)FindResource("PlotSettings_ButtonPressedBrush");
                    FallingEdgeButton.Background = (System.Windows.Media.Brush)FindResource("PlotSettings_TitleBarBrush");
                }
                else
                {
                    RisingEdgeButton.Background = (System.Windows.Media.Brush)FindResource("PlotSettings_TitleBarBrush");
                    FallingEdgeButton.Background = (System.Windows.Media.Brush)FindResource("PlotSettings_ButtonPressedBrush");
                }
            }
        }

        private void TriggerChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings != null)
            {
                // Directly set the selected channel (may be null if nothing selected)
                Settings.TriggerSourceChannel = TriggerChannelComboBox.SelectedItem as Channel;
            }
        }

        private void RisingEdgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.TriggerEdge = TriggerEdgeType.Rising;
            }
        }

        private void FallingEdgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.TriggerEdge = TriggerEdgeType.Falling;
            }
        }

        /// <summary>
        /// Handles Normal mode button click
        /// </summary>
        private void NormalModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.SingleShotMode = false;
            }
        }

        /// <summary>
        /// Handles Single mode button click
        /// </summary>
        private void SingleModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.SingleShotMode = true;
            }
        }
    }
}
