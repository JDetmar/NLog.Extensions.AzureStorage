using System;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;

namespace NLog.Extensions.AzureStorage
{
    /// <summary>
    /// Shared sync-over-async bridge for closing an Azure SDK client connection from a target's
    /// synchronous <c>CloseTarget</c> during NLog shutdown. The close outcome is always observed -
    /// a fault is surfaced via <see cref="InternalLogger"/> and never becomes an unobserved task
    /// exception - the wait is bounded so a hung close cannot stall shutdown, and it never throws.
    /// </summary>
    internal static class AsyncTargetCloseHelper
    {
        // Upper bound on how long shutdown blocks waiting for the connection to close. Generous
        // enough for a healthy-but-slow teardown (an over-tight cap truncates those), yet bounded so
        // a hung SDK close cannot stall process shutdown. On timeout the close is not abandoned: its
        // outcome is still observed asynchronously, so a later fault never goes unnoticed.
        private static readonly TimeSpan ConnectionCloseTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Runs <paramref name="closeAsync"/> on the thread pool and waits up to the bounded timeout,
        /// observing and logging any fault. Returns normally even on failure so a target's shutdown
        /// can never be broken by a connection close.
        /// </summary>
        public static void CloseConnection(Func<Task> closeAsync, string targetType, string clientName, string targetName)
        {
            try
            {
                var closeTask = Task.Run(closeAsync);
                if (!closeTask.Wait(ConnectionCloseTimeout))
                {
                    // Still closing after the bound: observe the eventual outcome so a later fault is
                    // never an unobserved task exception, and surface it via InternalLogger.
                    closeTask.ContinueWith(
                        t => InternalLogger.Warn(t.Exception?.GetBaseException(), "{0}(Name={1}): Failed to close {2} connection during shutdown", targetType, targetName, clientName),
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    InternalLogger.Warn("{0}(Name={1}): {2} connection still closing after {3}; continuing shutdown", targetType, targetName, clientName, ConnectionCloseTimeout);
                }
            }
            catch (Exception ex)
            {
                // CloseAsync faulted within the wait window (Wait rethrows it) or could not be started -
                // observed here and logged, never rethrown so shutdown cannot fail.
                InternalLogger.Warn(ex.GetBaseException(), "{0}(Name={1}): Failed to close {2} connection during shutdown", targetType, targetName, clientName);
            }
        }
    }
}
