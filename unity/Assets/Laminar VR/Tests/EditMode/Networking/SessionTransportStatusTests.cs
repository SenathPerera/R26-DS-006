using System;
using LaminarVR.AdaptiveMeditation.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class SessionTransportStatusTests
    {
        [Test]
        public void Constructor_StoresStructuredTransitionAndTrimmedDiagnostic()
        {
            var status = new SessionTransportStatus(
                SessionTransportConnectionState.Connecting,
                SessionTransportConnectionState.Disconnected,
                SessionTransportStatusReason.ConnectionFailed,
                " mock-failure ");

            Assert.That(
                status.PreviousState,
                Is.EqualTo(SessionTransportConnectionState.Connecting));
            Assert.That(
                status.CurrentState,
                Is.EqualTo(SessionTransportConnectionState.Disconnected));
            Assert.That(
                status.Reason,
                Is.EqualTo(SessionTransportStatusReason.ConnectionFailed));
            Assert.That(status.DiagnosticCode, Is.EqualTo("mock-failure"));
        }

        [Test]
        public void Constructor_RejectsStatusWithoutStateChange()
        {
            Assert.Throws<ArgumentException>(
                () => new SessionTransportStatus(
                    SessionTransportConnectionState.Connected,
                    SessionTransportConnectionState.Connected,
                    SessionTransportStatusReason.ConnectSucceeded));
        }
    }
}
