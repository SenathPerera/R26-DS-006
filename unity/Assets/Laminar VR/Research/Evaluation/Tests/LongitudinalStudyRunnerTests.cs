using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class LongitudinalStudyRunnerTests
    {
        [Test]
        public async Task SmokeMatrix_CarriesOnlyTheIntendedModel_AndKeepsProbesReadOnly()
        {
            Assert.That(LongitudinalEvaluationProfileLoader.ProtocolId,
                Is.EqualTo("laminar-vr-longitudinal-simulation-v1.0"));
            Assert.That(LongitudinalEvaluationProfileLoader.TryLoad(
                out var bundle, out var mapping, out var error),
                Is.True, error);
            Assert.That(mapping.ConfigurationVersion, Is.EqualTo(4));
            Assert.That(bundle.Scene.ActionSteps.Illumination,
                Is.EqualTo(0.15f).Within(1e-5));
            Assert.That(bundle.Scene.ActionSteps.Warmth,
                Is.EqualTo(0.35f).Within(1e-5));
            Assert.That(bundle.Scene.TransitionDurationSeconds,
                Is.EqualTo(6d));

            var sink = new CheckingSink();
            var completed = await new LongitudinalStudyRunner().RunAsync(
                bundle, sink, null, CancellationToken.None, 1, 2, 4, true);

            Assert.That(completed, Is.EqualTo(68));
            Assert.That(sink.SessionCount, Is.EqualTo(68));
            Assert.That(sink.ProbeCount, Is.EqualTo(34 * 3 * 4));
        }

        [Test]
        public async Task ProgressCallback_ReportsEveryCompletedSession()
        {
            Assert.That(LongitudinalEvaluationProfileLoader.TryLoad(
                out var bundle, out _, out var error), Is.True, error);
            var sink = new CheckingSink();
            var callbackCount = 0;
            var completed = await new LongitudinalStudyRunner().RunAsync(
                bundle, sink, count => callbackCount = count,
                CancellationToken.None, 1, 1, 1, true);

            Assert.That(completed, Is.EqualTo(34));
            Assert.That(callbackCount, Is.EqualTo(34));
            Assert.That(sink.SessionCount, Is.EqualTo(34));
        }

        [Test]
        public void Drift_ChangesOnlyResponseOptimumAfterSessionTen()
        {
            var preference = new EnvironmentState(
                0.2f, 0.7f, 0.4f, 0.6f, 0.5f);
            Assert.That(LongitudinalStudyRunner.ResponsePreference(
                preference, LongitudinalScenario.BetweenSessionDrift, 10),
                Is.EqualTo(preference));
            var drifted = LongitudinalStudyRunner.ResponsePreference(
                preference, LongitudinalScenario.BetweenSessionDrift, 11);
            Assert.That(drifted.Illumination, Is.EqualTo(0.8f).Within(1e-6));
            Assert.That(drifted.Warmth, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(drifted.AtmosphericSoftness,
                Is.EqualTo(preference.AtmosphericSoftness));
            Assert.That(drifted.ColorRichness,
                Is.EqualTo(preference.ColorRichness));
            Assert.That(drifted.AmbientMotion,
                Is.EqualTo(preference.AmbientMotion));
        }

        private sealed class CheckingSink : ILongitudinalResultSink
        {
            private readonly Dictionary<string, long> previousUpdates =
                new Dictionary<string, long>();

            public int SessionCount { get; private set; }
            public int ProbeCount { get; private set; }

            public void OnProbe(
                LongitudinalScenario scenario,
                SyntheticArchetypeId archetype,
                int profileSeed,
                LongitudinalPolicyCondition condition,
                int checkpoint,
                int probeIndex,
                SafeOracleDecisionRecord result,
                long modelUpdateCount)
            {
                Assert.That(profileSeed, Is.EqualTo(
                    scenario == LongitudinalScenario.Primary ? 510000 : 520000));
                Assert.That(result.PseudoRegret,
                    Is.GreaterThanOrEqualTo(-1e-8));
                ProbeCount++;
            }

            public void OnSession(
                LongitudinalScenario scenario,
                SyntheticArchetypeId archetype,
                int profileSeed,
                int sessionIndex,
                int sessionSeed,
                LongitudinalPolicyCondition condition,
                EvaluationSessionSummary summary,
                EvaluationSessionResult result,
                long priorModelUpdateCount)
            {
                Assert.That(profileSeed, Is.EqualTo(
                    scenario == LongitudinalScenario.Primary ? 510000 : 520000));
                var key = scenario + "/" + archetype + "/"
                    + profileSeed + "/" + condition;
                if (condition == LongitudinalPolicyCondition.CarryLinUcb)
                {
                    Assert.That(priorModelUpdateCount,
                        Is.EqualTo(previousUpdates.TryGetValue(key, out var count)
                            ? count : 0L));
                    Assert.That(result.ModelSnapshot.TotalUpdateCount,
                        Is.EqualTo(priorModelUpdateCount
                            + result.RewardAppliedCount));
                    previousUpdates[key] = result.ModelSnapshot.TotalUpdateCount;
                }
                else if (condition == LongitudinalPolicyCondition.ResetLinUcb)
                {
                    Assert.That(priorModelUpdateCount, Is.Zero);
                    Assert.That(result.ModelSnapshot.TotalUpdateCount,
                        Is.EqualTo(result.RewardAppliedCount));
                }

                Assert.That(result.AllAppliedStatesSafe, Is.True);
                Assert.That(summary.InvalidModelUpdateCount, Is.Zero);
                SessionCount++;
            }
        }
    }
}
