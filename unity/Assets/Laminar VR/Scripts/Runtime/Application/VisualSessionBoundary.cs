using System;
using System.Collections.Concurrent;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Networking;
using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Visual Session Boundary")]
    [DisallowMultipleComponent]
    public sealed class VisualSessionBoundary : MonoBehaviour
    {
        private const int DefaultMaximumMessagesPerFrame = 64;

        [Header("Composition Root")]
        [SerializeField]
        private ProductionSessionCoordinator productionCoordinator = null;

        [Header("Main-Thread Dispatch")]
        [SerializeField, Min(1)]
        private int maximumMessagesPerFrame = DefaultMaximumMessagesPerFrame;

        private readonly ConcurrentQueue<InboundMessage> inboundMessages =
            new ConcurrentQueue<InboundMessage>();

        private bool hasForwardedSessionContext;
        private string forwardedSessionId;
        private string forwardedParticipantPseudonym;
        private EnvironmentState forwardedPreferredEnvironment;

        public int PendingMessageCount => inboundMessages.Count;

        public int RejectedMessageCount { get; private set; }

        public string LastDispatchError { get; private set; } = string.Empty;

        private void Update()
        {
            ProcessPendingMessages();
        }

        private void OnValidate()
        {
            maximumMessagesPerFrame = Math.Max(
                1,
                maximumMessagesPerFrame);
        }

        public void Configure(ProductionSessionCoordinator coordinator)
        {
            productionCoordinator = coordinator != null
                ? coordinator
                : throw new ArgumentNullException(nameof(coordinator));
        }

        public void ReceiveSessionContext(
            string sessionId,
            string participantPseudonym,
            EnvironmentState safePreferredEnvironment)
        {
            inboundMessages.Enqueue(
                InboundMessage.ForSessionContext(
                    sessionId,
                    participantPseudonym,
                    safePreferredEnvironment));
        }

        public void ReceivePhysiology(PhysiologyWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            inboundMessages.Enqueue(InboundMessage.ForPhysiology(window));
        }

        public void ReceiveCommand(
            string commandId,
            SessionCommandType commandType)
        {
            inboundMessages.Enqueue(
                InboundMessage.ForCommand(commandId, commandType));
        }

        public void ReceiveConnectionState(
            SessionTransportConnectionState connectionState)
        {
            inboundMessages.Enqueue(
                InboundMessage.ForConnectionState(connectionState));
        }

        public int ProcessPendingMessages()
        {
            if (productionCoordinator == null)
            {
                LastDispatchError =
                    "Assign a ProductionSessionCoordinator to the visual session boundary.";
                return 0;
            }

            var processedCount = 0;
            LastDispatchError = string.Empty;
            while (processedCount < maximumMessagesPerFrame
                && inboundMessages.TryDequeue(out var message))
            {
                try
                {
                    Dispatch(message);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is InvalidOperationException)
                {
                    RejectedMessageCount++;
                    LastDispatchError = exception.Message;
                    Debug.LogError(
                        "[VisualSessionBoundary] inbound_message_rejected"
                        + " kind=" + message.Kind
                        + " reason=" + exception.Message,
                        this);
                }

                processedCount++;
            }

            return processedCount;
        }

        private void Dispatch(InboundMessage message)
        {
            switch (message.Kind)
            {
                case InboundMessageKind.SessionContext:
                    ForwardSessionContext(message);
                    break;
                case InboundMessageKind.Physiology:
                    productionCoordinator.SubmitPhysiology(message.Physiology);
                    break;
                case InboundMessageKind.Command:
                    productionCoordinator.SubmitCommand(
                        message.CommandId,
                        message.CommandType);
                    break;
                case InboundMessageKind.ConnectionState:
                    productionCoordinator.SetNetworkConnected(
                        message.ConnectionState
                            == SessionTransportConnectionState.Connected);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(message),
                        message.Kind,
                        "Unsupported inbound message kind.");
            }
        }

        private void ForwardSessionContext(InboundMessage message)
        {
            if (hasForwardedSessionContext)
            {
                if (string.Equals(
                        forwardedSessionId,
                        message.SessionId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        forwardedParticipantPseudonym,
                        message.ParticipantPseudonym,
                        StringComparison.Ordinal)
                    && forwardedPreferredEnvironment
                        == message.SafePreferredEnvironment)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The active visual session context cannot change after it "
                    + "has been forwarded to the production coordinator.");
            }

            productionCoordinator.ConfigureSessionContext(
                message.SessionId,
                message.ParticipantPseudonym,
                message.SafePreferredEnvironment);
            hasForwardedSessionContext = true;
            forwardedSessionId = message.SessionId?.Trim();
            forwardedParticipantPseudonym =
                message.ParticipantPseudonym?.Trim();
            forwardedPreferredEnvironment = message.SafePreferredEnvironment;
        }

        private enum InboundMessageKind
        {
            SessionContext,
            Physiology,
            Command,
            ConnectionState
        }

        private readonly struct InboundMessage
        {
            private InboundMessage(
                InboundMessageKind kind,
                string sessionId,
                string participantPseudonym,
                EnvironmentState safePreferredEnvironment,
                PhysiologyWindow physiology,
                string commandId,
                SessionCommandType commandType,
                SessionTransportConnectionState connectionState)
            {
                Kind = kind;
                SessionId = sessionId;
                ParticipantPseudonym = participantPseudonym;
                SafePreferredEnvironment = safePreferredEnvironment;
                Physiology = physiology;
                CommandId = commandId;
                CommandType = commandType;
                ConnectionState = connectionState;
            }

            public InboundMessageKind Kind { get; }

            public string SessionId { get; }

            public string ParticipantPseudonym { get; }

            public EnvironmentState SafePreferredEnvironment { get; }

            public PhysiologyWindow Physiology { get; }

            public string CommandId { get; }

            public SessionCommandType CommandType { get; }

            public SessionTransportConnectionState ConnectionState { get; }

            public static InboundMessage ForSessionContext(
                string sessionId,
                string participantPseudonym,
                EnvironmentState safePreferredEnvironment)
            {
                return new InboundMessage(
                    InboundMessageKind.SessionContext,
                    sessionId,
                    participantPseudonym,
                    safePreferredEnvironment,
                    null,
                    null,
                    default,
                    default);
            }

            public static InboundMessage ForPhysiology(
                PhysiologyWindow physiology)
            {
                return new InboundMessage(
                    InboundMessageKind.Physiology,
                    null,
                    null,
                    default,
                    physiology,
                    null,
                    default,
                    default);
            }

            public static InboundMessage ForCommand(
                string commandId,
                SessionCommandType commandType)
            {
                return new InboundMessage(
                    InboundMessageKind.Command,
                    null,
                    null,
                    default,
                    null,
                    commandId,
                    commandType,
                    default);
            }

            public static InboundMessage ForConnectionState(
                SessionTransportConnectionState connectionState)
            {
                return new InboundMessage(
                    InboundMessageKind.ConnectionState,
                    null,
                    null,
                    default,
                    null,
                    null,
                    default,
                    connectionState);
            }
        }
    }
}
