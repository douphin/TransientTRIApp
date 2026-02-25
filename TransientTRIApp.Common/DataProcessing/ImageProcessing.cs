using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using OpenCvSharp;
using TransientTRIApp.Common.Models;
using Mat = Emgu.CV.Mat;

namespace TransientTRIApp.Common
{
    public class ImageProcessing
    {

        // Class-level variables to prevent GC pressure at 30 FPS
        private static Mat _matDark = new Mat();
        private static Mat _matCold = new Mat();
        private static Mat _matHot = new Mat();
        private static Mat _matColdPrime = new Mat();
        private static Mat _matHotPrime = new Mat();
        private static Mat _matHotDoublePrime = new Mat();
        private static Mat _matResultSubtracted = new Mat();
        private static Mat _matResultDivided = new Mat();
        private static Mat _matResultTempScaled = new Mat();
        private static Mat _matGray = new Mat();
        private static Mat _matNormalized = new Mat();
        private static Mat _matColorMap = new Mat();

        private static Mat _returnedBitmap = new Mat();

        // 1. Pre-calculate the Jet Palette ONCE at startup
        private static Mat _jetLUT = new Mat(1, 256, DepthType.Cv8U, 3);

        private static MCvScalar _green = new MCvScalar(0, 255, 0);
        private static MCvScalar _red = new MCvScalar(0, 0, 255);
        private static MCvScalar _blue = new MCvScalar(255, 0, 0);
        private static MCvScalar _yellow = new MCvScalar(0, 255, 255);
        private static MCvScalar _white = new MCvScalar(255, 255, 255);

        public static void PreProcess()
        {
            // Fill it once using ApplyColorMap on a gradient
            using (Mat gradient = new Mat(1, 256, DepthType.Cv8U, 1))
            {
                byte[] values = new byte[256];
                for (int i = 0; i < 256; i++) values[i] = (byte)i;
                System.Runtime.InteropServices.Marshal.Copy(values, 0, gradient.DataPointer, 256);

                // Use ApplyColorMap once to "bake" the Jet colors into our LUT
                _jetLUT = new Mat();
                CvInvoke.ApplyColorMap(gradient, _jetLUT, Emgu.CV.CvEnum.ColorMapType.Jet);
            }
        }

        public static Bitmap ProcessFrame(CameraFrame cf)
        {
            UpdateMatFromBitmap(cf.Frame, _matHot);

            _returnedBitmap = ComputeChange(cf);

            if (cf.roi != null)
                CvInvoke.Rectangle(_returnedBitmap, cf.roi, _red, 2);

            return _returnedBitmap.ToBitmap();
        }

        public static Mat ComputeChange(CameraFrame cf)
        {
            if (cf.IsHotFrameRolling == false)
                return _matHot;

            if (cf.SubtractDarkFrame)
            {
                CvInvoke.Subtract(_matHot, _matDark, _matHotPrime);
                CvInvoke.Subtract(_matHotPrime, _matColdPrime, _matResultSubtracted);

                // Possible Solution for tracking ROI https://docs.opencv.org/4.x/df/def/tutorial_js_meanshift.html
                //if (true)
                //{
                //    CvInvoke.MeanShift(_matHotPrime, )
                //}
            }
            else
            {
                CvInvoke.Subtract(_matHot, _matCold, _matResultSubtracted);
            }

            if (cf.DivideByColdFrame)
                CvInvoke.Divide(_matResultSubtracted, _matColdPrime, _matResultDivided);
            else
                _matResultDivided = _matResultSubtracted;

            // TODO apply cf.ColdFrameTCReading
            if (cf.ScaleByTemperature)
                _matResultTempScaled = _matResultDivided * (cf.AdHocFactor / cf.Coefficient);
            else
                _matResultTempScaled = _matResultDivided;

            CvInvoke.CvtColor(_matResultDivided, _matGray, ColorConversion.Bgra2Gray);

            if (cf.NormalizeBeforeMap)
                CvInvoke.Normalize(_matGray, _matNormalized, 0, 255, NormType.MinMax, DepthType.Cv8U);

            // Theoretically we could use a LUT which would be quicked instead of doing ApplyColorMap() every time, but I can't get it to work so maybe if speed is an issue in the future take another look
            //CvInvoke.LUT(_matNormalized, _jetLUT, _matColorMap);
            if (cf.ApplyColorMap)
                CvInvoke.ApplyColorMap(_matNormalized, _matColorMap, Emgu.CV.CvEnum.ColorMapType.Jet);

            return _matColorMap;
        }

