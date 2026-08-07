using System.Diagnostics;

namespace CodexAccountSwitcher.Remote;

internal static class SleepRequestScheduler
{
    public static void Schedule(Action action, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                action();
            }
            catch (Exception error)
            {
                Trace.TraceError("Deferred Windows sleep failed: {0}", error);
            }
        });
    }
}
