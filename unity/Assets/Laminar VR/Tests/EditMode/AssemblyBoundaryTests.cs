using System;
using System.Linq;
using LaminarVR.AdaptiveMeditation.Core;
using LaminarVR.AdaptiveMeditation.Runtime;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode
{
    public sealed class AssemblyBoundaryTests
    {
        [Test]
        public void CoreAssembly_DoesNotReferenceUnityEngine()
        {
            var coreAssembly = typeof(CoreAssemblyMarker).Assembly;
            var referencesUnityEngine = coreAssembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name != null
                    && reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal));

            Assert.That(
                coreAssembly.GetName().Name,
                Is.EqualTo("LaminarVR.AdaptiveMeditation.Core"));
            Assert.That(referencesUnityEngine, Is.False);
        }

        [Test]
        public void RuntimeAssembly_UsesExpectedAssemblyName()
        {
            var runtimeAssemblyName = typeof(RuntimeAssemblyMarker).Assembly.GetName().Name;

            Assert.That(
                runtimeAssemblyName,
                Is.EqualTo("LaminarVR.AdaptiveMeditation.Runtime"));
        }
    }
}
