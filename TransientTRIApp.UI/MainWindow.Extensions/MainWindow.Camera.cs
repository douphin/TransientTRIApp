using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Emgu.CV.Aruco;
using LiveCharts;
using LiveCharts.Wpf;
using TransientTRIApp.Common;
using TransientTRIApp.Common.Events;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Models;
using TransientTRIApp.Core;
using TransientTRIApp.Core.Camera;
using TransientTRIApp.Core.GPU;
using TransientTRIApp.Core.Hardware;

namespace TransientTRIApp.UI
{
    // This file will be used to hold all of the camera logic
    public partial class MainWindow : Window
    {
        // Global Variables
        private readonly Mutex _bitmapMutex = new Mutex();
        private volatile bool _isHotFrameRolling = false;
        private Bitmap _currentCameraBitmap;
        private Bitmap _currentDisplayBitmap;
        private Bitmap _coldFrameBitmap;
        private Bitmap _darkFrameBitmap;
        private Bitmap _hotFrameBitmap;
        private double _tcReadingForDarkFrame;
        private double _tcReadingForColdFrame;
        private double _tcReadingForHotFrame;
        private DateTime _darkFrameTime;
        private DateTime _coldFrameTime;
        private DateTime _hotFrameTime;

        private long _frameCount = 0;
        private readonly object _frameCountLock = new object();

        // variables needed for ROI rectangle
        private Rectangle _drawnRect;
        private Rectangle _draggingRect;
        private int _startX;
        private int _startY;
        private bool _isDraggingROI;
        private double _elementImageScaleRateX = -1;
        private double _elementImageScaleRateY = -1;

        /// <summary>
        /// Will take the current bitmap and get pixel value count for it and then display that on the graph for it
        /// </summary>
        private void PixelValueCountTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Bitmap localBitmap;

            _bitmapMutex.WaitOne();
            {
                localBitmap = _currentCameraBitmap?.Clone() as Bitmap;
            }
            _bitmapMutex.ReleaseMutex();

            if (localBitmap != null)
            {
                var data = ImageProcessing.GetPixelValueCount(localBitmap);
                UI(() =>
                {
                    PixelValueCount.Clear();
                    PixelValueCount.AddRange(data);
                    PixelValueCountChart.AxisY[0].MaxValue = data.Max();
                    localBitmap.Dispose();
                });
            }
        }

