using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScottPlot.Plottables;
using System.Windows;
using System.Timers;
using ScottPlot.WPF;
using ScottPlot;

namespace SerialPlotDN_WPF.Model
{
    public class DataAndPlotManager
    {
        private readonly System.Timers.Timer AddNewDataTimer = new() { Interval = 10, Enabled = true, AutoReset = true };
        private readonly System.Timers.Timer UpdatePlotTimer = new() { Interval = 20, Enabled = true, AutoReset = true };
        private readonly ScottPlot.Plottables.DataStreamer Streamer;
        public const int dataToBeAdded = 100;

        //private readonly ref struct 
        private WpfPlotGL plot;
        
        public DataAndPlotManager(ref ScottPlot.WPF.WpfPlotGL plot) 
        {
            //this.plot = plot;

            plot.Refresh();
            this.plot = plot;

            Streamer = plot.Plot.Add.DataStreamer(5000);
            Streamer.ViewScrollLeft();

            InitTimer();
        }

        private void InitTimer()
        {
            AddNewDataTimer.Elapsed += AddNewData;
            AddNewDataTimer.Start();

            UpdatePlotTimer.Elapsed += UpdatePlot;
            UpdatePlotTimer.Start();
        }

        private void AddNewData(Object source, ElapsedEventArgs e)
        {
            double[] valuesA = new double[dataToBeAdded];
            for (int i = 0; i < dataToBeAdded; i++)
            {

                valuesA[i] = 10 * Math.Sin(2 * Math.PI * (double)i / dataToBeAdded) + 0.01 * DataGenerator.GenerateRandomNumber();
            }
            Streamer.AddRange(valuesA);
        }

        private void UpdatePlot(Object source, ElapsedEventArgs e)
        {
            if (Streamer.HasNewData)
            {
                this.plot.Refresh();

            }
        }


    }
}
