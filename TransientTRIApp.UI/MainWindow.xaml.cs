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
using TransientTRIApp.Common.Util;
using TransientTRIApp.Core;
using TransientTRIApp.Core.Camera;
using TransientTRIApp.Core.GPU;
using TransientTRIApp.Core.Hardware;
using Emgu.CV.Aruco;
using System.Diagnostics;
using System.Linq;

/*
 * TODO:
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
        private readonly GPUMonitoringService _gpuMonitor;
        private readonly IPulseGeneratorService _pulseGenerator;
        private readonly ICameraService _cameraService;
        private readonly IThermocoupleService _thermocoupleService;

        private DispatcherTimer _debounceTimer;
        private const int DebounceDelayMs = 2500; // how long to wait after slider stops moving

        private readonly System.Timers.Timer _frameTimer = new System.Timers.Timer();
        private readonly System.Timers.Timer _pixelValueCountTimer = new System.Timers.Timer();

        public ChartValues<double> GPUUtilizationData0;
        public ChartValues<double> GPUTemperatureData0;
        public ChartValues<double> GPUUtilizationData1;
        public ChartValues<double> GPUTemperatureData1;
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
            ImageProcessing.ImageShiftEvent += UpdateImageShift;

            InitializeChartData();
            BindUISliders();
            UpdateGPUDropDownList();
            BindUIDropDowns();

            ImageProcessing.PreProcess();

            this.DataContext = this;
        }

        private void InitializeChartData()
        {
            GPUUtilizationData0 = new ChartValues<double>();
            GPUTemperatureData0 = new ChartValues<double>();
            GPUUtilizationData1 = new ChartValues<double>();
            GPUTemperatureData1 = new ChartValues<double>();
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
                    TriggerRateSlider.Value = Helper.Clamp(value, TriggerRateSlider.Minimum, TriggerRateSlider.Maximum);
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
                    PulseWidthSlider.Value = Helper.Clamp(value, PulseWidthSlider.Minimum, PulseWidthSlider.Maximum);
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
                    LVPeakSlider.Value = Helper.Clamp(value, LVPeakSlider.Minimum, LVPeakSlider.Maximum);
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
                    ExposureSlider.Value = Helper.Clamp(value, ExposureSlider.Minimum, ExposureSlider.Maximum);
                }
            };
        }

        private void BindUIDropDowns()
        {
            _gpuLoadOptionParams = new System.Collections.Generic.Dictionary<string, GPULoadUIVisibility>
            {
                { "Static Load"          ,  new GPULoadUIVisibility(GPULoadUIVisibility.Static, true, false, true, false, false, false)},
                { "Square Wave"          ,  new GPULoadUIVisibility(GPULoadUIVisibility.Square, true, true, true, true, false, true)},
                { "Sawtooth"             ,  new GPULoadUIVisibility(GPULoadUIVisibility.Sawtooth, true, true, true, true, true, true)},
                { "Exponential Sawtooth" ,  new GPULoadUIVisibility(GPULoadUIVisibility.ExpSawtooth, true, true, true, true, true, true)},
                { "Sine Wave"            ,  new GPULoadUIVisibility(GPULoadUIVisibility.Sine, true, true, true, true, true, true)},
            };

            GPULoadingOptions.ItemsSource = _gpuLoadOptionParams.Keys;
            
            GPULoadingOptions.SelectionChanged += (s, e) =>
            {
                GPULoadUIVisibility tempParams = _gpuLoadOptionParams[GPULoadingOptions.SelectedValue.ToString()];

                GPULoadTimeInput.Visibility = AssignVisibility(tempParams.IsShowingGPULoadTimeInput);
                GPULoadTimeInputLabel.Visibility = AssignVisibility(tempParams.IsShowingGPULoadTimeInput);

                GPUMinLoadPercentage.Visibility = AssignVisibility(tempParams.IsShowingGPUMinLoadPercentage);
                GPUMinLoadPercentageLabel.Visibility = AssignVisibility(tempParams.IsShowingGPUMinLoadPercentage);

                GPUMaxLoadPercentage.Visibility = AssignVisibility(tempParams.IsShowingGPUMaxLoadPercentage);
                GPUMaxLoadPercentageLabel.Visibility = AssignVisibility(tempParams.IsShowingGPUMaxLoadPercentage);

                GPUWavePeriod.Visibility = AssignVisibility(tempParams.IsShowingGPUWavePeriod);
                GPUWavePeriodLabel.Visibility = AssignVisibility(tempParams.IsShowingGPUWavePeriod);

                GPUWaveStepLength.Visibility = AssignVisibility(tempParams.IsShowingGPUWaveStepLength);
                GPUWaveStepLengthLabel.Visibility = AssignVisibility(tempParams.IsShowingGPUWaveStepLength);

                GPURestTime.Visibility = AssignVisibility(tempParams.IsShowingGPURestTime);
                GPURestTimeLabel.Visibility = AssignVisibility(tempParams.IsShowingGPURestTime);
            };
            GPULoadingOptions.SelectedIndex = 0;
        }    

        private Visibility AssignVisibility(bool shouldShow)
        {
            return shouldShow ? Visibility.Visible : Visibility.Hidden;
        }

        private void UI(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // Setup chart series
            var gpuUtilizationSeries0 = new LineSeries
            {
                Title = "0GPU Utilization (%)",
                Values = GPUUtilizationData0,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.CornflowerBlue,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,           
            };

            var gpuTemperatureSeries0 = new LineSeries
            {
                Title = "0GPU Temperature (°C)",
                Values = GPUTemperatureData0,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.Orange,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,               
            };

            var gpuUtilizationSeries1 = new LineSeries
            {
                Title = "1GPU Utilization (%)",
                Values = GPUUtilizationData1,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.DarkViolet,
                Fill = System.Windows.Media.Brushes.Transparent,
                PointGeometrySize = 0,
                LineSmoothness = 0,
            };

            var gpuTemperatureSeries1 = new LineSeries
            {
                Title = "1GPU Temperature (°C)",
                Values = GPUTemperatureData1,
                StrokeThickness = 2,
                Stroke = System.Windows.Media.Brushes.Crimson,
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

            GPUChart.Series = new SeriesCollection { gpuUtilizationSeries0, gpuTemperatureSeries0, gpuUtilizationSeries1, gpuTemperatureSeries1, tcTemperatureSeries };
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

        private void OnStartGPULoadClicked(object sender, RoutedEventArgs e)
        {
            HandleGPULoad();
        }

        private void OnRefreshListClicked(object sender, RoutedEventArgs e)
        {
            UpdateGPUDropDownList();
        }
    }
}