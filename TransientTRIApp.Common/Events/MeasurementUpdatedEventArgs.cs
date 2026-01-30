using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Common.Events
{
    public class MeasurementUpdatedEventArgs
    {
        public MeasurementData MeasurementData { get; set; }

        public MeasurementUpdatedEventArgs(MeasurementData measurementData)
        {
            MeasurementData = measurementData;
        }
    }
}
