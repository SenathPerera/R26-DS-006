using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Runtime.Application;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Application
{
    public sealed class QuestClientIdentityProviderTests
    {
        [Test]
        public void GetOrCreate_PersistsAndReusesPseudonymousIdentity()
        {
            var store = new MemoryStore();
            var provider = new QuestClientIdentityProvider(store);

            var first = provider.GetOrCreate();
            var second = provider.GetOrCreate();

            Assert.That(first, Does.StartWith("quest-"));
            Assert.That(first, Has.Length.EqualTo(38));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void GetOrCreate_ReplacesInvalidStoredIdentity()
        {
            var store = new MemoryStore();
            store.Write(QuestClientIdentityProvider.PreferenceKey, "participant-name");
            var provider = new QuestClientIdentityProvider(store);

            var identity = provider.GetOrCreate();

            Assert.That(identity, Does.StartWith("quest-"));
            Assert.That(identity, Does.Not.Contain("participant"));
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        private sealed class MemoryStore : IQuestClientIdentityStore
        {
            private readonly Dictionary<string, string> values =
                new Dictionary<string, string>();

            public int SaveCount { get; private set; }

            public string Read(string key)
            {
                return values.TryGetValue(key, out var value)
                    ? value
                    : string.Empty;
            }

            public void Write(string key, string value)
            {
                values[key] = value;
            }

            public void Save()
            {
                SaveCount++;
            }
        }
    }
}
