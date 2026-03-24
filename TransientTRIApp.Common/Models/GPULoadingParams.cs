using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class GPULoadingParams
    {
        public string SelectedGPUKeyParam { get; set; }
        // Pick your waveform:
        public Func<double, double> Waveform;
        public double GPULoadTimeInputParam { get; set; }
        public double GPUMinLoadPercentageParam { get; set; }
        public double GPUMaxLoadPercentageParam { get; set; }
        public double GPUWavePeriodParam { get; set; }
        public double GPUWaveStepLengthParam { get; set; }
        public double GPURestTimeParam { get; set; }

        public GPULoadingParams(
            string selectedGPUKeyParam, 
            Func<double, double> waveform,
            double gPULoadTimeInputParam, 
            double gPUMinLoadPercentageParam, 
            double gPUMaxLoadPercentageParam, 
            double gPUWavePeriodParam, 
            double gPUWaveStepLengthParam, 
            double gPURestTimeParam
            )
        {
            SelectedGPUKeyParam = selectedGPUKeyParam;
            Waveform = waveform;
            GPULoadTimeInputParam = gPULoadTimeInputParam;
            GPUMinLoadPercentageParam = gPUMinLoadPercentageParam;
            GPUMaxLoadPercentageParam = gPUMaxLoadPercentageParam;
            GPUWavePeriodParam = gPUWavePeriodParam;
            GPUWaveStepLengthParam = gPUWaveStepLengthParam;
            GPURestTimeParam = gPURestTimeParam;
        }
    }
}
