using System.Collections.Generic;

namespace AdaptiveAudioVR.RL.Agent
{
    public sealed class AudioRLReplayBuffer
    {
        private readonly int capacity;
        private readonly List<AudioRLTransition> transitions;
        private int nextWriteIndex;

        public int Count => transitions.Count;
        public int Capacity => capacity;

        public AudioRLReplayBuffer(int capacity)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
            transitions = new List<AudioRLTransition>(this.capacity);
        }

        public void Add(AudioRLTransition transition)
        {
            if (transition == null)
            {
                return;
            }

            AudioRLTransition snapshot = transition.Snapshot();
            if (transitions.Count < capacity)
            {
                transitions.Add(snapshot);
                return;
            }

            transitions[nextWriteIndex] = snapshot;
            nextWriteIndex = (nextWriteIndex + 1) % capacity;
        }

        public IReadOnlyList<AudioRLTransition> Snapshot()
        {
            var ordered = new List<AudioRLTransition>(transitions.Count);
            if (transitions.Count < capacity || nextWriteIndex == 0)
            {
                ordered.AddRange(transitions);
                return ordered;
            }

            for (int i = 0; i < transitions.Count; i++)
            {
                int index = (nextWriteIndex + i) % transitions.Count;
                ordered.Add(transitions[index]);
            }

            return ordered;
        }
    }
}
