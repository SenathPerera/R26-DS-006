using System;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class TaskReconnectDelayTests
    {
        [Test]
        public async Task DelayAsync_AcceptsZeroDelay()
        {
            var delay = new TaskReconnectDelay();

            var delayTask = delay.DelayAsync(0d, CancellationToken.None);
            await delayTask;

            Assert.That(delayTask.IsCompletedSuccessfully, Is.True);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void DelayAsync_RejectsInvalidDuration(double delaySeconds)
        {
            var delay = new TaskReconnectDelay();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => delay.DelayAsync(delaySeconds, CancellationToken.None));
        }
    }
}
