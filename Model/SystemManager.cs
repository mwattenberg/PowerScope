using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Centralized system manager that coordinates all updates (plot, measurements, etc.)
    /// Uses a single timer to drive all system updates for consistent performance
    /// </summary>
    public class SystemManager : INotifyPropertyChanged, IDisposable
    {
        private Timer _masterTimer;
        private bool _isRunning = false;
        private bool _disposed = false;
        
        // Update interval counters
        private int _updateCounter = 0;
        private int _measurementUpdateInterval = 5; // Update measurements every 5th cycle (for ~90ms at 60 FPS)
        
        // Managed components
        private PlotManager _plotManager;
        private readonly List<Measurement> _measurements = new List<Measurement>();
        
        // Update rate settings
        private double _updateIntervalMs = 16.67; // Default to ~60 FPS

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Whether the system is currently running updates
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
        }

        /// <summary>
        /// Update interval in milliseconds
        /// </summary>
        public double UpdateIntervalMs
        {
            get => _updateIntervalMs;
            set
            {
                if (Math.Abs(_updateIntervalMs - value) > 0.1)
                {
                    _updateIntervalMs = value;
                    UpdateMeasurementInterval();
                    OnPropertyChanged(nameof(UpdateIntervalMs));
                    
                    // If running, restart timer with new interval
                    if (_isRunning)
                    {
                        StopUpdates();
                        StartUpdates();
                    }
                }
            }
        }

        public SystemManager()
        {
            // Timer will be created when StartUpdates() is called
        }

        /// <summary>
        /// Set the PlotManager to be updated by this SystemManager
        /// </summary>
        public void SetPlotManager(PlotManager plotManager)
        {
            _plotManager = plotManager;
            
            // Subscribe to plot settings changes to update our interval
            if (_plotManager?.Settings != null)
            {
                _plotManager.Settings.PropertyChanged += PlotSettings_PropertyChanged;
                UpdateIntervalMs = _plotManager.Settings.TimerInterval;
            }
        }

        private void PlotSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.PlotUpdateRateFPS) || 
                e.PropertyName == nameof(PlotSettings.PlotUpdateRateFpsOption))
            {
                UpdateIntervalMs = _plotManager.Settings.TimerInterval;
            }
        }

        /// <summary>
        /// Register a measurement to be updated by the system
        /// </summary>
        public void RegisterMeasurement(Measurement measurement)
        {
            lock (_measurements)
            {
                if (!_measurements.Contains(measurement))
                {
                    _measurements.Add(measurement);
                }
            }
        }

        /// <summary>
        /// Unregister a measurement from system updates
        /// </summary>
        public void UnregisterMeasurement(Measurement measurement)
        {
            lock (_measurements)
            {
                _measurements.Remove(measurement);
            }
        }

        /// <summary>
        /// Start all system updates
        /// </summary>
        public void StartUpdates()
        {
            if (_disposed || _isRunning) return;

            int intervalMs = (int)Math.Round(_updateIntervalMs);
            _masterTimer = new Timer(MasterUpdateCallback, null, 0, intervalMs);
            IsRunning = true;
        }

        /// <summary>
        /// Stop all system updates
        /// </summary>
        public void StopUpdates()
        {
            if (!_isRunning) return;

            _masterTimer?.Dispose();
            _masterTimer = null;
            IsRunning = false;
            _updateCounter = 0;
        }

        /// <summary>
        /// Master update callback - coordinates all system updates
        /// </summary>
        private void MasterUpdateCallback(object state)
        {
            if (_disposed) return;

            try
            {
                _updateCounter++;

                // Always update the plot
                _plotManager?.UpdatePlot();

                // Update measurements at a lower frequency (e.g., every 5th update for ~90ms at 60 FPS)
                if (_updateCounter >= _measurementUpdateInterval)
                {
                    _updateCounter = 0;
                    UpdateMeasurements();
                }
            }
            catch (Exception ex)
            {
                // Log error but continue running
                System.Diagnostics.Debug.WriteLine($"SystemManager update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update all registered measurements
        /// </summary>
        private void UpdateMeasurements()
        {
            lock (_measurements)
            {
                // This can be parallelized across multiple cores!
                Parallel.ForEach(_measurements, measurement =>
                {
                    if (!measurement.IsDisposed)
                    {
                        measurement.UpdateMeasurement(); // CPU-intensive math
                    }
                });
            }
        }

        /// <summary>
        /// Calculate how often measurements should be updated relative to plot updates
        /// Target: measurements update approximately every 90ms
        /// </summary>
        private void UpdateMeasurementInterval()
        {
            double targetMeasurementMs = 90.0;
            _measurementUpdateInterval = Math.Max(1, (int)Math.Round(targetMeasurementMs / _updateIntervalMs));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopUpdates();
                
                // Unsubscribe from plot settings
                if (_plotManager?.Settings != null)
                {
                    _plotManager.Settings.PropertyChanged -= PlotSettings_PropertyChanged;
                }
            }
        }
    }
}