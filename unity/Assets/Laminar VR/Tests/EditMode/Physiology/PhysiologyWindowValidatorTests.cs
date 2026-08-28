using LaminarVR.AdaptiveMeditation.Physiology;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Physiology
{
    public sealed class PhysiologyWindowValidatorTests
    {
        private readonly PhysiologyWindowValidator validator =
            new PhysiologyWindowValidator(CreateConfiguration());

        [Test]
        public void Validate_AcceptsCurrentComponentBPointDecision()
        {
            var result = validator.Validate(CreateWindow(), 1000d);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo(PhysiologyValidationReasonCode.Accepted));
        }

        [Test]
        public void Validate_AcceptsBandDecisionWithoutReDerivingItsLabel()
        {
            var stress = new StressDecision(
                StressDecisionMode.Band,
                null,
                0,
                2,
                "producer-authoritative-band",
                0.1d,
                false,
                new StressProbabilityVector(0.4d, 0.1d, 0.45d, 0.05d),
                1.15d);

            var result = validator.Validate(CreateWindow(stress: stress), 1000d);

            Assert.That(result.Accepted, Is.True);
        }

        [Test]
        public void Validate_AcceptsCurrentComponentBBandPayloadSemantics()
        {
            var stress = new StressDecision(
                StressDecisionMode.Band,
                null,
                1,
                2,
                "mild-to-moderate",
                0.10d,
                true,
                new StressProbabilityVector(0.08d, 0.40d, 0.50d, 0.02d),
                1.46d);

            var result = validator.Validate(CreateWindow(stress: stress), 1000d);

            Assert.That(result.Accepted, Is.True);
        }

        [Test]
        public void Validate_AcceptsMissingOptionalHrvMetrics()
        {
            var result = validator.Validate(
                CreateWindow(rmssdMs: null, sdnnMs: null),
                1000d);

            Assert.That(result.Accepted, Is.True);
        }

        [Test]
        public void Validate_DoesNotReplaceAuthoritativeLevelWithProbabilityArgmax()
        {
            var stress = CreatePointStress(
                pointLevel: 0,
                probabilities: new StressProbabilityVector(0.1d, 0.1d, 0.7d, 0.1d),
                label: "relaxed");

            var result = validator.Validate(CreateWindow(stress: stress), 1000d);

            Assert.That(result.Accepted, Is.True);
        }

        [TestCaseSource(nameof(InvalidWindowCases))]
        public void Validate_RejectsInvalidWindow(
            PhysiologyWindow window,
            double receivedUtcUnixSeconds,
            PhysiologyValidationReasonCode expectedReason)
        {
            var result = validator.Validate(window, receivedUtcUnixSeconds);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
        }

        private static object[] InvalidWindowCases()
        {
            return new object[]
            {
                new object[]
                {
                    null,
                    1000d,
                    PhysiologyValidationReasonCode.PayloadMissing
                },
                new object[]
                {
                    CreateWindow(sourceTimestamp: double.NaN),
                    1000d,
                    PhysiologyValidationReasonCode.TimestampInvalid
                },
                new object[]
                {
                    CreateWindow(sourceTimestamp: 1000.01d),
                    1000d,
                    PhysiologyValidationReasonCode.SourceTimestampMismatch
                },
                new object[]
                {
                    CreateWindow(windowStart: 1000d),
                    1000d,
                    PhysiologyValidationReasonCode.WindowOrderInvalid
                },
                new object[]
                {
                    CreateWindow(windowStart: 980d),
                    1000d,
                    PhysiologyValidationReasonCode.WindowTooShort
                },
                new object[]
                {
                    CreateWindow(),
                    997d,
                    PhysiologyValidationReasonCode.FutureTimestamp
                },
                new object[]
                {
                    CreateWindow(),
                    1091d,
                    PhysiologyValidationReasonCode.StaleAtReceipt
                },
                new object[]
                {
                    CreateWindow(heartRateBpm: 0d),
                    1000d,
                    PhysiologyValidationReasonCode.HeartRateInvalid
                },
                new object[]
                {
                    CreateWindow(rmssdMs: double.NaN),
                    1000d,
                    PhysiologyValidationReasonCode.RmssdInvalid
                },
                new object[]
                {
                    CreateWindow(sdnnMs: -1d),
                    1000d,
                    PhysiologyValidationReasonCode.SdnnInvalid
                },
                new object[]
                {
                    CreateWindow(signalQuality: 1.1d),
                    1000d,
                    PhysiologyValidationReasonCode.SignalQualityInvalid
                },
                new object[]
                {
                    new PhysiologyWindow(
                        1000d,
                        940d,
                        1000d,
                        78d,
                        34d,
                        42d,
                        null,
                        0.95d),
                    1000d,
                    PhysiologyValidationReasonCode.StressDecisionMissing
                },
                new object[]
                {
                    CreateWindow(stress: CreatePointStress(pointLevel: 4)),
                    1000d,
                    PhysiologyValidationReasonCode.StressLevelsInvalid
                },
                new object[]
                {
                    CreateWindow(stress: CreatePointStress(label: " ")),
                    1000d,
                    PhysiologyValidationReasonCode.StressLabelMissing
                },
                new object[]
                {
                    CreateWindow(stress: CreatePointStress(confidence: double.NaN)),
                    1000d,
                    PhysiologyValidationReasonCode.StressConfidenceInvalid
                },
                new object[]
                {
                    CreateWindow(
                        stress: CreatePointStress(
                            probabilities:
                                new StressProbabilityVector(-0.1d, 0.2d, 0.8d, 0.1d))),
                    1000d,
                    PhysiologyValidationReasonCode.StressProbabilitiesInvalid
                },
                new object[]
                {
                    CreateWindow(
                        stress: CreatePointStress(
                            probabilities:
                                new StressProbabilityVector(0.1d, 0.2d, 0.5d, 0.1d))),
                    1000d,
                    PhysiologyValidationReasonCode.StressProbabilitySumInvalid
                },
                new object[]
                {
                    CreateWindow(stress: CreatePointStress(continuousScore: 3.1d)),
                    1000d,
                    PhysiologyValidationReasonCode.ContinuousStressScoreInvalid
                },
                new object[]
                {
                    CreateWindow(
                        stress: new StressDecision(
                            StressDecisionMode.Band,
                            null,
                            0,
                            2,
                            "relaxed-to-moderate",
                            0.1d,
                            true,
                            new StressProbabilityVector(0.4d, 0.1d, 0.45d, 0.05d),
                            1.15d)),
                    1000d,
                    PhysiologyValidationReasonCode.StressLevelsInvalid
                }
            };
        }

        private static PhysiologyWindow CreateWindow(
            double sourceTimestamp = 1000d,
            double windowStart = 940d,
            double windowEnd = 1000d,
            double heartRateBpm = 78d,
            double? rmssdMs = 34d,
            double? sdnnMs = 42d,
            StressDecision stress = null,
            double signalQuality = 0.95d)
        {
            return new PhysiologyWindow(
                sourceTimestamp,
                windowStart,
                windowEnd,
                heartRateBpm,
                rmssdMs,
                sdnnMs,
                stress ?? CreatePointStress(),
                signalQuality);
        }

        private static StressDecision CreatePointStress(
            int pointLevel = 2,
            string label = "moderate",
            double confidence = 0.5d,
            StressProbabilityVector? probabilities = null,
            double continuousScore = 1.7d)
        {
            return new StressDecision(
                StressDecisionMode.Point,
                pointLevel,
                null,
                null,
                label,
                confidence,
                false,
                probabilities
                    ?? new StressProbabilityVector(0.1d, 0.2d, 0.6d, 0.1d),
                continuousScore);
        }

        private static PhysiologyValidationConfiguration CreateConfiguration()
        {
            return new PhysiologyValidationConfiguration(
                "test",
                1,
                90d,
                30d,
                2d,
                0.001d,
                0.005d,
                0.8d,
                0.9d,
                4);
        }
    }
}
