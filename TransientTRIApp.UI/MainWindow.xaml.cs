using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    public partial class MainWindow : Window
    {
        private readonly IGPUMonitoringService _gpuMonitor;
        private readonly IPulseGeneratorService _pulseGenerator;
        private readonly ICameraService _cameraService;
        private DateTime[] _timestamps;
        private int _dataPointCount = 0;
        private const int MaxDataPoints = 60; // Keep last 60 readings
        private object _bitmapLock = new object();
        private volatile bool _isHotFrameRolling = false;
        private Bitmap _currentDisplayBitmap;
        private Bitmap _capturedBitmap;
        private double _actualTriggerRate = 0;
        private double _actualPulseWidth = 0;
        private double _actualLVPeak = 0;
        private long _frameCount = 0;
        private object _frameCountLock = new object();

        public ChartValues<double> GPUUtilizationData;
        public ChartValues<double> GPUTemperatureData;

        public MainWindow()
        {
            InitializeComponent();

            _cameraService = new CameraService();
            _gpuMonitor = new GPUMonitoringService();
            _pulseGenerator = new PulseGeneratorService();

            _cameraService.FrameReady += OnFrameReady;
            _gpuMonitor.MetricsUpdated += OnGPUMetricsUpdated;

            InitializeChartData();
            BindSliders();

            this.DataContext = this;
        }

        private void InitializeChartData()
        {
            GPUUtilizationData = new ChartValues<double>();
            GPUTemperatureData = new ChartValues<double>();
            _timestamps = new DateTime[MaxDataPoints];
        }

        private void BindSliders()
        {
            // Trigger Rate
            TriggerRateSlider.ValueChanged += (s, e) =>
            {
                TriggerRateValue.Text = $"{TriggerRateSlider.Value:F0}";
                TriggerRateInput.Text = TriggerRateSlider.Value.ToString("F0");
            };
            TriggerRateInput.TextChanged += (s, e) =>
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
            PulseWidthInput.TextChanged += (s, e) =>
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
            LVPeakInput.TextChanged += (s, e) =>
            {
                if (double.TryParse(LVPeakInput.Text, out double value))
                {
                    LVPeakSlider.Value = Clamp(value, LVPeakSlider.Minimum, LVPeakSlider.Maximum);
                }
            };
            LVPeakValue.Text = LVPeakSlider.Value.ToString();
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
            var utilizationSeries = new LineSeries
            {
                Title = "GPU Utilization (%)",
                Values = GPUUtilizationData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.CornflowerBlue,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
                
            };

            var temperatureSeries = new LineSeries
            {
                Title = "GPU Temperature (°C)",
                Values = GPUTemperatureData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.OrangeRed,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
                
                
            };

            GPUChart.Series = new SeriesCollection { utilizationSeries, temperatureSeries };

            ReadMachineSettings();

            _cameraService.Start();
            _gpuMonitor.Start(1000); // Start with 1 second interval   
            StartTimers();
        }

        private void StartTimers()
        {
            System.Timers.Timer frameTimer = new System.Timers.Timer();
            frameTimer.Interval = 1000;
            frameTimer.Elapsed += FrameTimer_Elapsed;
            frameTimer.Start();
        }

        private void FrameTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_frameCountLock)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FPSCounter.Text = _frameCount.ToString();
                    _frameCount = 0;
                }));
            }
        }

        private void OnFrameReady(object sender, CameraFrameEventArgs e)
        {
            UI(() =>
            {
                Bitmap bmp;
                // Store the current bitmap temporarily
                lock (_bitmapLock)
                {
                    _currentDisplayBitmap = (Bitmap)e.Bmp.Clone(); // Clone it for storage
                    if (_isHotFrameRolling)
                    {
                        bmp = ImageProcessing.SubtractBitmapsWithEmguCV(_currentDisplayBitmap, _capturedBitmap);
                    }
                    else
                    {
                        bmp = e.Bmp;
                    }
                }

                // Display it as before
                CameraImage.Source = ConvertBitmap(bmp);

                lock (_frameCountLock)
                {
                    _frameCount++;
                }
            });
        }

        private void OnGPUMetricsUpdated(object sender, GPUMetrics metrics)
        {
            UI(() =>
            {
                AddGPUDataPoint(metrics.GPUUtilization, metrics.GPUTemperature, metrics.Timestamp);
            });
        }

        private void AddGPUDataPoint(double utilization, double temperature, DateTime timestamp)
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

            GPUUtilizationData.Add(utilization);
            GPUTemperatureData.Add(temperature);
            _timestamps[_dataPointCount - 1] = timestamp;
        }

        private void OnSetColdFrameClicked(object sender, RoutedEventArgs e)
        {
            lock (_bitmapLock)
            {
                if (_currentDisplayBitmap != null)
                {
                    // Dispose old captured bitmap
                    _capturedBitmap?.Dispose();

                    // Clone the current display bitmap for storage
                    _capturedBitmap = (Bitmap)_currentDisplayBitmap.Clone();

                    //MessageBox.Show("Frame captured successfully!", "Capture", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    //MessageBox.Show("No frame available to capture", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void OnStartHotFrameClicked(object sender, RoutedEventArgs e)
        {
            _isHotFrameRolling = !_isHotFrameRolling;
            HotFrameToggle.Content = _isHotFrameRolling ? "Pause Hot Frame" : "Start Hot Frame";
        }

        private void OnConfigurePulseGenerator(object sender, RoutedEventArgs e)
        {
            ConfigurePulseGenerator();
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
                    MessageBox.Show($"Configuring pulse generator:\n" +
                        $"Trigger Rate: {triggerRate:F0} Hz\n" +
                        $"Pulse Width: {pulseWidth:E2} s\n" +
                        $"LV Peak: {lvPeak:F2} V",
                        "Configuration", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Uncomment when GPIB device is available:
                    _pulseGenerator.Connect("GPIB0::6::INSTR");
                    _pulseGenerator.Configure(triggerRate, pulseWidth, lvPeak);
                    _pulseGenerator.Disconnect();

                    Thread.Sleep(500); // Give it a moment
                    _pulseGenerator.Connect("GPIB0::6::INSTR");
                    var actualSettings = _pulseGenerator.GetCurrentSettings();
                    _pulseGenerator.Disconnect();

                    // Update stored values
                    if (actualSettings.ContainsKey("TriggerRateHz"))
                        _actualTriggerRate = actualSettings["TriggerRateHz"];
                    if (actualSettings.ContainsKey("PulseWidthSec"))
                        _actualPulseWidth = actualSettings["PulseWidthSec"];
                    if (actualSettings.ContainsKey("LVPeakV"))
                        _actualLVPeak = actualSettings["LVPeakV"];
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
                    var settings = _pulseGenerator.GetCurrentSettings();
                    _pulseGenerator.Disconnect();

                    if (settings.ContainsKey("TriggerRateHz"))
                        _actualTriggerRate = settings["TriggerRateHz"];
                    if (settings.ContainsKey("PulseWidthSec"))
                        _actualPulseWidth = settings["PulseWidthSec"];
                    if (settings.ContainsKey("LVPeakV"))
                        _actualLVPeak = settings["LVPeakV"];
                });

                UpdateActualValuesDisplay();
                Console.WriteLine("Machine settings read successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading machine settings: {ex.Message}");
            }
        }

        private void OnUpdateIntervalApply(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(UpdateIntervalBox.Text, out int intervalMs))
            {
                _gpuMonitor.SetUpdateInterval(intervalMs);
                MessageBox.Show($"Update interval set to {intervalMs}ms", "GPU Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Please enter a valid interval in milliseconds", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnStartClicked(object sender, RoutedEventArgs e)
        {
            _frameCount = 0;
            FPSCounter.Text = "0";

            _cameraService.Start();
            _gpuMonitor.Start(1000);
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            _cameraService.Stop();
            _gpuMonitor.Stop();
        }

        // Method to retrieve the captured bitmap (thread-safe)
        public Bitmap GetCapturedBitmap()
        {
            lock (_bitmapLock)
            {
                // Return a clone so caller doesn't accidentally dispose it
                return _capturedBitmap?.Clone() as Bitmap;
            }
        }

        // Method to save captured bitmap to file
        public void SaveCapturedBitmapToFile(string filePath)
        {
            lock (_bitmapLock)
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
        }

        // Clean up when window closes
        protected override void OnClosed(EventArgs e)
        {
            lock (_bitmapLock)
            {
                _capturedBitmap?.Dispose();
                _currentDisplayBitmap?.Dispose();
            }

            _cameraService.Stop();
            Thread.Sleep(100);
            _gpuMonitor.Stop();
            Thread.Sleep(100);
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
    }
}