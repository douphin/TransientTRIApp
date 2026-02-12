using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;

namespace TransientTRIApp.Common
{
    public class ImageProcessing
    {
        public static Bitmap ColdFrame { get; set; }

        public static Bitmap SubtractBitmapsWithEmguCV(Bitmap bmp1, Bitmap bmp2)
        {
            try
            {
                // Convert Bitmaps to Emgu CV Mat objects
                var mat1 = BitmapExtension.ToMat(bmp1);
                var mat2 = BitmapExtension.ToMat(bmp2);

                ///Console.WriteLine($"mat1 depth: {mat1.Depth}, channels: {mat1.NumberOfChannels}");
                ///Console.WriteLine($"mat2 depth: {mat2.Depth}, channels: {mat2.NumberOfChannels}");

                Mat resultMat = new Mat();

                // Perform the subtraction
                CvInvoke.Subtract(mat1, mat2, resultMat);

                ///Console.WriteLine($"resultMat depth: {resultMat.Depth}, channels: {resultMat.NumberOfChannels}");

                // Convert to 8-bit unsigned
                Mat result8U = new Mat();
                resultMat.ConvertTo(result8U, Emgu.CV.CvEnum.DepthType.Cv8U);

                ///Console.WriteLine($"result8U depth: {result8U.Depth}, channels: {result8U.NumberOfChannels}");

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

                ///Console.WriteLine($"grayscale depth: {grayscale.Depth}, channels: {grayscale.NumberOfChannels}");

                // Normalize to 0-255
                Mat normalized = new Mat();
                CvInvoke.Normalize(grayscale, normalized, 0, 255, Emgu.CV.CvEnum.NormType.MinMax);

                ///Console.WriteLine($"normalized depth: {normalized.Depth}, channels: {normalized.NumberOfChannels}");

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
