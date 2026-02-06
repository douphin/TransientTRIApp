using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TransientTRIApp.Common.Interfaces;
using TransientTRIApp.Common.Models;

namespace TransientTRIApp.Core.GPU
{
    public class GPUMonitoringService : IGPUMonitoringService, IDisposable
    {
        private CancellationTokenSource _cts;
        private int _updateIntervalMs = 1000;
        private bool _running = false;
        private readonly string _nvidiaSmiBPath = @"C:\Users\UC-TEC-TRI\Documents\nvidia-smi.exe";

        public event EventHandler<GPUMetrics> MetricsUpdated;

        public void Start(int updateIntervalMs = 1000)
        {
            if (_running)
                return;

            _updateIntervalMs = updateIntervalMs;
            _cts = new CancellationTokenSource();
            _running = true;

            Task.Run(() => MonitoringLoop(_cts.Token));
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
        }

        public void SetUpdateInterval(int intervalMs)
        {
            _updateIntervalMs = Math.Max(100, intervalMs); // Minimum 100ms
            Console.WriteLine($"GPU monitoring update interval set to {_updateIntervalMs}ms");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private void MonitoringLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var metrics = QueryGPUMetrics();
                    if (metrics != null)
                    {
                        MetricsUpdated?.Invoke(this, metrics);
                    }
                    Thread.Sleep(_updateIntervalMs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in GPU monitoring loop: {ex.Message}");
                    Thread.Sleep(_updateIntervalMs);
                }
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
