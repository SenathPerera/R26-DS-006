using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class LinUcbSensitivityCatalogTests
    {
        [Test]
        public void Catalog_ContainsExactlyFrozenGridAndOneProductionCell()
        {
            Assert.That(LinUcbSensitivityCatalog.Count, Is.EqualTo(12));
            var identities = new HashSet<string>();
            var productionCount = 0;
            for (var index = 0;
                index < LinUcbSensitivityCatalog.Count;
                index++)
            {
                var cell = LinUcbSensitivityCatalog.GetAt(index);
                Assert.That(identities.Add(cell.ExperimentId), Is.True);
                if (cell.IsProductionCell)
                {
                    productionCount++;
                    Assert.That(cell.ExplorationCoefficient, Is.EqualTo(0.25d));
                    Assert.That(cell.RidgeRegularization, Is.EqualTo(1d));
                }
            }

            Assert.That(productionCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SensitivityVariant_UsesCellConfigurationInPolicyLog()
        {
            Assert.That(
                FrozenEvaluationProfileLoader.TryLoad(
                    out var productionBundle,
                    out var validationError),
                Is.True,
                validationError);
            var cell = LinUcbSensitivityCatalog.GetAt(0);
            var variant = productionBundle.CreateLinUcbSensitivityVariant(cell);

            var result = await new EvaluationExperimentRunner()
                .RunCellDetailedAsync(
                    variant,
                    cell.ExperimentId,
                    SyntheticArchetypeId.A1PreferenceAlignedBalanced,
                    SyntheticFaultScenarioId.R0Nominal,
                    EvaluationPolicyCondition.ContextualBandit,
                    EvaluationExperimentRunner.StudyCFirstSeed);

            Assert.That(variant.LinUcb.ExplorationCoefficient,
                Is.EqualTo(cell.ExplorationCoefficient));
            Assert.That(variant.LinUcb.RidgeRegularization,
                Is.EqualTo(cell.RidgeRegularization));
            Assert.That(result.Summary.ExperimentId,
                Is.EqualTo(cell.ExperimentId));
            var loggedDecision = result.Session.Decisions
                .First(item => item.Result.Decision != null)
                .Result.Decision;
            Assert.That(loggedDecision.PolicyVersion,
                Does.Contain(cell.ConfigurationId));
        }
    }
}
