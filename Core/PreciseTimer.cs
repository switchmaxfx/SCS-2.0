using System.Diagnostics;

namespace SCS.Core;

/// <summary>
/// Sub-millisecond waits. Python's time.sleep() rides the default 15.6 ms Windows timer tick, so a
/// "20 ms" pause could be anywhere from 15 to 31+ ms, and much worse when the CPU is pegged by the game.
/// Here we sleep on a high-resolution waitable timer for the bulk of the wait and spin the last
/// fraction of a millisecond, so timings land within ~0.1 ms of the target. One instance per thread.
/// </summary>
public sealed class PreciseTimer : IDisposable
{
    private readonly IntPtr _timer;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;
    private const double SpinThresholdMs = 1.2;

    public PreciseTimer()
    {
        _timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null,
            Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
        if (_timer == IntPtr.Zero)
        {
            // Pre-1803 Windows: fall back to a normal timer (timeBeginPeriod(1) keeps it ~1 ms accurate).
            _timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null, 0, Native.TIMER_ALL_ACCESS);
        }
    }

    public static long Now => Stopwatch.GetTimestamp();

    public static long FromMs(double ms) => (long)(ms * TicksPerMs);

    public static double ToMs(long ticks) => ticks / TicksPerMs;

    public void WaitMs(double ms)
    {
        if (ms <= 0) return;
        WaitUntil(Now + FromMs(ms));
    }

    public void WaitUntil(long targetTimestamp)
    {
        while (true)
        {
            long remaining = targetTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;

            double remainingMs = remaining / TicksPerMs;
            if (remainingMs > SpinThresholdMs && _timer != IntPtr.Zero)
            {
                // Negative due time = relative, in 100 ns units. Leave ~1 ms for the spin phase.
                long due = -(long)((remainingMs - 1.0) * 10_000);
                if (due < 0 && Native.SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    Native.WaitForSingleObject(_timer, 0xFFFFFFFF);
                    continue;
                }
            }

            // Final stretch: spin. Yield occasionally so a hyperthread sibling isn't starved.
            var sw = new SpinWait();
            while (Stopwatch.GetTimestamp() < targetTimestamp)
            {
                if (sw.Count > 50) Thread.Yield(); else sw.SpinOnce(-1);
            }
            return;
        }
    }

    public void Dispose()
    {
        if (_timer != IntPtr.Zero) Native.CloseHandle(_timer);
    }
}
