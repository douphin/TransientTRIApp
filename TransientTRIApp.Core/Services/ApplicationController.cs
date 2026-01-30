using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Core.Camera;

namespace TransientTRIApp.Core.Services
{
    public class ApplicationController
    {
        public IHardwareService Hardware { get; }
        public ICameraService Camera { get; }
        public ApplicationController(
            IHardwareService hardware,
            ICameraService camera)
        {
            Hardware = hardware;
            Camera = camera;
        }

        public void StartAll()
        {
            Hardware.Start();
            Camera.Start();
        }

        public void StopAll()
        {
            Camera.Stop();
            Hardware.Stop();
        }
    }
}

