using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NationalInstruments.DAQmx;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Core.Hardware
{
    public class ThermocoupleService : IThermocoupleService, IDisposable
    {
        public event EventHandler<ThermocoupleReadings> TempReady;

        private CancellationTokenSource _cts;
        private int _updateIntervalMs = 1000;
        private bool _running = false;
        private bool _isTimerConfigured = false;
        private NationalInstruments.DAQmx.Task _task = null;
        private System.Timers.Timer _timer = new System.Timers.Timer();

        public void Start(int? updateIntervalMs)
        {
            if (_running) 
                return;

            if (updateIntervalMs != null)
                _updateIntervalMs = (int)updateIntervalMs;

            if (_isTimerConfigured == false)
            {            
                _timer.Elapsed += (sender, e) => OnTakeReadingElapsed(sender, e, _task);
                _isTimerConfigured = true;
            }
            _timer.Interval = _updateIntervalMs;

            _running = true;
            _cts = new CancellationTokenSource();
            

            StartTCTask();
        }

        public void StartTCTask()
        {
            try
            {
                _task = new NationalInstruments.DAQmx.Task("ThermocoupleTask");

                _task.AIChannels.CreateThermocoupleChannel(
                    "Dev2/ai0",
                    "Thermocouple",
                    0,
                    100,
                    AIThermocoupleType.K,
                    AITemperatureUnits.DegreesC
                );

                _task.Start();
                Console.WriteLine("Thermocouple Task Started");

                Thread.Sleep(1000 - DateTime.Now.Millisecond);
                _timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Connecting to TC: {ex.Message}");
            }
        }

        public void OnTakeReadingElapsed(object sender, ElapsedEventArgs e, NationalInstruments.DAQmx.Task task)
        {
            try
            {
                AnalogSingleChannelReader reader = new AnalogSingleChannelReader(task.Stream);

                double temp = reader.ReadSingleSample();

                TempReady?.Invoke(this, new ThermocoupleReadings(temp, DateTime.Now));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Taking TC Temperature Reading {ex.Message}");
            }
        }

        public void SetUpdateInterval(int intervalMs)
        {
            _updateIntervalMs = Math.Max(100, intervalMs); // Minimum 100ms
            _timer.Stop();
            _timer.Interval = _updateIntervalMs;
            Thread.Sleep(1000 - DateTime.Now.Millisecond);
            _timer.Start();
            Console.WriteLine($"TC monitoring update interval set to {_updateIntervalMs}ms");
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _timer.Stop();
            EndTCTask();
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        public void EndTCTask()
        {
            if (_task != null)
            {
                try
                {
                    _task.Stop();
                    Console.WriteLine("Thermocouple Task Stopped");
                }
                catch
                {
                    Console.WriteLine("Error Stopping TC Task");
                }
            }

            if (_task != null)
            {
                try
                {
                    _task.Dispose();
                    Console.WriteLine("Thermocouple Task Disposed");
                }
                catch
                {
                    Console.WriteLine("Error Disposing TC Task");
                }
            }
        }
    }
}
