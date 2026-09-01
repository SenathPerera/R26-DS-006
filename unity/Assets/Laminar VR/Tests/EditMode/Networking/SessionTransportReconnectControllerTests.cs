using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class SessionTransportReconnectControllerTests
    {
        [Test]
        public async Task ReconnectAsync_RetriesWithBoundedScheduleUntilConnected()
        {
            var transport = CreateTransport();
            transport.FailNextConnect("failure-1");
            transport.FailNextConnect("failure-2");
            var delay = new RecordingDelay();
            var controller = CreateController(transport, delay, 4);

            var result = await controller.ReconnectAsync(CancellationToken.None);

            Assert.That(result.Connected, Is.True);
            Assert.That(result.AttemptsMade, Is.EqualTo(3));
            Assert.That(result.LastFailure, Is.Null);
            Assert.That(delay.Delays, Is.EqualTo(new[] { 1d, 2d, 4d }));
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
        }

        [Test]
        public async Task ReconnectAsync_ReturnsLastFailureWhenAttemptsAreExhausted()
        {
            var transport = CreateTransport();
            transport.FailNextConnect("failure-1");
            transport.FailNextConnect("failure-2");
            var delay = new RecordingDelay();
            var controller = CreateController(transport, delay, 2);

            var result = await controller.ReconnectAsync(CancellationToken.None);

            Assert.That(result.Connected, Is.False);
            Assert.That(result.Exhausted, Is.True);
            Assert.That(result.AttemptsMade, Is.EqualTo(2));
            Assert.That(result.LastFailure, Is.TypeOf<System.InvalidOperationException>());
            Assert.That(delay.Delays, Is.EqualTo(new[] { 1d, 2d }));
        }

        [Test]
        public async Task ReconnectAsync_ConnectedTransportRequiresNoDelayOrAttempt()
        {
            var transport = CreateTransport();
            await transport.ConnectAsync(CancellationToken.None);
            var delay = new RecordingDelay();
            var controller = CreateController(transport, delay, 3);

            var result = await controller.ReconnectAsync(CancellationToken.None);

            Assert.That(result.Connected, Is.True);
            Assert.That(result.AttemptsMade, Is.Zero);
            Assert.That(delay.Delays, Is.Empty);
        }

        [Test]
        public void ReconnectAsync_PropagatesCancellationWithoutConnecting()
        {
            var transport = CreateTransport();
            var delay = new RecordingDelay();
            var controller = CreateController(transport, delay, 3);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.CatchAsync<System.OperationCanceledException>(
                    async () => await controller.ReconnectAsync(cancellation.Token));
            }

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
        }

        private static SessionTransportReconnectController<
            string,
            string,
            string> CreateController(
                LocalMockSessionTransport<string, string, string> transport,
                IReconnectDelay delay,
                int maximumAttempts)
        {
            return new SessionTransportReconnectController<
                string,
                string,
                string>(
                    transport,
                    new ReconnectBackoffConfiguration(
                        "reconnect-test",
                        1,
                        maximumAttempts,
                        1d,
                        5d,
                        2d),
                    delay);
        }

        private static LocalMockSessionTransport<string, string, string>
            CreateTransport()
        {
            return new LocalMockSessionTransport<string, string, string>();
        }

        private sealed class RecordingDelay : IReconnectDelay
        {
            public List<double> Delays { get; } = new List<double>();

            public Task DelayAsync(
                double delaySeconds,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delays.Add(delaySeconds);
                return Task.CompletedTask;
            }
        }
    }
}
