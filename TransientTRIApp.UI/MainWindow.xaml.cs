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

/*
 * TODO:
 * ROI image alignment
 * Use ReaderWriterLockSlim
 * Implement Image Processing Step Procedure
 * Add GPU loading
 * Add Calibration settings
 *  -communicate with stage
 *  -set temperature and take frames
 *  
 *  
 */

namespace TransientTRIApp.UI
{
    public partial class MainWindow : Window
    {
        private readonly IGPUMonitoringService _gpuMonitor;
        private readonly IPulseGeneratorService _pulseGenerator;
        private readonly ICameraService _cameraService;
        private readonly IThermocoupleService _thermocoupleService;

        private DispatcherTimer _debounceTimer;
        private const int DebounceDelayMs = 2500; // how long to wait after slider stops moving

        private readonly System.Timers.Timer _frameTimer = new System.Timers.Timer();
        private readonly System.Timers.Timer _pixelValueCountTimer = new System.Timers.Timer();

        public ChartValues<double> GPUUtilizationData;
        public ChartValues<double> GPUTemperatureData;
        public ChartValues<double> TCTemperatureData;
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
            BindUISliders();

            ImageProcessing.PreProcess();

            this.DataContext = this;
        }

        private void InitializeChartData()
        {
            GPUUtilizationData = new ChartValues<double>();
            GPUTemperatureData = new ChartValues<double>();
            TCTemperatureData = new ChartValues<double>();
            PixelValueCount = new ChartValues<double>();
        }

        // For areas of the UI where there are both sliders and text boxes, those inputs need to be tied together with logic, which is what happens here
        private void BindUISliders()
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
  
            // Exposure
            ExposureSlider.ValueChanged += (s, e) =>
            {
                ExposureValue.Text = $"{ExposureSlider.Value:F1}";
                ExposureInput.Text = ExposureSlider.Value.ToString("F1");

                // Update camera exposure in real-time
                _cameraService.SetExposure(ExposureSlider.Value * 1000);

                // This will automatically adjust LED settings based on exposure
                if (AdjustLED.IsChecked == true)
                {
                    PulseWidthSlider.Value = ExposureSlider.Value / 1000 / 2;
                    TriggerRateSlider.Value = 1 / PulseWidthSlider.Value;

                    // This will start a timer after the exposure is adjusted, at the conclusion of that timer, it will send commands to change LED settings
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

            _pixelValueCountTimer.Interval = 1000;
            _pixelValueCountTimer.Elapsed += PixelValueCountTimer_Elapsed;
            _pixelValueCountTimer.Start();

            // Setup debounce timer
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelayMs);
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                _ = ConfigurePulseGenerator();
            };
        }

        private void StartAll()
        {
            _frameCount = 0;
            FPSCounter.Text = "0";

            _cameraService.Start();
            _gpuMonitor.Start(1000);
            _frameTimer.Start();
            _pixelValueCountTimer.Start();
        }

        private void StopAll()
        {
            _cameraService.Stop();
            _gpuMonitor.Stop();
            _thermocoupleService.Stop();
            _frameTimer.Stop();
            _pixelValueCountTimer.Stop();
        }

        // Clean up when window closes
        protected override void OnClosed(EventArgs e)
        {
            _frameTimer.Stop();
            _pixelValueCountTimer.Stop();
            _cameraService.Stop();
            Thread.Sleep(100);
            _gpuMonitor.Stop();
            Thread.Sleep(100);

            _bitmapMutex.WaitOne();
            {
                _coldFrameBitmap?.Dispose();
                _currentCameraBitmap?.Dispose();
            }
            _bitmapMutex.ReleaseMutex();
            _bitmapMutex.Dispose();

            if (_isRecording)
                _ = StopRecording();

            base.OnClosed(e);
        }

        // Button Handlers
        // If you bake logic directly into the button handler, it can be hard to call that function elsewhere so we like to separate them a bit

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

        private void OnSetHotFrameClicked(object sender, RoutedEventArgs e)
        {
            SetHotFrame();
        }

        private void OnComputeCTRClicked(object sender, RoutedEventArgs e)
        {
            ComputerCTR();
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

        private void CameraImage_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            CameraImage.Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void CameraImage_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            CameraImage.Cursor = System.Windows.Input.Cursors.Arrow;
            _isDraggingROI = false;
        }

        private void CameraImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var XY = e.GetPosition(CameraImage);

            _startX = (int)XY.X;
            _startY = (int)XY.Y;
            _isDraggingROI = true;
            _draggingRect = new Rectangle((int)(_startX * _elementImageScaleRateX),(int)(_startY * _elementImageScaleRateY), 0, 0);
        }

        private void CameraImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingROI == false)
                return;

            _isDraggingROI = false;
            _drawnRect = _draggingRect;
        }

        private void CameraImage_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingROI = false;
        }

        private void CameraImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if ( _isDraggingROI )
            {
                var XY = e.GetPosition(CameraImage);
                _draggingRect.Width = (int)(_elementImageScaleRateX * XY.X - _draggingRect.X);
                _draggingRect.Height = (int)(_elementImageScaleRateY * XY.Y - _draggingRect.Y);
            }
        }

        private void OnSaveFrameClicked(object sender, RoutedEventArgs e)
        {
            SaveCurrentFrame($"{_savedFrameCount}_Manual");
        }
    }
}