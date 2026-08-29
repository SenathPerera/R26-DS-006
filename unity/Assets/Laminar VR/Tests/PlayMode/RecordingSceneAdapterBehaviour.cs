using System.Collections.Generic;
using LaminarVR.AdaptiveMeditation.Environment;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Tests.PlayMode
{
    public sealed class RecordingSceneAdapterBehaviour
        : MonoBehaviour, ISceneEnvironmentAdapter
    {
        private readonly List<EnvironmentState> appliedStates =
            new List<EnvironmentState>();

        public int ApplyCount => appliedStates.Count;

        public EnvironmentState LastAppliedState =>
            appliedStates[appliedStates.Count - 1];

        public string SceneId => "playmode-recording-scene";

        public SceneBindingValidation ValidateBindings()
        {
            return SceneBindingValidation.Succeeded();
        }

        public void ApplyState(EnvironmentState state)
        {
            appliedStates.Add(state);
        }
    }
}
