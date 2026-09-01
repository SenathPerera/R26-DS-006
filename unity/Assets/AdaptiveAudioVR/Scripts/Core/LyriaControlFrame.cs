using System;
using System.Text;
using UnityEngine;

namespace AdaptiveAudioVR.Core
{
    [Serializable]
    public class LyriaControlFrame
    {
        public string strategyName;
        public string actionName;
        [TextArea(3, 8)] public string promptSummary;
        public float latestReward;
        public PromptWeight[] weightedPrompts;
        public LyriaGenerationConfig config;

        public void Normalize()
        {
            strategyName = string.IsNullOrWhiteSpace(strategyName) ? "Neutral Baseline" : strategyName.Trim();
            actionName = string.IsNullOrWhiteSpace(actionName) ? "Stabilize" : actionName.Trim();
            promptSummary = string.IsNullOrWhiteSpace(promptSummary) ? "Instrumental meditation." : promptSummary.Trim();

            if (weightedPrompts == null || weightedPrompts.Length == 0)
            {
                weightedPrompts = new[] { new PromptWeight("instrumental meditation", 1f) };
            }

            for (int i = 0; i < weightedPrompts.Length; i++)
            {
                weightedPrompts[i] = weightedPrompts[i].Normalize();
            }

            config = config.Normalize();
        }

        public string ToDisplayString()
        {
            var builder = new StringBuilder();
            builder.Append(strategyName);
            builder.Append(" | ");
            builder.Append(actionName);
            builder.Append(" | ");
            builder.Append(config.ToDisplayString());
            return builder.ToString();
        }
    }
}
