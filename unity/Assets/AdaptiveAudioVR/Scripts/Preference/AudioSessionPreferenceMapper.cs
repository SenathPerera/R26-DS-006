using System;
using AdaptiveAudioVR.Core;
using LaminarVR.AdaptiveMeditation.Networking;
using UnityEngine;

namespace AdaptiveAudioVR.Preference
{
    public sealed class AudioSessionPreferences
    {
        public string SessionId;
        public string ParticipantPseudonym;
        public string PreferenceMode;
        public UserPreferences LongTerm;
        public UserPreferences Effective;
    }

    public static class AudioSessionPreferenceMapper
    {
        public static bool TryMap(SessionRelayConfigurationMessage configuration,
            out AudioSessionPreferences preferences, out string error)
        {
            preferences = null;
            error = "audio-preferences-missing";
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.RawJson)) return false;

            EnvelopeDto envelope;
            try { envelope = JsonUtility.FromJson<EnvelopeDto>(configuration.RawJson); }
            catch (ArgumentException) { error = "audio-preferences-json-invalid"; return false; }
            var payload = envelope?.payload;
            var bundle = payload?.audioPreferences;
            if (bundle == null) return false;
            if (!string.Equals(payload.sessionId, configuration.SessionId, StringComparison.Ordinal)
                || !string.Equals(payload.participantPseudonym, configuration.ParticipantPseudonym, StringComparison.Ordinal)
                || !string.Equals(payload.sceneId, configuration.SceneId, StringComparison.Ordinal))
            {
                error = "audio-preferences-session-mismatch";
                return false;
            }
            if (string.IsNullOrWhiteSpace(configuration.ParticipantPseudonym)
                || configuration.ParticipantPseudonym == "U001"
                || configuration.ParticipantPseudonym == "FallbackUser"
                || configuration.ParticipantPseudonym == "anonymous"
                || configuration.ParticipantPseudonym == "participant-demo")
            {
                error = "audio-participant-pseudonym-invalid";
                return false;
            }
            if (bundle.schemaVersion != "mindsync-audio-preferences-v1"
                || (bundle.preferenceMode != "usual" && bundle.preferenceMode != "adjust")
                || !TryReadSessionOverridePresence(configuration.RawJson, out bool hasSessionOverride)
                || (bundle.preferenceMode == "adjust") != hasSessionOverride)
            {
                error = "audio-preference-mode-invalid";
                return false;
            }
            if (configuration.SceneId != "temple-pond")
            {
                error = "audio-scene-unsupported";
                return false;
            }
            if (!TryMapProfile(bundle.longTerm, configuration.ParticipantPseudonym, out var longTerm))
            {
                error = "audio-preference-profile-invalid";
                return false;
            }

            UserPreferences effective = longTerm;
            if (bundle.preferenceMode == "adjust"
                && !TryMapProfile(
                    bundle.sessionOverride,
                    configuration.ParticipantPseudonym,
                    out effective))
            {
                error = "audio-preference-profile-invalid";
                return false;
            }

            preferences = new AudioSessionPreferences {
                SessionId = configuration.SessionId,
                ParticipantPseudonym = configuration.ParticipantPseudonym,
                PreferenceMode = bundle.preferenceMode,
                LongTerm = longTerm,
                Effective = effective
            };
            error = string.Empty;
            return true;
        }

        private static bool TryReadSessionOverridePresence(string json, out bool hasSessionOverride)
        {
            hasSessionOverride = false;
            const string property = "\"sessionOverride\"";
            int index = json.IndexOf(property, StringComparison.Ordinal);
            if (index < 0) return false;

            index += property.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            if (index >= json.Length || json[index] != ':') return false;
            index++;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            if (index >= json.Length) return false;

            // Validate the wire value so the mode rule follows the JSON contract.
            if (json[index] == '{')
            {
                hasSessionOverride = true;
                return true;
            }

            const string nullToken = "null";
            int afterNull = index + nullToken.Length;
            return afterNull <= json.Length
                && string.Compare(json, index, nullToken, 0, nullToken.Length,
                    StringComparison.Ordinal) == 0
                && (afterNull == json.Length || char.IsWhiteSpace(json[afterNull])
                    || json[afterNull] == ',' || json[afterNull] == '}');
        }

        private static bool TryMapProfile(ProfileDto source, string participant,
            out UserPreferences profile)
        {
            profile = null;
            if (source == null || source.preferredInstruments == null
                || source.preferredInstruments.Length == 0 || source.preferredInstruments.Length > 6
                || !OneOf(source.mood, "calm", "focused", "sleepy", "energized")
                || !OneOf(source.tempoPreference, "slow", "medium", "fast")
                || !OneOf(source.ambientMusicBalance, "mostly_ambience", "balanced", "mostly_music")
                || !OneOf(source.brightnessPreference, "soft_dark", "neutral", "bright_clear")
                || !OneOf(source.rhythmPreference, "minimal", "gentle_pulse", "more_motion")
                || !OneOf(source.natureSoundPreference, "none", "low", "medium", "high")
                || !OneOf(source.reverbPreference, "dry", "balanced", "spacious")
                || !OneOf(source.volumePreference, "low", "medium", "high")
                || !Normalized(source.audioIntensity) || !Normalized(source.noveltyTolerance)) return false;

            for (var i = 0; i < source.preferredInstruments.Length; i++)
            {
                if (!OneOf(source.preferredInstruments[i], "piano", "flute", "pad", "strings", "bells", "drone")) return false;
                for (var j = 0; j < i; j++)
                    if (source.preferredInstruments[j] == source.preferredInstruments[i]) return false;
            }

            profile = new UserPreferences {
                userId = participant,
                preferredInstruments = (string[])source.preferredInstruments.Clone(),
                mood = source.mood,
                tempoPreference = source.tempoPreference,
                preferredAmbience = "temple",
                ambientMusicBalance = source.ambientMusicBalance,
                brightnessPreference = source.brightnessPreference,
                rhythmPreference = source.rhythmPreference,
                natureSoundPreference = source.natureSoundPreference,
                reverbPreference = source.reverbPreference,
                volumePreference = source.volumePreference,
                audioIntensity = source.audioIntensity,
                noveltyTolerance = source.noveltyTolerance,
                avoidDissonance = source.avoidDissonance
            };
            return true;
        }

        private static bool Normalized(float value) => !float.IsNaN(value)
            && !float.IsInfinity(value) && value >= 0f && value <= 1f;

        private static bool OneOf(string value, params string[] choices)
        {
            foreach (var choice in choices) if (value == choice) return true;
            return false;
        }

#pragma warning disable 0649
        [Serializable] private sealed class EnvelopeDto { public PayloadDto payload; }
        [Serializable] private sealed class PayloadDto
        {
            public string sessionId;
            public string participantPseudonym;
            public string sceneId;
            public BundleDto audioPreferences;
        }
        [Serializable] private sealed class BundleDto
        {
            public string schemaVersion;
            public ProfileDto longTerm;
            public string preferenceMode;
            public ProfileDto sessionOverride;
        }
        [Serializable] private sealed class ProfileDto
        {
            public string[] preferredInstruments;
            public string mood;
            public string tempoPreference;
            public float audioIntensity;
            public string ambientMusicBalance;
            public string brightnessPreference;
            public float noveltyTolerance;
            public bool avoidDissonance;
            public string rhythmPreference;
            public string natureSoundPreference;
            public string reverbPreference;
            public string volumePreference;
        }
#pragma warning restore 0649
    }
}
