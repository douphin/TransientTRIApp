using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Core.GPU
{
    public class GPUMonitoringService : IGPUMonitoringService, IDisposable
    {
        private CancellationTokenSource _cts;
        private int _updateIntervalMs = 1000;
        private bool _running = false;
        private bool _isTimerConfigured = false;
        private readonly string _nvidiaSmiBPath = @"C:\Users\UC-TEC-TRI\Documents\nvidia-smi.exe";
        System.Timers.Timer _timer = new System.Timers.Timer();

        public event EventHandler<GPUMetrics> MetricsUpdated;

        public void Start(int? updateIntervalMs)
        {
            if (_running)
                return;

            if (updateIntervalMs != null)
            {
                _updateIntervalMs = (int)updateIntervalMs;
            }

            if (_isTimerConfigured == false)
            {
                _timer.Elapsed += (sender, e) => OnTakeReadingElapsed(sender, e);
                _isTimerConfigured = true;
            }
            _timer.Interval = _updateIntervalMs;

            _cts = new CancellationTokenSource();
            _running = true;

            Thread.Sleep(1000 - DateTime.Now.Millisecond);
            _timer.Start();
        }

        public void Stop()
        {
            _running = false;
            _timer.Stop();
            _cts?.Cancel();
        }

        public void SetUpdateInterval(int intervalMs)
        {
            _updateIntervalMs = Math.Max(100, intervalMs); // Minimum 100ms
            _timer.Stop();
            _timer.Interval = _updateIntervalMs;
            Thread.Sleep(1000 - DateTime.Now.Millisecond);
            _timer.Start();
            Console.WriteLine($"GPU monitoring update interval set to {_updateIntervalMs}ms");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private void OnTakeReadingElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var metrics = QueryGPUMetrics();
                if (metrics != null)
                {
                    MetricsUpdated?.Invoke(this, metrics);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GPU monitoring loop: {ex.Message}");
            }
        }

        private GPUMetrics QueryGPUMetrics()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _nvidiaSmiBPath,
                    Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Console.WriteLine("Failed to start nvidia-smi process");
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000); // 5 second timeout

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        Console.WriteLine("nvidia-smi returned empty output");
                        return null;
                    }

                    // Parse output: "25, 45" (utilization, temperature)
                    string[] parts = output.Trim().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2)
                    {
                        if (double.TryParse(parts[0].Trim(), out double utilization) &&
                            double.TryParse(parts[1].Trim(), out double temperature))
                        {
                            return new GPUMetrics
                            {
                                GPUUtilization = utilization,
                                GPUTemperature = temperature,
                                Timestamp = DateTime.Now
                            };
                        }
                    }

                    Console.WriteLine($"Failed to parse nvidia-smi output: {output}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying GPU metrics: {ex.Message}");
                return null;
            }
        }
    }
}
