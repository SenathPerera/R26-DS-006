using System.Collections;
using AdaptiveAudioVR.Core;
using UnityEngine;

namespace AdaptiveAudioVR.Audio
{
    public class AudioMixerController : MonoBehaviour
    {
        [Header("Required Sources")]
        [SerializeField] private AudioSource meditationSource = null;
        [SerializeField] private AudioSource ambientSource = null;

        [Header("Optional Filters")]
        [SerializeField] private AudioLowPassFilter meditationLowPass = null;
        [SerializeField] private AudioReverbFilter meditationReverb = null;

        [Header("Tuning")]
        [SerializeField] private bool requireExplicitSessionStart = true;
        [SerializeField] private float smoothingSpeed = 4f;
        [SerializeField] private float minPitch = 0.92f;
        [SerializeField] private float maxPitch = 1.08f;
        [SerializeField] private float minLowPassCutoff = 1200f;
        [SerializeField] private float maxLowPassCutoff = 18000f;
        [SerializeField] private float dryReverbLevel = -7000f;
        [SerializeField] private float wetReverbLevel = -1600f;
        [SerializeField] private float defaultMeditationCrossfadeSeconds = 2.5f;

        public AudioParameters CurrentAppliedParameters { get; private set; }
        public bool IsMuted { get; private set; }
        public bool IsMeditationPlaybackPaused { get; private set; }
        public bool IsSessionPlaybackStarted { get; private set; }
        public bool IsCrossfading => crossfadeRoutine != null;
        public AudioClip CurrentMeditationClip => currentMeditationSource != null ? currentMeditationSource.clip : null;
        public AudioClip CurrentAmbientClip => ambientSource != null ? ambientSource.clip : null;
        public bool IsMeditationPlaying => currentMeditationSource != null && currentMeditationSource.isPlaying;
        public float CurrentMeditationTimeSeconds => currentMeditationSource != null ? currentMeditationSource.time : 0f;
        public float CurrentMeditationClipLengthSeconds => CurrentMeditationClip != null ? CurrentMeditationClip.length : 0f;
        public float CurrentMeditationTimeRemainingSeconds => Mathf.Max(0f, CurrentMeditationClipLengthSeconds - CurrentMeditationTimeSeconds);
        public float CurrentMeditationPlaybackNormalized =>
            CurrentMeditationClipLengthSeconds > 0.01f ? Mathf.Clamp01(CurrentMeditationTimeSeconds / CurrentMeditationClipLengthSeconds) : 0f;

        private AudioParameters targetParameters;
        private AudioSource currentMeditationSource;
        private AudioSource transitionMeditationSource;
        private AudioLowPassFilter currentMeditationLowPass;
        private AudioLowPassFilter transitionMeditationLowPass;
        private AudioReverbFilter currentMeditationReverb;
        private AudioReverbFilter transitionMeditationReverb;
        private Coroutine crossfadeRoutine;
        private float currentMeditationBlend = 1f;
        private float transitionMeditationBlend;
        private float[] meditationScratchA;
        private float[] meditationScratchB;

        private void Awake()
        {
            ResolveRequiredSources();
            InitializeMeditationSources();
        }

        private void Start()
        {
            if (requireExplicitSessionStart)
            {
                HoldSessionPlayback();
                return;
            }

            BeginSessionPlayback();
        }

        public bool BeginSessionPlayback()
        {
            InitializeMeditationSources();
            if (currentMeditationSource == null || currentMeditationSource.clip == null)
            {
                return false;
            }

            currentMeditationSource.loop = true;
            currentMeditationSource.time = 0f;
            currentMeditationSource.Play();

            if (ambientSource != null && ambientSource.clip != null)
            {
                ambientSource.loop = true;
                ambientSource.time = 0f;
                ambientSource.Play();
            }

            IsSessionPlaybackStarted = true;
            IsMeditationPlaybackPaused = false;
            ApplyNow(CurrentAppliedParameters);
            return true;
        }

        public void HoldSessionPlayback()
        {
            InitializeMeditationSources();
            StopAndRewind(currentMeditationSource);
            StopAndRewind(transitionMeditationSource);
            StopAndRewind(ambientSource);
            currentMeditationBlend = 1f;
            transitionMeditationBlend = 0f;
            IsSessionPlaybackStarted = false;
            IsMeditationPlaybackPaused = false;
        }

