using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Events;

namespace TransientTRIApp.Common.Interfaces
{
    public interface ICameraService
    {
        event EventHandler<CameraFrameEventArgs> FrameReady;
        void Start();
        void Stop();
    }
}
