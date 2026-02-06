using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using Basler.Pylon;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;

public class LabCamService_v3 : ICameraService, IDisposable
{
    private CancellationTokenSource _cts;
    private Camera _camera;
    private bool _disposed = false;

    public event EventHandler<CameraFrameEventArgs> FrameReady;

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException("CameraService");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            try
            {
                _camera = new Camera();
                _camera.Open();
                Console.WriteLine("Camera opened successfully.");

                // Configure camera to match your current settings
                _camera.Parameters[PLCamera.ExposureTimeAbs].SetValue(10000.0); // 10ms
                _camera.Parameters[PLCamera.GainRaw].SetValue(370);

                Console.WriteLine($"Exposure set to: {_camera.Parameters[PLCamera.ExposureTimeAbs].GetValue()}");
                Console.WriteLine($"Gain set to: {_camera.Parameters[PLCamera.GainRaw].GetValue()}");

                // Start continuous acquisition
                _camera.StreamGrabber.Start();

                while (!token.IsCancellationRequested)
                {
                    IGrabResult grabResult = null;
                    Bitmap bmp = null;

                    try
                    {
                        grabResult = _camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.ThrowException);

                        if (grabResult != null && grabResult.GrabSucceeded)
                        {
                            // Determine pixel format based on grab result
                            PixelFormat pixelFormat = DeterminePixelFormat(grabResult);

                            // Create bitmap with correct dimensions
                            bmp = new Bitmap((int)grabResult.Width, (int)grabResult.Height, pixelFormat);

                            // Copy image data directly from grab result to bitmap
                            CopyGrabResultToBitmap(grabResult, bmp);

                            // Invoke event with the bitmap
                            FrameReady?.Invoke(this, new CameraFrameEventArgs(bmp));
                            bmp = null; // Don't dispose here - the UI will handle it
                        }
                        else if (grabResult != null)
                        {
                            Console.WriteLine($"Grab failed: {grabResult.ErrorDescription}");
                        }

                        Thread.Sleep(33); // ~30 FPS
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error capturing frame: {ex.Message}");
                        bmp?.Dispose();
                    }
                    finally
                    {
                        // Dispose grab result immediately to free resources
                        grabResult?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Camera initialization error: {ex.Message}");
            }
            finally
            {
                if (_camera != null)
                {
                    try
                    {
                        _camera.StreamGrabber.Stop();
                        _camera.Close();
                        _camera.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error closing camera: {ex.Message}");
                    }
                }
            }
        }, token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    //private Bitmap GrabResultToBitmap(IGrabResult grabResult)
    //{
    //    PixelDataConverter converter = new PixelDataConverter();
    //    Bitmap[] bmp = { new Bitmap(grabResult.Width, grabResult.Height, PixelFormat.Format8bppIndexed) };
    //    converter.Convert<Bitmap>(bmp, grabResult);
    //    return bmp[0];
    //}

    private PixelFormat DeterminePixelFormat(IGrabResult grabResult)
    {
        // Check the pixel type from the grab result
        // Most GigE cameras are either Mono8 or Bayer formats
        string pixelType = grabResult.PixelTypeValue.ToString();

        Console.WriteLine($"Pixel type: {pixelType}");

        if (pixelType.Contains("Mono"))
        {
            return PixelFormat.Format8bppIndexed;
        }
        else if (pixelType.Contains("RGB") || pixelType.Contains("Bayer"))
        {
            return PixelFormat.Format32bppRgb;
        }

        // Default to 8-bit grayscale
        return PixelFormat.Format8bppIndexed;
    }

    private void CopyGrabResultToBitmap(IGrabResult grabResult, Bitmap bitmap)
    {
        try
        {
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                bitmap.PixelFormat);

            try
            {
                // Get image data from grab result and copy to bitmap
                IntPtr imageData = (IntPtr)grabResult.PixelData;
                int bytesPerPixel = (bitmap.PixelFormat == PixelFormat.Format8bppIndexed) ? 1 : 4;
                int dataSize = (int)(grabResult.Width * grabResult.Height * bytesPerPixel);

                // Copy from unmanaged buffer to managed bitmap
                byte[] buffer = new byte[dataSize];
                System.Runtime.InteropServices.Marshal.Copy(imageData, buffer, 0, dataSize);
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bitmapData.Scan0, dataSize);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying grab result to bitmap: {ex.Message}");
        }
    }
}