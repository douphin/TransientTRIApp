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
    // This file will be used to hold all of the Pulse Generator / LED logic
    public partial class MainWindow : Window
    {
        // Global Variables
        private double _actualTriggerRate = 0;
        private double _actualPulseWidth = 0;
        private double _actualLVPeak = 0;

        /// <summary>
        /// Sends a Command to the Pulse Generator to run a single cycle, which effectively turns off the LED 
        /// </summary>
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

        /// <summary>
        /// Sends a command to the Pulse Generator to set the trigger rate to the selected value, which effectively turns the LED back on
        /// </summary>
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

        /// <summary>
        /// Will take UI settings and compare them to Pulse Generator settings and send commands to update and pulse generator settings that don't match
        /// </summary>
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

        /// <summary>
        /// Updates UI with actual settings read off of the Pulse Generator
        /// </summary>
        private void UpdateActualValuesDisplay()
        {
            TriggerRateActual.Text = $"(Actual: {_actualTriggerRate:F0} Hz)";
            PulseWidthActual.Text = $"(Actual: {_actualPulseWidth:E2} s)";
            LVPeakActual.Text = $"(Actual: {_actualLVPeak:F2} V)";
        }

        /// <summary>
        /// Will Send commmands to intialize Pulse Generator and Read off current settings, updating UI to match current settings
        /// </summary>
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

                // Set camera exposure to correct value to time it up with LED based on pulse generator settings
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
    }
}
