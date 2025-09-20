using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for HorizontalControl.xaml
    /// </summary>
    public partial class HorizontalControl : UserControl
    {
        // Brushes for mode button states
        private static readonly SolidColorBrush ActiveBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly SolidColorBrush InactiveBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));

        /// <summary>
        /// PlotSettings instance used as DataContext
        /// </summary>
        public PlotSettings Settings
        {
            get 
            { 
                return DataContext as PlotSettings; 
            }
            set 
            { 
                DataContext = value; 
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();
            
            // Subscribe to DataContext changes to update mode buttons
            DataContextChanged += HorizontalControl_DataContextChanged;
            Loaded += HorizontalControl_Loaded;
        }

        private void HorizontalControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateModeButtonAppearance();
        }

        private void HorizontalControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
                UpdateModeButtonAppearance(); // Update buttons when DataContext changes
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.TriggerModeEnabled))
            {
                UpdateModeButtonAppearance();
            }
        }

        private void UpdateModeButtonAppearance()
        {
            if (Settings != null && RollButton != null && TriggerButton != null)
            {
                if (Settings.TriggerModeEnabled)
                {
                    // Trigger mode is active
                    RollButton.Background = InactiveBrush;
                    TriggerButton.Background = ActiveBrush;
                }
                else
                {
                    // Roll mode is active (default)
                    RollButton.Background = ActiveBrush;
                    TriggerButton.Background = InactiveBrush;
                }
            }
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Min(Settings.Xmax * 2, Settings.BufferSize);
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Max(Settings.Xmax / 2, 128);
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize) && Settings != null && bufferSize != Settings.BufferSize)
            {
                Settings.BufferSize = Math.Clamp(bufferSize, 1000, 100000000);
            }
        }

        private void SamplesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SamplesTextBox.Text, out int samples) && Settings != null && samples != Settings.Xmax)
                Settings.Xmax = Math.Max(samples, 1);
        }

        private void RollButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.TriggerModeEnabled = false; // Roll mode means trigger is disabled
                // UpdateModeButtonAppearance() will be called automatically via PropertyChanged
            }
        }

        private void TriggerButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.TriggerModeEnabled = true; // Enable trigger mode
                // UpdateModeButtonAppearance() will be called automatically via PropertyChanged
            }
        }
    }
}
