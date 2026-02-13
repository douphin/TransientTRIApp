using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Common.Interfaces
{
    public interface IThermocoupleService
    {
        event EventHandler<ThermocoupleReadings> TempReady;
        void Start(int updateIntervalMs = 1000);
        void Stop();
    }
}
