using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransientTRIApp.Common.Models
{
    public class CameraFrame : IDisposable
    {
        public Bitmap Frame { get; set; }
        public DateTime CaptureTime { get; set; }
        public double RecentTCReading { get; set; }
        public double ColdFrameTCReading { get; set; }
        public bool IsHotFrameRolling { get; set; }
        public bool SubtractDarkFrame { get; set; }
        public bool TrackROI { get; set; }
        public bool DivideByColdFrame { get; set; }
        public bool ScaleByTemperature { get; set; }
        public bool NormalizeBeforeMap {  get; set; }
        public bool ApplyColorMap { get; set; }
        public double Coefficient {  get; set; }
        public double AdHocFactor { get; set; }
        public Rectangle roi {  get; set; }

        public void Dispose()
        {
            Frame?.Dispose();
        }
        

    }
}
