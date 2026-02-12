using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Models;


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

                    var bmp = MakeGrayscale3(BitmapConverter.ToBitmap(mat));
                    
                    FrameReady?.Invoke(
                        this,
                        new CameraFrameEventArgs((Bitmap)bmp.Clone()));

                    Thread.Sleep(33); // ~30 FPS
                }
            }, token);
        }

        public void Stop() => _cts?.Cancel();

        public void Dispose() => Stop();

        public static Bitmap MakeGrayscale3(Bitmap original)
        {
            //create a blank bitmap the same size as original
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);

            //get a graphics object from the new image
            using (Graphics g = Graphics.FromImage(newBitmap))
            {

                //create the grayscale ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                   new float[][]
                   {
             new float[] {.3f, .3f, .3f, 0, 0},
             new float[] {.59f, .59f, .59f, 0, 0},
             new float[] {.11f, .11f, .11f, 0, 0},
             new float[] {0, 0, 0, 1, 0},
             new float[] {0, 0, 0, 0, 1}
                   });

                //create some image attributes
                using (ImageAttributes attributes = new ImageAttributes())
                {

                    //set the color matrix attribute
                    attributes.SetColorMatrix(colorMatrix);

                    //draw the original image on the new image
                    //using the grayscale color matrix
                    g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return newBitmap;
        }

    }

}