        /// <summary>
        /// Will update the UI FPS metric
        /// </summary>
        private void FrameTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_frameCountLock)
            {
                UI(() =>
                {
                    FPSCounter.Text = "FPS: " + _frameCount.ToString();
                    _frameCount = 0;
                });
            }         
        }

        /// <summary>
        /// Will Process any incoming frames as necessary and then submit them to the UI. Will also take take darkframe and start LED on program startup.
        /// </summary>
        private void OnFrameReady(object sender, CameraFrameEventArgs e)
        {
            UI(() =>
            {
                // Build an object with any data we might need for image processing, including flags
                CameraFrame cf = new CameraFrame
                {
                    Frame = null,
                    CaptureTime = DateTime.Now,
                    IsHotFrameRolling = _isHotFrameRolling,
                    RecentTCReading = TCTemperatureData.LastOrDefault(),
                    ColdFrameTCReading = _tcReadingForColdFrame,
                    SubtractDarkFrame = SubtractDarkFrame.IsChecked == true,
                    DivideByColdFrame = DivideByColdFrame.IsChecked == true,
                    ScaleByTemperature = ScaleByTemperature.IsChecked == true,
                    TrackROI = false,
                    NormalizeBeforeMap = true,
                    ApplyColorMap = true,
                    roi = _isDraggingROI ? _draggingRect : _drawnRect
                };

                if (double.TryParse(Coefficient.Text, out double coe))
                    cf.Coefficient = coe;
                if (double.TryParse(Coefficient.Text, out double adhoc))
                    cf.Coefficient = adhoc;


                // Store the current bitmap temporarily
                try
                {
                    _bitmapMutex.WaitOne();
                    {
                        _currentCameraBitmap = (Bitmap)e.Bmp.Clone(); // Clone it for storage

                        cf.Frame = (Bitmap)_currentCameraBitmap.Clone();
                        _currentDisplayBitmap = ImageProcessing.ProcessFrame(cf);

                        cf.Dispose();

                        // Saves Scale Rate on program startup between actual image size and image display size, usually ~2.26 I think
                        if (_elementImageScaleRateX == -1 && _frameCount > 10)
                        {
                            _elementImageScaleRateX = _currentCameraBitmap.Width / CameraImage.ActualWidth;
                            _elementImageScaleRateY = _currentCameraBitmap.Height / CameraImage.ActualHeight;
                        }

                        // Set current image on UI
                        CameraImage.Source = ConvertBitmap(_currentDisplayBitmap);
                    }
                    _bitmapMutex.ReleaseMutex();
                }
                // An exception can be thrown if the mutex is waiting when the program ends and it gets disposed, this is just to smooth that out
                catch (ObjectDisposedException ex)
                {
                    Console.WriteLine(ex.Message);
                    return;
                }

                // This should really only be true just after program startup, to autofill the darkframe
                if (_darkFrameBitmap == null && _frameCount > 10)
                {
                    SetDarkFrame();
                    StartLED();
                }

                

                lock (_frameCountLock)
                {
                    _frameCount++;
                    Frame.Text = _frameCount.ToString();
                }
            });
        }


        /// <summary>
        /// Method to retrieve the captured bitmap (thread-safe)
        /// </summary>
        public Bitmap GetCapturedBitmap()
        {
            Bitmap temp;
            _bitmapMutex.WaitOne();
            {
                // Return a clone so caller doesn't accidentally dispose it
                temp = _coldFrameBitmap?.Clone() as Bitmap;
            }
            _bitmapMutex.ReleaseMutex();
            return temp;
        }

        /// <summary>
        /// Will hold mutex to hold the current frame as the darkframe, will also submit that frame to the ImageProcessing class.
        /// This can be called from the OnSetDarkFrameClicked button handler, but will usually get called automatically on program start up after the camera has intialized
        /// </summary>
        private void SetDarkFrame()
        {
            _bitmapMutex.WaitOne();
            {
                if (_currentCameraBitmap != null)
                {
                    // Dispose old captured bitmap
                    _darkFrameBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _darkFrameBitmap = (Bitmap)_currentCameraBitmap.Clone();
                    _tcReadingForDarkFrame = TCTemperatureData.LastOrDefault();
                    ImageProcessing.SetNewMatRefDark(_darkFrameBitmap);
                    _darkFrameTime = DateTime.Now;
                    DarkFrameTime.Text = _darkFrameTime.ToLongTimeString();
                    DarkFrameTemp.Text = _tcReadingForDarkFrame.ToString("F1") + " (°C)";
                }
                else
                {
                    MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _bitmapMutex.ReleaseMutex();
        }

        /// <summary>
        /// Will hold mutex to hold the current frame as the cold frame, will also submit that frame to the ImageProcessing class.
        /// This can be called from the OnSetColdFrameClicked button handler, as of right now there is no situation where this will get called automatically
        /// </summary>
        private void SetColdFrame()
        {
            _bitmapMutex.WaitOne();
            {
                if (_currentCameraBitmap != null)
                {
                    // Dispose old captured bitmap
                    _coldFrameBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _coldFrameBitmap = (Bitmap)_currentCameraBitmap.Clone();
                    _tcReadingForColdFrame = TCTemperatureData.LastOrDefault();
                    ImageProcessing.SetNewMatRefCold(_coldFrameBitmap);
                    _coldFrameTime = DateTime.Now;
                    ColdFrameTime.Text = _coldFrameTime.ToLongTimeString();
                    ColdFrameTemp.Text = _tcReadingForColdFrame.ToString("F1") + " (°C)";
                }
                else
                {
                    MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _bitmapMutex.ReleaseMutex();
        }

        /// <summary>
        /// Will hold mutex to hold the current frame as the hot frame, will also submit that frame to the ImageProcessing class.
        /// This can be called from the OnSetHotFrameClicked button handler, as of right now there is no situation where this will get called automatically
        /// </summary>
        private void SetHotFrame()
        {
            _bitmapMutex.WaitOne();
            {
                if (_currentCameraBitmap != null)
                {
                    // Dispose old captured bitmap
                    _hotFrameBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _hotFrameBitmap = (Bitmap)_currentCameraBitmap.Clone();
                    _tcReadingForHotFrame = TCTemperatureData.LastOrDefault();
                    ImageProcessing.SetNewMatRefHot(_hotFrameBitmap);
                    _hotFrameTime = DateTime.Now;
                    HotFrameTime.Text = _hotFrameTime.ToLongTimeString();
                    HotFrameTemp.Text = _tcReadingForHotFrame.ToString("F1") + " (°C)";
                }
                else
                {
                    MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            _bitmapMutex.ReleaseMutex();
        }

        /// <summary>
        /// Will just toggle a flag for the Hotframe and then change button text based on the flag's new value
        /// </summary>
        private void StartHotFrame()
        {
            _isHotFrameRolling = !_isHotFrameRolling;
            HotFrameToggle.Content = _isHotFrameRolling ? "Pause Hot Frame" : "Start Hot Frame";
        }

        /// <summary>
        /// It is important that all bitmap/bitmap adjacent objects get disposed to avoid memory leaks
        /// </summary>
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Convert a Bitmap to a BitmapSource so that it can be displayed in the UI
        /// </summary>
        private static BitmapSource ConvertBitmap(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
                //bitmap.Dispose();
            }
        }

        // Add this method to save a frame
        private void SaveCurrentFrame(string label)
        {
            if (_isRecording == false && label.Contains("Final") == false && label.Contains("Initial") == false)
                return;

            Bitmap localCameraFrame;
            Bitmap localDisplayFrame;

            _bitmapMutex.WaitOne();
            {
                localCameraFrame = _currentCameraBitmap?.Clone() as Bitmap;
                localDisplayFrame = _currentDisplayBitmap?.Clone() as Bitmap;
            }
            _bitmapMutex.ReleaseMutex();

            if (localCameraFrame == null)
            {
                Console.WriteLine("No frame available to save");
                return;
            }

            try
            {
                string filename = $"{label}_{DateTime.Now:HH-mm-ss-fff}.png";
                string filepath = Path.Combine(_recordingSessionFolder, "images", filename);
                 

                localCameraFrame.Save(filepath, System.Drawing.Imaging.ImageFormat.Png);

                if (_isHotFrameRolling)
                {
                    filename = $"{label}_HotFrame.png";
                    filepath = Path.Combine(_recordingSessionFolder, "images", filename);

                    localDisplayFrame.Save(filepath, System.Drawing.Imaging.ImageFormat.Png);
                }

                _savedFrameCount++;

                Console.WriteLine($"Frame saved: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving frame: {ex.Message}");
            }
            finally
            {
                localCameraFrame?.Dispose();
                localDisplayFrame?.Dispose();
            }
        }

        public void ComputerCTR()
        {
            var CTRs = ImageProcessing.ComputeCTR(_tcReadingForHotFrame - _tcReadingForColdFrame,
                _isDraggingROI ? _draggingRect : _drawnRect);

            AvgCTRofImage.Text = CTRs.Item1.ToString();
            AvgCTRofROI.Text = CTRs.Item2.ToString();
        }
    }
}