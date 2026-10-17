using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class SyntheticFaultScenarioTests
    {
        [Test]
        public void Catalog_ContainsFrozenR0ThroughR12Definitions()
        {
            Assert.That(SyntheticFaultScenarioCatalog.Count, Is.EqualTo(13));
            var moderate = SyntheticFaultScenarioCatalog.Get(
                SyntheticFaultScenarioId.R11CombinedModerate);
            Assert.That(moderate.NoiseMultiplier, Is.EqualTo(1.5d));
            Assert.That(moderate.DropProbability, Is.EqualTo(0.15d));
            Assert.That(moderate.ArrivalLatencySeconds, Is.EqualTo(15d));
            Assert.That(moderate.LowQualityEveryNthEmission, Is.EqualTo(6));
            var severe = SyntheticFaultScenarioCatalog.Get(
                SyntheticFaultScenarioId.R12CombinedSevere);
            Assert.That(severe.NoiseMultiplier, Is.EqualTo(2.5d));
            Assert.That(severe.DropProbability, Is.EqualTo(0.3d));
            Assert.That(severe.ArrivalLatencySeconds, Is.EqualTo(60d));
            Assert.That(severe.LowQualityEveryNthEmission, Is.EqualTo(4));
            Assert.That(severe.NetworkGapStartSeconds, Is.EqualTo(600d));
            Assert.That(severe.NetworkGapEndSeconds, Is.EqualTo(780d));
        }

        [Test]
        public void PoorQuality_AppliesToEveryFourthEmission()
        {
            var model = CreateModel(
                SyntheticFaultScenarioId.R5IntermittentPoorQuality);

            Emit(model, 1, 60d);
            Emit(model, 2, 120d);
            Emit(model, 3, 180d);
            var fourth = Emit(model, 4, 240d);

            Assert.That(
                fourth.Window.SignalQuality,
                Is.EqualTo(FaultInjectingPhysiologyModel.PoorSignalQuality));
        }

        [Test]
        public void ArrivalLatency_OffsetsDeliveryWithoutChangingSourceWindow()
        {
            var model = CreateModel(
                SyntheticFaultScenarioId.R6ModerateArrivalLatency);

            var emission = Emit(model, 1, 60d);

            Assert.That(emission.DeliveryMonotonicTimeSeconds, Is.EqualTo(90d));
            Assert.That(
                emission.Window.WindowEndUtcUnixSeconds,
                Is.EqualTo(1700000060d));
        }

        [Test]
        public void StaleBurst_OffsetsExactlyThreeDeclaredSourceTimestamps()
        {
            var model = CreateModel(SyntheticFaultScenarioId.R8StaleBurst);
            SyntheticPhysiologyEmission emission = default;
            for (var sequence = 1; sequence <= 9; sequence++)
            {
                var time = sequence * 60d;
                emission = Emit(model, sequence, time);
                var expectedOffset = time >= 420d && time <= 540d
                    ? 120d
                    : 0d;
                Assert.That(
                    emission.Window.WindowEndUtcUnixSeconds
                        - emission.Window.SourceTimestampUtcUnixSeconds,
                    Is.EqualTo(expectedOffset),
                    "Emission " + sequence);
            }
        }

        [Test]
        public void OutOfOrderPair_Delivers540BeforeHeld480Window()
        {
            var model = CreateModel(SyntheticFaultScenarioId.R9OutOfOrderPair);
            SyntheticPhysiologyEmission at480 = default;
            SyntheticPhysiologyEmission at540 = default;
            for (var sequence = 1; sequence <= 9; sequence++)
            {
                var emission = Emit(model, sequence, sequence * 60d);
                if (sequence == 8)
                {
                    at480 = emission;
                }
                else if (sequence == 9)
                {
                    at540 = emission;
                }
            }

            Assert.That(at540.DeliveryMonotonicTimeSeconds, Is.EqualTo(540d));
            Assert.That(at480.DeliveryMonotonicTimeSeconds, Is.EqualTo(541d));
        }

        [Test]
        public void RandomLoss_SameSeedProducesSameSchedule()
        {
            var first = CreateModel(SyntheticFaultScenarioId.R4SevereLoss);
            var replay = CreateModel(SyntheticFaultScenarioId.R4SevereLoss);
            var droppedCount = 0;
            for (var sequence = 1; sequence <= 19; sequence++)
            {
                var time = sequence * 60d;
                var firstEmission = Emit(first, sequence, time);
                var replayEmission = Emit(replay, sequence, time);
                Assert.That(
                    replayEmission.Dropped,
                    Is.EqualTo(firstEmission.Dropped));
                if (firstEmission.Dropped)
                {
                    droppedCount++;
                }
            }

            Assert.That(droppedCount, Is.GreaterThan(0));
        }

        [Test]
        public void NetworkGap_UsesClosedStartAndOpenEnd()
        {
            var schedule = SyntheticFaultSessionFactory.CreateNetworkSchedule(
                SyntheticFaultScenarioId.R10NetworkGap);

            Assert.That(schedule.IsConnected(419.999d), Is.True);
            Assert.That(schedule.IsConnected(420d), Is.False);
            Assert.That(schedule.IsConnected(599.999d), Is.False);
            Assert.That(schedule.IsConnected(600d), Is.True);
            Assert.That(schedule.GetNextChangeAfter(0d), Is.EqualTo(420d));
            Assert.That(schedule.GetNextChangeAfter(420d), Is.EqualTo(600d));
            Assert.That(schedule.GetNextChangeAfter(600d), Is.Null);
        }

        private static ISyntheticPhysiologyModel CreateModel(
            SyntheticFaultScenarioId scenarioId)
        {
            var replication = SyntheticReplicationFactory.Create(
                200000,
                SyntheticArchetypeId.A1PreferenceAlignedBalanced);
            var model = SyntheticFaultSessionFactory.CreatePhysiologyModel(
                replication,
                scenarioId);
            model.Initialize(new SyntheticPhysiologySessionContext(
                "fault-test",
                1700000000d,
                replication.Preference));
            return model;
        }

        private static SyntheticPhysiologyEmission Emit(
            ISyntheticPhysiologyModel model,
            int sequence,
            double timeSeconds)
        {
            return model.CreateEmission(new SyntheticPhysiologyRequest(
                sequence,
                timeSeconds,
                EvaluationRuntimeBundle.SyntheticWindowDurationSeconds,
                null));
        }
    }
}
