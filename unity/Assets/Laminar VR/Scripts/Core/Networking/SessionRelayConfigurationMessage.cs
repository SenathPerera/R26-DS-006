using System;
using System.Globalization;
using LaminarVR.AdaptiveMeditation.Environment;
using LaminarVR.AdaptiveMeditation.Policy;

namespace LaminarVR.AdaptiveMeditation.Networking
{
    public sealed class SessionRelayConfigurationMessage
    {
        public SessionRelayConfigurationMessage(
            string schemaVersion,
            string messageId,
            string sessionId,
            string participantPseudonym,
            string sceneId,
            StudyPolicyMode studyPolicyMode,
            EnvironmentState preferredEnvironment,
            SessionContextSnapshot sessionContext)
        {
            SchemaVersion = RequireIdentifier(
                schemaVersion,
                nameof(schemaVersion));
            MessageId = RequireIdentifier(messageId, nameof(messageId));
            SessionId = RequireIdentifier(sessionId, nameof(sessionId));
            ParticipantPseudonym = RequireIdentifier(
                participantPseudonym,
                nameof(participantPseudonym));
            SceneId = RequireIdentifier(sceneId, nameof(sceneId));
            if (!Enum.IsDefined(typeof(StudyPolicyMode), studyPolicyMode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(studyPolicyMode));
            }

            StudyPolicyMode = studyPolicyMode;
            if (!preferredEnvironment.IsNormalized)
            {
                throw new ArgumentException(
                    "Relay preferences must use normalized [0, 1] values.",
                    nameof(preferredEnvironment));
            }

            PreferredEnvironment = preferredEnvironment;
            SessionContext = sessionContext
                ?? throw new ArgumentNullException(nameof(sessionContext));
        }

        public string SchemaVersion { get; }

        public string MessageId { get; }

        public string SessionId { get; }

        public string ParticipantPseudonym { get; }

        public string SceneId { get; }

        public StudyPolicyMode StudyPolicyMode { get; }

        public string StudyCondition =>
            StudyConditionCodec.ToWireValue(StudyPolicyMode);

        public EnvironmentState PreferredEnvironment { get; }

        public SessionContextSnapshot SessionContext { get; }

        private static string RequireIdentifier(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty relay identifier is required.",
                    parameterName);
            }

            return value.Trim();
        }
    }

    public sealed class SessionContextSnapshot
        : IEquatable<SessionContextSnapshot>
    {
        public const string SupportedSchemaVersion =
            "mindsync-session-context-v1";

        public SessionContextSnapshot(
            string schemaVersion,
            string collectedAt,
            double subjectiveStress,
            double moodValence,
            double fatigue,
            double sleepQuality,
            bool headacheOrEyeStrainToday,
            int timeOfDayMinutes,
            int sessionSequenceNumber,
            int? daysSincePreviousSession)
        {
            if (!string.Equals(
                    schemaVersion,
                    SupportedSchemaVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The session-context schema version is unsupported.",
                    nameof(schemaVersion));
            }

            var normalizedCollectedAt = collectedAt?.Trim();
            var hasExplicitOffset = !string.IsNullOrEmpty(normalizedCollectedAt)
                && (normalizedCollectedAt.EndsWith(
                        "Z",
                        StringComparison.OrdinalIgnoreCase)
                    || (normalizedCollectedAt.Length >= 6
                        && (normalizedCollectedAt[
                                normalizedCollectedAt.Length - 6] == '+'
                            || normalizedCollectedAt[
                                normalizedCollectedAt.Length - 6] == '-')
                        && normalizedCollectedAt[
                            normalizedCollectedAt.Length - 3] == ':'));
            if (!hasExplicitOffset
                || !DateTimeOffset.TryParse(
                    normalizedCollectedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                throw new ArgumentException(
                    "Session context requires an ISO-8601 collection timestamp.",
                    nameof(collectedAt));
            }

            ValidateRange(subjectiveStress, 0d, 10d, nameof(subjectiveStress));
            ValidateRange(moodValence, -1d, 1d, nameof(moodValence));
            ValidateRange(fatigue, 0d, 1d, nameof(fatigue));
            ValidateRange(sleepQuality, 0d, 1d, nameof(sleepQuality));
            if (timeOfDayMinutes < 0 || timeOfDayMinutes > 1439)
            {
                throw new ArgumentOutOfRangeException(nameof(timeOfDayMinutes));
            }

            if (sessionSequenceNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionSequenceNumber));
            }

            if (daysSincePreviousSession.HasValue
                && daysSincePreviousSession.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(daysSincePreviousSession));
            }

            SchemaVersion = SupportedSchemaVersion;
            CollectedAt = normalizedCollectedAt;
            SubjectiveStress = subjectiveStress;
            MoodValence = moodValence;
            Fatigue = fatigue;
            SleepQuality = sleepQuality;
            HeadacheOrEyeStrainToday = headacheOrEyeStrainToday;
            TimeOfDayMinutes = timeOfDayMinutes;
            SessionSequenceNumber = sessionSequenceNumber;
            DaysSincePreviousSession = daysSincePreviousSession;
        }

        public string SchemaVersion { get; }
        public string CollectedAt { get; }
        public double SubjectiveStress { get; }
        public double MoodValence { get; }
        public double Fatigue { get; }
        public double SleepQuality { get; }
        public bool HeadacheOrEyeStrainToday { get; }
        public int TimeOfDayMinutes { get; }
        public int SessionSequenceNumber { get; }
        public int? DaysSincePreviousSession { get; }

        public bool Equals(SessionContextSnapshot other)
        {
            return other != null
                && string.Equals(
                    SchemaVersion,
                    other.SchemaVersion,
                    StringComparison.Ordinal)
                && string.Equals(
                    CollectedAt,
                    other.CollectedAt,
                    StringComparison.Ordinal)
                && SubjectiveStress.Equals(other.SubjectiveStress)
                && MoodValence.Equals(other.MoodValence)
                && Fatigue.Equals(other.Fatigue)
                && SleepQuality.Equals(other.SleepQuality)
                && HeadacheOrEyeStrainToday == other.HeadacheOrEyeStrainToday
                && TimeOfDayMinutes == other.TimeOfDayMinutes
                && SessionSequenceNumber == other.SessionSequenceNumber
                && DaysSincePreviousSession == other.DaysSincePreviousSession;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SessionContextSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.Ordinal.GetHashCode(
                    SchemaVersion);
                hashCode = (hashCode * 397)
                    ^ StringComparer.Ordinal.GetHashCode(CollectedAt);
                hashCode = (hashCode * 397) ^ SubjectiveStress.GetHashCode();
                hashCode = (hashCode * 397) ^ MoodValence.GetHashCode();
                hashCode = (hashCode * 397) ^ Fatigue.GetHashCode();
                hashCode = (hashCode * 397) ^ SleepQuality.GetHashCode();
                hashCode = (hashCode * 397)
                    ^ HeadacheOrEyeStrainToday.GetHashCode();
                hashCode = (hashCode * 397) ^ TimeOfDayMinutes;
                hashCode = (hashCode * 397) ^ SessionSequenceNumber;
                hashCode = (hashCode * 397)
                    ^ DaysSincePreviousSession.GetHashCode();
                return hashCode;
            }
        }

        private static void ValidateRange(
            double value,
            double minimum,
            double maximum,
            string parameterName)
        {
            if (double.IsNaN(value)
                || double.IsInfinity(value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
