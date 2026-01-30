using NationalInstruments.Vision.Acquisition;
using NationalInstruments.Vision.Acquisition.Imaqdx;
using NationalInstruments.Vision;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;

public class CameraService : ICameraService, IDisposable
{
    private CancellationTokenSource _cts;
    private ImaqdxSession _cameraSession;
    public event EventHandler<CameraFrameEventArgs> FrameReady;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() =>
        {
            try
            {
                // Initialize the camera session with its IP address
                // You can find the camera name in NI-MAX
                string cameraName = GetCameraNameByIP("192.168.1.100"); // Replace with your camera IP

                _cameraSession = new ImaqdxSession(cameraName);
                _cameraSession.Acquisition.StartAcquisition();

                VisionImage image = new VisionImage();

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Grab frame from camera
                        _cameraSession.Acquisition.GetLatestImageVisionImage(image, 5000); // 5 second timeout

                        // Convert VisionImage to Bitmap
                        Bitmap bmp = (Bitmap)image.GetBitmap().Clone();
                        FrameReady?.Invoke(
                            this,
                            new CameraFrameEventArgs(bmp));

                        Thread.Sleep(33); // ~30 FPS
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error capturing frame: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Camera initialization error: {ex.Message}");
            }
            finally
            {
                _cameraSession?.Acquisition.StopAcquisition();
                _cameraSession?.Dispose();
            }
        }, token);
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose() => Stop();

    /// <summary>
    /// Helper method to find camera name by IP address
    /// </summary>
    private string GetCameraNameByIP(string ipAddress)
    {
        string[] cameraNames = ImaqdxScanner.GetCameraNames(true);

        foreach (string name in cameraNames)
        {
            using (var session = new ImaqdxSession(name))
            {
                try
                {
                    string interfaceIP = session.GetAttribute(ImaqdxAttributeId.InterfaceIpAddress).ToString();
                    if (interfaceIP == ipAddress)
                        return name;
                }
                catch { }
            }
        }

        throw new Exception($"Camera with IP {ipAddress} not found");
    }
}