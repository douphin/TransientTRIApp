using System;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp.Internal;

public class MultimediaTimer
{
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeSetEvent(uint uDelay, uint uResolution,
        TimerEventDelegate lpTimeProc, IntPtr dwUser, uint fuEvent);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeKillEvent(uint uTimerId);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    private delegate void TimerEventDelegate(uint uTimerId, uint uMsg, IntPtr dwUser, IntPtr dw1, IntPtr dw2);

    private const uint TIME_PERIODIC = 1;
    private const uint EVENT_TYPE = 1;

    private uint _timerId = 0;
    private TimerEventDelegate _timerDelegate;

    public event Action OnTick;

    public void Start(int intervalMs)
    {
        if (_timerId != 0)
            Stop();

        // Keep delegate alive with class field to prevent GC
        _timerDelegate = TimerCallback;

        _timerId = timeSetEvent((uint)intervalMs, 0, _timerDelegate, IntPtr.Zero, TIME_PERIODIC);

        if (_timerId == 0)
            throw new Exception("Failed to create multimedia timer");

        Console.WriteLine($"Multimedia timer started with {intervalMs}ms interval");
    }

    public void Stop()
    {
        if (_timerId != 0)
        {
            timeKillEvent(_timerId);
            _timerId = 0;
            Console.WriteLine("Multimedia timer stopped");
        }
    }

    private void TimerCallback(uint uTimerId, uint uMsg, IntPtr dwUser, IntPtr dw1, IntPtr dw2)
    {
        OnTick?.Invoke();
    }

    public bool IsRunning => _timerId != 0;

    public static void PreciseWait(double milliseconds)
    {
        // Start high-res timer
        TimeBeginPeriod(1);

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long targetTicks = (long)(milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000);

            // 1. Coarse Sleep: Only if wait is long enough to justify context switch
            if (milliseconds > 2.0)
            {
                // Sleep for slightly less than target to avoid overshooting
                System.Threading.Thread.Sleep((int)(milliseconds - 1.5));
            }

            // 2. Fine Spin: Busy-wait for the remaining micro-durations
            while (sw.ElapsedTicks < targetTicks)
            {
                // Optional: Thread.SpinWait(1) can reduce power slightly vs empty loop
            }
        }
        finally
        {
            // Must reset the system clock resolution
            TimeEndPeriod(1);
        }
    }

}