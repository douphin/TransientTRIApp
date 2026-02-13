using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Common.Interfaces
{
    public interface IGPUMonitoringService
    {
        event EventHandler<GPUMetrics> MetricsUpdated;
        void Start(int? updateIntervalMs);
        void Stop();
        void SetUpdateInterval(int intervalMs);
    }
}
