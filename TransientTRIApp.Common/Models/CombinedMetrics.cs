using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class CombinedMetrics 
    {
        public DateTime TCTimestamp { get; set; }
        public DateTime GPUTimestamp { get; set; }
        public double TCTemperature { get; set; }
        public double GPUTemperature0 {  get; set; }
        public double GPUUtilization0 { get; set; }
        public double GPUTemperature1 { get; set; }
        public double GPUUtilization1 { get; set; }

        public CombinedMetrics(GPUMetrics gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Timestamp;
            GPUTemperature0 = gpuMetrics.GPUTemperature;
            GPUUtilization0 = gpuMetrics.GPUUtilization;
        }

        public CombinedMetrics((GPUMetrics, GPUMetrics) gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Item1.Timestamp;
            GPUTemperature0 = gpuMetrics.Item1.GPUTemperature;
            GPUUtilization0 = gpuMetrics.Item1.GPUUtilization;
            GPUTemperature1 = gpuMetrics.Item2?.GPUTemperature == null ? -1 : gpuMetrics.Item2.GPUTemperature;
            GPUUtilization1 = gpuMetrics.Item2?.GPUUtilization == null ? -1 : gpuMetrics.Item2.GPUUtilization;
        }

        public CombinedMetrics(ThermocoupleReadings readings)
        {
            TCTimestamp = readings.Timestamp;
            TCTemperature = readings.TCTemperature;
        }

        public void Update(GPUMetrics gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Timestamp;
            GPUTemperature0 = gpuMetrics.GPUTemperature;
            GPUUtilization0 = gpuMetrics.GPUUtilization;
        }

        public void Update((GPUMetrics, GPUMetrics) gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Item1.Timestamp;
            GPUTemperature0 = gpuMetrics.Item1.GPUTemperature;
            GPUUtilization0 = gpuMetrics.Item1.GPUUtilization;
            GPUTemperature1 = gpuMetrics.Item2?.GPUTemperature == null ? -1 : gpuMetrics.Item2.GPUTemperature;
            GPUUtilization1 = gpuMetrics.Item2?.GPUUtilization == null ? -1 : gpuMetrics.Item2.GPUUtilization;
        }

        public void Update(ThermocoupleReadings readings)
        {
            TCTimestamp = readings.Timestamp;
            TCTemperature = readings.TCTemperature;
        }

    }
}
