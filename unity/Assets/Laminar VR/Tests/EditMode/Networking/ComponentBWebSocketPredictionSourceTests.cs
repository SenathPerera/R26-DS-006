using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class ComponentBWebSocketPredictionSourceTests
    {
        private const string ValidPointJson =
            "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
            + "\"sdnn\":40,\"stress\":{\"mode\":\"point\","
            + "\"level\":0,\"label\":\"relaxed\",\"confidence\":0.7,"
            + "\"probabilities\":{\"relaxed\":0.7,\"mild\":0.2,"
            + "\"moderate\":0.08,\"high\":0.02},"
            + "\"continuous_score\":0.42},\"signalQuality\":0.95,"
            + "\"windowStart\":940,\"windowEnd\":1000}";

        [Test]
        public void Constructor_RejectsInvalidConnectionConfiguration()
        {
            Assert.Throws<ArgumentException>(
                () => new ComponentBWebSocketPredictionSource(
                    new Uri("https://component-b.example/stream"),
                    20d,
                    65536));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ComponentBWebSocketPredictionSource(
                    new Uri("wss://component-b.example/stream"),
                    0d,
                    65536));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ComponentBWebSocketPredictionSource(
                    new Uri("wss://component-b.example/stream"),
                    20d,
                    0));
        }

        [Test]
        public async Task Connect_ReceivesPredictionSendsKeepaliveAndDisconnects()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(ValidPointJson);
            var delay = new ControlledKeepaliveDelay();
            var source = CreateSource(connection, delay);
            var statuses = new List<SessionTransportStatus>();
            var received = new TaskCompletionSource<PhysiologyWindow>();
            source.StatusChanged += statuses.Add;
            source.PhysiologyReceived += value => received.TrySetResult(value);

            await source.ConnectAsync(CancellationToken.None);
            var window = await WithTimeout(received.Task);
            await WaitUntilAsync(() => delay.PendingCount > 0);
            delay.ReleaseOne();
            await WaitUntilAsync(() => connection.SentTexts.Count == 1);
            await source.DisconnectAsync(CancellationToken.None);

            Assert.That(window.Stress.Mode, Is.EqualTo(StressDecisionMode.Point));
            Assert.That(connection.SentTexts, Is.EqualTo(new[] { "keepalive" }));
            Assert.That(connection.AbortCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(connection.DisposeCount, Is.EqualTo(1));
            Assert.That(
                source.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(statuses, Has.Count.EqualTo(4));
            Assert.That(
                statuses[0].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectRequested));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectSucceeded));
            Assert.That(
                statuses[2].Reason,
                Is.EqualTo(SessionTransportStatusReason.DisconnectRequested));
            Assert.That(
                statuses[3].Reason,
                Is.EqualTo(SessionTransportStatusReason.DisconnectSucceeded));
        }

        [Test]
        public async Task InvalidPayload_IsRejectedWithoutClosingConnection()
        {
            var connection = new FakeConnection();
            connection.EnqueueText("{}");
            var source = CreateSource(
                connection,
                new ControlledKeepaliveDelay());
            var rejected = new TaskCompletionSource<
                ComponentBStressPayloadParseReasonCode>();
            source.PayloadRejected += value => rejected.TrySetResult(value);

            await source.ConnectAsync(CancellationToken.None);
            var reason = await WithTimeout(rejected.Task);

            Assert.That(
                reason,
                Is.EqualTo(
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing));
            Assert.That(
                source.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));

            await source.DisconnectAsync(CancellationToken.None);
        }

        [Test]
        public async Task RemoteClose_ReportsConnectionLoss()
        {
            var connection = new FakeConnection();
            var source = CreateSource(
                connection,
                new ControlledKeepaliveDelay());
            var statuses = new List<SessionTransportStatus>();
            source.StatusChanged += statuses.Add;

            await source.ConnectAsync(CancellationToken.None);
            connection.EnqueueClose();
            await WaitUntilAsync(
                () => source.ConnectionState
                    == SessionTransportConnectionState.Disconnected);

            Assert.That(statuses, Has.Count.EqualTo(3));
            Assert.That(
                statuses[2].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionLost));
            Assert.That(
                statuses[2].DiagnosticCode,
                Is.EqualTo("component-b-remote-closed"));
            Assert.That(connection.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ConnectFailure_ReturnsToDisconnectedWithStructuredStatus()
        {
            var connection = new FakeConnection
            {
                ConnectException = new InvalidOperationException("secret detail")
            };
            var source = CreateSource(
                connection,
                new ControlledKeepaliveDelay());
            var statuses = new List<SessionTransportStatus>();
            source.StatusChanged += statuses.Add;

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await source.ConnectAsync(CancellationToken.None));

            Assert.That(
                source.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionFailed));
            Assert.That(
                statuses[1].DiagnosticCode,
                Is.EqualTo("component-b-connect-failed"));
            Assert.That(connection.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ConnectCancellation_ReturnsToDisconnected()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var connection = new FakeConnection
                {
                    BeforeConnect = cancellation.Cancel
                };
                var source = CreateSource(
                    connection,
                    new ControlledKeepaliveDelay());
                var statuses = new List<SessionTransportStatus>();
                source.StatusChanged += statuses.Add;

                Assert.CatchAsync<OperationCanceledException>(
                    async () => await source.ConnectAsync(cancellation.Token));

                Assert.That(
                    source.ConnectionState,
                    Is.EqualTo(SessionTransportConnectionState.Disconnected));
                Assert.That(statuses, Has.Count.EqualTo(2));
                Assert.That(
                    statuses[1].Reason,
                    Is.EqualTo(SessionTransportStatusReason.OperationCancelled));
            }
        }

        [Test]
        public async Task GeneralReconnectController_RetriesComponentBSource()
        {
            var first = new FakeConnection
            {
                ConnectException = new InvalidOperationException("failure-1")
            };
            var second = new FakeConnection
            {
                ConnectException = new InvalidOperationException("failure-2")
            };
            var connected = new FakeConnection();
            var source = new ComponentBWebSocketPredictionSource(
                new Uri("wss://component-b.example/stream"),
                20d,
                65536,
                new ComponentBStressPayloadParser(),
                new QueueConnectionFactory(first, second, connected),
                new ControlledKeepaliveDelay());
            var reconnectDelay = new RecordingReconnectDelay();
            var controller = new ConnectionReconnectController(
                source,
                new ReconnectBackoffConfiguration(
                    "component-b-reconnect-test",
                    1,
                    3,
                    1d,
                    4d,
                    2d),
                reconnectDelay);

            var result = await controller.ReconnectAsync(
                CancellationToken.None);

            Assert.That(result.Connected, Is.True);
            Assert.That(result.AttemptsMade, Is.EqualTo(3));
            Assert.That(reconnectDelay.Delays, Is.EqualTo(new[] { 1d, 2d, 4d }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(
                source.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));

            await source.DisconnectAsync(CancellationToken.None);
        }

        private static ComponentBWebSocketPredictionSource CreateSource(
            FakeConnection connection,
            IComponentBKeepaliveDelay keepaliveDelay)
        {
            return new ComponentBWebSocketPredictionSource(
                new Uri("wss://component-b.example/stream"),
                20d,
                65536,
                new ComponentBStressPayloadParser(),
                new SingleConnectionFactory(connection),
                keepaliveDelay);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(2000));
            Assert.That(completed, Is.SameAs(task), "Timed out awaiting event.");
            return await task;
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(2d);
            while (!predicate())
            {
                if (DateTime.UtcNow >= timeoutAt)
                {
                    Assert.Fail("Timed out awaiting asynchronous state.");
                }

                await Task.Delay(10);
            }
        }

        private sealed class SingleConnectionFactory
            : IComponentBWebSocketConnectionFactory
        {
            private readonly IComponentBWebSocketConnection connection;

            public SingleConnectionFactory(
                IComponentBWebSocketConnection connection)
            {
                this.connection = connection;
            }

            public IComponentBWebSocketConnection Create()
            {
                return connection;
            }
        }

        private sealed class QueueConnectionFactory
            : IComponentBWebSocketConnectionFactory
        {
            private readonly Queue<IComponentBWebSocketConnection> connections;

            public QueueConnectionFactory(
                params IComponentBWebSocketConnection[] connections)
            {
                this.connections =
                    new Queue<IComponentBWebSocketConnection>(connections);
            }

            public IComponentBWebSocketConnection Create()
            {
                return connections.Dequeue();
            }
        }

        private sealed class RecordingReconnectDelay : IReconnectDelay
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

        private sealed class ControlledKeepaliveDelay
            : IComponentBKeepaliveDelay
        {
            private readonly object synchronization = new object();
            private readonly Queue<TaskCompletionSource<bool>> pending =
                new Queue<TaskCompletionSource<bool>>();

            public int PendingCount
            {
                get
                {
                    lock (synchronization)
                    {
                        return pending.Count;
                    }
                }
            }

            public Task DelayAsync(
                double delaySeconds,
                CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<bool>();
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken));
                lock (synchronization)
                {
                    pending.Enqueue(completion);
                }

                return completion.Task;
            }

            public void ReleaseOne()
            {
                TaskCompletionSource<bool> completion;
                lock (synchronization)
                {
                    completion = pending.Dequeue();
                }

                completion.TrySetResult(true);
            }
        }

        private sealed class FakeConnection : IComponentBWebSocketConnection
        {
            private readonly object synchronization = new object();
            private readonly Queue<ComponentBWebSocketMessage> messages =
                new Queue<ComponentBWebSocketMessage>();
            private readonly SemaphoreSlim messageAvailable =
                new SemaphoreSlim(0);

            public Exception ConnectException { get; set; }

            public Action BeforeConnect { get; set; }

            public bool IsOpen { get; private set; }

            public List<string> SentTexts { get; } = new List<string>();

            public int AbortCount { get; private set; }

            public int DisposeCount { get; private set; }

            public Task ConnectAsync(
                Uri endpoint,
                CancellationToken cancellationToken)
            {
                BeforeConnect?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (ConnectException != null)
                {
                    throw ConnectException;
                }

                IsOpen = true;
                return Task.CompletedTask;
            }

            public Task SendTextAsync(
                string text,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SentTexts.Add(text);
                return Task.CompletedTask;
            }

            public async Task<ComponentBWebSocketMessage> ReceiveTextAsync(
                int maximumMessageBytes,
                CancellationToken cancellationToken)
            {
                await messageAvailable.WaitAsync(cancellationToken);
                lock (synchronization)
                {
                    return messages.Dequeue();
                }
            }

            public void Abort()
            {
                AbortCount++;
                IsOpen = false;
            }

            public void Dispose()
            {
                DisposeCount++;
                IsOpen = false;
            }

            public void EnqueueText(string text)
            {
                Enqueue(ComponentBWebSocketMessage.FromText(text));
            }

            public void EnqueueClose()
            {
                Enqueue(ComponentBWebSocketMessage.Closed);
            }

            private void Enqueue(ComponentBWebSocketMessage message)
            {
                lock (synchronization)
                {
                    messages.Enqueue(message);
                }

                messageAvailable.Release();
            }
        }
    }
}
