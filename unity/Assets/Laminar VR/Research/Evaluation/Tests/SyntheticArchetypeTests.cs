using LaminarVR.AdaptiveMeditation.Environment;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.ResearchEvaluation.Tests
{
    public sealed class SyntheticArchetypeTests
    {
        private static readonly EnvironmentState Preference =
            new EnvironmentState(0.3f, 0.4f, 0.5f, 0.6f, 0.7f);

        [Test]
        public void Catalog_AllDeclaredOptima_HaveUnitUtility()
        {
            Assert.That(SyntheticArchetypeCatalog.Count, Is.EqualTo(9));
            for (var index = 0;
                index < SyntheticArchetypeCatalog.Count;
                index++)
            {
                var archetype = SyntheticArchetypeCatalog.GetAt(index);
                var time = archetype.Id
                    == SyntheticArchetypeId.A8NonStationaryResponder
                        ? 570d
                        : 300d;
                var optimum = archetype.CreateOptimum(Preference, time);

                Assert.That(
                    archetype.CalculateUtility(Preference, optimum, time),
                    Is.EqualTo(1d).Within(1e-9d),
                    archetype.Code);
            }
        }

        [Test]
        public void MotionAverse_ThresholdAddsDeclaredDiscontinuity()
        {
            var archetype = SyntheticArchetypeCatalog.Get(
                SyntheticArchetypeId.A5MotionAverseThreshold);
            var below = new EnvironmentState(0.3f, 0.4f, 0.5f, 0.6f, 0.35f);
            var above = new EnvironmentState(0.3f, 0.4f, 0.5f, 0.6f, 0.36f);

            var discontinuity = archetype.CalculateUtility(
                    Preference,
                    below,
                    300d)
                - archetype.CalculateUtility(Preference, above, 300d);

            Assert.That(discontinuity, Is.GreaterThan(0.19d));
        }

        [Test]
        public void NonStationary_ChangesOnlyAtDeclaredBoundary()
        {
            var archetype = SyntheticArchetypeCatalog.Get(
                SyntheticArchetypeId.A8NonStationaryResponder);
            var before = archetype.CreateOptimum(Preference, 569.999d);
            var after = archetype.CreateOptimum(Preference, 570d);

            Assert.That(before, Is.EqualTo(Preference));
            Assert.That(after.Illumination, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(after.Warmth, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(
                after.AtmosphericSoftness,
                Is.EqualTo(Preference.AtmosphericSoftness));
            Assert.That(
                after.ColorRichness,
                Is.EqualTo(Preference.ColorRichness));
            Assert.That(after.AmbientMotion, Is.EqualTo(Preference.AmbientMotion));
        }
    }
}