        private void Update()
        {
            float fadeAwareSmoothing = Mathf.Lerp(smoothingSpeed * 1.75f, smoothingSpeed * 0.45f, targetParameters.fade);
            CurrentAppliedParameters = AudioParameters.Lerp(CurrentAppliedParameters, targetParameters, Time.deltaTime * fadeAwareSmoothing);
            ApplyNow(CurrentAppliedParameters);
        }

        public void SetTargetParameters(AudioParameters parameters)
        {
            targetParameters = parameters.Clamp01();
            targetParameters.NormalizeMix();
        }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
            ApplyNow(CurrentAppliedParameters);
        }

        public void SetMeditationPlaybackPaused(bool paused)
        {
            IsMeditationPlaybackPaused = paused;

            SetSourcePaused(currentMeditationSource, paused);
            SetSourcePaused(transitionMeditationSource, paused);
        }

        public void ReplaceMeditationClip(AudioClip clip, bool restartPlayback = true)
        {
            InitializeMeditationSources();

            if (currentMeditationSource == null || clip == null)
            {
                return;
            }

            if (crossfadeRoutine != null)
            {
                StopCoroutine(crossfadeRoutine);
                crossfadeRoutine = null;
            }

            transitionMeditationBlend = 0f;
            currentMeditationBlend = 1f;

            if (transitionMeditationSource != null)
            {
                transitionMeditationSource.Stop();
                transitionMeditationSource.clip = null;
                transitionMeditationSource.volume = 0f;
            }

            bool wasPlaying = currentMeditationSource.isPlaying;
            currentMeditationSource.Stop();
            currentMeditationSource.clip = clip;
            currentMeditationSource.loop = true;

            if ((restartPlayback || wasPlaying) && IsSessionPlaybackStarted)
            {
                currentMeditationSource.Play();
            }
        }

        public bool CrossfadeToMeditationClip(AudioClip clip, float durationSeconds = -1f, bool restartPlayback = true)
        {
            InitializeMeditationSources();

            if (clip == null || currentMeditationSource == null || transitionMeditationSource == null)
            {
                return false;
            }

            if (currentMeditationSource.clip == clip)
            {
                return false;
            }

            float adaptiveFadeSeconds = Mathf.Lerp(1.25f, 6f, CurrentAppliedParameters.fade);
            float resolvedDuration = durationSeconds > 0f
                ? durationSeconds
                : Mathf.Max(defaultMeditationCrossfadeSeconds, adaptiveFadeSeconds);
            if (resolvedDuration <= 0.01f || currentMeditationSource.clip == null)
            {
                ReplaceMeditationClip(clip, restartPlayback);
                return true;
            }

            if (crossfadeRoutine != null)
            {
                StopCoroutine(crossfadeRoutine);
            }

            crossfadeRoutine = StartCoroutine(CrossfadeMeditationCoroutine(clip, resolvedDuration, restartPlayback));
            return true;
        }

