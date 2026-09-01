using System;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public enum SessionRelayInboundMessageKind
    {
        SessionConfiguration,
        SessionCommand
    }

    public sealed class SessionRelayInboundMessage
    {
        private SessionRelayInboundMessage(
            SessionRelayInboundMessageKind kind,
            SessionRelayConfigurationMessage configuration,
            SessionRelayCommandMessage command)
        {
            Kind = kind;
            Configuration = configuration;
            Command = command;
        }

        public SessionRelayInboundMessageKind Kind { get; }

        public SessionRelayConfigurationMessage Configuration { get; }

        public SessionRelayCommandMessage Command { get; }

        public static SessionRelayInboundMessage ForConfiguration(
            SessionRelayConfigurationMessage configuration)
        {
            return new SessionRelayInboundMessage(
                SessionRelayInboundMessageKind.SessionConfiguration,
                configuration
                    ?? throw new ArgumentNullException(nameof(configuration)),
                null);
        }

        public static SessionRelayInboundMessage ForCommand(
            SessionRelayCommandMessage command)
        {
            return new SessionRelayInboundMessage(
                SessionRelayInboundMessageKind.SessionCommand,
                null,
                command ?? throw new ArgumentNullException(nameof(command)));
        }
    }
}
