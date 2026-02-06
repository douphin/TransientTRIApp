using System;
using System.Drawing;
using System.Runtime.InteropServices;
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

        public ChartValues<double> GPUUtilizationData;
        public ChartValues<double> GPUTemperatureData;

        public MainWindow()
        {
            InitializeComponent();

            _cameraService = new WebCamService();
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
                PointGeometrySize = 0
            };

            var temperatureSeries = new LineSeries
            {
                Title = "GPU Temperature (°C)",
                Values = GPUTemperatureData,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.OrangeRed,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0
            };

            GPUChart.Series = new SeriesCollection { utilizationSeries, temperatureSeries };

            _cameraService.Start();
            _gpuMonitor.Start(1000); // Start with 1 second interval
        }

        private void OnFrameReady(object sender, CameraFrameEventArgs e)
        {
            UI(() =>
            {
                CameraImage.Source = ConvertBitmap(e.Bmp);
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

        private void OnConfigurePulseGenerator(object sender, RoutedEventArgs e)
        {
            try
            {
                double triggerRate = TriggerRateSlider.Value;
                double pulseWidth = PulseWidthSlider.Value;
                double lvPeak = LVPeakSlider.Value;

                MessageBox.Show($"Configuring pulse generator:\n" +
                    $"Trigger Rate: {triggerRate:F0} Hz\n" +
                    $"Pulse Width: {pulseWidth:E2} s\n" +
                    $"LV Peak: {lvPeak:F2} V",
                    "Configuration", MessageBoxButton.OK, MessageBoxImage.Information);

                // Uncomment when GPIB device is available:
                // _pulseGenerator.Connect("GPIB0::6::INSTR");
                // _pulseGenerator.Configure(triggerRate, pulseWidth, lvPeak);
                // _pulseGenerator.Disconnect();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            _cameraService.Start();
            _gpuMonitor.Start(1000);
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            _cameraService.Stop();
            _gpuMonitor.Stop();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cameraService.Stop();
            _gpuMonitor.Stop();
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