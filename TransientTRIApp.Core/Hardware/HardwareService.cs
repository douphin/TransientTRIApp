using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Models;


namespace TransientTRIApp.Core.Hardware
{
    public class HardwareService : IHardwareService, IDisposable
    {
        public event EventHandler<MeasurementUpdatedEventArgs> MeasurementUpdated;

        public void Start()
        {
            // Configure DAQmx tasks
            // Configure hardware timing (sample clock)
            // Start background read loop
        }

        public void Stop()
        {
            // Stop tasks safely
        }

        private void OnDataAvailable(double voltage, double current)
        {
            MeasurementUpdated?.Invoke(
                this,
                new MeasurementUpdatedEventArgs(
                    new MeasurementData
                    {
                        Timestamp = DateTime.UtcNow,
                        Voltage = voltage,
                        Current = current
                    }));
        }

        public void Dispose()
        {
            Stop();
        }
    }

}
