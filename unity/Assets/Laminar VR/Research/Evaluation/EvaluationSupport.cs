using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation
{
    internal sealed class RecordingEvaluationSceneAdapter
        : ISceneEnvironmentAdapter
    {
        private readonly SceneEnvironmentProfile profile;
        private readonly List<EnvironmentState> appliedStates =
            new List<EnvironmentState>();

        public RecordingEvaluationSceneAdapter(SceneEnvironmentProfile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(
                nameof(profile));
        }

        public string SceneId => profile.SceneId;

        public bool AllAppliedStatesSafe { get; private set; } = true;

        public SceneBindingValidation ValidateBindings()
        {
            return SceneBindingValidation.Succeeded();
        }

        public void ApplyState(EnvironmentState state)
        {
            appliedStates.Add(state);
            AllAppliedStatesSafe &= state.IsNormalized
                && profile.Limits.Contains(state);
        }

        public EnvironmentState[] CopyAppliedStates()
        {
            return appliedStates.ToArray();
        }
    }

    internal sealed class InMemoryTelemetryEventSink : ITelemetryEventSink
    {
        private readonly List<TelemetryEvent> events =
            new List<TelemetryEvent>();

        public Task AppendAsync(
            TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(telemetryEvent ?? throw new ArgumentNullException(
                nameof(telemetryEvent)));
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public TelemetryEvent[] CopyEvents()
        {
            return events.ToArray();
        }
    }

    internal readonly struct PendingSyntheticDelivery
    {
        public PendingSyntheticDelivery(
            int emissionSequence,
            SyntheticPhysiologyEmission emission)
        {
            EmissionSequence = emissionSequence;
            Emission = emission;
        }

        public int EmissionSequence { get; }

        public SyntheticPhysiologyEmission Emission { get; }
    }
}
