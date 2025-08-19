using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SerialPlotDN_WPF.View.UserControls
{
    public partial class RunControl : UserControl
    {
        public enum RunState
        {
            Running,
            Stopped
        }

        private RunState _currentState = RunState.Stopped;
        public RunState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnRunStateChanged(value);
                    UpdateRunButtonUI();
                }
            }
        }

        public event EventHandler<RunState> RunStateChanged;

        public RunControl()
        {
            InitializeComponent();
            UpdateRunButtonUI();
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentState = CurrentState == RunState.Stopped ? RunState.Running : RunState.Stopped;
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for Record functionality
        }

        private void UpdateRunButtonUI()
        {
            if (RunButton == null) return;
            if (CurrentState == RunState.Running)
            {
                RunButton.Content = "Stop";
                RunButton.Background = new SolidColorBrush(Colors.Red);
            }
            else
            {
                RunButton.Content = "Run";
                RunButton.Background = new SolidColorBrush(Colors.LimeGreen);
            }
        }

        protected virtual void OnRunStateChanged(RunState newState)
        {
            RunStateChanged?.Invoke(this, newState);
        }
    }
}
