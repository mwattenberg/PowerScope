using System;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a single FFT peak with frequency and amplitude data for data binding
    /// </summary>
    public class FFTPeakData
    {
        public double Frequency { get; }
        public double Magnitude { get; }
        
        public FFTPeakData(double frequency, double magnitude)
        {
            Frequency = frequency;
            Magnitude = magnitude;
        }
        
        /// <summary>
        /// Formatted frequency string with appropriate units (Hz, kHz, MHz)
        /// </summary>
        public string FrequencyText
        {
            get
            {
                if (Frequency >= 1000000)
                {
                    return $"{Frequency / 1000000.0:F2} MHz";
                }
                else if (Frequency >= 1000)
                {
                    return $"{Frequency / 1000.0:F1} kHz";
                }
                else
                {
                    return $"{Frequency:F1} Hz";
                }
            }
        }
        
        /// <summary>
        /// Formatted amplitude string in linear scale
        /// </summary>
        public string AmplitudeText
        {
            get
            {
                if (Magnitude >= 0.001)
                {
                    return $"{Magnitude:F4}";
                }
                else if (Magnitude > 0)
                {
                    return $"{Magnitude:E2}";
                }
                else
                {
                    return "0";
                }
            }
        }
        
        /// <summary>
        /// Formatted amplitude string in dB scale
        /// </summary>
        public string AmplitudeDbText
        {
            get
            {
                double amplitudeDb = Magnitude > 0 ? 20.0 * Math.Log10(Magnitude) : -100.0;
                return $"{amplitudeDb:F1}";
            }
        }
    }
}