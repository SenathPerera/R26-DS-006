using System;

namespace AdaptiveAudioVR.Core
{
    // Sequence positions belong to the PCM queue, so dropped data cannot count as playback.
    public sealed class AudioPlaybackMarker
    {
        public const string Protocol = "mindsync-audio-playback-v1";
        public string RequestId { get; private set; }
        public bool Rendered { get; private set; }
        public bool Lost { get; private set; }
        private long boundary = -1;
        private long guardSamples;

        public void Begin(string requestId, long guard)
        {
            if (string.IsNullOrWhiteSpace(requestId) || guard < 0) throw new ArgumentException("Invalid playback marker.");
            RequestId = requestId;
            guardSamples = guard;
            boundary = -1;
            Rendered = Lost = false;
        }

        public void Enqueued(string requestId, long firstSample)
        {
            if (!Lost && boundary < 0 && RequestId != null && RequestId == requestId)
                boundary = firstSample + guardSamples;
        }

        public void Read(long firstSample, int count)
        {
            if (!Lost && boundary >= firstSample && boundary < firstSample + count) Rendered = true;
        }

        public void Dropped(long firstSample, int count)
        {
            if (!Rendered && boundary >= firstSample && boundary < firstSample + count) Lost = true;
        }

        public void Invalidate() { if (RequestId != null) Lost = true; }
    }

    public static class AudioMusicalMapping
    {
        public const string Schema = "audio-musical-controls-v2";
        public static int Bpm(float tempo, int offset = 0) =>
            Math.Max(60, Math.Min(200, (int)Math.Round(60 + Math.Max(0, Math.Min(1, tempo)) * 52) + offset));

        public static bool RequiresGeneration(AudioParameters before, AudioParameters after) =>
            Math.Abs(before.density - after.density) > 0.00001f || Math.Abs(before.tempo - after.tempo) > 0.00001f;
    }
}
