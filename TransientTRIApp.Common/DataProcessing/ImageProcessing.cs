using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace TransientTRIApp.Common
{
    public class ImageProcessing
    {

        // Class-level variables to prevent GC pressure at 30 FPS
        private static Mat _matRef = new Mat();
        private static Mat _matCurrent = new Mat();
        private static Mat _matResult = new Mat();
        private static Mat _matGray = new Mat();
        private static Mat _matNormalized = new Mat();
        private static Mat _matColorMap = new Mat();

        // 1. Pre-calculate the Jet Palette ONCE at startup
        private static Mat _jetLUT = new Mat(1, 256, DepthType.Cv8U, 3);

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

        public static Bitmap ProcessFrame(Bitmap currentBmp, Bitmap referenceBmp)
        {
            // 1. Convert Current Frame
            UpdateMatFromBitmap(currentBmp, _matCurrent);

            // 2. Convert Reference Frame (Only if size changed or first run)
            if (_matRef.IsEmpty || _matRef.Size != _matCurrent.Size || _matRef.NumberOfChannels != _matCurrent.NumberOfChannels)
            {
                UpdateMatFromBitmap(referenceBmp, _matRef);
            }

            // 3. Subtract
            // This will now work because UpdateMatFromBitmap ensures identical layouts
            CvInvoke.Subtract(_matCurrent, _matRef, _matResult);

            // 4. Processing Pipeline
            CvInvoke.CvtColor(_matResult, _matGray, ColorConversion.Bgra2Gray);
            CvInvoke.Normalize(_matGray, _matNormalized, 0, 255, NormType.MinMax, DepthType.Cv8U);
            CvInvoke.ApplyColorMap(_matNormalized, _matColorMap, Emgu.CV.CvEnum.ColorMapType.Jet);

            // 2. In your 30 FPS loop, use CvInvoke.LUT instead
            // This is significantly faster than calling ApplyColorMap every frame
            //CvInvoke.LUT(_matNormalized, _jetLUT, _matColorMap);

            return _matColorMap.ToBitmap();
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
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }




        public static Bitmap SubtractBitmapsWithEmguCV(Bitmap bmp1, Bitmap bmp2)
        {
            try
            {
                //Console.WriteLine("Start of Image Processing");

                // Convert Bitmaps to Emgu CV Mat objects
                var mat1 = FastBitmapToMat(bmp1);
                var mat2 = FastBitmapToMat(bmp2);

                //Console.WriteLine("Initial Conversion Complete");

                //Console.WriteLine($"mat1 depth: {mat1.Depth}, channels: {mat1.NumberOfChannels}");
                //Console.WriteLine($"mat2 depth: {mat2.Depth}, channels: {mat2.NumberOfChannels}");

                Mat resultMat = new Mat();

                // Perform the subtraction
                CvInvoke.Subtract(mat1, mat2, resultMat);

                ///Console.WriteLine($"resultMat depth: {resultMat.Depth}, channels: {resultMat.NumberOfChannels}");
               
                // Convert to 8-bit unsigned
                Mat result8U = new Mat();
                resultMat.ConvertTo(result8U, Emgu.CV.CvEnum.DepthType.Cv8U);

                //Console.WriteLine($"result8U depth: {result8U.Depth}, channels: {result8U.NumberOfChannels}");

                // Convert to single channel grayscale if it's multi-channel
                Mat grayscale = new Mat();
                if (result8U.NumberOfChannels > 1)
                {
                    CvInvoke.CvtColor(result8U, grayscale, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray);
                }
                else
                {
                    grayscale = result8U.Clone();
                }

                //Console.WriteLine($"grayscale depth: {grayscale.Depth}, channels: {grayscale.NumberOfChannels}");

                // Normalize to 0-255
                Mat normalized = new Mat();
                CvInvoke.Normalize(grayscale, normalized, 0, 255, Emgu.CV.CvEnum.NormType.MinMax);

                //Console.WriteLine($"normalized depth: {normalized.Depth}, channels: {normalized.NumberOfChannels}");

                // Apply colormap
                Mat colorMat = new Mat();
                CvInvoke.ApplyColorMap(normalized, colorMat, Emgu.CV.CvEnum.ColorMapType.Jet);

                // Convert the result Mat back to a Bitmap
                Bitmap resultBmp = BitmapExtension.ToBitmap(colorMat);

                // Clean up
                mat1.Dispose();
                mat2.Dispose();
                resultMat.Dispose();
                result8U.Dispose();
                grayscale.Dispose();
                normalized.Dispose();
                colorMat.Dispose();

                return resultBmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SubtractBitmapsWithEmguCV: {ex.Message}");
                throw;
            }
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
            original.Dispose();
            return newBitmap;
        }
    }
}
