using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace TransientTRIApp.Common.Events
{
    public class CameraFrameEventArgs
    {
        public Bitmap Bmp { get; set; }
        public CameraFrameEventArgs(Bitmap bmp) 
        {
            Bmp = bmp;
        }
    }
}
