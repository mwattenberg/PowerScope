using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PowerScope.View.UserControls
{
    public partial class RunControl : UserControl
    {
        public enum RunStates
        {
            Running,
            Stopped
        }

        public enum RecordStates
        {
            Recording,
            Stopped
        }

        private RunStates _runstate;
        private RecordStates _recordstate;
        public event EventHandler<RunStates> RunStateChanged;
        public event EventHandler<RecordStates> RecordStateChanged;
        public event EventHandler ClearClicked;
        public event EventHandler LoadClicked;

        public RunStates RunState
        {
            get 
            { 
                return _runstate; 
            }
            private set
            {
                if (_runstate != value)
                {
                    _runstate = value;
                    if (RunStateChanged != null)
                        RunStateChanged.Invoke(this, _runstate);
                    UpdateRunButtonUI();
                }
            }
        }

        public RecordStates RecordState
        {
            get 
            { 
                return _recordstate; 
            }
            private set
            {
                if (_recordstate != value)
                {
                    _recordstate = value;
                    if (RecordStateChanged != null)
                        RecordStateChanged.Invoke(this, _recordstate);
                    UpdateRecordButtonUI();
                }
            }
        }

        /// <summary>
        /// Controls recording state. Setting this updates button appearance without firing events.
        /// Used by MainWindow to confirm recording started after dialog is confirmed.
        /// </summary>
        public bool IsRecording
        {
            get { return _recordstate == RecordStates.Recording; }
            set
            {
                RecordStates newState = value ? RecordStates.Recording : RecordStates.Stopped;
        
                if (_recordstate != newState)
                {
                    _recordstate = newState;
                    UpdateRecordButtonUI();
                    // Don't fire event when property is set externally
                }
            }
        }

        public RunControl()
        {
            InitializeComponent();
            this.RunState = RunStates.Stopped; // Default state
            this.RecordState = RecordStates.Stopped; // Default state
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if(RunState == RunStates.Running)
                this.RunState = RunStates.Stopped;
            else
                this.RunState = RunStates.Running;
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if(_recordstate == RecordStates.Recording)
            {
                // Stopping - directly set property and fire event
                IsRecording = false;
                RecordStateChanged?.Invoke(this, RecordStates.Stopped);
            }
            else
            {
                // Starting - fire event, let MainWindow set property on success
                // DON'T change state yet - MainWindow will set IsRecording = true if successful
                RecordStateChanged?.Invoke(this, RecordStates.Recording);
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadClicked?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateRunButtonUI()
        {
            if (RunButton == null) 
                return;
                
            if (RunState == RunStates.Running)
            {
                RunButton.Content = "Stop";
                RunButton.Background = new SolidColorBrush(Colors.Red);
                RunButton.Tag = "Red"; // Update Tag for hover effects
            }
            else
            {
                RunButton.Content = "Run 🏃‍♂️‍➡️";
                RunButton.Background = new SolidColorBrush(Colors.LimeGreen);
                RunButton.Tag = "LimeGreen"; // Update Tag for hover effects
            }
        }

        private void UpdateRecordButtonUI()
        {
            if (RecordButton == null) 
                return;
                
            if (RecordState == RecordStates.Recording)
            {
                RecordButton.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = 
                    {
                        new TextBlock 
                        { 
                            Text = "Pause", 
                            Foreground = Brushes.White, 
                            VerticalAlignment = VerticalAlignment.Center 
                        }
                    }
                };
                RecordButton.Background = new SolidColorBrush(Colors.Gray);
                RecordButton.Tag = "Gray"; // Update Tag for hover effects
            }
            else
            {
                RecordButton.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = 
                    {
                        new TextBlock 
                        { 
                            Text = "⬤ Record", 
                            Foreground = Brushes.White, 
                            VerticalAlignment = VerticalAlignment.Center 
                        }
                    }
                };
                RecordButton.Background = new SolidColorBrush(Colors.Red);
                RecordButton.Tag = "Red"; // Update Tag for hover effects
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
