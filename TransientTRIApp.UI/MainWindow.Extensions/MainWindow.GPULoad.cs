using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Media;
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
    // This file will be used to hold all of the camera logic
    public partial class MainWindow : Window
    {
        private bool _isGPULoadInProgress = false;
        private CancellationTokenSource _gpuCTS = new CancellationTokenSource();
        private GPULoadingService _gpuLoadingService = new GPULoadingService();
        private Dictionary<string, GPULoadUIVisibility> _gpuLoadOptionParams;

        public void UpdateGPUDropDownList()
        {
            GPUOptions.ItemsSource = _gpuLoadingService.GetListOfGPUs();
            
            if (GPUOptions.SelectedItem == null)
            {
                GPUOptions.SelectedIndex = 0;
            }
        }

        public void HandleGPULoad()
        {
            if (_isGPULoadInProgress == false)
            {
                GPULoadingStatus.Text = "";

                Progress<string> _progress = new Progress<string>(UpdateGPULoadStatus);

                //TimeSpan ts = double.TryParse(GPULoadTimeInput.Text, out double loadTime) && loadTime > 0 ? TimeSpan.FromSeconds(loadTime) : TimeSpan.Zero;

                GPULoadUIVisibility tempParams = _gpuLoadOptionParams[GPULoadingOptions.SelectedValue.ToString()];

                GPULoadingParams gpuParams = new GPULoadingParams(
                    GPUOptions.SelectedItem.ToString(),
                    tempParams.Waveform,
                    double.Parse(GPULoadTimeInput.Text),
                    double.Parse(GPUMinLoadPercentage.IsVisible ? GPUMinLoadPercentage.Text : "0"),
                    double.Parse(GPUMaxLoadPercentage.IsVisible ? GPUMaxLoadPercentage.Text : "1"),
                    double.Parse(GPUWavePeriod.IsVisible ? GPUWavePeriod.Text : "0"),
                    double.Parse(GPUWaveStepLength.IsVisible ? GPUWaveStepLength.Text : "1"),
                    double.Parse(GPURestTime.IsVisible ? GPURestTime.Text  : "0")
                    );

                _gpuLoadingService.RunAsync(gpuParams, _gpuCTS.Token, _progress);

                StartGPULoad.Content = "Cancel GPU Load";
                StartGPULoad.Background = System.Windows.Media.Brushes.Red;
                _isGPULoadInProgress = true;
                GPULoadingOptions.IsEnabled = false;
            }
            else
            {
                EndGPULoad();
            }
        }

        private void UpdateGPULoadStatus(string status)
        {
            GPULoadingStatus.Text = status;

            if (status.Contains("completed"))
                EndGPULoad();
        }

        private void EndGPULoad()
        {
            _gpuCTS.Cancel();
            _gpuCTS.Dispose();
            _gpuCTS = new CancellationTokenSource();

            StartGPULoad.Content = "Start GPU Load";
            StartGPULoad.Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#3498db");
            _isGPULoadInProgress = false;
            GPULoadingOptions.IsEnabled = true;
        }
    }
}
