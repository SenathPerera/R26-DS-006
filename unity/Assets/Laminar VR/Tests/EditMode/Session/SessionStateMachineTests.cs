using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Session;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Session
{
    public sealed class SessionStateMachineTests
    {
        [Test]
        public void SetupFlow_ReachesReadyAndStartBeginsAcclimatization()
        {
            var machine = new SessionStateMachine();

            Assert.That(machine.Initialize(0d), Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.AwaitingConfig));
            Assert.That(machine.AcceptConfiguration(CreateTiming(), 1d), Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.LoadingScene));
            Assert.That(machine.MarkSceneLoaded(2d), Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Ready));

            var result = machine.ProcessCommand(
                "start",
                SessionCommandType.Start,
                3d,
                false);

            Assert.That(result.Applied, Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Acclimatization));
        }

        [Test]
        public void AdvanceTo_TransitionsAcrossAllTimedPhases()
        {
            var machine = CreateStartedMachine();

            machine.AdvanceTo(10d);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Adaptive));

            machine.AdvanceTo(30d);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Stabilization));

            machine.AdvanceTo(35d);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Completed));
            Assert.That(machine.ActiveSessionElapsedSeconds, Is.EqualTo(35d));
        }

        [Test]
        public void AdvanceTo_LargeTimeStepPreservesEveryPhaseTransition()
        {
            var machine = CreateStartedMachine();
            var transitions = new List<SessionPhaseTransition>();
            machine.PhaseChanged += transitions.Add;

            machine.AdvanceTo(35d);

            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Completed));
            Assert.That(transitions.Count, Is.EqualTo(3));
            Assert.That(
                transitions[0].Reason,
                Is.EqualTo(SessionTransitionReason.AcclimatizationElapsed));
            Assert.That(transitions[0].MonotonicTimeSeconds, Is.EqualTo(10d));
            Assert.That(
                transitions[1].Reason,
                Is.EqualTo(SessionTransitionReason.AdaptiveDurationElapsed));
            Assert.That(transitions[1].MonotonicTimeSeconds, Is.EqualTo(30d));
            Assert.That(
                transitions[2].Reason,
                Is.EqualTo(SessionTransitionReason.StabilizationDurationElapsed));
            Assert.That(transitions[2].MonotonicTimeSeconds, Is.EqualTo(35d));
        }

        [Test]
        public void AdaptivePhase_EmitsScheduledOpportunitiesButNotAtItsEndBoundary()
        {
            var machine = CreateStartedMachine();
            var opportunities = new List<SessionDecisionOpportunity>();
            machine.DecisionOpportunityReached += opportunities.Add;

            machine.AdvanceTo(30d);

            Assert.That(opportunities.Count, Is.EqualTo(4));
            Assert.That(opportunities[0].SequenceNumber, Is.EqualTo(1));
            Assert.That(opportunities[0].MonotonicTimeSeconds, Is.EqualTo(14d));
            Assert.That(opportunities[0].AdaptiveElapsedSeconds, Is.EqualTo(4d));
            Assert.That(opportunities[3].MonotonicTimeSeconds, Is.EqualTo(26d));
            Assert.That(opportunities[3].AdaptiveElapsedSeconds, Is.EqualTo(16d));
            Assert.That(machine.DecisionOpportunityCount, Is.EqualTo(4));
        }

        [Test]
        public void Pause_FreezesActiveTimingAndDecisionSchedule()
        {
            var machine = CreateStartedMachine();
            machine.AdvanceTo(15d);
            var decisionCountBeforePause = machine.DecisionOpportunityCount;

            var pause = machine.ProcessCommand(
                "pause",
                SessionCommandType.Pause,
                15d,
                false);
            machine.AdvanceTo(100d);

            Assert.That(pause.Applied, Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Paused));
            Assert.That(machine.ActiveSessionElapsedSeconds, Is.EqualTo(15d));
            Assert.That(machine.AdaptiveElapsedSeconds, Is.EqualTo(5d));
            Assert.That(
                machine.DecisionOpportunityCount,
                Is.EqualTo(decisionCountBeforePause));
        }

        [Test]
        public void Resume_RequiresFreshPhysiologyAndContinuesExistingSchedule()
        {
            var machine = CreatePausedMachine();
            var opportunities = new List<SessionDecisionOpportunity>();
            machine.DecisionOpportunityReached += opportunities.Add;

            var rejected = machine.ProcessCommand(
                "resume-stale",
                SessionCommandType.Resume,
                100d,
                false);
            var accepted = machine.ProcessCommand(
                "resume-fresh",
                SessionCommandType.Resume,
                101d,
                true);
            machine.AdvanceTo(104d);

            Assert.That(
                rejected.ResultCode,
                Is.EqualTo(SessionCommandResultCode.FreshPhysiologyRequired));
            Assert.That(accepted.Applied, Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Adaptive));
            Assert.That(machine.AdaptiveElapsedSeconds, Is.EqualTo(8d));
            Assert.That(opportunities.Count, Is.EqualTo(1));
            Assert.That(opportunities[0].MonotonicTimeSeconds, Is.EqualTo(104d));
        }

        [Test]
        public void RejectedCommandId_IsStillIdempotentWhenReplayed()
        {
            var machine = CreatePausedMachine();

            var rejected = machine.ProcessCommand(
                "resume-1",
                SessionCommandType.Resume,
                20d,
                false);
            var duplicate = machine.ProcessCommand(
                "resume-1",
                SessionCommandType.Resume,
                21d,
                true);

            Assert.That(
                rejected.ResultCode,
                Is.EqualTo(SessionCommandResultCode.FreshPhysiologyRequired));
            Assert.That(
                duplicate.ResultCode,
                Is.EqualTo(SessionCommandResultCode.DuplicateIgnored));
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Paused));
        }

        [Test]
        public void DuplicateAcceptedCommand_DoesNotApplyTwice()
        {
            var machine = CreateStartedMachine();
            machine.AdvanceTo(12d);

            var first = machine.ProcessCommand(
                "pause-once",
                SessionCommandType.Pause,
                12d,
                false);
            var duplicate = machine.ProcessCommand(
                "pause-once",
                SessionCommandType.Pause,
                13d,
                false);

            Assert.That(first.Applied, Is.True);
            Assert.That(
                duplicate.ResultCode,
                Is.EqualTo(SessionCommandResultCode.DuplicateIgnored));
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Paused));
        }

        [TestCase(SessionCommandType.Stop, SessionTransitionReason.StopCommand)]
        [TestCase(
            SessionCommandType.EmergencyStop,
            SessionTransitionReason.EmergencyStopCommand)]
        public void TerminationCommand_AbortsAndRecordsStructuredReason(
            SessionCommandType commandType,
            SessionTransitionReason expectedReason)
        {
            var machine = CreateStartedMachine();
            SessionPhaseTransition observed = default;
            machine.PhaseChanged += transition => observed = transition;

            var result = machine.ProcessCommand(
                "terminate",
                commandType,
                5d,
                false);

            Assert.That(result.Applied, Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Aborted));
            Assert.That(observed.Reason, Is.EqualTo(expectedReason));
        }

        [Test]
        public void EmergencyStop_IsAvailableBeforeAConfigurationExists()
        {
            var machine = new SessionStateMachine();
            machine.Initialize(0d);

            var result = machine.ProcessCommand(
                "local-emergency",
                SessionCommandType.EmergencyStop,
                1d,
                false);

            Assert.That(result.Applied, Is.True);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Aborted));
        }

        [Test]
        public void TerminalSession_RejectsNewCommands()
        {
            var machine = CreateStartedMachine();
            machine.ProcessCommand(
                "stop",
                SessionCommandType.Stop,
                1d,
                false);

            var result = machine.ProcessCommand(
                "start-again",
                SessionCommandType.Start,
                2d,
                false);

            Assert.That(
                result.ResultCode,
                Is.EqualTo(SessionCommandResultCode.SessionAlreadyTerminal));
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Aborted));
        }

        [Test]
        public void InvalidCommandInputs_AreRejectedWithoutChangingPhase()
        {
            var machine = CreateStartedMachine();

            var missingId = machine.ProcessCommand(
                " ",
                SessionCommandType.Pause,
                1d,
                false);
            var unsupported = machine.ProcessCommand(
                "unknown",
                (SessionCommandType)99,
                1d,
                false);
            var wrongPhase = machine.ProcessCommand(
                "resume",
                SessionCommandType.Resume,
                1d,
                true);

            Assert.That(
                missingId.ResultCode,
                Is.EqualTo(SessionCommandResultCode.InvalidCommandId));
            Assert.That(
                unsupported.ResultCode,
                Is.EqualTo(SessionCommandResultCode.UnsupportedCommand));
            Assert.That(
                wrongPhase.ResultCode,
                Is.EqualTo(SessionCommandResultCode.InvalidForCurrentPhase));
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Acclimatization));
        }

        [Test]
        public void FatalError_AbortsOnlyOnce()
        {
            var machine = CreateStartedMachine();

            Assert.That(machine.AbortForFatalError(1d), Is.True);
            Assert.That(machine.AbortForFatalError(2d), Is.False);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.Aborted));
        }

        [Test]
        public void AdvanceTo_RejectsClockMovingBackwards()
        {
            var machine = CreateStartedMachine();
            machine.AdvanceTo(5d);

            Assert.Throws<ArgumentOutOfRangeException>(() => machine.AdvanceTo(4d));
        }

        [Test]
        public void Initialize_IsIdempotent()
        {
            var machine = new SessionStateMachine();

            Assert.That(machine.Initialize(0d), Is.True);
            Assert.That(machine.Initialize(1d), Is.False);
            Assert.That(machine.Phase, Is.EqualTo(VrSessionPhase.AwaitingConfig));
        }

        private static SessionStateMachine CreateStartedMachine()
        {
            var machine = new SessionStateMachine();
            machine.Initialize(0d);
            machine.AcceptConfiguration(CreateTiming(), 0d);
            machine.MarkSceneLoaded(0d);
            machine.ProcessCommand(
                "start",
                SessionCommandType.Start,
                0d,
                false);
            return machine;
        }

        private static SessionStateMachine CreatePausedMachine()
        {
            var machine = CreateStartedMachine();
            machine.AdvanceTo(15d);
            machine.ProcessCommand(
                "pause",
                SessionCommandType.Pause,
                15d,
                false);
            return machine;
        }

        private static SessionTimingConfiguration CreateTiming()
        {
            return new SessionTimingConfiguration(
                "test-timing",
                1,
                10d,
                20d,
                5d,
                4d);
        }
    }
}

