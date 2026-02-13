using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class ThermocoupleReadings
    {
        public double TCTemperature { get; set; }
        public DateTime Timestamp { get; set; }

        public ThermocoupleReadings(double TCTemperature, DateTime Timestamp)
        {
            this.TCTemperature = TCTemperature;
            this.Timestamp = Timestamp;
        }
    }
}
