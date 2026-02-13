using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Interfaces
{
    public interface IPulseGeneratorService
    {
        double TriggerRateHz { get; set; }
        double PulseWidthSec { get; set; }
        double LVPeakV { get; set; }

        void Connect(string gpibAddress);
        void InitialConfiguration();
        void Configure(double triggerRateHz, double pulseWidthSec, double lvPeakV);
        void Disconnect();
        void GetCurrentSettings();
    }
}
