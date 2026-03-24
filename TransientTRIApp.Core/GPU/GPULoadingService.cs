using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCL.Net;
using TransientTRIApp.Common.Models;
using TransientTRIApp.Common.Util;

public class GPULoadingService
{
    public Dictionary<string, Device> gpus;

    public IEnumerable<string> GetListOfGPUs()
    {
        try
        {
            Platform[] platforms = Cl.GetPlatformIDs(out ErrorCode error);
            CheckError(error);
            Device[] devices = Cl.GetDeviceIDs(platforms[0], OpenCL.Net.DeviceType.All, out error);
            CheckError(error);

            int i = 0;

            gpus = devices.AsEnumerable().ToDictionary(d => $"{i++}: {Cl.GetDeviceInfo(d, DeviceInfo.Name, out error)}");

            return gpus.Keys;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error Get List of GPUs {ex}");
            return null;
        }
    }

    public void RunAsync(GPULoadingParams gpuParams, CancellationToken cancellationToken, IProgress<string> status = null)
    {
        try
        {
            Task.Run(() =>
            {
                //// Initialize OpenCL       
                //Platform[] platforms = Cl.GetPlatformIDs(out ErrorCode error);
                //CheckError(error);

                //status?.Report($"{platforms.Length}");

                //// Automatically select the first platform
                //Platform platform = platforms[0];
                //status?.Report($"Selected Platform: {Cl.GetPlatformInfo(platform, PlatformInfo.Name, out error)}");

                //// Get GPU devices
                //Device[] devices = Cl.GetDeviceIDs(platform, OpenCL.Net.DeviceType.All, out error);
                //CheckError(error);

                //if (devices.Length == 0)
                //{
                //    status?.Report("No GPU devices found on the selected platform. Exiting.");
                //    return;
                //}
                //status?.Report($"{devices.Length}");
                //// Automatically select the first GPU device
                //Device device = devices[0];
                //status?.Report($"Selected GPU Device: {Cl.GetDeviceInfo(device, DeviceInfo.Name, out error)}");

                Device device = gpus[gpuParams.SelectedGPUKeyParam];

                // Create OpenCL context and queue
                Context context = Cl.CreateContext(null, 1, new[] { device }, null, IntPtr.Zero, out ErrorCode error);
                CheckError(error);
                CommandQueue commandQueue = Cl.CreateCommandQueue(context, device, CommandQueueProperties.None, out error);
                CheckError(error);

                // Kernel: Intense GPU computations (matrix multiplication)
                string kernelSource = @"
        __kernel void compute(__global float* A, __global float* B, __global float* C, int N) {
            int row = get_global_id(0);
            int col = get_global_id(1);
            
            if (row < N && col < N) {
                float sum = 0.0f;
                for (int k = 0; k < N; k++) {
                    sum += A[row * N + k] * B[k * N + col];
                }
                C[row * N + col] = sum;
            }
        }";

                // Create program and kernel
                OpenCL.Net.Program program = Cl.CreateProgramWithSource(context, 1, new[] { kernelSource }, null, out error);
                CheckError(error);
                error = Cl.BuildProgram(program, 1, new[] { device }, string.Empty, null, IntPtr.Zero);
                if (error != ErrorCode.Success)
                {
                    InfoBuffer buildLog = Cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.Log, out _);
                    status?.Report("Build Log:\n" + buildLog);
                    throw new Exception($"OpenCL Build Program Failed: {error}");
                }
                Kernel kernel = Cl.CreateKernel(program, "compute", out error);
                CheckError(error);

                // Matrix dimensions
                int N = 256; // Adjust size to tune GPU load
                int matrixSize = N * N;
                float[] A = new float[matrixSize];
                float[] B = new float[matrixSize];
                float[] C = new float[matrixSize];

                // Initialize matrices
                Random rand = new Random();
                for (int i = 0; i < matrixSize; i++)
                {
                    A[i] = (float)rand.NextDouble();
                    B[i] = (float)rand.NextDouble();
                }

                // Create buffers
                IMem bufferA = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(matrixSize * sizeof(float)), A, out error);
                CheckError(error);
                IMem bufferB = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)(matrixSize * sizeof(float)), B, out error);
                CheckError(error);
                IMem bufferC = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)(matrixSize * sizeof(float)), null, out error);
                CheckError(error);

                // Set kernel arguments
                Cl.SetKernelArg(kernel, 0, bufferA);
                Cl.SetKernelArg(kernel, 1, bufferB);
                Cl.SetKernelArg(kernel, 2, bufferC);
                Cl.SetKernelArg(kernel, 3, N);

                // Work size
                IntPtr[] globalWorkSize = { (IntPtr)N, (IntPtr)N };

                // Execution loop: 2s high usage, 5s idle
                status?.Report("Starting GPU computation...");

                Stopwatch timer = Stopwatch.StartNew();
                int totalExecutions = 0;
                bool phasePrimed = false;

                // Run kernel for exactly X seconds, the reason we go through the effort of converting to timespan, even though I guess we don't really need to, is so that if we ever need to convert the time duration to something else like ms, we don't have to do it ourselves, the type handles it for us
                while (timer.Elapsed.TotalSeconds < TimeSpan.FromSeconds(gpuParams.GPULoadTimeInputParam).TotalSeconds && cancellationToken.IsCancellationRequested == false)
                {
                    //error = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 2, null, globalWorkSize, null, 0, null, out _);
                    //CheckError(error);
                    //Cl.Finish(commandQueue);
                    //totalExecutions++;

                    double elapsed = timer.Elapsed.TotalSeconds;

                    // Phase within current period (0.0 → 1.0)
                    double phase = (elapsed % gpuParams.GPUWavePeriodParam) / gpuParams.GPUWavePeriodParam;

                    if (phasePrimed == false && phase > 0.5)
                        phasePrimed = true;

                    // Map waveform output to duty cycle in [minLoad, maxLoad]
                    double duty = gpuParams.GPUMinLoadPercentageParam + gpuParams.Waveform(phase) * (gpuParams.GPUMaxLoadPercentageParam - gpuParams.GPUMinLoadPercentageParam);
                    duty = Helper.Clamp(duty, 0.0, 1.0);

                    int onMs = (int)(gpuParams.GPUWaveStepLengthParam * duty);
                    int offMs = (int)(gpuParams.GPUWaveStepLengthParam - onMs);

                    if (onMs > 0)
                    {
                        error = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 2,
                            null, globalWorkSize, null, 0, null, out _);
                        CheckError(error);
                        Cl.Finish(commandQueue);
                        totalExecutions++;
                    }

                    if (offMs > 0)
                        Thread.Sleep(offMs);

                    if (gpuParams.GPURestTimeParam > 0 && phasePrimed == true && phase < 0.5)
                    {
                        Thread.Sleep(1000 * (int)gpuParams.GPURestTimeParam);
                        phasePrimed = false;
                    }
                }

                timer.Stop();
                status?.Report($"Computation completed: {totalExecutions} executions in {timer.Elapsed.TotalSeconds:F2} seconds.");
            });
        }
        catch (Exception ex)
        {
            status?.Report("Error in GPU Loading Cycle");
            Console.WriteLine(ex.ToString());
        }
    }

    static void CheckError(ErrorCode error)
    {
        if (error != ErrorCode.Success)
        {
            throw new Exception($"OpenCL Error: {error}");
        }
    }
}