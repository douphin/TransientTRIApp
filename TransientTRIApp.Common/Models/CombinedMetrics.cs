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
        public double GPUTemperature {  get; set; }
        public double GPUUtilization { get; set; }

        public CombinedMetrics(GPUMetrics gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Timestamp;
            GPUTemperature = gpuMetrics.GPUTemperature;
            GPUUtilization = gpuMetrics.GPUUtilization;
        }

        public CombinedMetrics(ThermocoupleReadings readings)
        {
            TCTimestamp = readings.Timestamp;
            TCTemperature = readings.TCTemperature;
        }

        public void Update(GPUMetrics gpuMetrics)
        {
            GPUTimestamp = gpuMetrics.Timestamp;
            GPUTemperature = gpuMetrics.GPUTemperature;
            GPUUtilization = gpuMetrics.GPUUtilization;
        }

        public void Update(ThermocoupleReadings readings)
        {
            TCTimestamp = readings.Timestamp;
            TCTemperature = readings.TCTemperature;
        }

    }
}