        public void GetMeditationOutputData(float[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return;
            }

            EnsureScratchBuffers(buffer.Length);
            ZeroBuffer(buffer);
            SampleSource(currentMeditationSource, meditationScratchA);
            SampleSource(transitionMeditationSource, meditationScratchB);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = Mathf.Clamp(meditationScratchA[i] + meditationScratchB[i], -1f, 1f);
            }
        }

        private IEnumerator CrossfadeMeditationCoroutine(AudioClip clip, float durationSeconds, bool restartPlayback)
        {
            bool shouldPlay = IsSessionPlaybackStarted && (restartPlayback || currentMeditationSource.isPlaying);

            transitionMeditationSource.Stop();
            transitionMeditationSource.clip = clip;
            transitionMeditationSource.loop = true;
            transitionMeditationSource.time = 0f;
            transitionMeditationBlend = 0f;

            if (shouldPlay)
            {
                transitionMeditationSource.Play();
            }

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / durationSeconds);
                currentMeditationBlend = 1f - t;
                transitionMeditationBlend = t;
                ApplyNow(CurrentAppliedParameters);
                yield return null;
            }

            AudioSource completedSource = currentMeditationSource;
            currentMeditationSource = transitionMeditationSource;
            transitionMeditationSource = completedSource;

            AudioLowPassFilter completedLowPass = currentMeditationLowPass;
            currentMeditationLowPass = transitionMeditationLowPass;
            transitionMeditationLowPass = completedLowPass;

            AudioReverbFilter completedReverb = currentMeditationReverb;
            currentMeditationReverb = transitionMeditationReverb;
            transitionMeditationReverb = completedReverb;

            currentMeditationBlend = 1f;
            transitionMeditationBlend = 0f;

            if (transitionMeditationSource != null)
            {
                transitionMeditationSource.Stop();
                transitionMeditationSource.clip = null;
                transitionMeditationSource.volume = 0f;
            }

            ApplyNow(CurrentAppliedParameters);
            crossfadeRoutine = null;
        }

        private void ApplyNow(AudioParameters parameters)
        {
            float overallGain = Mathf.Lerp(0.45f, 1f, parameters.intensity);
            float meditationVolume = IsMuted ? 0f : Mathf.Clamp01(parameters.musicMix * overallGain);
            float ambientVolume = IsMuted ? 0f : Mathf.Clamp01(parameters.ambientMix * overallGain);
            float meditationPitch = Mathf.Lerp(minPitch, maxPitch, parameters.tempo);
            float ambientPitch = Mathf.Lerp(0.98f, 1.02f, parameters.density);
            float lowPassCutoff = Mathf.Lerp(minLowPassCutoff, maxLowPassCutoff, parameters.brightness);
            float reverbLevel = Mathf.Lerp(dryReverbLevel, wetReverbLevel, Mathf.Clamp01((parameters.intensity + parameters.fade) * 0.5f));

            ApplyMeditationSource(currentMeditationSource, currentMeditationLowPass, currentMeditationReverb, meditationVolume * currentMeditationBlend, meditationPitch, lowPassCutoff, reverbLevel);
            ApplyMeditationSource(transitionMeditationSource, transitionMeditationLowPass, transitionMeditationReverb, meditationVolume * transitionMeditationBlend, meditationPitch, lowPassCutoff, reverbLevel);

            if (ambientSource != null)
            {
                ambientSource.volume = ambientVolume;
                ambientSource.pitch = ambientPitch;
            }
        }

        private static void ApplyMeditationSource(
            AudioSource source,
            AudioLowPassFilter lowPass,
            AudioReverbFilter reverb,
            float volume,
            float pitch,
            float lowPassCutoff,
            float reverbLevel)
        {
            if (source != null)
            {
                source.volume = volume;
                source.pitch = pitch;
            }

            if (lowPass != null)
            {
                lowPass.cutoffFrequency = lowPassCutoff;
            }

            if (reverb != null)
            {
                reverb.enabled = volume > 0.0001f;
                reverb.reverbLevel = reverbLevel;
            }
        }

        private static void SetSourcePaused(AudioSource source, bool paused)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            if (paused)
            {
                if (source.isPlaying)
                {
                    source.Pause();
                }
            }
            else
            {
                source.UnPause();
            }
        }

        private static void StopAndRewind(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            if (source.clip != null)
            {
                source.time = 0f;
            }
        }

        private void InitializeMeditationSources()
        {
            ResolveRequiredSources();

            if (currentMeditationSource != null && transitionMeditationSource != null)
            {
                return;
            }

            if (meditationSource == null)
            {
                return;
            }

            currentMeditationSource = meditationSource;
            currentMeditationLowPass = meditationLowPass;
            currentMeditationReverb = meditationReverb;

            GameObject secondaryObject = new GameObject($"{meditationSource.gameObject.name}_Crossfade", typeof(AudioSource));
            secondaryObject.transform.SetParent(meditationSource.transform.parent, false);
            transitionMeditationSource = secondaryObject.GetComponent<AudioSource>();
            CopySourceSettings(meditationSource, transitionMeditationSource);
            transitionMeditationSource.playOnAwake = false;
            transitionMeditationSource.loop = true;
            transitionMeditationSource.volume = 0f;

            if (meditationLowPass != null)
            {
                transitionMeditationLowPass = secondaryObject.AddComponent<AudioLowPassFilter>();
                transitionMeditationLowPass.enabled = meditationLowPass.enabled;
                transitionMeditationLowPass.cutoffFrequency = meditationLowPass.cutoffFrequency;
                transitionMeditationLowPass.lowpassResonanceQ = meditationLowPass.lowpassResonanceQ;
            }

            if (meditationReverb != null)
            {
                transitionMeditationReverb = secondaryObject.AddComponent<AudioReverbFilter>();
                transitionMeditationReverb.enabled = meditationReverb.enabled;
                transitionMeditationReverb.reverbPreset = AudioReverbPreset.User;
                transitionMeditationReverb.dryLevel = meditationReverb.dryLevel;
                transitionMeditationReverb.room = meditationReverb.room;
                transitionMeditationReverb.roomHF = meditationReverb.roomHF;
                transitionMeditationReverb.roomLF = meditationReverb.roomLF;
                transitionMeditationReverb.decayTime = meditationReverb.decayTime;
                transitionMeditationReverb.decayHFRatio = meditationReverb.decayHFRatio;
                transitionMeditationReverb.reflectionsLevel = meditationReverb.reflectionsLevel;
                transitionMeditationReverb.reflectionsDelay = meditationReverb.reflectionsDelay;
                transitionMeditationReverb.reverbLevel = meditationReverb.reverbLevel;
                transitionMeditationReverb.reverbDelay = meditationReverb.reverbDelay;
                transitionMeditationReverb.hfReference = meditationReverb.hfReference;
                transitionMeditationReverb.lfReference = meditationReverb.lfReference;
                transitionMeditationReverb.diffusion = meditationReverb.diffusion;
                transitionMeditationReverb.density = meditationReverb.density;
            }
        }

        private void ResolveRequiredSources()
        {
            if (meditationSource == null)
            {
                GameObject meditationObject = GameObject.Find("MeditationPlayer");
                if (meditationObject != null)
                {
                    meditationSource = meditationObject.GetComponent<AudioSource>();
                }
            }

            if (ambientSource == null)
            {
                GameObject ambientObject = GameObject.Find("AmbientPlayer");
                if (ambientObject == null)
                {
                    ambientObject = GameObject.Find("AmbientPlayer ");
                }

                if (ambientObject != null)
                {
                    ambientSource = ambientObject.GetComponent<AudioSource>();
                }
            }

            if (meditationLowPass == null && meditationSource != null)
            {
                meditationLowPass = meditationSource.GetComponent<AudioLowPassFilter>();
            }

            if (meditationReverb == null && meditationSource != null)
            {
                meditationReverb = meditationSource.GetComponent<AudioReverbFilter>();
            }
        }

        private static void CopySourceSettings(AudioSource source, AudioSource target)
        {
            target.outputAudioMixerGroup = source.outputAudioMixerGroup;
            target.mute = source.mute;
            target.bypassEffects = source.bypassEffects;
            target.bypassListenerEffects = source.bypassListenerEffects;
            target.bypassReverbZones = source.bypassReverbZones;
            target.playOnAwake = false;
            target.loop = source.loop;
            target.priority = source.priority;
            target.volume = source.volume;
            target.pitch = source.pitch;
            target.panStereo = source.panStereo;
            target.spatialBlend = source.spatialBlend;
            target.reverbZoneMix = source.reverbZoneMix;
            target.dopplerLevel = source.dopplerLevel;
            target.spread = source.spread;
            target.rolloffMode = source.rolloffMode;
            target.minDistance = source.minDistance;
            target.maxDistance = source.maxDistance;
            target.ignoreListenerPause = source.ignoreListenerPause;
            target.ignoreListenerVolume = source.ignoreListenerVolume;
            target.velocityUpdateMode = source.velocityUpdateMode;
        }

        private void EnsureScratchBuffers(int length)
        {
            if (meditationScratchA == null || meditationScratchA.Length != length)
            {
                meditationScratchA = new float[length];
            }

            if (meditationScratchB == null || meditationScratchB.Length != length)
            {
                meditationScratchB = new float[length];
            }
        }

        private static void SampleSource(AudioSource source, float[] buffer)
        {
            ZeroBuffer(buffer);
            if (source == null)
            {
                return;
            }

            source.GetOutputData(buffer, 0);
        }

        private static void ZeroBuffer(float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0f;
            }
        }
    }
}
