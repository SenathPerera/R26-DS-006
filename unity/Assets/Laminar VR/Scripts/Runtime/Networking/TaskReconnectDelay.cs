using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public sealed class TaskReconnectDelay : IReconnectDelay
    {
        public Task DelayAsync(
            double delaySeconds,
            CancellationToken cancellationToken)
        {
            if (double.IsNaN(delaySeconds)
                || double.IsInfinity(delaySeconds)
                || delaySeconds < 0d
                || delaySeconds > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delaySeconds),
                    delaySeconds,
                    "Reconnect delay must fit a non-negative TimeSpan.");
            }

            return Task.Delay(
                TimeSpan.FromSeconds(delaySeconds),
                cancellationToken);
        }
    }
}
