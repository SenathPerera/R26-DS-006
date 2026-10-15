using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class FrozenEvaluationProfileLoaderTests
    {
        [Test]
        public void TryLoad_ApprovedPilotBundle_ReturnsFrozenConfiguration()
        {
            var loaded = FrozenEvaluationProfileLoader.TryLoad(
                out var bundle,
                out var validationError);

            Assert.That(loaded, Is.True, validationError);
            Assert.That(bundle.Scene.SceneId, Is.EqualTo("temple-pond"));
            Assert.That(
                bundle.Timing.ConfigurationId,
                Is.EqualTo("temple-pond-session-pilot-v1"));
            Assert.That(bundle.DecisionOpportunityCount, Is.EqualTo(11));
            Assert.That(
                bundle.LinUcb.FeatureCount,
                Is.EqualTo(bundle.FeatureVectorBuilder.FeatureCount));
            Assert.That(bundle.LinUcb.ExplorationCoefficient,
                Is.EqualTo(0.25d));
            Assert.That(bundle.LinUcb.RidgeRegularization,
                Is.EqualTo(1d));
        }
    }
}
