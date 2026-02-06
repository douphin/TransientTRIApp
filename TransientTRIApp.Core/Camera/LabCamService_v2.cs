using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;

public class LabCamService_v2 : ICameraService, IDisposable
{
    private CancellationTokenSource _cts;
    private uint _sessionHandle = uint.MaxValue;

    public event EventHandler<CameraFrameEventArgs> FrameReady;

    // P/Invoke declarations for IMAQdx
    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxOpenCamera(string cameraName, uint mode, ref uint sessionHandle);

    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxCloseCamera(uint sessionHandle);

    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxStartAcquisition(uint sessionHandle);

    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxStopAcquisition(uint sessionHandle);

    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxGrabFrame(uint sessionHandle, IntPtr buffer, int bufferSize, int timeout);

    [DllImport("niimaqdxSysapiExpert.dll")]
    private static extern int IMAQdxGetImageData(uint sessionHandle, IntPtr buffer, uint bufferSize, uint timeout);

    private const uint SESSION_UNUSED = uint.MaxValue;
    private const int IMAQdx_SUCCESS = 0;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() =>
        {
            try
            {
                // Open camera session with camera name "cam2"
                uint handle = SESSION_UNUSED;
                int status = IMAQdxOpenCamera("cam2", 0, ref handle);

                if (status != IMAQdx_SUCCESS)
                {
                    Console.WriteLine($"Failed to open camera. Status: {status}");
                    return;
                }

                _sessionHandle = handle;

                // Start acquisition
                status = IMAQdxStartAcquisition(handle);
                if (status != IMAQdx_SUCCESS)
                {
                    Console.WriteLine($"Failed to start acquisition. Status: {status}");
                    IMAQdxCloseCamera(handle);
                    return;
                }

                // Allocate buffer for frame data (adjust size based on your camera resolution)
                // For a typical resolution, start with 2048x2048 RGB (12MB)
                int bufferSize = 1626 * 1236 * 3;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Grab frame from camera (5000ms timeout)
                            status = IMAQdxGrabFrame(handle, buffer, bufferSize, 5000);

                            if (status == IMAQdx_SUCCESS)
                            {
                                // Convert buffer to Bitmap and raise event
                                Bitmap bmp = ByteArrayToBitmap(buffer, 1626, 1236); // Adjust width/height
                                FrameReady?.Invoke(this, new CameraFrameEventArgs(bmp));
                            }

                            Thread.Sleep(33); // ~30 FPS
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error capturing frame: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                    IMAQdxStopAcquisition(handle);
                    IMAQdxCloseCamera(handle);
                    _sessionHandle = SESSION_UNUSED;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Camera initialization error: {ex.Message}");
            }
        }, token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        Stop();
        if (_sessionHandle != SESSION_UNUSED)
        {
            IMAQdxStopAcquisition(_sessionHandle);
            IMAQdxCloseCamera(_sessionHandle);
        }
    }

    private Bitmap ByteArrayToBitmap(IntPtr buffer, int width, int height)
    {
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        BitmapData bmpData = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        byte[] managedBuffer = new byte[width * height * 3];
        Marshal.Copy(buffer, managedBuffer, 0, width * height * 3);
        Marshal.Copy(managedBuffer, 0, bmpData.Scan0, width * height * 3);

        bmp.UnlockBits(bmpData);
        return bmp;
    }
}