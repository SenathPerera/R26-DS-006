using System;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy.ContextualBandit
{
    public sealed class DisjointLinUcbModelTests
    {
        private const string FeatureSchema = "linucb-test-features/1";

        [Test]
        public void Constructor_InitializesIndependentRidgePriorForEveryAction()
        {
            var model = CreateModel(ridge: 2d);

            for (var actionIndex = 0;
                actionIndex <= (int)EnvironmentAction.DecreaseAmbientMotion;
                actionIndex++)
            {
                var state = model.CaptureArmState(
                    (EnvironmentAction)actionIndex);
                Assert.That(state.UpdateCount, Is.Zero);
                Assert.That(state.GetDesignMatrixValue(0, 0), Is.EqualTo(2d));
                Assert.That(state.GetDesignMatrixValue(1, 1), Is.EqualTo(2d));
                Assert.That(state.GetDesignMatrixValue(0, 1), Is.Zero);
                Assert.That(state.GetDesignMatrixValue(1, 0), Is.Zero);
                Assert.That(state.GetRewardVectorValue(0), Is.Zero);
                Assert.That(state.GetRewardVectorValue(1), Is.Zero);
            }

            Assert.That(model.TotalUpdateCount, Is.Zero);
        }

        [Test]
        public void Select_MatchesKnownInitialLinUcbScore()
        {
            var model = CreateModel(ridge: 2d, alpha: 0.4d);
            var featureVector = CreateFeatureVector(1d, 0.5d);

            var selection = model.Select(
                featureVector,
                StandardCandidates());
            var score = FindScore(
                selection,
                EnvironmentAction.IncreaseWarmth);
            var expectedStandardError = Math.Sqrt(0.625d);

            Assert.That(score.ExpectedReward, Is.Zero);
            Assert.That(
                score.StandardError,
                Is.EqualTo(expectedStandardError).Within(1e-12d));
            Assert.That(
                score.ExplorationBonus,
                Is.EqualTo(0.4d * expectedStandardError).Within(1e-12d));
            Assert.That(
                score.Score,
                Is.EqualTo(0.4d * expectedStandardError).Within(1e-12d));
        }

        [Test]
        public void Update_MatchesKnownMatrixVectorAndExpectedReward()
        {
            var model = CreateModel(ridge: 1d, alpha: 0d);
            var featureVector = CreateFeatureVector(1d, 0.5d);

            model.Update(
                EnvironmentAction.IncreaseWarmth,
                featureVector,
                2d);

            var state = model.CaptureArmState(
                EnvironmentAction.IncreaseWarmth);
            Assert.That(state.GetDesignMatrixValue(0, 0), Is.EqualTo(2d));
            Assert.That(state.GetDesignMatrixValue(0, 1), Is.EqualTo(0.5d));
            Assert.That(state.GetDesignMatrixValue(1, 0), Is.EqualTo(0.5d));
            Assert.That(state.GetDesignMatrixValue(1, 1), Is.EqualTo(1.25d));
            Assert.That(state.GetRewardVectorValue(0), Is.EqualTo(2d));
            Assert.That(state.GetRewardVectorValue(1), Is.EqualTo(1d));

            var selection = model.Select(
                featureVector,
                StandardCandidates());
            var updatedScore = FindScore(
                selection,
                EnvironmentAction.IncreaseWarmth);
            Assert.That(
                updatedScore.ExpectedReward,
                Is.EqualTo(10d / 9d).Within(1e-12d));
            Assert.That(
                selection.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
        }

        [Test]
        public void Update_ModifiesOnlyExecutedActionArm()
        {
            var model = CreateModel();
            var featureVector = CreateFeatureVector(0.25d, -0.5d);

            model.Update(
                EnvironmentAction.DecreaseAmbientMotion,
                featureVector,
                0.75d);

            var updated = model.CaptureArmState(
                EnvironmentAction.DecreaseAmbientMotion);
            var untouched = model.CaptureArmState(
                EnvironmentAction.IncreaseAmbientMotion);
            Assert.That(updated.UpdateCount, Is.EqualTo(1L));
            Assert.That(updated.GetDesignMatrixValue(0, 1), Is.EqualTo(-0.125d));
            Assert.That(updated.GetRewardVectorValue(0), Is.EqualTo(0.1875d));
            Assert.That(untouched.UpdateCount, Is.Zero);
            Assert.That(untouched.GetDesignMatrixValue(0, 0), Is.EqualTo(1d));
            Assert.That(untouched.GetDesignMatrixValue(0, 1), Is.Zero);
            Assert.That(untouched.GetRewardVectorValue(0), Is.Zero);
            Assert.That(model.TotalUpdateCount, Is.EqualTo(1L));
        }

        [Test]
        public void Update_RejectsInvalidRewardWithoutMutatingArm()
        {
            var model = CreateModel();
            var before = model.CaptureArmState(EnvironmentAction.NoChange);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => model.Update(
                    EnvironmentAction.NoChange,
                    CreateFeatureVector(1d, 0d),
                    double.NaN));

            var after = model.CaptureArmState(EnvironmentAction.NoChange);
            Assert.That(after.UpdateCount, Is.EqualTo(before.UpdateCount));
            Assert.That(
                after.GetDesignMatrixValue(0, 0),
                Is.EqualTo(before.GetDesignMatrixValue(0, 0)));
            Assert.That(
                after.GetRewardVectorValue(0),
                Is.EqualTo(before.GetRewardVectorValue(0)));
            Assert.That(model.TotalUpdateCount, Is.Zero);
        }

        [Test]
        public void Select_UsesNoChangeForExactInitialTie()
        {
            var model = CreateModel(alpha: 0d);

            var selection = model.Select(
                CreateFeatureVector(1d, 0d),
                new[]
                {
                    new ContextualBanditCandidate(
                        EnvironmentAction.DecreaseWarmth,
                        0.1d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.NoChange,
                        0d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.IncreaseWarmth,
                        0.1d)
                });

            Assert.That(
                selection.SelectedAction,
                Is.EqualTo(EnvironmentAction.NoChange));
        }

        [Test]
        public void Select_UsesLowerMagnitudeBeforeEnumOrder()
        {
            var model = CreateSingleFeatureModel(alpha: 0d);
            var featureVector = CreateSingleFeatureVector(1d);
            model.Update(EnvironmentAction.NoChange, featureVector, -1d);

            var selection = model.Select(
                featureVector,
                new[]
                {
                    new ContextualBanditCandidate(
                        EnvironmentAction.IncreaseWarmth,
                        0.2d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.NoChange,
                        0d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.DecreaseWarmth,
                        0.1d)
                });

            Assert.That(
                selection.SelectedAction,
                Is.EqualTo(EnvironmentAction.DecreaseWarmth));
        }

        [Test]
        public void Select_UsesLowerEnumForOtherwiseEqualChangingActions()
        {
            var model = CreateSingleFeatureModel(alpha: 0d);
            var featureVector = CreateSingleFeatureVector(1d);
            model.Update(EnvironmentAction.NoChange, featureVector, -1d);

            var selection = model.Select(
                featureVector,
                new[]
                {
                    new ContextualBanditCandidate(
                        EnvironmentAction.DecreaseWarmth,
                        0.1d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.NoChange,
                        0d),
                    new ContextualBanditCandidate(
                        EnvironmentAction.IncreaseWarmth,
                        0.1d)
                });

            Assert.That(
                selection.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
        }

        [Test]
        public void Select_IsIndependentOfCandidateInputOrder()
        {
            var model = CreateSingleFeatureModel(alpha: 0d);
            var featureVector = CreateSingleFeatureVector(1d);
            model.Update(EnvironmentAction.NoChange, featureVector, -1d);
            var first = new ContextualBanditCandidate(
                EnvironmentAction.IncreaseWarmth,
                0.1d);
            var second = new ContextualBanditCandidate(
                EnvironmentAction.DecreaseWarmth,
                0.1d);
            var noChange = new ContextualBanditCandidate(
                EnvironmentAction.NoChange,
                0d);

            var forward = model.Select(
                featureVector,
                new[] { first, second, noChange });
            var reverse = model.Select(
                featureVector,
                new[] { noChange, second, first });

            Assert.That(forward.SelectedAction, Is.EqualTo(reverse.SelectedAction));
            Assert.That(
                forward.SelectedAction,
                Is.EqualTo(EnvironmentAction.IncreaseWarmth));
        }

        [Test]
        public void Select_RejectsMissingOrDuplicateNoChangeCandidate()
        {
            var model = CreateModel();
            var featureVector = CreateFeatureVector(1d, 0d);
            var noChange = new ContextualBanditCandidate(
                EnvironmentAction.NoChange,
                0d);

            Assert.Throws<ArgumentException>(
                () => model.Select(
                    featureVector,
                    new[]
                    {
                        new ContextualBanditCandidate(
                            EnvironmentAction.IncreaseWarmth,
                            0.1d)
                    }));
            Assert.Throws<ArgumentException>(
                () => model.Select(
                    featureVector,
                    new[] { noChange, noChange }));
        }

        [Test]
        public void Select_RejectsFeatureDimensionSchemaAndBoundsMismatch()
        {
            var model = CreateModel();

            Assert.Throws<ArgumentException>(
                () => model.Select(
                    new FeatureVector(FeatureSchema, new[] { 1d }),
                    StandardCandidates()));
            Assert.Throws<ArgumentException>(
                () => model.Select(
                    new FeatureVector("other-schema", new[] { 1d, 0d }),
                    StandardCandidates()));
            Assert.Throws<ArgumentException>(
                () => model.Select(
                    new FeatureVector(FeatureSchema, new[] { 1.01d, 0d }),
                    StandardCandidates()));
        }

        [Test]
        public void CollinearUpdatesWithSmallRidgeRemainFinite()
        {
            var model = new DisjointLinUcbModel(
                new LinUcbModelConfiguration(
                    "ill-conditioned-test",
                    1,
                    FeatureSchema,
                    3,
                    1e-9d,
                    0.1d));
            var featureVector = new FeatureVector(
                FeatureSchema,
                new[] { 1d, 1d, 1d });
            for (var index = 0; index < 100; index++)
            {
                model.Update(
                    EnvironmentAction.IncreaseWarmth,
                    featureVector,
                    index % 2 == 0 ? 0.25d : -0.25d);
            }

            var selection = model.Select(
                featureVector,
                StandardCandidates());

            for (var index = 0;
                index < selection.CandidateScoreCount;
                index++)
            {
                var score = selection.GetCandidateScore(index);
                Assert.That(double.IsNaN(score.Score), Is.False);
                Assert.That(double.IsInfinity(score.Score), Is.False);
                Assert.That(score.StandardError, Is.GreaterThanOrEqualTo(0d));
            }
        }

        [Test]
        public void Reset_RestoresAllArmPriorsAndCounts()
        {
            var model = CreateModel(ridge: 1.5d);
            model.Update(
                EnvironmentAction.IncreaseWarmth,
                CreateFeatureVector(1d, 0.5d),
                2d);

            model.Reset();
            var state = model.CaptureArmState(
                EnvironmentAction.IncreaseWarmth);

            Assert.That(model.TotalUpdateCount, Is.Zero);
            Assert.That(state.UpdateCount, Is.Zero);
            Assert.That(state.GetDesignMatrixValue(0, 0), Is.EqualTo(1.5d));
            Assert.That(state.GetDesignMatrixValue(1, 1), Is.EqualTo(1.5d));
            Assert.That(state.GetDesignMatrixValue(0, 1), Is.Zero);
            Assert.That(state.GetRewardVectorValue(0), Is.Zero);
        }

        private static DisjointLinUcbModel CreateModel(
            double ridge = 1d,
            double alpha = 0.2d)
        {
            return new DisjointLinUcbModel(
                new LinUcbModelConfiguration(
                    "linucb-test",
                    1,
                    FeatureSchema,
                    2,
                    ridge,
                    alpha));
        }

        private static DisjointLinUcbModel CreateSingleFeatureModel(
            double alpha)
        {
            return new DisjointLinUcbModel(
                new LinUcbModelConfiguration(
                    "linucb-tie-test",
                    1,
                    FeatureSchema,
                    1,
                    1d,
                    alpha));
        }

        private static FeatureVector CreateFeatureVector(
            double first,
            double second)
        {
            return new FeatureVector(
                FeatureSchema,
                new[] { first, second });
        }

        private static FeatureVector CreateSingleFeatureVector(double value)
        {
            return new FeatureVector(FeatureSchema, new[] { value });
        }

        private static ContextualBanditCandidate[] StandardCandidates()
        {
            return new[]
            {
                new ContextualBanditCandidate(
                    EnvironmentAction.NoChange,
                    0d),
                new ContextualBanditCandidate(
                    EnvironmentAction.IncreaseWarmth,
                    0.1d)
            };
        }

        private static ContextualBanditActionScore FindScore(
            ContextualBanditSelection selection,
            EnvironmentAction action)
        {
            for (var index = 0;
                index < selection.CandidateScoreCount;
                index++)
            {
                var score = selection.GetCandidateScore(index);
                if (score.Action == action)
                {
                    return score;
                }
            }

            Assert.Fail("The requested action score was not found.");
            return default;
        }
    }
}
