using System;
using LaminarVR.AdaptiveMeditation.Networking;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public interface ISessionRelayTransport : ISessionTransport<
        SessionRelayConfigurationMessage,
        SessionRelayCommandMessage,
        SessionRelayQuestState>
    {
        event Action<
            SessionRelayInboundRejectionReason,
            string> InboundMessageRejected;

        string ActiveSessionId { get; }
    }

    public interface ISessionRelayTransportFactory
    {
        ISessionRelayTransport Create(
            SessionRelayConnectionInfo connectionInfo);
    }

    public sealed class SessionRelayTransportFactory
        : ISessionRelayTransportFactory
    {
        public ISessionRelayTransport Create(
            SessionRelayConnectionInfo connectionInfo)
        {
            return new SessionRelayWebSocketTransport(connectionInfo);
        }
    }
}
