using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class SessionRelayWebSocketTransportTests
    {
        private const string SchemaVersion = "mindsync-relay-test-v1";

        [Test]
        public async Task ConnectAsync_PairsBeforeReportingConnected()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(CreateAcceptedPairingJson());
            var transport = CreateTransport(connection);
            var statuses = new List<SessionTransportStatus>();
            transport.StatusChanged += statuses.Add;

            await transport.ConnectAsync(CancellationToken.None);

            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Connected));
            Assert.That(transport.ActiveSessionId, Is.EqualTo("session-42"));
            Assert.That(connection.SentTexts, Has.Count.EqualTo(1));
            Assert.That(
                connection.SentTexts[0],
                Does.Contain("\"messageType\":\"pairing_request\""));
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(
                statuses[1].Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectSucceeded));

            await transport.DisconnectAsync(CancellationToken.None);
        }

        [Test]
        public void ConnectAsync_RejectsExpiredPairingCodeWithoutLeakingIt()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(CreateRejectedPairingJson());
            var transport = CreateTransport(connection);
            SessionTransportStatus lastStatus = default;
            transport.StatusChanged += status => lastStatus = status;

            var exception = Assert.CatchAsync<Exception>(
                async () => await transport.ConnectAsync(
                    CancellationToken.None));

            Assert.That(exception.Message, Does.Not.Contain("482913"));
            Assert.That(
                transport.ConnectionState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(
                lastStatus.Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionFailed));
            Assert.That(
                lastStatus.DiagnosticCode,
                Is.EqualTo("relay-pairing-rejected"));
        }

        [Test]
        public async Task ReceiveLoop_RoutesSessionMessagesAndDropsDuplicateCommand()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(CreateAcceptedPairingJson());
            var transport = CreateTransport(connection);
            var configurationCount = 0;
            var commandCount = 0;
            var duplicateCount = 0;
            transport.SessionConfigurationReceived +=
                _ => Interlocked.Increment(ref configurationCount);
            transport.SessionCommandReceived +=
                _ => Interlocked.Increment(ref commandCount);
            transport.InboundMessageRejected += (reason, _) =>
            {
                if (reason
                    == SessionRelayInboundRejectionReason.DuplicateMessage)
                {
                    Interlocked.Increment(ref duplicateCount);
                }
            };
            await transport.ConnectAsync(CancellationToken.None);

            connection.EnqueueText(CreateConfigurationJson());
            connection.EnqueueText(CreateCommandJson(
                "command-1",
                "session-42"));
            connection.EnqueueText(CreateCommandJson(
                "command-1",
                "session-42"));
            await WaitUntilAsync(
                () => Volatile.Read(ref configurationCount) == 1
                    && Volatile.Read(ref commandCount) == 1
                    && Volatile.Read(ref duplicateCount) == 1);

            Assert.That(configurationCount, Is.EqualTo(1));
            Assert.That(commandCount, Is.EqualTo(1));
            Assert.That(duplicateCount, Is.EqualTo(1));

            await transport.DisconnectAsync(CancellationToken.None);
        }

        [Test]
        public async Task ReceiveLoop_RejectsMessageForAnotherSession()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(CreateAcceptedPairingJson());
            var transport = CreateTransport(connection);
            var commandCount = 0;
            var rejection = -1;
            transport.SessionCommandReceived +=
                _ => Interlocked.Increment(ref commandCount);
            transport.InboundMessageRejected +=
                (reason, _) => Interlocked.Exchange(
                    ref rejection,
                    (int)reason);
            await transport.ConnectAsync(CancellationToken.None);

            connection.EnqueueText(CreateCommandJson(
                "command-2",
                "session-other"));
            await WaitUntilAsync(() => Volatile.Read(ref rejection) >= 0);

            Assert.That(commandCount, Is.Zero);
            Assert.That(
                (SessionRelayInboundRejectionReason)rejection,
                Is.EqualTo(SessionRelayInboundRejectionReason.SessionMismatch));

            await transport.DisconnectAsync(CancellationToken.None);
        }

        [Test]
        public async Task PublishQuestStateAsync_SendsOnlyForPairedSession()
        {
            var connection = new FakeConnection();
            connection.EnqueueText(CreateAcceptedPairingJson());
            var transport = CreateTransport(connection);
            await transport.ConnectAsync(CancellationToken.None);

            await transport.PublishQuestStateAsync(
                new SessionRelayQuestState(
                    "state-1",
                    "session-42",
                    VrSessionPhase.Ready,
                    1787282898.4d),
                CancellationToken.None);

            Assert.That(connection.SentTexts, Has.Count.EqualTo(2));
            Assert.That(
                connection.SentTexts[1],
                Does.Contain("\"messageType\":\"quest_state\""));
            Assert.Throws<ArgumentException>(
                () => transport.PublishQuestStateAsync(
                    new SessionRelayQuestState(
                        "state-2",
                        "session-other",
                        VrSessionPhase.Ready,
                        1787282899d),
                    CancellationToken.None));

            await transport.DisconnectAsync(CancellationToken.None);
        }

        private static SessionRelayWebSocketTransport CreateTransport(
            FakeConnection connection)
        {
            var info = new SessionRelayConnectionInfo(
                new Uri("wss://relay.example.test/session"),
                SchemaVersion,
                "482913",
                "quest-install-7",
                "1.2.0",
                65536,
                32);
            return new SessionRelayWebSocketTransport(
                info,
                new SessionRelayJsonCodec(SchemaVersion),
                new SessionRelayInboundMessageParser(SchemaVersion),
                new FakeConnectionFactory(connection),
                new FixedMessageIdSource());
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(1);
            }

            Assert.Fail("The asynchronous relay condition was not reached.");
        }

        private static string CreateAcceptedPairingJson()
        {
            return "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"pair-result-1\","
                + "\"messageType\":\"pairing_result\","
                + "\"payload\":{\"accepted\":true,"
                + "\"sessionId\":\"session-42\"}}";
        }

        private static string CreateRejectedPairingJson()
        {
            return "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"pair-result-2\","
                + "\"messageType\":\"pairing_result\","
                + "\"payload\":{\"accepted\":false,"
                + "\"rejectionCode\":\"pairing-code-expired\"}}";
        }

        private static string CreateConfigurationJson()
        {
            return "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"config-1\","
                + "\"messageType\":\"session_configuration\","
                + "\"payload\":{\"sessionId\":\"session-42\","
                + "\"participantPseudonym\":\"P017\","
                + "\"sceneId\":\"temple-pond\","
                + "\"preferredEnvironment\":{\"illumination\":0.3,"
                + "\"warmth\":0.6,\"atmosphericSoftness\":0.2,"
                + "\"colorRichness\":0.7,\"ambientMotion\":0.4}}}";
        }

        private static string CreateCommandJson(
            string messageId,
            string sessionId)
        {
            return "{\"schemaVersion\":\"mindsync-relay-test-v1\","
                + "\"messageId\":\"" + messageId + "\","
                + "\"messageType\":\"session_command\","
                + "\"payload\":{\"sessionId\":\"" + sessionId + "\","
                + "\"command\":\"start\"}}";
        }

        private sealed class FixedMessageIdSource
            : ISessionRelayMessageIdSource
        {
            private int nextId;

            public string CreateMessageId()
            {
                nextId++;
                return "generated-" + nextId;
            }
        }

        private sealed class FakeConnectionFactory
            : ISessionRelayWebSocketConnectionFactory
        {
            private readonly FakeConnection connection;

            public FakeConnectionFactory(FakeConnection connection)
            {
                this.connection = connection;
            }

            public ISessionRelayWebSocketConnection Create()
            {
                return connection;
            }
        }

        private sealed class FakeConnection
            : ISessionRelayWebSocketConnection
        {
            private readonly object synchronization = new object();
            private readonly Queue<SessionRelayWebSocketMessage> messages =
                new Queue<SessionRelayWebSocketMessage>();
            private TaskCompletionSource<SessionRelayWebSocketMessage> waiter;

            public List<string> SentTexts { get; } = new List<string>();

            public bool IsOpen { get; private set; }

            public Task ConnectAsync(
                Uri endpoint,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IsOpen = true;
                return Task.CompletedTask;
            }

            public Task SendTextAsync(
                string text,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsOpen)
                {
                    throw new InvalidOperationException();
                }

                SentTexts.Add(text);
                return Task.CompletedTask;
            }

            public Task<SessionRelayWebSocketMessage> ReceiveTextAsync(
                int maximumMessageBytes,
                CancellationToken cancellationToken)
            {
                lock (synchronization)
                {
                    if (messages.Count > 0)
                    {
                        return Task.FromResult(messages.Dequeue());
                    }

                    var pending = new TaskCompletionSource<
                        SessionRelayWebSocketMessage>();
                    waiter = pending;
                    cancellationToken.Register(
                        () => pending.TrySetCanceled());
                    return pending.Task;
                }
            }

            public void EnqueueText(string text)
            {
                TaskCompletionSource<SessionRelayWebSocketMessage> pending;
                var message = SessionRelayWebSocketMessage.FromText(text);
                lock (synchronization)
                {
                    pending = waiter;
                    waiter = null;
                    if (pending == null)
                    {
                        messages.Enqueue(message);
                        return;
                    }
                }

                pending.TrySetResult(message);
            }

            public void Abort()
            {
                IsOpen = false;
                lock (synchronization)
                {
                    waiter?.TrySetCanceled();
                    waiter = null;
                }
            }

            public void Dispose()
            {
                IsOpen = false;
            }
        }
    }
}
