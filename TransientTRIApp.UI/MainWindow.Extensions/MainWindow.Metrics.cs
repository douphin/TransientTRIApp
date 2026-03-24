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
    // This file will be used to hold all of the logic for the metrics like temperature and stuff
    public partial class MainWindow : Window
    {
        // Global Variables
        private const int MaxDataPoints = 60; // Keep last 60 readings
        private readonly int _metricSampleRate = 1000;

        /// <summary>
        /// Update graph in UI with most recent GPU values and if recording, save them to CombinedMetrics object
        /// </summary>
        private void OnGPUMetricsUpdated(object sender, (GPUMetrics, GPUMetrics) metrics)
        {
            UI(() =>
            {
                GPUUtilizationData0.Add(metrics.Item1.GPUUtilization);
                if (GPUUtilizationData0.Count > MaxDataPoints)
                    GPUUtilizationData0.RemoveAt(0);

                GPUTemperatureData0.Add(metrics.Item1.GPUTemperature);
                if (GPUTemperatureData0.Count > MaxDataPoints)
                    GPUTemperatureData0.RemoveAt(0);

                if (metrics.Item2 != null)
                {
                    GPUUtilizationData1.Add(metrics.Item2.GPUUtilization);
                    if (GPUUtilizationData1.Count > MaxDataPoints)
                        GPUUtilizationData1.RemoveAt(0);

                    GPUTemperatureData1.Add(metrics.Item2.GPUTemperature);
                    if (GPUTemperatureData1.Count > MaxDataPoints)
                        GPUTemperatureData1.RemoveAt(0);
                }
            });

            if (_isRecording)
            {
                lock (_csvModelLock)
                {
                    if (_csvModel.TryGetValue(metrics.Item1.Timestamp.ToString(), out var value))
                        value.Update(metrics);
                    else
                        _csvModel.Add(metrics.Item1.Timestamp.ToString(), new CombinedMetrics(metrics));
                }
            }
        }

        /// <summary>
        /// Update graph in UI with most recent TC values and if recording, save them to CombinedMetrics object
        /// </summary>
        public void OnTCReadingsUpdated(object sender, ThermocoupleReadings readings)
        {
            UI(() =>
            {
                TCTemperatureData.Add(readings.TCTemperature);
                if (TCTemperatureData.Count > MaxDataPoints)
                    TCTemperatureData.RemoveAt(0);
            });

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
        }

        /// <summary>
        /// So this should theoretically change how often readings are taken, but I think it might be a little broken right now...
        /// </summary>
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
    }
}
