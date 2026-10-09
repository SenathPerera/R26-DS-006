using LaminarVR.AdaptiveMeditation.Physiology;
using LaminarVR.AdaptiveMeditation.Runtime.Networking;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Networking
{
    public sealed class ComponentBStressPayloadParserTests
    {
        private readonly ComponentBStressPayloadParser parser =
            new ComponentBStressPayloadParser();

        [Test]
        public void TryParse_MapsBandPredictionWithoutReDerivingDecision()
        {
            const string Json =
                "{\"timestamp\":1787282898.4,\"heartRate\":78.4,"
                + "\"rmssd\":34.1,\"sdnn\":42.0,\"stress\":{"
                + "\"mode\":\"band\",\"level_low\":1,\"level_high\":2,"
                + "\"label\":\"mild-to-moderate\",\"confidence\":0.54,"
                + "\"adjacent\":true,\"probabilities\":{\"relaxed\":0.08,"
                + "\"mild\":0.36,\"moderate\":0.54,\"high\":0.02},"
                + "\"continuous_score\":1.5},\"signalQuality\":0.92,"
                + "\"windowStart\":1787282838.4,"
                + "\"windowEnd\":1787282898.4}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(
                reason,
                Is.EqualTo(ComponentBStressPayloadParseReasonCode.Accepted));
            Assert.That(window.SourceTimestampUtcUnixSeconds, Is.EqualTo(1787282898.4d));
            Assert.That(window.WindowStartUtcUnixSeconds, Is.EqualTo(1787282838.4d));
            Assert.That(window.WindowEndUtcUnixSeconds, Is.EqualTo(1787282898.4d));
            Assert.That(window.HeartRateBpm, Is.EqualTo(78.4d));
            Assert.That(window.RmssdMs, Is.EqualTo(34.1d));
            Assert.That(window.SdnnMs, Is.EqualTo(42d));
            Assert.That(window.SignalQuality, Is.EqualTo(0.92d));
            Assert.That(window.Stress.Mode, Is.EqualTo(StressDecisionMode.Band));
            Assert.That(window.Stress.PointLevel, Is.Null);
            Assert.That(window.Stress.BandLowLevel, Is.EqualTo(1));
            Assert.That(window.Stress.BandHighLevel, Is.EqualTo(2));
            Assert.That(window.Stress.Label, Is.EqualTo("mild-to-moderate"));
            Assert.That(window.Stress.Confidence, Is.EqualTo(0.54d));
            Assert.That(window.Stress.Adjacent, Is.True);
            Assert.That(window.Stress.Probabilities.Level0Probability, Is.EqualTo(0.08d));
            Assert.That(window.Stress.Probabilities.Level1Probability, Is.EqualTo(0.36d));
            Assert.That(window.Stress.Probabilities.Level2Probability, Is.EqualTo(0.54d));
            Assert.That(window.Stress.Probabilities.Level3Probability, Is.EqualTo(0.02d));
            Assert.That(window.Stress.ContinuousScore, Is.EqualTo(1.5d));
        }

        [Test]
        public void TryParse_MapsPointPredictionAndOptionalAdjacent()
        {
            const string Json =
                "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
                + "\"sdnn\":40,\"stress\":{\"mode\":\"point\","
                + "\"level\":0,\"label\":\"relaxed\",\"confidence\":0.7,"
                + "\"probabilities\":{\"relaxed\":0.7,\"mild\":0.2,"
                + "\"moderate\":0.08,\"high\":0.02},"
                + "\"continuous_score\":0.42},\"signalQuality\":0.95,"
                + "\"windowStart\":940,\"windowEnd\":1000}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.True);
            Assert.That(
                reason,
                Is.EqualTo(ComponentBStressPayloadParseReasonCode.Accepted));
            Assert.That(window.Stress.Mode, Is.EqualTo(StressDecisionMode.Point));
            Assert.That(window.Stress.PointLevel, Is.EqualTo(0));
            Assert.That(window.Stress.BandLowLevel, Is.Null);
            Assert.That(window.Stress.BandHighLevel, Is.Null);
            Assert.That(window.Stress.Adjacent, Is.Null);
        }

        [TestCase(null, ComponentBStressPayloadParseReasonCode.PayloadEmpty)]
        [TestCase("", ComponentBStressPayloadParseReasonCode.PayloadEmpty)]
        [TestCase("{", ComponentBStressPayloadParseReasonCode.JsonMalformed)]
        public void TryParse_RejectsEmptyOrMalformedJson(
            string json,
            ComponentBStressPayloadParseReasonCode expectedReason)
        {
            var parsed = parser.TryParse(json, out var window, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(window, Is.Null);
            Assert.That(reason, Is.EqualTo(expectedReason));
        }

        [Test]
        public void TryParse_RejectsMissingStressBlock()
        {
            const string Json =
                "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
                + "\"sdnn\":40,\"signalQuality\":0.95,"
                + "\"windowStart\":940,\"windowEnd\":1000}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(window, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    ComponentBStressPayloadParseReasonCode.StressBlockMissing));
        }

        [Test]
        public void TryParse_RejectsMissingProbabilityBlock()
        {
            const string Json =
                "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
                + "\"sdnn\":40,\"stress\":{\"mode\":\"point\","
                + "\"level\":0,\"label\":\"relaxed\",\"confidence\":0.7,"
                + "\"continuous_score\":0.42},\"signalQuality\":0.95,"
                + "\"windowStart\":940,\"windowEnd\":1000}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(window, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(ComponentBStressPayloadParseReasonCode
                    .ProbabilityBlockMissing));
        }

        [Test]
        public void TryParse_RejectsBandWithoutBothModeSpecificLevels()
        {
            const string Json =
                "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
                + "\"sdnn\":40,\"stress\":{\"mode\":\"band\","
                + "\"level_high\":2,\"label\":\"mild-to-moderate\","
                + "\"confidence\":0.1,\"adjacent\":true,"
                + "\"probabilities\":{\"relaxed\":0.1,\"mild\":0.4,"
                + "\"moderate\":0.45,\"high\":0.05},"
                + "\"continuous_score\":1.45},\"signalQuality\":0.95,"
                + "\"windowStart\":940,\"windowEnd\":1000}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(window, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing));
        }

        [Test]
        public void TryParse_RejectsUnsupportedStressMode()
        {
            const string Json =
                "{\"timestamp\":1000,\"heartRate\":72,\"rmssd\":31,"
                + "\"sdnn\":40,\"stress\":{\"mode\":\"range\","
                + "\"label\":\"unknown\",\"confidence\":0.1,"
                + "\"probabilities\":{\"relaxed\":0.1,\"mild\":0.4,"
                + "\"moderate\":0.45,\"high\":0.05},"
                + "\"continuous_score\":1.45},\"signalQuality\":0.95,"
                + "\"windowStart\":940,\"windowEnd\":1000}";

            var parsed = parser.TryParse(Json, out var window, out var reason);

            Assert.That(parsed, Is.False);
            Assert.That(window, Is.Null);
            Assert.That(
                reason,
                Is.EqualTo(
                    ComponentBStressPayloadParseReasonCode.StressModeUnsupported));
        }
    }
}
