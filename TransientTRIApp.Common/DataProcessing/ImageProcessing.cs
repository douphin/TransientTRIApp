using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using OpenCvSharp;
using TransientTRIApp.Common.DataProcessing;
using TransientTRIApp.Common.Models;
using Mat = Emgu.CV.Mat;

namespace TransientTRIApp.Common
{
    public class ImageProcessing
    {

        // Class-level variables to prevent Garbage Collector (GC) pressure at 20 FPS
        // So to elaborate why we're declaring 5 million mats. Basically allocating/deallocating memory space for a mat can take a hot second
        // which means that if we are trying to create/destroy a bunch of Mats every frame we could be slowing everything down by a lot.
        // To avoid this, we pre-declare all (most) of the mats we'll need, so once they get allocated, they stay allocated, and don't get garbage collected until
        // the program ends. 
        // Now obviously it does seem at least minorly problematic in some way to take the approach of declaring 5 million mats for anything we could possible need
        // but at least for now, I think its okay. There is another approach which uses a MatPool, which is just a Dictionary<string, Mat> but this is effectively
        // the same thing that we are doing here it just cleans up initialization while slightly complicating the processs of referencing and assigning Mat values.
        // Still, it feels like there has got to be a better way. Which if true, I'd be all ears to learn about, but I'll admit I haven't looked too hard to find it.
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

        private static Mat _inputMatA = new Mat();
        private static Mat _inputMatB = new Mat();
        private static Mat _buffer = new Mat();

        private static Mat _roiMat = new Mat();

        private static Mat _aligned = new Mat();

        private static Mat _hannWindowScaled = new Mat();
        private static Mat _hannWindowNative = new Mat();

        private static Mat _currentGray = new Mat();
        private static Mat _referenceGray = new Mat();
        private static Mat _gray = new Mat();

        private static Mat _currentFrameScaled = new Mat();        
        private static Mat _currentFrameNative = new Mat();
        private static Mat _currentFramePrimeScaled = new Mat();
        private static Mat _currentFramePrimeNative = new Mat();

        private static Mat _referenceFrameScaled = new Mat();
        private static Mat _referenceFrameNative = new Mat();
        private static Mat _referenceFramePrimeScaled = new Mat();   
        private static Mat _referenceFramePrimeNative = new Mat();

        private static System.Drawing.Size _processSize; // Lower resolution for speed

        private static Mat _returnedBitmap = new Mat();

        // 1. Pre-calculate the Jet Palette ONCE at startup
        private static Mat _jetLUT = new Mat(1, 256, DepthType.Cv8U, 3);

        private static MCvScalar _green = new MCvScalar(0, 255, 0);
        private static MCvScalar _red = new MCvScalar(0, 0, 255);
        private static MCvScalar _blue = new MCvScalar(255, 0, 0);
        private static MCvScalar _yellow = new MCvScalar(0, 255, 255);
        private static MCvScalar _white = new MCvScalar(255, 255, 255);

        public static EventHandler<(double, double)> ImageShiftEvent;

        public static int _isShiftRunning = 0;

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

            Task.Run(() => { RoutineImageShift(); });

            return _returnedBitmap.ToBitmap();
        }

