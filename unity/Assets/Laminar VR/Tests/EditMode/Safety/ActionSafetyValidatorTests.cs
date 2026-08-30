using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Safety;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Safety
{
    public sealed class ActionSafetyValidatorTests
    {
        private readonly ActionSafetyValidator validator = new ActionSafetyValidator();

        [Test]
        public void Validate_AcceptsSafeAction()
        {
            var result = validator.Validate(
                EnvironmentAction.IncreaseWarmth,
                CreateState(),
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Modified, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.IncreaseWarmth));
            Assert.That(result.ReasonCode, Is.EqualTo(ActionValidationReasonCode.Accepted));
            Assert.That(result.SafeTarget.Warmth, Is.EqualTo(0.6f).Within(0.00001f));
            Assert.That(result.AppliedVariation, Is.EqualTo(0.1d).Within(0.00001d));
        }

        [Test]
        public void Validate_AcceptsNoChangeAsAFirstClassAction()
        {
            var current = CreateState();

            var result = validator.Validate(
                EnvironmentAction.NoChange,
                current,
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Modified, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(result.SafeTarget, Is.EqualTo(current));
            Assert.That(result.AppliedVariation, Is.Zero);
        }

        [Test]
        public void Validate_ClipsPartialMovementToSceneRange()
        {
            var current = new EnvironmentState(0.75f, 0.5f, 0.5f, 0.5f, 0.5f);

            var result = validator.Validate(
                EnvironmentAction.IncreaseIllumination,
                current,
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Modified, Is.True);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.IncreaseIllumination));
            Assert.That(result.ReasonCode, Is.EqualTo(ActionValidationReasonCode.RangeClipped));
            Assert.That(result.RequestedTarget.Illumination, Is.EqualTo(0.85f).Within(0.00001f));
            Assert.That(result.SafeTarget.Illumination, Is.EqualTo(0.8f));
            Assert.That(result.AppliedVariation, Is.EqualTo(0.05d).Within(0.00001d));
        }

        [Test]
        public void Validate_ReplacesActionAtBoundaryWithNoChange()
        {
            var current = new EnvironmentState(0.8f, 0.5f, 0.5f, 0.5f, 0.5f);

            var result = validator.Validate(
                EnvironmentAction.IncreaseIllumination,
                current,
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Modified, Is.True);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(ActionValidationReasonCode.ParameterAtBoundary));
            Assert.That(result.SafeTarget, Is.EqualTo(current));
        }

        [TestCase(SafetyBlockReason.SessionNotAdaptive, ActionValidationReasonCode.SessionNotAdaptive)]
        [TestCase(SafetyBlockReason.SignalInvalid, ActionValidationReasonCode.SignalInvalid)]
        [TestCase(SafetyBlockReason.SignalStale, ActionValidationReasonCode.SignalStale)]
        [TestCase(SafetyBlockReason.CooldownActive, ActionValidationReasonCode.CooldownActive)]
        [TestCase(SafetyBlockReason.SensitivityRestriction, ActionValidationReasonCode.SensitivityRestriction)]
        [TestCase(SafetyBlockReason.Paused, ActionValidationReasonCode.Paused)]
        [TestCase(SafetyBlockReason.EmergencyStop, ActionValidationReasonCode.EmergencyStop)]
        [TestCase(SafetyBlockReason.TransitionActive, ActionValidationReasonCode.TransitionActive)]
        [TestCase(SafetyBlockReason.Stabilization, ActionValidationReasonCode.Stabilization)]
        [TestCase(SafetyBlockReason.ConfigurationError, ActionValidationReasonCode.ConfigurationError)]
        public void Validate_ReplacesBlockedActionWithNoChange(
            SafetyBlockReason blockReason,
            ActionValidationReasonCode expectedReason)
        {
            var runtimeState = new SafetyRuntimeState(blockReason, null, 0, 0d);

            var result = validator.Validate(
                EnvironmentAction.IncreaseWarmth,
                CreateState(),
                CreateProfile(),
                runtimeState,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
        }

        [Test]
        public void Validate_EnforcesConsecutiveDirectionLimit()
        {
            var runtimeState = new SafetyRuntimeState(
                SafetyBlockReason.None,
                EnvironmentAction.IncreaseWarmth,
                2,
                0.2d);

            var result = validator.Validate(
                EnvironmentAction.IncreaseWarmth,
                CreateState(),
                CreateProfile(),
                runtimeState,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(ActionValidationReasonCode.ConsecutiveDirectionLimit));
        }

        [Test]
        public void Validate_EnforcesSessionTotalVariationLimit()
        {
            var runtimeState = new SafetyRuntimeState(
                SafetyBlockReason.None,
                EnvironmentAction.IncreaseIllumination,
                1,
                0.45d);

            var result = validator.Validate(
                EnvironmentAction.IncreaseWarmth,
                CreateState(),
                CreateProfile(),
                runtimeState,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(
                result.ReasonCode,
                Is.EqualTo(ActionValidationReasonCode.TotalVariationLimit));
            Assert.That(result.AppliedVariation, Is.Zero);
        }

        [Test]
        public void Validate_RejectsCurrentStateOutsideSceneLimits()
        {
            var outside = new EnvironmentState(0.1f, 0.5f, 0.5f, 0.5f, 0.5f);

            var result = validator.Validate(
                EnvironmentAction.NoChange,
                outside,
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo(ActionValidationReasonCode.ConfigurationError));
            Assert.That(result.SafeTarget, Is.EqualTo(outside));
        }

        [Test]
        public void Validate_RejectsUnknownAction()
        {
            var result = validator.Validate(
                (EnvironmentAction)99,
                CreateState(),
                CreateProfile(),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ExecutedAction, Is.EqualTo(EnvironmentAction.NoChange));
            Assert.That(result.ReasonCode, Is.EqualTo(ActionValidationReasonCode.ConfigurationError));
        }

        [Test]
        public void Validate_UsesTheStepForTheProposedDimension()
        {
            var actionSteps = new EnvironmentActionStepConfiguration(
                0.1f,
                0.25f,
                0.3f,
                0.2f,
                0.2f);

            var result = validator.Validate(
                EnvironmentAction.IncreaseAtmosphericSoftness,
                CreateState(),
                CreateProfile(actionSteps),
                SafetyRuntimeState.Ready,
                CreateSafetyLimits());

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.SafeTarget.AtmosphericSoftness,
                Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(result.AppliedVariation,
                Is.EqualTo(0.3d).Within(1e-6d));
        }

        private static SceneEnvironmentProfile CreateProfile(
            EnvironmentActionStepConfiguration actionSteps = null)
        {
            var range = new NormalizedRange(0.2f, 0.8f);
            var limits = new EnvironmentStateLimits(range, range, range, range, range);
            return new SceneEnvironmentProfile(
                "test-scene",
                "Test Scene",
                CreateState(),
                limits,
                actionSteps ?? new EnvironmentActionStepConfiguration(
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f,
                    0.1f),
                2f,
                5f);
        }

        private static ActionSafetyLimits CreateSafetyLimits()
        {
            return new ActionSafetyLimits(2, 0.5d);
        }

        private static EnvironmentState CreateState()
        {
            return new EnvironmentState(0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
        }
    }
}
