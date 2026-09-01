using System;
using LaminarVR.AdaptiveMeditation.Policy;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Policy
{
    public sealed class FeatureVectorTests
    {
        [Test]
        public void ConstructorAndToArray_DefensivelyCopyValues()
        {
            var source = new[] { 1d, 2d, 3d };
            var vector = new FeatureVector(" schema/test ", source);
            source[0] = 99d;

            var copy = vector.ToArray();
            copy[1] = 99d;

            Assert.That(vector.SchemaVersion, Is.EqualTo("schema/test"));
            Assert.That(vector.Count, Is.EqualTo(3));
            Assert.That(vector[0], Is.EqualTo(1d));
            Assert.That(vector[1], Is.EqualTo(2d));
        }

        [Test]
        public void CopyTo_CopiesAllFeaturesAtRequestedOffset()
        {
            var vector = new FeatureVector("schema/test", new[] { 1d, 2d });
            var destination = new double[4];

            vector.CopyTo(destination, 1);

            Assert.That(destination, Is.EqualTo(new[] { 0d, 1d, 2d, 0d }));
        }

        [TestCase(null)]
        [TestCase(" ")]
        public void Constructor_RejectsMissingSchemaVersion(string schemaVersion)
        {
            Assert.Throws<ArgumentException>(
                () => new FeatureVector(schemaVersion, new[] { 1d }));
        }

        [Test]
        public void Constructor_RejectsMissingEmptyOrNonFiniteValues()
        {
            Assert.Throws<ArgumentNullException>(
                () => new FeatureVector("schema/test", null));
            Assert.Throws<ArgumentException>(
                () => new FeatureVector("schema/test", Array.Empty<double>()));
            Assert.Throws<ArgumentException>(
                () => new FeatureVector("schema/test", new[] { double.NaN }));
            Assert.Throws<ArgumentException>(
                () => new FeatureVector(
                    "schema/test",
                    new[] { double.PositiveInfinity }));
        }
    }
}
