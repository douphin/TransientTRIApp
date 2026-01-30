using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Models;
using TransientTRIApp.Common.Interfaces;
using System.Threading;
using OpenCvSharp;
using System.Drawing;
using OpenCvSharp.Extensions;


namespace TransientTRIApp.Core.Camera
{
    public class WebCamService : ICameraService, IDisposable
    {
        private CancellationTokenSource _cts;

        public event EventHandler<CameraFrameEventArgs> FrameReady;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(() =>
            {
                var cap = new VideoCapture(0);
                var mat = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (!cap.Read(mat) || mat.Empty())
                        continue;

                    var bmp = BitmapConverter.ToBitmap(mat);
                    FrameReady?.Invoke(
                        this,
                        new CameraFrameEventArgs((Bitmap)bmp.Clone()));

                    Thread.Sleep(33); // ~30 FPS
                }
            }, token);
        }

        public void Stop() => _cts?.Cancel();

        public void Dispose() => Stop();
    }
 
}
