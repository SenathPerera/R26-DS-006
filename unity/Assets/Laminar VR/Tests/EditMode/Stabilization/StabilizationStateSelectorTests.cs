using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Stabilization;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Stabilization
{
    public sealed class StabilizationStateSelectorTests
    {
        [Test]
        public void Select_UsesConfiguredRewardRecencyAndPreferencePenalty()
        {
            var selector = CreateSelector(maximumOutcomes: 3);
            var preference = State(0.5f);
            selector.RecordOutcome(Outcome("older-high", 10L, 0.8f, 1d));
            selector.RecordOutcome(Outcome("recent-close", 11L, 0.55f, 0.9d));

            var result = selector.Select(preference, FullLimits());

            Assert.That(result.UsedPreferenceFallback, Is.False);
            Assert.That(result.SelectedTransitionId, Is.EqualTo("recent-close"));
            Assert.That(result.ReasonCode,
                Is.EqualTo(StabilizationStateSelector.BestRecentReasonCode));
            Assert.That(result.EvaluationCount, Is.EqualTo(2));
        }

        [Test]
        public void Select_ExcludesDiscomfortSafetyAndOutsideLimitCandidates()
        {
            var selector = CreateSelector(maximumOutcomes: 4);
            selector.RecordOutcome(Outcome(
                "discomfort",
                1L,
                0.5f,
                10d,
                discomfort: true));
            selector.RecordOutcome(Outcome(
                "safety",
                2L,
                0.5f,
                9d,
                safety: true));
            selector.RecordOutcome(Outcome("outside", 3L, 0.9f, 8d));
            selector.RecordOutcome(Outcome("eligible", 4L, 0.6f, 1d));
            var limits = UniformLimits(0.2f, 0.8f);

            var result = selector.Select(State(0.5f), limits);

            Assert.That(result.SelectedTransitionId, Is.EqualTo("eligible"));
            Assert.That(result.GetEvaluation(0).ExclusionReason,
                Is.EqualTo(
                    StabilizationCandidateExclusionReason.DiscomfortReported));
            Assert.That(result.GetEvaluation(1).ExclusionReason,
                Is.EqualTo(
                    StabilizationCandidateExclusionReason.SafetyConcernReported));
            Assert.That(result.GetEvaluation(2).ExclusionReason,
                Is.EqualTo(
                    StabilizationCandidateExclusionReason.OutsideSafeLimits));
        }

        [Test]
        public void Select_FallsBackToSafePreferenceWhenNoCandidateIsEligible()
        {
            var selector = CreateSelector(maximumOutcomes: 3);
            var preference = State(0.4f);
            selector.RecordOutcome(Outcome(
                "excluded",
                1L,
                0.5f,
                5d,
                discomfort: true));

            var result = selector.Select(preference, FullLimits());

            Assert.That(result.UsedPreferenceFallback, Is.True);
            Assert.That(result.SelectedState, Is.EqualTo(preference));
            Assert.That(result.SelectedTransitionId, Is.Null);
            Assert.That(result.ReasonCode,
                Is.EqualTo(
                    StabilizationStateSelector.PreferenceFallbackReasonCode));
        }

        [Test]
        public void RecordOutcome_KeepsOnlyConfiguredRecentWindow()
        {
            var selector = CreateSelector(maximumOutcomes: 2);
            selector.RecordOutcome(Outcome("discarded", 1L, 0.5f, 10d));
            selector.RecordOutcome(Outcome("second", 2L, 0.5f, 1d));
            selector.RecordOutcome(Outcome("third", 3L, 0.5f, 2d));

            var result = selector.Select(State(0.5f), FullLimits());

            Assert.That(selector.OutcomeCount, Is.EqualTo(2));
            Assert.That(result.EvaluationCount, Is.EqualTo(2));
            Assert.That(result.GetEvaluation(0).Outcome.TransitionId,
                Is.EqualTo("second"));
        }

        [Test]
        public void Configuration_RejectsUninitializedResearchValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StabilizationSelectionConfiguration(
                    "config",
                    1,
                    0,
                    0.8d,
                    0.1d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StabilizationSelectionConfiguration(
                    "config",
                    1,
                    3,
                    0d,
                    0.1d));
        }

        private static StabilizationStateSelector CreateSelector(
            int maximumOutcomes)
        {
            return new StabilizationStateSelector(
                new StabilizationSelectionConfiguration(
                    "stabilization-test",
                    1,
                    maximumOutcomes,
                    0.5d,
                    0.5d));
        }

        private static StabilizationOutcome Outcome(
            string id,
            long sequence,
            float value,
            double reward,
            bool discomfort = false,
            bool safety = false)
        {
            return new StabilizationOutcome(
                id,
                sequence,
                State(value),
                reward,
                discomfort,
                safety);
        }

        private static EnvironmentState State(float value)
        {
            return new EnvironmentState(value, value, value, value, value);
        }

        private static EnvironmentStateLimits FullLimits()
        {
            return UniformLimits(0f, 1f);
        }

        private static EnvironmentStateLimits UniformLimits(
            float minimum,
            float maximum)
        {
            var range = new NormalizedRange(minimum, maximum);
            return new EnvironmentStateLimits(range, range, range, range, range);
        }
    }
}
