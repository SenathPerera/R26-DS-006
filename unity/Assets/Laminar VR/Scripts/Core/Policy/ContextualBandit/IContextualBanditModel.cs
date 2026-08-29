using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public interface IContextualBanditModel
    {
        string ModelVersion { get; }

        string FeatureSchemaVersion { get; }

        int FeatureCount { get; }

        long TotalUpdateCount { get; }

        ContextualBanditSelection Select(
            FeatureVector featureVector,
            IReadOnlyList<ContextualBanditCandidate> candidates);

        void Update(
            EnvironmentAction executedAction,
            FeatureVector featureVector,
            double reward);

        void Reset();
    }
}
