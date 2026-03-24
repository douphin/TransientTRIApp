using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class GPULoadUIVisibility
    {
        public bool IsShowingGPULoadTimeInput { get; set; }
        public bool IsShowingGPUMinLoadPercentage { get; set; }
        public bool IsShowingGPUMaxLoadPercentage { get; set; }
        public bool IsShowingGPUWavePeriod { get; set; }
        public bool IsShowingGPUWaveStepLength { get; set; }
        public bool IsShowingGPURestTime { get; set; }

        // Pick your waveform:
        public Func<double, double> Waveform;

        public GPULoadUIVisibility(
            bool isShowingGPULoadTimeInput,
            bool isShowingGPUMinLoadPercentage, 
            bool isShowingGPUMaxLoadPercentage, 
            bool isShowingGPUWavePeriod,
            bool isShowingGPUWaveStepLength,
            bool isShowingGPURestTime) 
        {
            IsShowingGPULoadTimeInput = isShowingGPULoadTimeInput;
            IsShowingGPUMinLoadPercentage = isShowingGPUMinLoadPercentage;
            IsShowingGPUMaxLoadPercentage = isShowingGPUMaxLoadPercentage;
            IsShowingGPUWavePeriod = isShowingGPUWavePeriod;
            IsShowingGPUWaveStepLength = isShowingGPUWaveStepLength;
            IsShowingGPURestTime = isShowingGPURestTime;
        }

        public GPULoadUIVisibility(
            Func<double, double> waveform,
            bool isShowingGPULoadTimeInput,
            bool isShowingGPUMinLoadPercentage,
            bool isShowingGPUMaxLoadPercentage,
            bool isShowingGPUWavePeriod,
            bool isShowingGPUWaveStepLength,
            bool isShowingGPURestTime)
        {
            Waveform = waveform;
            IsShowingGPULoadTimeInput = isShowingGPULoadTimeInput;
            IsShowingGPUMinLoadPercentage = isShowingGPUMinLoadPercentage;
            IsShowingGPUMaxLoadPercentage = isShowingGPUMaxLoadPercentage;
            IsShowingGPUWavePeriod = isShowingGPUWavePeriod;
            IsShowingGPUWaveStepLength = isShowingGPUWaveStepLength;
            IsShowingGPURestTime = isShowingGPURestTime;
        }

        // Waveform functions — input is a decimal percentage of progress through the period, all return 0.0..1.0 representing decimal percent of how loaded GPU in regards to specified maximum
        public static double ExpSawtooth(double phase) => (Math.Exp(phase * 3.0) - 1.0) / (Math.Exp(3.0) - 1.0);

        public static double Sawtooth(double phase) => phase;

        public static double Sine(double phase) => (Math.Sin(phase * 2 * Math.PI - Math.PI / 2) + 1.0) / 2.0;

        public static double Square(double phase) => 1;

        public static double Static(double phase) => 1;
    }
}