        public static Mat ComputeChange(CameraFrame cf)
        {
            if (cf.IsHotFrameRolling == false)
                return _matHot;

            if (cf.SubtractDarkFrame)
            {
                CvInvoke.Subtract(_matHot, _matDark, _matHotPrime);
                
                // Possible Solution for tracking ROI https://docs.opencv.org/4.x/df/def/tutorial_js_meanshift.html
                // Update : the meanshift idea I had earlier was maybe a little too overkill so I'm using PhaseCorrelate
                // Scroll down to the phase correlate section https://docs.opencv.org/4.x/d7/df3/group__imgproc__motion.html#ga552420a2ace9ef3fb053cd630fdb4952
                if (cf.TrackROI)
                {
                    _matHotDoublePrime = ScaledAlignFrameWithPhaseCorrelation(_matHotPrime, _matColdPrime);
                }

                CvInvoke.Subtract( cf.TrackROI ? _matHotDoublePrime : _matHotPrime, _matColdPrime, _matResultSubtracted);
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

            // Theoretically we could use a Lookup Table (LUT) which would be quicked instead of doing ApplyColorMap() every time, but I can't get it to work so maybe if speed is an issue in the future take another look
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

        private static Mat ScaledAlignFrameWithPhaseCorrelation(Mat currentFrame, Mat referenceFrame)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            _currentFrameScaled = currentFrame;
            _referenceFrameScaled = referenceFrame;

            if (_processSize.IsEmpty)
                _processSize = new System.Drawing.Size(currentFrame.Width / 2, currentFrame.Height / 2);

            CvInvoke.CvtColor(_currentFrameScaled, _gray, ColorConversion.Bgr2Gray);
            CvInvoke.Resize(_gray, _currentGray, _processSize);

            CvInvoke.CvtColor(_referenceFrameScaled, _gray, ColorConversion.Bgr2Gray);
            CvInvoke.Resize(_gray, _referenceGray, _processSize);

            if (_hannWindowScaled.Size != _currentGray.Size)
                CvInvoke.CreateHanningWindow(_hannWindowScaled, new System.Drawing.Size(_currentGray.Width, _currentGray.Height), DepthType.Cv32F);

            //Console.WriteLine($" First {sw.ElapsedMilliseconds}");

            _currentGray.ConvertTo(_currentFramePrimeScaled, DepthType.Cv32F, (double)1 / (double)255);
            _referenceGray.ConvertTo(_referenceFramePrimeScaled, DepthType.Cv32F, (double)1/(double)255);
            //Console.WriteLine($" Second {sw.ElapsedMilliseconds}");

            // Phase correlation finds translation (x, y offset)
            // Returns: the shift amount
            var shift = CvInvoke.PhaseCorrelate(_referenceFramePrimeScaled, _currentFramePrimeScaled, _hannWindowScaled, out double _);
            //Console.WriteLine($" Third {sw.ElapsedMilliseconds}");

            float scaleX = (float)currentFrame.Width / _processSize.Width;
            float scaleY = (float)currentFrame.Height / _processSize.Height;

            float finalShiftX = (float)shift.X * scaleX;
            float finalShiftY = (float)shift.Y * scaleY - 1;

            // shift[0] = x offset, shift[1] = y offset
            //Console.WriteLine($"Vibration detected: X={shift.X:F2}px, Y={shift.Y:F2}px Upscaled X={finalShiftX:F2}px, Y={finalShiftY:F2}px");

            using (Matrix<float> matrix = new Matrix<float>(2, 3))
            {
                // Set values directly using [row, col] indexers
                matrix[0, 0] = 1.0f;
                matrix[0, 1] = 0.0f;
                matrix[0, 2] = (float)finalShiftX; // The X translation

                matrix[1, 0] = 0.0f;
                matrix[1, 1] = 1.0f;
                matrix[1, 2] = (float)finalShiftY; // The Y translation

                // matrix.Mat gives you the underlying Mat required by WarpAffine
                CvInvoke.WarpAffine(_currentFrameScaled, _aligned, matrix.Mat, _currentFrameScaled.Size);
                sw.Stop();
                //Console.WriteLine($" Final {sw.ElapsedMilliseconds}");
                return _aligned;
            }
        }

        private static Mat NativeAlignFrameWithPhaseCorrelation(Mat currentFrame, Mat referenceFrame, out (double, double) shift)
        {          
            Stopwatch sw = new Stopwatch();
            sw.Start();

            _currentFrameNative = currentFrame;
            _referenceFrameNative = referenceFrame;

            CvInvoke.CvtColor(_currentFrameNative, _currentGray, ColorConversion.Bgr2Gray);
            CvInvoke.CvtColor(_referenceFrameNative, _referenceGray, ColorConversion.Bgr2Gray);

            if (_hannWindowNative.Size != _currentGray.Size)
                CvInvoke.CreateHanningWindow(_hannWindowNative, new System.Drawing.Size(_currentGray.Width, _currentGray.Height), DepthType.Cv32F);

            //Console.WriteLine($" First {sw.ElapsedMilliseconds}");
            //Console.WriteLine($"{currentFrame.Depth.ToString()} {referenceFrame.Depth.ToString()}");

            _currentGray.ConvertTo(_currentFramePrimeNative, DepthType.Cv32F, (double)1 / (double)255);
            _referenceGray.ConvertTo(_referenceFramePrimeNative, DepthType.Cv32F, (double)1 / (double)255);
            //Console.WriteLine($" Second {sw.ElapsedMilliseconds}");
            //Console.WriteLine($"{currentFramePrime.GetType().ToString()} {referenceFramePrime.Depth.ToString()}");

            // Phase correlation finds translation (x, y offset)
            // Returns: the shift amount
            var pointShift = CvInvoke.PhaseCorrelate(_referenceFramePrimeNative, _currentFramePrimeNative, _hannWindowNative, out double _);
           // Console.WriteLine($" Third {sw.ElapsedMilliseconds}");

            // shift[0] = x offset, shift[1] = y offset
            //Console.WriteLine($"Vibration detected: X={pointShift.X:F2}px, Y={pointShift.Y:F2}px");
            shift = (pointShift.X,  pointShift.Y);

            using (Matrix<float> matrix = new Matrix<float>(2, 3))
            {
                // Set values directly using [row, col] indexers
                matrix[0, 0] = 1.0f;
                matrix[0, 1] = 0.0f;
                matrix[0, 2] = (float)pointShift.X; // The X translation

                matrix[1, 0] = 0.0f;
                matrix[1, 1] = 1.0f;
                matrix[1, 2] = (float)pointShift.Y; // The Y translation

                // matrix.Mat gives you the underlying Mat required by WarpAffine
                CvInvoke.WarpAffine(_currentFrameNative, _aligned, matrix.Mat, _currentFrameNative.Size);
                sw.Stop();
                //Console.WriteLine($" Final {sw.ElapsedMilliseconds}");
                return _aligned;
            }
        }

        public static void RoutineImageShift()
        {
            // If already running, skip this frame entirely
            // okay so this part is actually pretyty important, it prevents the memory from ballooning when the app is out of focus
            if (Interlocked.CompareExchange(ref _isShiftRunning, 1, 0) != 0)
                return;

            try
            {
                if (_matHot.IsEmpty || _matCold.IsEmpty)
                    return;

                NativeAlignFrameWithPhaseCorrelation(_matCold, _matHot, out (double, double) shift);
                ImageShiftEvent?.Invoke(null, shift);
            }
            finally
            {
                Interlocked.Exchange(ref _isShiftRunning, 0);
            }
        }

        public static void SetNewMatRefDark(Bitmap frame)
        {
            _matDark?.Dispose();
            _matDark = new Mat();
            UpdateMatFromBitmap(frame, _matDark);
        }

        public static void SetNewMatRefCold(Bitmap frame)
        {
            _matCold?.Dispose();
            _matCold = new Mat();
            UpdateMatFromBitmap(frame, _matCold);
            CvInvoke.Subtract(_matCold, _matDark, _matColdPrime);
        }

        public static void SetNewMatRefHot(Bitmap frame)
        {
            _matHot?.Dispose();
            _matHot = new Mat();
            UpdateMatFromBitmap(frame, _matHot);
            CvInvoke.Subtract(_matHot, _matDark, _matHotPrime);
        }

        public static (double, double) ComputeCTR(double tempChange, Rectangle roi)
        {
            Mat matHotDoublePrime = NativeAlignFrameWithPhaseCorrelation(_matHotPrime, _matColdPrime, out _);
            Mat matResultSubtracted = new Mat();
            Mat matResultDivided = new Mat();

            // Ih'' - Ic' = Irs
            CvInvoke.Subtract(matHotDoublePrime, _matColdPrime, matResultSubtracted);

            // Irs / Ic' = Ird
            CvInvoke.Divide(matResultSubtracted, _matColdPrime, matResultDivided);

            // Ird / deltaT = Ctr(x, y)
            Mat matResultTempScaled = matResultDivided / tempChange;
            Mat roiMat = new Mat(matResultTempScaled, roi);

            var imageMean = CvInvoke.Mean(matResultTempScaled);
            var roiMean = CvInvoke.Mean(roiMat);

            Console.WriteLine($"{imageMean.V0} {imageMean.V1} {imageMean.V2} {imageMean.V3}");
            Console.WriteLine($"{roiMean.V0} {roiMean.V1} {roiMean.V2} {roiMean.V3}");

            return (imageMean.V0, roiMean.V0);
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

        // So when we do the phase correlate to track image translation, we use a hanning window to focus on the center of the image instead of the edges
        // This function, which I did get straight from Claude, should theoretically bias the hanning window towards an roi. idk if this is actually needed 
        // or what but maybe it might be helpful at some point for some reason.
        private Mat CreateBiasedHanningWindow(System.Drawing.Rectangle roi, int width, int height)
        {
            // 1. Create standard Hanning window
            Mat hannWindow = new Mat();
            CvInvoke.CreateHanningWindow(hannWindow, new System.Drawing.Size(width, height), DepthType.Cv32F);

            // 2. Create a weight mask - start with low base weight everywhere
            Mat weightMask = new Mat(height, width, DepthType.Cv32F, 1);
            weightMask.SetTo(new MCvScalar(0.1)); // low weight outside ROI

            // 3. Set ROI region to full weight
            Mat roiRegion = new Mat(weightMask, roi);
            roiRegion.SetTo(new MCvScalar(1.0));

            // 4. Optional: blur the mask edges to avoid sharp transitions
            // Sharp edges in the weight mask can introduce frequency artifacts
            CvInvoke.GaussianBlur(weightMask, weightMask,
                new System.Drawing.Size(roi.Width / 4 | 1, roi.Height / 4 | 1), // ensure odd kernel size
                0);

            // 5. Multiply the Hanning window by the weight mask
            Mat biasedHann = new Mat();
            CvInvoke.Multiply(hannWindow, weightMask, biasedHann);

            // 6. Normalize so peak = 1.0 (optional but keeps amplitude consistent)
            double min = 0, max = 0;

            System.Drawing.Point minLoc = new System.Drawing.Point(0, 0);
            System.Drawing.Point maxLoc = new System.Drawing.Point(0, 0);

            CvInvoke.MinMaxLoc(biasedHann, ref min, ref max, ref minLoc, ref maxLoc);
            if (max > 0)
                CvInvoke.Multiply(biasedHann, new ScalarArray(new MCvScalar(1.0 / max)), biasedHann);

            weightMask.Dispose();
            roiRegion.Dispose();
            return biasedHann;
        }
    }
}
