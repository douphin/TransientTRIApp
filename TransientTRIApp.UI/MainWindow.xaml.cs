using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
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
using Emgu.CV.Aruco;
using System.Diagnostics;
using System.Linq;

namespace TransientTRIApp.UI
{
    public partial class MainWindow : Window
    {
        private readonly IGPUMonitoringService _gpuMonitor;
        private readonly IPulseGeneratorService _pulseGenerator;
        private readonly ICameraService _cameraService;
        private readonly IThermocoupleService _thermocoupleService;
        private DateTime[] _timestamps;
        private int _dataPointCount = 0;
        private const int MaxDataPoints = 60; // Keep last 60 readings
        private object _bitmapLock = new object();
        private Mutex _bitmapMutex = new Mutex();
        private volatile bool _isHotFrameRolling = false;
        private Bitmap _currentDisplayBitmap;
        private Bitmap _capturedBitmap;
        private Bitmap _darkFrameBitmap;
        private double _actualTriggerRate = 0;
        private double _actualPulseWidth = 0;
        private double _actualLVPeak = 0;
        private long _frameCount = 0;
        private readonly object _frameCountLock = new object();
        private readonly int _metricSampleRate = 1000;

        private DispatcherTimer _debounceTimer;
        private const int DebounceDelayMs = 2500; // Wait 500ms after slider stops moving

        private readonly System.Timers.Timer _frameTimer = new System.Timers.Timer();

        private string _recordingFolderPath;
        private StreamWriter _csvFile;
        private volatile bool _isRecording = false;
        private object _csvModelLock = new object();
        private System.Collections.Generic.Dictionary<string, CombinedMetrics> _csvModel = new System.Collections.Generic.Dictionary<string, CombinedMetrics>();

        public ChartValues<double> GPUUtilizationData;
        public ChartValues<double> GPUTemperatureData;
        public ChartValues<double> TCTemperatureData;
        public ChartValues<double> CameraExposureTiming;
        public ChartValues<double> PulseGeneratorTiming;
        public ChartValues<double> PixelValueCount;

        public MainWindow()
        {
            InitializeComponent();

            _cameraService = new CameraService();
            _gpuMonitor = new GPUMonitoringService();
            _pulseGenerator = new PulseGeneratorService();
            _thermocoupleService = new ThermocoupleService();

            _cameraService.FrameReady += OnFrameReady;
            _gpuMonitor.MetricsUpdated += OnGPUMetricsUpdated;
            _thermocoupleService.TempReady += OnTCReadingsUpdated;

            InitializeChartData();
            BindGPUSliders();
            BindCameraSliders();

            ImageProcessing.PreProcess();

            this.DataContext = this;
        }

        private void InitializeChartData()
        {
            GPUUtilizationData = new ChartValues<double>();
            GPUTemperatureData = new ChartValues<double>();
            TCTemperatureData = new ChartValues<double>();
            CameraExposureTiming = new ChartValues<double>();
            PulseGeneratorTiming = new ChartValues<double>();
            PixelValueCount = new ChartValues<double>();
            _timestamps = new DateTime[MaxDataPoints];
        }

