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
    // This file will be used to hold all of the logic for recording data both metrics and frame data
    public partial class MainWindow : Window
    {
        // Global Variables
        private string _recordingSourceFolder;
        private string _recordingSessionFolder;
        private StreamWriter _csvFile;
        private volatile bool _isRecording = false;
        private readonly object _csvModelLock = new object();
        private System.Collections.Generic.Dictionary<string, CombinedMetrics> _csvModel = new System.Collections.Generic.Dictionary<string, CombinedMetrics>();
        private int _savedFrameCount = 0;

        /// <summary>
        /// Handles Folder browser dialog for UI and then saving resulting chosen path
        /// </summary>
        private void SelectRecordingFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select folder to save recording data";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _recordingSourceFolder = dialog.SelectedPath;
                RecordingFolderDisplay.Text = System.IO.Path.GetFileName(_recordingSourceFolder);
                RecordingToggleButton.IsEnabled = true;
                Console.WriteLine($"Recording folder selected: {_recordingSourceFolder}");
            }
        }

        /// <summary>
        /// Calles appropriate function to toggle recording
        /// </summary>
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

        /// <summary>
        /// Creates CSV with headers that will hold metrics and creates necessary folders to save run data and adjusts UI
        /// </summary>
        private void StartRecording()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _recordingSessionFolder = System.IO.Path.Combine(_recordingSourceFolder, $"Recording_{timestamp}");
                Directory.CreateDirectory(_recordingSessionFolder);
                string metricsPath = System.IO.Path.Combine(_recordingSessionFolder, $"metrics_{timestamp}.csv");

                _csvFile = new StreamWriter(metricsPath, true);
                _csvFile.WriteLine("TCTimestamp, GPUTimestamp,TCTemperature(C),GPUTemperature(C),GPUUtilization(%)");
                _csvFile.Flush();

                // Create images subfolder
                string imagesFolder = System.IO.Path.Combine(_recordingSessionFolder, "images");
                Directory.CreateDirectory(imagesFolder);

                string darkFrameFilename = Path.Combine(imagesFolder, $"0_DarkFrame_{_darkFrameTime:HH-mm-ss-fff}.png");
                string coldFrameFilename = Path.Combine(imagesFolder, $"0_ColdFrame_{_coldFrameTime:HH-mm-ss-fff}.png");

                _darkFrameBitmap?.Save(darkFrameFilename, System.Drawing.Imaging.ImageFormat.Png);
                _coldFrameBitmap?.Save(coldFrameFilename, System.Drawing.Imaging.ImageFormat.Png);

                SaveCurrentFrame($"{_savedFrameCount}_Initial");

                _isRecording = true;
                SaveFrameButton.IsEnabled = true;
                RecordingToggleButton.Content = "Stop Recording";
                RecordingToggleButton.Background = System.Windows.Media.Brushes.IndianRed;
                RecordingFolderDisplay.Text += " [RECORDING]";

                Console.WriteLine($"Recording started: {metricsPath}");
                //MessageBox.Show($"Recording started!\nMetrics saved to: {metricsPath}", "Recording Started", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting recording: {ex.ToString()}");
            }
        }

        /// <summary>
        /// Writes saved values into CSV and Adjusts UI to reflect accordingly
        /// </summary>
        private async Task StopRecording()
        {
            try
            {
                RecordingToggleButton.Content = "Saving...";
                _isRecording = false;
                SaveFrameButton.IsEnabled = false;

                await Task.Run(() =>
                {
                    SaveCurrentFrame($"{_savedFrameCount}_Final");

                    foreach (CombinedMetrics item in _csvModel.Values)
                    {
                        _csvFile.WriteLine($"{item.TCTimestamp:yyyy-MM-dd HH:mm:ss.fff},{item.GPUTimestamp:yyyy-MM-dd HH:mm:ss.fff},{item.TCTemperature:F2},{item.GPUTemperature:F2},{item.GPUUtilization:F1}");
                        _csvFile.Flush();
                    }
                });

                _csvFile?.Close();
                _csvFile?.Dispose();
                _csvModel = new System.Collections.Generic.Dictionary<string, CombinedMetrics>();

                RecordingToggleButton.Content = "Start Recording";
                RecordingToggleButton.Background = System.Windows.Media.Brushes.LimeGreen;
                RecordingFolderDisplay.Text = RecordingFolderDisplay.Text.Replace(" [RECORDING]", "");

                Console.WriteLine("Recording stopped");
                //MessageBox.Show("Recording stopped!", "Recording Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping recording: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
