namespace LaminarVR.AdaptiveMeditation.Policy
{
    public interface IFeatureVectorBuilder
    {
        int FeatureCount { get; }

        string FeatureSchemaVersion { get; }

        string GetFeatureName(int index);

        FeatureVector Build(PolicyObservation observation);
    }
}