        private void BindGPUSliders()
        {
            // Trigger Rate
            TriggerRateSlider.ValueChanged += (s, e) =>
            {
                TriggerRateValue.Text = $"{TriggerRateSlider.Value:F0}";
                TriggerRateInput.Text = TriggerRateSlider.Value.ToString("F0");
            };
            TriggerRateInput.LostFocus += (s, e) =>
            {
                if (double.TryParse(TriggerRateInput.Text, out double value))
                {
                    TriggerRateSlider.Value = Clamp(value, TriggerRateSlider.Minimum, TriggerRateSlider.Maximum);
                }
            };
            TriggerRateValue.Text = TriggerRateSlider.Value.ToString();

            // Pulse Width
            PulseWidthSlider.ValueChanged += (s, e) =>
            {
                PulseWidthValue.Text = $"{PulseWidthSlider.Value:E2}";
                PulseWidthInput.Text = PulseWidthSlider.Value.ToString("E2");
            };
            PulseWidthInput.LostFocus += (s, e) =>
            {
                if (double.TryParse(PulseWidthInput.Text, out double value))
                {
                    PulseWidthSlider.Value = Clamp(value, PulseWidthSlider.Minimum, PulseWidthSlider.Maximum);
                }
            };
            PulseWidthValue.Text = PulseWidthSlider.Value.ToString();

            // LV Peak
            LVPeakSlider.ValueChanged += (s, e) =>
            {
                LVPeakValue.Text = $"{LVPeakSlider.Value:F2}";
                LVPeakInput.Text = LVPeakSlider.Value.ToString("F2");
            };
            LVPeakInput.LostFocus += (s, e) =>
            {
                if (double.TryParse(LVPeakInput.Text, out double value))
                {
                    LVPeakSlider.Value = Clamp(value, LVPeakSlider.Minimum, LVPeakSlider.Maximum);
                }
            };
            LVPeakValue.Text = LVPeakSlider.Value.ToString();

            //CalculateLEDTiming(TriggerRateSlider.Value, PulseWidthSlider.Value);
        }

        private void BindCameraSliders()
        {
            // Exposure
            ExposureSlider.ValueChanged += (s, e) =>
            {
                ExposureValue.Text = $"{ExposureSlider.Value:F1}";
                ExposureInput.Text = ExposureSlider.Value.ToString("F1");

                // Update camera exposure in real-time
                _cameraService.SetExposure(ExposureSlider.Value * 1000);

                if (AdjustLED.IsChecked == true)
                {
                    PulseWidthSlider.Value = ExposureSlider.Value / 1000 / 2;
                    TriggerRateSlider.Value = 1 / PulseWidthSlider.Value;

                    if (SubmitLED.IsChecked == true)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                }
            };

            ExposureInput.LostFocus += (s, e) =>
            {
                if (double.TryParse(ExposureInput.Text, out double value))
                {
                    ExposureSlider.Value = Clamp(value, ExposureSlider.Minimum, ExposureSlider.Maximum);
                }
            };

            // Offset
            //OffsetSlider.ValueChanged += (s, e) =>
            //{
            //    OffsetInput.Text = $"{OffsetSlider.Value:F1}";
            //    OffsetInput.Text = OffsetSlider.Value.ToString("F1");

            //    // Update camera offset in real-time
            //    UpdateCameraFrameOffset((int)OffsetSlider.Value);
            //};

            //OffsetInput.TextChanged += (s, e) =>
            //{
            //    if (double.TryParse(OffsetInput.Text, out double value))
            //    {
            //        OffsetSlider.Value = Clamp(value, OffsetSlider.Minimum, OffsetSlider.Maximum);
            //    }
            //};
        }

        private void CalculateLEDTiming(double triggerRate, double pulseWidth)
        {
            //int higherCounter = 0;

            for(int i = 0; i < 100; i++)
            {
                //if (i )
            }
        }

        private void UpdateCameraFrameOffset(int offset)
        {
            _cameraService.UpdateCameraFrameOffset(offset);
        }

