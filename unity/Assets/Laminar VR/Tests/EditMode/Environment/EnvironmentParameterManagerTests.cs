using System;
using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Environment
{
    public sealed class EnvironmentParameterManagerTests
    {
        [Test]
        public void Constructor_AppliesInitialNormalizedState()
        {
            var adapter = new RecordingAdapter();
            var initial = CreateState(0.5f);

            var manager = new EnvironmentParameterManager(initial, adapter);

            Assert.That(manager.CurrentState, Is.EqualTo(initial));
            Assert.That(adapter.AppliedStates, Has.Count.EqualTo(1));
            Assert.That(adapter.AppliedStates[0], Is.EqualTo(initial));
        }

        [Test]
        public void AdvanceTransition_InterpolatesAndCompletesDeterministically()
        {
            var adapter = new RecordingAdapter();
            var manager = new EnvironmentParameterManager(
                CreateState(0.5f),
                adapter);
            var target = new EnvironmentState(
                0.5f,
                0.7f,
                0.5f,
                0.5f,
                0.5f);
            manager.BeginTransition("transition-1", target, 10d, 4d);

            var midpoint = manager.AdvanceTransition(12d);
            var completed = manager.AdvanceTransition(15d);

            Assert.That(
                midpoint.Status,
                Is.EqualTo(EnvironmentTransitionStatus.InProgress));
            Assert.That(midpoint.NormalizedProgress, Is.EqualTo(0.5d));
            Assert.That(midpoint.State.Warmth, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(
                completed.Status,
                Is.EqualTo(EnvironmentTransitionStatus.Completed));
            Assert.That(
                completed.CompletedMonotonicTimeSeconds,
                Is.EqualTo(14d));
            Assert.That(manager.CurrentState, Is.EqualTo(target));
            Assert.That(manager.IsTransitionActive, Is.False);
        }

        [Test]
        public void BeginTransition_RejectsConcurrentTransition()
        {
            var manager = new EnvironmentParameterManager(
                CreateState(0.5f),
                new RecordingAdapter());
            manager.BeginTransition("first", CreateState(0.6f), 1d, 2d);

            Assert.Throws<InvalidOperationException>(
                () => manager.BeginTransition(
                    "second",
                    CreateState(0.7f),
                    2d,
                    2d));
        }

        [Test]
        public void CancelTransition_FreezesLastAppliedSafeState()
        {
            var manager = new EnvironmentParameterManager(
                CreateState(0.5f),
                new RecordingAdapter());
            manager.BeginTransition("cancel-me", CreateState(0.7f), 1d, 4d);
            manager.AdvanceTransition(3d);
            var frozen = manager.CurrentState;

            var cancelled = manager.CancelTransition(out var transitionId);
            var idle = manager.AdvanceTransition(10d);

            Assert.That(cancelled, Is.True);
            Assert.That(transitionId, Is.EqualTo("cancel-me"));
            Assert.That(idle.Status, Is.EqualTo(EnvironmentTransitionStatus.Idle));
            Assert.That(manager.CurrentState, Is.EqualTo(frozen));
        }

        private static EnvironmentState CreateState(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private sealed class RecordingAdapter : ISceneEnvironmentAdapter
        {
            public List<EnvironmentState> AppliedStates { get; } =
                new List<EnvironmentState>();

            public void ApplyState(EnvironmentState state)
            {
                AppliedStates.Add(state);
            }
        }
    }
}
