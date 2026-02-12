using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using Basler.Pylon;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;

public class CameraService : ICameraService, IDisposable
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
                            // Convert grab result to bitmap
                            bmp = ConvertGrabResultToBitmap(grabResult);

                            if (bmp != null)
                            {
                                // Invoke event with the bitmap
                                FrameReady?.Invoke(this, new CameraFrameEventArgs(bmp));
                                bmp = null; // Don't dispose here - the UI will handle it
                            }
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
                Console.WriteLine("Done");
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

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _cts?.Dispose();
        _disposed = true;
    }

    private Bitmap ConvertGrabResultToBitmap(IGrabResult grabResult)
    {
        try
        {
            string pixelType = grabResult.PixelTypeValue.ToString();
            //Console.WriteLine($"Pixel type: {pixelType}");

            // For Mono12, we need to convert 12-bit data to 8-bit for display
            if (pixelType.Contains("Mono12"))
            {
                return ConvertMono12ToBitmap(grabResult);
            }
            else if (pixelType.Contains("Mono8"))
            {
                return ConvertMono8ToBitmap(grabResult);
            }
            else if (pixelType.Contains("RGB") || pixelType.Contains("Bayer"))
            {
                return ConvertColorToBitmap(grabResult);
            }

            // Default: try Mono8
            return ConvertMono8ToBitmap(grabResult);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting grab result: {ex.Message}");
            return null;
        }
    }

    private byte[] GetPixelDataAsBytes(object pixelDataObj, int expectedSize)
    {
        if (pixelDataObj is byte[] byteArray)
        {
            //Console.WriteLine("PixelData is already byte[]");
            return byteArray;
        }
        else if (pixelDataObj is IntPtr ptr && ptr != IntPtr.Zero)
        {
            //Console.WriteLine("PixelData is IntPtr, converting to byte[]");
            byte[] buffer = new byte[expectedSize];
            System.Runtime.InteropServices.Marshal.Copy(ptr, buffer, 0, expectedSize);
            return buffer;
        }
        else
        {
            Console.WriteLine($"Unexpected PixelData type: {pixelDataObj?.GetType().Name ?? "null"}");
            return null;
        }
    }

    private Bitmap ConvertMono12ToBitmap(IGrabResult grabResult)
    {
        try
        {
            int width = (int)grabResult.Width;
            int height = (int)grabResult.Height;

            // Create 8-bit grayscale bitmap
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

            // Set grayscale palette
            ColorPalette palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            bmp.Palette = palette;

            BitmapData bitmapData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format8bppIndexed);

            try
            {
                // Mono12 uses 2 bytes per pixel (12 bits packed)
                int expectedSize = width * height * 2;
                byte[] managedBuffer = GetPixelDataAsBytes(grabResult.PixelData, expectedSize);

                if (managedBuffer == null || managedBuffer.Length < expectedSize)
                {
                    Console.WriteLine("Failed to get pixel data");
                    bmp.Dispose();
                    return null;
                }

                byte[] outputBuffer = new byte[width * height];

                // Convert 12-bit to 8-bit
                for (int i = 0; i < width * height; i++)
                {
                    // Read 12-bit value (little-endian)
                    ushort value12bit = (ushort)(managedBuffer[i * 2] | (managedBuffer[i * 2 + 1] << 8));
                    // Scale from 12-bit (0-4095) to 8-bit (0-255)
                    byte value8bit = (byte)(value12bit >> 4); // Right shift by 4 to convert 12-bit to 8-bit
                    outputBuffer[i] = value8bit;
                }

                System.Runtime.InteropServices.Marshal.Copy(outputBuffer, 0, bitmapData.Scan0, outputBuffer.Length);
                //Console.WriteLine("Mono12 conversion successful");
            }
            finally
            {
                bmp.UnlockBits(bitmapData);
            }

            return bmp;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting Mono12: {ex.Message}");
            return null;
        }
    }

    private Bitmap ConvertMono8ToBitmap(IGrabResult grabResult)
    {
        try
        {
            int width = (int)grabResult.Width;
            int height = (int)grabResult.Height;

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

            // Set grayscale palette
            ColorPalette palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            bmp.Palette = palette;

            BitmapData bitmapData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format8bppIndexed);

            try
            {
                int dataSize = width * height;
                byte[] managedBuffer = GetPixelDataAsBytes(grabResult.PixelData, dataSize);

                if (managedBuffer == null || managedBuffer.Length < dataSize)
                {
                    Console.WriteLine("Failed to get pixel data");
                    bmp.Dispose();
                    return null;
                }

                System.Runtime.InteropServices.Marshal.Copy(managedBuffer, 0, bitmapData.Scan0, dataSize);
                //Console.WriteLine("Mono8 conversion successful");
            }
            finally
            {
                bmp.UnlockBits(bitmapData);
            }

            return bmp;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting Mono8: {ex.Message}");
            return null;
        }
    }

    private Bitmap ConvertColorToBitmap(IGrabResult grabResult)
    {
        try
        {
            int width = (int)grabResult.Width;
            int height = (int)grabResult.Height;

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            BitmapData bitmapData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);

            try
            {
                int dataSize = width * height * 4;
                byte[] managedBuffer = GetPixelDataAsBytes(grabResult.PixelData, dataSize);

                if (managedBuffer == null || managedBuffer.Length < dataSize)
                {
                    Console.WriteLine("Failed to get pixel data");
                    bmp.Dispose();
                    return null;
                }

                System.Runtime.InteropServices.Marshal.Copy(managedBuffer, 0, bitmapData.Scan0, dataSize);
                //Console.WriteLine("Color conversion successful");
            }
            finally
            {
                bmp.UnlockBits(bitmapData);
            }

            return bmp;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting color: {ex.Message}");
            return null;
        }
    }
}