        private double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void UI(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // Setup chart series
            var gpuUtilizationSeries = new LineSeries
            {
                Title = "GPU Utilization (%)",
                Values = GPUUtilizationData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.CornflowerBlue,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,           
            };

            var gpuTemperatureSeries = new LineSeries
            {
                Title = "GPU Temperature (°C)",
                Values = GPUTemperatureData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.OrangeRed,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,               
            };

            var tcTemperatureSeries = new LineSeries
            {
                Title = "TC Temperature (°C)",
                Values = TCTemperatureData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.DarkSeaGreen,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
            };


            // This is for some timing thing I started working one but abandoned
            var cameraTiming = new LineSeries
            {
                Title = "Camera",
                Values = CameraExposureTiming,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.DarkSeaGreen,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
            };

            var pulseGen = new LineSeries
            {
                Title = "LED",
                Values = PulseGeneratorTiming,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.OrangeRed,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
            };

            //
            var pixelValueCount = new LineSeries
            {
                Title = "Value Count",
                Values = PixelValueCount,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.OrangeRed,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
            };

            GPUChart.Series = new SeriesCollection { gpuUtilizationSeries, gpuTemperatureSeries, tcTemperatureSeries };
            TimingChart.Series = new SeriesCollection { cameraTiming, pulseGen };
            PixelValueCountChart.Series = new SeriesCollection {  pixelValueCount };

            _ = ReadMachineSettings();

            _cameraService.Start();
            _gpuMonitor.Start(_metricSampleRate); // Start with 1 second interval   
            _thermocoupleService.Start(_metricSampleRate);
            InitializeTimers();
        }

