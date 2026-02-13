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

        public void Start(int updateIntervalMs = 1000)
        {
            if (_running) 
                return;

            _running = true;
            _cts = new CancellationTokenSource();
            _updateIntervalMs = updateIntervalMs;

            System.Threading.Tasks.Task.Run(() => { MeasurementLoop(_cts.Token); });

        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        public void MeasurementLoop(CancellationToken cts)
        {
            NationalInstruments.DAQmx.Task task = null;
            System.Timers.Timer timer = new System.Timers.Timer();

            try
            {
                task = new NationalInstruments.DAQmx.Task("ThermocoupleTask");

                task.AIChannels.CreateThermocoupleChannel(
                    "Dev2/ai0",
                    "Thermocouple",
                    0,
                    100,
                    AIThermocoupleType.K,
                    AITemperatureUnits.DegreesC
                );

                task.Start();
                Console.WriteLine("Thermocouple Task Started");

                
                timer.Interval = _updateIntervalMs;
                timer.Elapsed += (sender, e) => OnTakeReadingElapsed(sender, e, task);
                Thread.Sleep(1000 - DateTime.Now.Millisecond);
                timer.Start();
                while (!cts.IsCancellationRequested) ;

                //while (!cts.IsCancellationRequested)
                //{
                //    try
                //    {
                //        AnalogSingleChannelReader reader = new AnalogSingleChannelReader(task.Stream);

                //        double temp = reader.ReadSingleSample();

                //        TempReady?.Invoke(this, new ThermocoupleReadings(temp, DateTime.Now));

                //        Thread.Sleep(_updateIntervalMs);
                //    }
                //    catch (Exception ex)
                //    {
                //        Console.WriteLine($"Error Taking TC Temperature Reading {ex.Message}");
                //        Thread.Sleep(10 * 1000);
                //    }
                //}

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Connecting to TC{ex.Message}");
            }
            finally
            {
                timer.Stop();
                if (task != null)
                {
                    try
                    {
                        task.Stop();
                        Console.WriteLine("Thermocouple Task Stopped");
                    }
                    catch
                    {
                        Console.WriteLine("Error Stopping TC Task");
                    }
                }

                if (task != null)
                {
                    try
                    {
                        task.Dispose();
                        Console.WriteLine("Thermocouple Task Disposed");
                    }
                    catch
                    {
                        Console.WriteLine("Error Disposing TC Task");
                    }
                }
            }
        }

        public void OnTakeReadingElapsed(object sender , ElapsedEventArgs e, NationalInstruments.DAQmx.Task task)
        {
            try
            {
                AnalogSingleChannelReader reader = new AnalogSingleChannelReader(task.Stream);

                double temp = reader.ReadSingleSample();

                TempReady?.Invoke(this, new ThermocoupleReadings(temp, DateTime.Now));

                //Thread.Sleep(_updateIntervalMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Taking TC Temperature Reading {ex.Message}");
                //Thread.Sleep(10 * 1000);
            }
        }
    }
}
