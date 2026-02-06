using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Interfaces
{
    public interface IPulseGeneratorService
    {
        void Connect(string gpibAddress);
        void Configure(double triggerRateHz, double pulseWidthSec, double lvPeakV);
        void Disconnect();
        Dictionary<string, double> GetCurrentSettings();
    }
}