        private void InitializeTimers()
        {
            _frameTimer.Interval = 1000;
            _frameTimer.Elapsed += FrameTimer_Elapsed;
            _frameTimer.Start();

            // Setup debounce timer
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelayMs);
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                _ = ConfigurePulseGenerator(); // Call your function
            };
        }

        private void FrameTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Bitmap localBitmap = null;

            _bitmapMutex.WaitOne();
            {
                try
                {
                    if (_currentDisplayBitmap != null)
                        localBitmap = (Bitmap)_currentDisplayBitmap?.Clone();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            _bitmapMutex.ReleaseMutex();

            lock (_frameCountLock)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FPSCounter.Text = "FPS: " + _frameCount.ToString();
                    _frameCount = 0;
                }));
            }

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

        private void OnFrameReady(object sender, CameraFrameEventArgs e)
        {
            UI(() =>
            {
                Bitmap bmp;

                CameraFrame cf = new CameraFrame
                {
                    Frame = null,
                    CaptureTime = DateTime.Now,
                    RecentTCReading = TCTemperatureData.LastOrDefault(),
                    SubtractDarkFrame = SubtractDarkFrame.IsChecked == true,
                    DivideByColdFrame = DivideByColdFrame.IsChecked == true,
                    ScaleByTemperature = ScaleByTemperature.IsChecked == true,
                    TrackROI = false,
                    NormalizeBeforeMap = true,
                    ApplyColorMap = true,
                };
                 
                if (double.TryParse(Coefficient.Text, out double coe))
                    cf.Coefficient = coe;
                if (double.TryParse(Coefficient.Text, out double adhoc))
                    cf.Coefficient = adhoc;

                // Store the current bitmap temporarily
                _bitmapMutex.WaitOne();
                {
                    _currentDisplayBitmap = (Bitmap)e.Bmp.Clone(); // Clone it for storage

                    if (_isHotFrameRolling)
                    {
                        cf.Frame = (Bitmap)_currentDisplayBitmap.Clone();
                        bmp = ImageProcessing.ProcessFrame(cf);                
                    }
                    else
                    {
                        bmp = e.Bmp;
                    }

                    cf.Dispose();
                }
                _bitmapMutex.ReleaseMutex();

                // This should really only be true just after program startup, to autofill the darkframe
                if (_darkFrameBitmap == null && _frameCount > 10)
                {
                    SetDarkFrame();
                    StartLED();
                }

                // Display it as before
                CameraImage.Source = ConvertBitmap(bmp);

                lock (_frameCountLock)
                {
                    _frameCount++;
                    Frame.Text = _frameCount.ToString();
                }
            });
        }

        private void OnGPUMetricsUpdated(object sender, GPUMetrics metrics)
        {
            UI(() =>
            {
                AddGPUDataPoint(metrics);

                if (_isRecording)
                {
                    lock (_csvModelLock)
                    {
                        if (_csvModel.TryGetValue(metrics.Timestamp.ToString(), out var value))
                            value.Update(metrics);
                        else
                            _csvModel.Add(metrics.Timestamp.ToString(), new CombinedMetrics(metrics));
                    }
                }
            });
        }

        private void AddGPUDataPoint(GPUMetrics metrics)
        {
            if (_dataPointCount >= MaxDataPoints)
            {
                // Remove oldest data point
                GPUUtilizationData.RemoveAt(0);
                GPUTemperatureData.RemoveAt(0);
                Array.Copy(_timestamps, 1, _timestamps, 0, MaxDataPoints - 1);
            }
            else
            {
                _dataPointCount++;
            }

            GPUUtilizationData.Add(metrics.GPUUtilization);
            GPUTemperatureData.Add(metrics.GPUTemperature);
            _timestamps[_dataPointCount - 1] = metrics.Timestamp;
        }

        public void OnTCReadingsUpdated(object sender, ThermocoupleReadings readings)
        {
            UI(() =>
            {
                TCTemperatureData.Add(readings.TCTemperature);
                if (TCTemperatureData.Count > 60)
                    TCTemperatureData.RemoveAt(0);

                if (_isRecording)
                {
                    lock (_csvModelLock)
                    {
                        if (_csvModel.TryGetValue(readings.Timestamp.ToString(), out var value))
                            value.Update(readings);
                        else
                            _csvModel.Add(readings.Timestamp.ToString(), new CombinedMetrics(readings));
                    }
                }
            });
        }


        private void SelectRecordingFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select folder to save recording data";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _recordingFolderPath = dialog.SelectedPath;
                RecordingFolderDisplay.Text = System.IO.Path.GetFileName(_recordingFolderPath);
                RecordingToggleButton.IsEnabled = true;
                Console.WriteLine($"Recording folder selected: {_recordingFolderPath}");
            }
        }

        private void ToggleRecording()
        {
            if (_isRecording)
            {
                _ = StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string metricsPath = System.IO.Path.Combine(_recordingFolderPath, $"metrics_{timestamp}.csv");

                _csvFile = new StreamWriter(metricsPath, true);
                _csvFile.WriteLine("TCTimestamp, GPUTimestamp,TCTemperature(C),GPUTemperature(C),GPUUtilization(%)");
                _csvFile.Flush();

                _isRecording = true;
                RecordingToggleButton.Content = "Stop Recording";
                RecordingToggleButton.Background = System.Windows.Media.Brushes.IndianRed;
                RecordingFolderDisplay.Text += " [RECORDING]";

                Console.WriteLine($"Recording started: {metricsPath}");
                //MessageBox.Show($"Recording started!\nMetrics saved to: {metricsPath}", "Recording Started", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting recording: {ex.Message}");
            }
        }

        private async Task StopRecording()
        {
            try
            {
                RecordingToggleButton.Content = "Saving...";
                _isRecording = false;

                await Task.Run(() =>
                {
                    foreach(CombinedMetrics item in _csvModel.Values)
                    {
                        _csvFile.WriteLine($"{item.TCTimestamp:yyyy-MM-dd HH:mm:ss.fff},{item.GPUTimestamp:yyyy-MM-dd HH:mm:ss.fff},{item.TCTemperature:F2},{item.GPUTemperature:F2},{item.GPUUtilization:F1}");
                        _csvFile.Flush();
                    }
                });
       
                _csvFile?.Close();
                _csvFile?.Dispose();

                RecordingToggleButton.Content = "Start Recording";
                RecordingToggleButton.Background = System.Windows.Media.Brushes.LimeGreen;
                RecordingFolderDisplay.Text = RecordingFolderDisplay.Text.Replace(" [RECORDING]", "");

                Console.WriteLine("Recording stopped");
                MessageBox.Show("Recording stopped!", "Recording Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping recording: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetDarkFrame()
        {
            lock (_bitmapLock)
            {
                if (_currentDisplayBitmap != null)
                {
                    // Dispose old captured bitmap
                    _darkFrameBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _darkFrameBitmap = (Bitmap)_currentDisplayBitmap.Clone();
                    ImageProcessing.SetNewMatRefDark(_darkFrameBitmap);
                    DarkFrameTime.Text = DateTime.Now.ToLongTimeString();
                }
                else
                {
                    MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void SetColdFrame()
        {
            lock (_bitmapLock)
            {
                if (_currentDisplayBitmap != null)
                {
                    // Dispose old captured bitmap
                    _capturedBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _capturedBitmap = (Bitmap)_currentDisplayBitmap.Clone();
                    ImageProcessing.SetNewMatRefCold( _capturedBitmap );
                    ColdFrameTime.Text = DateTime.Now.ToLongTimeString();
                }
                else
                {
                    MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void StartHotFrame()
        {
            _isHotFrameRolling = !_isHotFrameRolling;
            HotFrameToggle.Content = _isHotFrameRolling ? "Pause Hot Frame" : "Start Hot Frame";
        }

        private void PauseLED()
        {
            try
            {
                Task.Run(() =>
                {
                    _pulseGenerator.Connect("GPIB0::6::INSTR");
                    _pulseGenerator.SendSingleCycle();
                    _pulseGenerator.Disconnect();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Connecting and Sending Single Cycle {ex.Message}");
            }
        }

        private void StartLED()
        {
            try
            {
                Task.Run(() =>
                {
                    _pulseGenerator.Connect("GPIB0::6::INSTR");
                    _pulseGenerator.SendTriggerRate();
                    _pulseGenerator.Disconnect();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Connecting and Sending Single Cycle {ex.Message}");
            }
        }

        private void OnConfigurePulseGenerator(object sender, RoutedEventArgs e)
        {
            _ = ConfigurePulseGenerator();
        }

        private async Task ConfigurePulseGenerator()
        {
            try
            {
                double triggerRate = TriggerRateSlider.Value;
                double pulseWidth = PulseWidthSlider.Value;
                double lvPeak = LVPeakSlider.Value;

                await Task.Run(() =>
                {
                    // Uncomment when GPIB device is available:
                    _pulseGenerator.Connect("GPIB0::6::INSTR");
                    _pulseGenerator.Configure(triggerRate, pulseWidth, lvPeak);
                    _pulseGenerator.Disconnect();

                    // Update stored values
                    _actualTriggerRate = _pulseGenerator.TriggerRateHz;
                    _actualPulseWidth = _pulseGenerator.PulseWidthSec;
                    _actualLVPeak = _pulseGenerator.LVPeakV;
                });

                // Update UI
                UpdateActualValuesDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateActualValuesDisplay()
        {
            TriggerRateActual.Text = $"(Actual: {_actualTriggerRate:F0} Hz)";
            PulseWidthActual.Text = $"(Actual: {_actualPulseWidth:E2} s)";
            LVPeakActual.Text = $"(Actual: {_actualLVPeak:F2} V)";
        }

        // Call this on startup to read initial machine settings
        private async Task ReadMachineSettings()
        {
            try
            {
                    await Task.Run(() =>
                    {
                        _pulseGenerator.Connect("GPIB0::6::INSTR");
                        _pulseGenerator.InitialConfiguration();
                        _pulseGenerator.GetCurrentSettings();
                        _pulseGenerator.Disconnect();

                        _actualTriggerRate = _pulseGenerator.TriggerRateHz;
                        _actualPulseWidth = _pulseGenerator.PulseWidthSec;
                        _actualLVPeak = _pulseGenerator.LVPeakV;
                    });

                UpdateActualValuesDisplay();

                TriggerRateSlider.Value = _actualTriggerRate;
                TriggerRateInput.Text = _actualTriggerRate.ToString();

                PulseWidthSlider.Value = _actualPulseWidth;
                PulseWidthInput.Text = _actualPulseWidth.ToString();

                LVPeakSlider.Value = _actualLVPeak;
                LVPeakInput.Text = _actualLVPeak.ToString();

                if (_actualPulseWidth > 0 && _actualTriggerRate > 0 && Math.Round(_actualTriggerRate * _actualPulseWidth) == 1)
                {
                    ExposureSlider.Value = _actualPulseWidth * 1000 * 2;
                }

                Console.WriteLine("Machine settings read successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading machine settings: {ex.Message}");
            }
        }

        private void UpdateInterval()
        {
            if (int.TryParse(UpdateIntervalBox.Text, out int intervalMs))
            {
                _gpuMonitor.SetUpdateInterval(intervalMs);
                _thermocoupleService.SetUpdateInterval(intervalMs);
                MessageBox.Show($"Update interval set to {intervalMs}ms", "GPU Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Please enter a valid interval in milliseconds", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartAll()
        {
            _frameCount = 0;
            FPSCounter.Text = "0";

            _cameraService.Start();
            _gpuMonitor.Start(1000);
            _frameTimer.Start();
        }

        private void StopAll()
        {
            _cameraService.Stop();
            _gpuMonitor.Stop();
            _thermocoupleService.Stop();
            _frameTimer.Stop();
        }

        // Method to retrieve the captured bitmap (thread-safe)
        public Bitmap GetCapturedBitmap()
        {
            Bitmap temp;
            _bitmapMutex.WaitOne();
            {
                // Return a clone so caller doesn't accidentally dispose it
                temp = _capturedBitmap?.Clone() as Bitmap;
            }
            _bitmapMutex.ReleaseMutex();
            return temp;
        }

        // Method to save captured bitmap to file
        public void SaveCapturedBitmapToFile(string filePath)
        {
            _bitmapMutex.WaitOne();
            {
                if (_capturedBitmap != null)
                {
                    try
                    {
                        _capturedBitmap.Save(filePath);
                        Console.WriteLine($"Bitmap saved to {filePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving bitmap: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("No captured bitmap to save");
                }
            }
            _bitmapMutex.ReleaseMutex();
        }

        // Clean up when window closes
        protected override void OnClosed(EventArgs e)
        {
            _frameTimer.Stop();
            _cameraService.Stop();
            Thread.Sleep(100);
            _gpuMonitor.Stop();
            Thread.Sleep(100);

            _bitmapMutex.WaitOne();
            {
                _capturedBitmap?.Dispose();
                _currentDisplayBitmap?.Dispose();
            }
            _bitmapMutex.ReleaseMutex();
            _bitmapMutex.Dispose();

            if (_isRecording)
                _ = StopRecording();

            base.OnClosed(e);
        }

        // --- Bitmap -> BitmapSource conversion ---
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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
                bitmap.Dispose();
            }
        }


        // Button Handlers
        // If you bake logic directly into the button handler, it can be hard to call that function elsewhere so wer separate them a bit

        private void OnSelectRecordingFolderClicked(object sender, RoutedEventArgs e)
        {
            SelectRecordingFolder();
        }

        private void OnToggleRecordingClicked(object sender, RoutedEventArgs e)
        {
            ToggleRecording();
        }

        private void OnSetDarkFrameClicked(object sender, RoutedEventArgs e)
        {
            SetDarkFrame();
        }

        private void OnSetColdFrameClicked(object sender, RoutedEventArgs e)
        {
            SetColdFrame();
        }

        private void OnStartHotFrameClicked(object sender, RoutedEventArgs e)
        {
            StartHotFrame();
        }

        private void OnPauseLEDClicked(object sender, RoutedEventArgs e)
        {
            PauseLED();
        }

        private void OnStartLEDClicked(object sender, RoutedEventArgs e)
        {
            StartLED();
        }

        private void OnConfigurePulseGeneratorClicked(object sender, RoutedEventArgs e)
        {
            _ = ConfigurePulseGenerator();
        }

        private void OnUpdateIntervalClicked(object sender, RoutedEventArgs e)
        {
            UpdateInterval();
        }

        private void OnStartAllClicked(object sender, RoutedEventArgs e)
        {
            StartAll();
        }

        private void OnStopAllClicked(object sender, RoutedEventArgs e)
        {
            StopAll();
        }
    }
}