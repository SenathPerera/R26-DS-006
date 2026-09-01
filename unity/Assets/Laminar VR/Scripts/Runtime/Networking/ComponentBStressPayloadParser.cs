using System;
using LaminarVR.AdaptiveMeditation.Physiology;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public enum ComponentBStressPayloadParseReasonCode
    {
        Accepted,
        PayloadEmpty,
        JsonMalformed,
        RequiredFieldMissing,
        StressBlockMissing,
        ProbabilityBlockMissing,
        StressModeUnsupported
    }

    public sealed class ComponentBStressPayloadParser
    {
        private static readonly string[] RequiredRootProperties =
        {
            "timestamp",
            "heartRate",
            "rmssd",
            "sdnn",
            "signalQuality",
            "windowStart",
            "windowEnd"
        };

        private static readonly string[] RequiredStressProperties =
        {
            "mode",
            "label",
            "confidence",
            "continuous_score"
        };

        private static readonly string[] RequiredProbabilityProperties =
        {
            "relaxed",
            "mild",
            "moderate",
            "high"
        };

        public bool TryParse(
            string json,
            out PhysiologyWindow window,
            out ComponentBStressPayloadParseReasonCode reasonCode)
        {
            window = null;
            reasonCode = ComponentBStressPayloadParseReasonCode.Accepted;

            if (string.IsNullOrWhiteSpace(json))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.PayloadEmpty;
                return false;
            }

            ComponentBStressPredictionDto prediction;
            try
            {
                prediction =
                    JsonUtility.FromJson<ComponentBStressPredictionDto>(json);
            }
            catch (ArgumentException)
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.JsonMalformed;
                return false;
            }

            if (prediction == null)
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.JsonMalformed;
                return false;
            }

            if (!HasNonNullProperties(json, RequiredRootProperties))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!HasNonNullProperty(json, "stress")
                || prediction.stress == null)
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.StressBlockMissing;
                return false;
            }

            if (!HasNonNullProperties(json, RequiredStressProperties))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!HasNonNullProperty(json, "probabilities")
                || prediction.stress.probabilities == null)
            {
                reasonCode = ComponentBStressPayloadParseReasonCode
                    .ProbabilityBlockMissing;
                return false;
            }

            if (!HasNonNullProperties(json, RequiredProbabilityProperties))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing;
                return false;
            }

            if (!TryMapStressMode(
                    prediction.stress,
                    out var mode,
                    out var pointLevel,
                    out var bandLowLevel,
                    out var bandHighLevel))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.StressModeUnsupported;
                return false;
            }

            if ((mode == StressDecisionMode.Point
                    && !HasNonNullProperty(json, "level"))
                || (mode == StressDecisionMode.Band
                    && (!HasNonNullProperty(json, "level_low")
                        || !HasNonNullProperty(json, "level_high"))))
            {
                reasonCode =
                    ComponentBStressPayloadParseReasonCode.RequiredFieldMissing;
                return false;
            }

            var adjacent = HasNonNullProperty(json, "adjacent")
                ? prediction.stress.adjacent
                : (bool?)null;
            var probabilities = new StressProbabilityVector(
                prediction.stress.probabilities.relaxed,
                prediction.stress.probabilities.mild,
                prediction.stress.probabilities.moderate,
                prediction.stress.probabilities.high);
            var stress = new StressDecision(
                mode,
                pointLevel,
                bandLowLevel,
                bandHighLevel,
                prediction.stress.label,
                prediction.stress.confidence,
                adjacent,
                probabilities,
                prediction.stress.continuous_score);

            window = new PhysiologyWindow(
                prediction.timestamp,
                prediction.windowStart,
                prediction.windowEnd,
                prediction.heartRate,
                prediction.rmssd,
                prediction.sdnn,
                stress,
                prediction.signalQuality);
            return true;
        }

        private static bool TryMapStressMode(
            ComponentBStressBlockDto stress,
            out StressDecisionMode mode,
            out int? pointLevel,
            out int? bandLowLevel,
            out int? bandHighLevel)
        {
            mode = default;
            pointLevel = null;
            bandLowLevel = null;
            bandHighLevel = null;

            switch (stress.mode)
            {
                case "point":
                    mode = StressDecisionMode.Point;
                    pointLevel = stress.level;
                    return true;
                case "band":
                    mode = StressDecisionMode.Band;
                    bandLowLevel = stress.level_low;
                    bandHighLevel = stress.level_high;
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasNonNullProperties(
            string json,
            string[] propertyNames)
        {
            for (var index = 0; index < propertyNames.Length; index++)
            {
                if (!HasNonNullProperty(json, propertyNames[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasNonNullProperty(
            string json,
            string propertyName)
        {
            var token = "\"" + propertyName + "\"";
            var searchStartIndex = 0;
            while (searchStartIndex < json.Length)
            {
                var propertyIndex = json.IndexOf(
                    token,
                    searchStartIndex,
                    StringComparison.Ordinal);
                if (propertyIndex < 0)
                {
                    return false;
                }

                var valueIndex = propertyIndex + token.Length;
                while (valueIndex < json.Length
                    && char.IsWhiteSpace(json[valueIndex]))
                {
                    valueIndex++;
                }

                if (valueIndex < json.Length && json[valueIndex] == ':')
                {
                    valueIndex++;
                    while (valueIndex < json.Length
                        && char.IsWhiteSpace(json[valueIndex]))
                    {
                        valueIndex++;
                    }

                    return valueIndex < json.Length
                        && !StartsWithJsonNull(json, valueIndex);
                }

                searchStartIndex = propertyIndex + token.Length;
            }

            return false;
        }

        private static bool StartsWithJsonNull(string json, int valueIndex)
        {
            const string NullToken = "null";
            return valueIndex + NullToken.Length <= json.Length
                && string.Compare(
                    json,
                    valueIndex,
                    NullToken,
                    0,
                    NullToken.Length,
                    StringComparison.Ordinal) == 0;
        }

#pragma warning disable 0649
        [Serializable]
        private sealed class ComponentBStressPredictionDto
        {
            public double timestamp;
            public double heartRate;
            public double rmssd;
            public double sdnn;
            public ComponentBStressBlockDto stress;
            public double signalQuality;
            public double windowStart;
            public double windowEnd;
        }

        [Serializable]
        private sealed class ComponentBStressBlockDto
        {
            public string mode;
            public int level;
            public int level_low;
            public int level_high;
            public string label;
            public double confidence;
            public bool adjacent;
            public ComponentBStressProbabilitiesDto probabilities;
            public double continuous_score;
        }

        [Serializable]
        private sealed class ComponentBStressProbabilitiesDto
        {
            public double relaxed;
            public double mild;
            public double moderate;
            public double high;
        }
#pragma warning restore 0649
    }
}
