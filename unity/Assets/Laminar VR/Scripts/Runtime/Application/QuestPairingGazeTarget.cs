using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [DisallowMultipleComponent]
    public sealed class QuestPairingGazeTarget : MonoBehaviour
    {
        public string Action { get; private set; }

        public void Configure(string action)
        {
            Action = action;
        }
    }
}