        private static void UpdateMatFromBitmap(Bitmap bmp, Mat targetMat)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                           ImageLockMode.ReadOnly, bmp.PixelFormat);
            try
            {
                // 4 channels for 32bpp
                using (Mat temp = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 4, data.Scan0, data.Stride))
                {
                    temp.CopyTo(targetMat); // This updates the class-level Mat memory
                }
                //targetMat = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, 4, data.Scan0, data.Stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void ProcessingStep(Action<Mat, Mat> action, bool shouldRun)
        {
            if (shouldRun)
            {

            }
        }

        public static IEnumerable<double> GetPixelValueCount(Bitmap frame)
        {
            double[] values = new double[256];
            double totalPixles = frame.Width * frame.Height;
            double perPixelPercentage = 100 / totalPixles;

            for (int i = 0; i < frame.Width; i++)
            {
                for (int j = 0; j < frame.Height; j++)
                {
                    values[frame.GetPixel(i, j).R] += perPixelPercentage;
                }
            }
     
            return values;
        }

        public static double[] GetPixelValueCountv2(Bitmap frame)
        {
            double[] values = new double[256];
            int totalPixels = frame.Width * frame.Height;
            double perPixelPercentage = 100.0 / totalPixels;

            // Lock the bitmap once
            BitmapData bmpData = frame.LockBits(
                new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.ReadOnly,
                frame.PixelFormat);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;

                    for (int y = 0; y < frame.Height; y++)
                    {
                        for (int x = 0; x < frame.Width; x++)
                        {
                            // For 8bpp grayscale, each pixel is 1 byte
                            byte grayscaleValue = ptr[y * stride + x];
                            values[grayscaleValue] += perPixelPercentage;
                        }
                    }
                }
            }
            finally
            {
                frame.UnlockBits(bmpData);
            }

            return values;
        }

        public static void SetNewMatRefCold(Bitmap frame)
        {
            _matCold?.Dispose();
            _matCold = new Mat();
            UpdateMatFromBitmap(frame, _matCold);
            CvInvoke.Subtract(_matCold, _matDark, _matColdPrime);
        }

        public static void SetNewMatRefDark(Bitmap frame)
        {
            _matDark?.Dispose();
            _matDark = new Mat();
            UpdateMatFromBitmap(frame, _matDark);
        }

        public static Mat FastBitmapToMat(Bitmap bmp)
        {
            // 1. Lock the bits to get the pointer to the raw pixel data
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                           ImageLockMode.ReadOnly, bmp.PixelFormat);

            // 2. Wrap a Mat around that pointer (No copy happens here!)
            // Note: Use 3 channels for 24bppRgb, 4 for 32bppPArgb
            int channels = (bmp.PixelFormat == PixelFormat.Format32bppPArgb || bmp.PixelFormat == PixelFormat.Format32bppArgb) ? 4 : 3;

            Mat mat = new Mat(bmp.Height, bmp.Width, DepthType.Cv8U, channels, data.Scan0, data.Stride);

            // 3. Since we want to use the Mat after the Bitmap is unlocked, we must clone it.
            // This is still faster than generic converters because it's a direct memory blit.
            Mat finalMat = mat.Clone();

            bmp.UnlockBits(data);
            return finalMat;
        }
    }
}
