using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SerialPlotDN_WPF.Model
{
    public class DataGenerator
    {
        static string frameStart = "AA AA";
        public static string GenerateData(int numberOfChannels)
        {
            Random rnd = new Random();
            float[] data = new float[numberOfChannels];
            string retVal = frameStart;

            for(int i = 0; i < data.Length; i++)
            {
                data[i] = (float)rnd.NextDouble() * 100;
                retVal = retVal + " " + data[i].ToString();
            }

            return retVal;
        }

        public static float GenerateRandomNumber()
        {
            return (float)(new Random().NextDouble() * 100);
        }

        public static ushort[] GenerateSineWaveData(int numberOfChannels, bool noise)
        {
            ushort[] data = new ushort[numberOfChannels +1];
            double now = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            double freq = 3.0; // 1 Hz base frequency
            double amplitude = 32767; // Max for uint16/2
            double offset = 32768; // Center for uint16
            double noiseAmplitude = amplitude * 0.02; // 10% of max value
            Random rnd = new Random();

            data[0] = 0xAAAA; // Frame start

            for (int i = 1; i < numberOfChannels; i++)
            {
                double phase = 2 * Math.PI * freq * now + (i * Math.PI / numberOfChannels);
                double value = Math.Sin(phase) * amplitude + offset;
                if (noise)
                {
                    value += (rnd.NextDouble() * 2.0 - 1.0) * noiseAmplitude;
                }
                data[i] = (ushort)Math.Clamp(value, 0, 65535);
            }
            return data;
        }

    }
}
