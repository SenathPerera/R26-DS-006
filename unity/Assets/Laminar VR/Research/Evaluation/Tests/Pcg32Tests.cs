using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class Pcg32Tests
    {
        [Test]
        public void NextUInt_ReferenceInitialization_MatchesPcgVector()
        {
            var random = new Pcg32(42UL, 54UL);
            var expected = new[]
            {
                0xA15C02B7U,
                0x7B47F409U,
                0xBA1D3330U,
                0x83D2F293U,
                0xBFA4784BU,
                0xCBED606EU
            };

            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(random.NextUInt(), Is.EqualTo(expected[index]));
            }
        }

        [Test]
        public void Create_SameSeedAndStream_ReplaysWithoutCrossStreamCoupling()
        {
            var first = EvaluationRandomStreams.Create(
                100000,
                EvaluationRandomStreamId.PhysiologyNoise);
            var replay = EvaluationRandomStreams.Create(
                100000,
                EvaluationRandomStreamId.PhysiologyNoise);
            var otherStream = EvaluationRandomStreams.Create(
                100000,
                EvaluationRandomStreamId.MissingWindowSchedule);
            var firstStreamFirstValue = EvaluationRandomStreams.Create(
                    100000,
                    EvaluationRandomStreamId.PhysiologyNoise)
                .NextUInt();
            var otherStreamFirstValue = otherStream.NextUInt();

            for (var index = 0; index < 16; index++)
            {
                Assert.That(replay.NextUInt(), Is.EqualTo(first.NextUInt()));
            }

            Assert.That(
                otherStreamFirstValue,
                Is.Not.EqualTo(firstStreamFirstValue));
        }

        [Test]
        public void NextInt_ReplaysAndRemainsInsideExclusiveBound()
        {
            var first = new Pcg32(1234UL, 5678UL);
            var replay = new Pcg32(1234UL, 5678UL);
            for (var index = 0; index < 1000; index++)
            {
                var value = first.NextInt(11);
                Assert.That(value, Is.InRange(0, 10));
                Assert.That(replay.NextInt(11), Is.EqualTo(value));
            }
        }
    }
}
