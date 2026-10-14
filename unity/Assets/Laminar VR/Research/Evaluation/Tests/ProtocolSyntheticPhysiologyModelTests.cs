using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class ProtocolSyntheticPhysiologyModelTests
    {
        [Test]
        public void Factory_ProtocolSeed_ProducesInRangeReproducibleInputs()
        {
            var first = SyntheticReplicationFactory.Create(
                100000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);
            var replay = SyntheticReplicationFactory.Create(
                100000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);

            Assert.That(first.Preference, Is.EqualTo(replay.Preference));
            Assert.That(
                first.SessionParameters.InitialStressScore,
                Is.EqualTo(replay.SessionParameters.InitialStressScore));
            Assert.That(first.Preference.Illumination, Is.InRange(0.2f, 0.8f));
            Assert.That(first.Preference.Warmth, Is.InRange(0.2f, 0.8f));
            Assert.That(
                first.Preference.AtmosphericSoftness,
                Is.InRange(0.2f, 0.8f));
            Assert.That(first.Preference.ColorRichness, Is.InRange(0.2f, 0.8f));
            Assert.That(first.Preference.AmbientMotion, Is.InRange(0.2f, 0.8f));
            Assert.That(
                first.SessionParameters.InitialStressScore,
                Is.InRange(1.7d, 2.7d));
            Assert.That(
                first.SessionParameters.InitialHeartRateBpm,
                Is.InRange(62d, 82d));
            Assert.That(
                first.SessionParameters.InitialRmssdMs,
                Is.InRange(25d, 65d));
            Assert.That(
                first.SessionParameters.NaturalRecoveryPerEmission,
                Is.InRange(0.02d, 0.06d));
        }

        [Test]
        public void CreateEmission_FirstTwoWindows_HaveFrozenBaselineDeviation()
        {
            var replication = SyntheticReplicationFactory.Create(
                100000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);
            var model = Initialize(replication);
            var first = Emit(model, 1, 60d, null).Window;
            var second = Emit(model, 2, 120d, null).Window;
            var parameters = replication.SessionParameters;

            Assert.That(
                (first.Stress.ContinuousScore
                    + second.Stress.ContinuousScore) / 2d,
                Is.EqualTo(parameters.InitialStressScore).Within(1e-12d));
            Assert.That(
                second.Stress.ContinuousScore
                    - first.Stress.ContinuousScore,
                Is.EqualTo(0.2d).Within(1e-12d));
            Assert.That(
                second.HeartRateBpm - first.HeartRateBpm,
                Is.EqualTo(4d).Within(1e-12d));
            Assert.That(
                first.RmssdMs.Value - second.RmssdMs.Value,
                Is.EqualTo(6d).Within(1e-12d));
            Assert.That(first.SdnnMs, Is.Null);
            Assert.That(
                first.SignalQuality,
                Is.EqualTo(
                    ProtocolSyntheticPhysiologyModel.NominalSignalQuality));
        }

        [Test]
        public void AlignedAction_ConsumesUtilityResponseOnce()
        {
            var replication = SyntheticReplicationFactory.Create(
                100000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);
            var control = Initialize(replication);
            var action = Initialize(replication);
            EmitBaselines(control);
            EmitBaselines(action);
            var before = replication.Preference;
            before = new EnvironmentState(
                before.Illumination,
                0f,
                before.AtmosphericSoftness,
                before.ColorRichness,
                before.AmbientMotion);
            var after = new EnvironmentState(
                before.Illumination,
                0.25f,
                before.AtmosphericSoftness,
                before.ColorRichness,
                before.AmbientMotion);
            var exposure = new SyntheticActionExposure(
                "aligned",
                EnvironmentAction.IncreaseWarmth,
                before,
                after,
                125d);

            var controlWindow = Emit(control, 3, 180d, null).Window;
            var actionWindow = Emit(action, 3, 180d, exposure).Window;

            Assert.That(
                actionWindow.Stress.ContinuousScore,
                Is.LessThan(controlWindow.Stress.ContinuousScore));
        }

        [Test]
        public void NonResponder_IgnoresEnvironmentResponse()
        {
            var replication = SyntheticReplicationFactory.Create(
                100000,
                SyntheticArchetypeId.A9PhysiologicalNonResponder);
            var control = Initialize(replication);
            var action = Initialize(replication);
            EmitBaselines(control);
            EmitBaselines(action);
            var exposure = new SyntheticActionExposure(
                "ignored",
                EnvironmentAction.IncreaseWarmth,
                new EnvironmentState(0f, 0f, 0f, 0f, 0f),
                replication.Preference,
                125d);

            var controlWindow = Emit(control, 3, 180d, null).Window;
            var actionWindow = Emit(action, 3, 180d, exposure).Window;

            Assert.That(
                actionWindow.Stress.ContinuousScore,
                Is.EqualTo(controlWindow.Stress.ContinuousScore));
            Assert.That(
                actionWindow.HeartRateBpm,
                Is.EqualTo(controlWindow.HeartRateBpm));
            Assert.That(
                actionWindow.RmssdMs,
                Is.EqualTo(controlWindow.RmssdMs));
        }

        private static ProtocolSyntheticPhysiologyModel Initialize(
            SyntheticReplication replication)
        {
            var model = replication.CreatePhysiologyModel();
            model.Initialize(new SyntheticPhysiologySessionContext(
                "test-session",
                1700000000d,
                replication.Preference));
            return model;
        }

        private static void EmitBaselines(
            ProtocolSyntheticPhysiologyModel model)
        {
            Emit(model, 1, 60d, null);
            Emit(model, 2, 120d, null);
        }

        private static SyntheticPhysiologyEmission Emit(
            ProtocolSyntheticPhysiologyModel model,
            int sequence,
            double timeSeconds,
            SyntheticActionExposure? exposure)
        {
            return model.CreateEmission(new SyntheticPhysiologyRequest(
                sequence,
                timeSeconds,
                EvaluationRuntimeBundle.SyntheticWindowDurationSeconds,
                exposure));
        }
    }
}
