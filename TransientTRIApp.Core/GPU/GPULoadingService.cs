using Silk.NET.OpenCL;
using System;
using System.Threading;
using System.Threading.Tasks;

public class GPULoadingService : IDisposable
{
    private readonly CL _cl;
    private IntPtr _context;
    private IntPtr _commandQueue;
    private IntPtr _program;
    private IntPtr _kernel;
    private IntPtr _outputBuffer;
    private bool _initialized;

    private const int WorkSize = 1024 * 256;
    private const string KernelSource = @"
        __kernel void BusyWork(__global float* output, ulong iterations) {
            int id = get_global_id(0);
            float val = (float)id;
            for (ulong i = 0; i < iterations; i++) {
                val = sin(val) * cos(val) + sqrt(fabs(val) + 1.0f);
            }
            output[id] = val;
        }";

    public GPULoadingService()
    {
        _cl = CL.GetApi();
    }

    public unsafe void Initialize()
    {
        if (_initialized) return;

        // 1. Get platform
        uint numPlatforms = 0;
        _cl.GetPlatformIDs(0, null, &numPlatforms);
        if (numPlatforms == 0) throw new Exception("No OpenCL platforms found.");

        var platforms = new IntPtr[numPlatforms];
        fixed (IntPtr* ptr = platforms)
            _cl.GetPlatformIDs(numPlatforms, ptr, null);

        IntPtr platform = platforms[0];

        // 2. Get GPU device
        uint numDevices = 0;
        int err = (int)_cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &numDevices);

        if (err != 0 || numDevices == 0)
        {
            _cl.GetDeviceIDs(platform, DeviceType.All, 0, null, &numDevices);
            if (numDevices == 0) throw new Exception("No OpenCL devices found.");
        }

        var devices = new IntPtr[numDevices];
        fixed (IntPtr* ptr = devices)
        {
            if (err != 0)
                _cl.GetDeviceIDs(platform, DeviceType.All, numDevices, ptr, null);
            else
                _cl.GetDeviceIDs(platform, DeviceType.Gpu, numDevices, ptr, null);
        }

        IntPtr device = devices[0];

        // 3. Create context
        int contextErr;
        _context = _cl.CreateContext(null, 1, &device, null, null, &contextErr);
        if (contextErr != 0) throw new Exception($"Failed to create context: {contextErr}");

        // 4. Create command queue
        int queueErr;
        _commandQueue = _cl.CreateCommandQueue(_context, device, CommandQueueProperties.None, &queueErr);
        if (queueErr != 0) throw new Exception($"Failed to create command queue: {queueErr}");

        // 5. Compile kernel
        CompileKernel(device);

        // 6. Create output buffer
        int bufErr;
        _outputBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
            (UIntPtr)(WorkSize * sizeof(float)), null, &bufErr);
        if (bufErr != 0) throw new Exception($"Failed to create buffer: {bufErr}");

        _initialized = true;
    }

    private unsafe void CompileKernel(IntPtr device)
    {
        int buildErr;
        var sourceBytes = System.Text.Encoding.ASCII.GetBytes(KernelSource);

        fixed (byte* sourcePtr = sourceBytes)
        {
            UIntPtr length = (UIntPtr)sourceBytes.Length;
            _program = _cl.CreateProgramWithSource(_context, 1, &sourcePtr, &length, &buildErr);
        }
        if (buildErr != 0) throw new Exception($"Failed to create program: {buildErr}");

        int compileErr = (int)_cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
        if (compileErr != 0)
        {
            UIntPtr logSize;
            _cl.GetProgramBuildInfo(_program, device, ProgramBuildInfo.BuildLog, UIntPtr.Zero, null, &logSize);
            var log = new byte[(int)logSize];
            fixed (byte* logPtr = log)
                _cl.GetProgramBuildInfo(_program, device, ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            throw new Exception("Kernel compile error:\n" + System.Text.Encoding.ASCII.GetString(log));
        }


        int kernelErr;
        _kernel = _cl.CreateKernel(_program, "BusyWork", &kernelErr);
        if (kernelErr != 0) throw new Exception($"Failed to create kernel: {kernelErr}");
    }

    public Task RunAsync(TimeSpan? duration, CancellationToken cancellationToken, IProgress<string> status = null)
    {
        if (!_initialized) throw new InvalidOperationException("Call Initialize() first.");

        return Task.Run(async () =>
        {
            status?.Report("GPU load started...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ulong iterations = 500000UL;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (duration.HasValue && duration.Value != TimeSpan.Zero
                    && stopwatch.Elapsed >= duration.Value)
                    break;

                DispatchKernel(iterations);
                await Task.Yield();
            }

            _cl.Flush(_commandQueue);
            _cl.Finish(_commandQueue);
            status?.Report("GPU load stopped.");

        }, cancellationToken);
    }

    private unsafe void DispatchKernel(ulong iterations)
    {
        IntPtr bufferArg = _outputBuffer;
        _cl.SetKernelArg(_kernel, 0, (UIntPtr)IntPtr.Size, &bufferArg);
        _cl.SetKernelArg(_kernel, 1, (UIntPtr)sizeof(ulong), &iterations);

        UIntPtr globalWorkSize = (UIntPtr)WorkSize;
        _cl.EnqueueNdrangeKernel(_commandQueue, _kernel, 1, null,
            &globalWorkSize, null, 0, null, null);

        _cl.Flush(_commandQueue);
    }

    public void Dispose()
    {
        if (_outputBuffer != IntPtr.Zero) _cl.ReleaseMemObject(_outputBuffer);
        if (_kernel != IntPtr.Zero) _cl.ReleaseKernel(_kernel);
        if (_program != IntPtr.Zero) _cl.ReleaseProgram(_program);
        if (_commandQueue != IntPtr.Zero) _cl.ReleaseCommandQueue(_commandQueue);
        if (_context != IntPtr.Zero) _cl.ReleaseContext(_context);
        _cl.Dispose();
    }
}