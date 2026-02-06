using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class GPUMetrics
    {
        public double GPUUtilization { get; set; } // 0-100%
        public double GPUTemperature { get; set; }  // Celsius
        public DateTime Timestamp { get; set; }
    }
}
