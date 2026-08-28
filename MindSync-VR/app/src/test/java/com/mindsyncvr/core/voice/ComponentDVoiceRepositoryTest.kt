package com.mindsyncvr.core.voice

import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory

/**
 * Exercises the live repository against a MockWebServer — verifies the wire
 * contract parses, that optional sections stay null, and that Component D's
 * meaningful codes (422 audio-reject, 503 unavailable) map to typed errors.
 * No ML model involved.
 */
class ComponentDVoiceRepositoryTest {

    private lateinit var server: MockWebServer
    private lateinit var repo: ComponentDVoiceRepository

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        val json = Json { ignoreUnknownKeys = true }
        val api = Retrofit.Builder()
            .baseUrl(server.url("/"))
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(ComponentDApi::class.java)
        repo = ComponentDVoiceRepository(api, json)
    }

    @After
    fun tearDown() = server.shutdown()

    private fun enqueue(code: Int, body: String) =
        server.enqueue(MockResponse().setResponseCode(code).setBody(body))

    @Test
    fun `health parses layer availability`() = runTest {
        enqueue(200, """{"status":"ok","layers":{"layer1_quality":true,"layer2_fusion":true,"layer3_compare":true,"layer4_crossmodal":true,"layer5_anomaly":false}}""")
        val h = repo.health().getOrThrow()
        assertEquals("ok", h.status)
        assertTrue(h.layers.fusion)
        assertEquals(false, h.layers.anomaly)
    }

    @Test
    fun `infer 200 maps to VoiceAnalysis`() = runTest {
        enqueue(200, """{"stress_score":6.4,"stress_level":"moderate","stress_type":"activated","confidence":0.71,"valence":-0.34,"arousal":0.12,"gate_mean":0.5,"quality":{"duration_sec":12.0,"rms":0.03,"clip_ratio":0.0,"speech_seconds":9.4,"speech_fraction":0.78,"speech_segments":4},"session_id":"s1"}""")
        val a = repo.analyzeVoice("s1", SessionPhase.Pre, AudioPayload(ByteArray(16))).getOrThrow()
        assertEquals(6.4, a.stressScore, 0.001)
        assertEquals("moderate", a.stressLevel)
        assertEquals(0.78, a.quality?.speechFraction ?: 0.0, 0.001)
        assertNull(a.body)
    }

    @Test
    fun `infer 422 maps to AudioRejected with reasons`() = runTest {
        enqueue(422, """{"detail":{"error":"audio rejected by layer 1","reasons":["too_quiet: rms 0.001","insufficient_speech"]}}""")
        val result = repo.analyzeVoice("s1", SessionPhase.Pre, AudioPayload(ByteArray(16)))
        val error = result.exceptionOrNull()?.voiceError
        assertTrue(error is VoiceError.AudioRejected)
        assertTrue((error as VoiceError.AudioRejected).reasons.any { it.contains("too_quiet") })
    }

    @Test
    fun `full-session with null crossmodal and anomaly stays null`() = runTest {
        enqueue(200, """{"stress_level":3.1,"confidence":0.7,"verdict":{"primary_signal":"change","session_helped":true,"direction":"improved","reliable":true,"note":"n"},"comparison":{"pre_stress":6.4,"post_stress":3.1,"delta":-3.3,"direction":"improved","improved":true,"magnitude":"large","reliable":true,"mean_confidence":0.7},"crossmodal":null,"anomaly":null,"personal_baseline":{"personalised":false,"baseline":null,"deviation":null,"z":null,"relative_band":null,"note":"learning"}}""")
        val r = repo.completeSession("s1").getOrThrow()
        assertNull(r.crossmodal)
        assertNull(r.anomaly)
        assertEquals(-3.3, r.comparison.delta, 0.001)
        assertTrue(r.comparison.improved)
    }

    @Test
    fun `companion 503 maps to BackendUnavailable`() = runTest {
        enqueue(503, """{"detail":"companion unavailable (is Ollama running?)"}""")
        val result = repo.companionMessage("s1", "hi", SessionPhase.Pre)
        assertTrue(result.exceptionOrNull()?.voiceError is VoiceError.BackendUnavailable)
    }

    @Test
    fun `full-session 404 maps to SessionIncomplete`() = runTest {
        enqueue(404, """{"detail":"need both pre and post"}""")
        val result = repo.completeSession("s1")
        assertEquals(VoiceError.SessionIncomplete, result.exceptionOrNull()?.voiceError)
    }

    @Test
    fun `ambient 400 maps to NoAudioCaptured with reasons (BUG-4)`() = runTest {
        enqueue(400, """{"detail":{"error":"no_audio","reasons":["No audio was captured"]}}""")
        val error = repo.ambientCheck(AudioPayload(ByteArray(16))).exceptionOrNull()?.voiceError
        assertTrue(error is VoiceError.NoAudioCaptured)
        assertTrue((error as VoiceError.NoAudioCaptured).reasons.any { it.contains("No audio") })
    }

    @Test
    fun `network drop maps to NetworkUnavailable`() = runTest {
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))
        val error = repo.analyzeVoice("s1", SessionPhase.Pre, AudioPayload(ByteArray(16))).exceptionOrNull()?.voiceError
        assertEquals(VoiceError.NetworkUnavailable, error)
    }

    @Test
    fun `voice-turn parses transcript reply and analysis, and sends correct multipart`() = runTest {
        enqueue(200, """{"transcript":"today was rough","reply":"tell me more","crisis":false,"accepted":true,"reasons":[],"quality":{"duration_sec":12.0,"rms":0.03,"clip_ratio":0.0,"speech_seconds":9.0,"speech_fraction":0.7,"speech_segments":3},"analysis":{"stress_score":6.4,"stress_level":"moderate","confidence":0.71,"valence":-0.34,"arousal":0.1,"session_id":"s1"},"session_id":"s1"}""")
        val r = repo.voiceTurn("s1", SessionPhase.Pre, AudioPayload(ByteArray(16)), isFinal = true, language = "english").getOrThrow()
        assertEquals("today was rough", r.transcript)
        assertEquals("tell me more", r.reply)
        assertFalse(r.crisis)
        assertEquals(6.4, r.analysis?.stressScore ?: 0.0, 0.001)

        val request = server.takeRequest()
        // Query params — including is_final, which the accumulation flow relies on.
        assertTrue(request.path!!.contains("/companion/voice-turn"))
        assertTrue(request.path!!.contains("is_final=true"))
        assertTrue(request.path!!.contains("phase=pre"))
        assertTrue(request.path!!.contains("language=english"))
        // Multipart: field name exactly "file", a .wav filename, audio/wav content type.
        val body = request.body.readUtf8()
        assertTrue(body.contains("""name="file""""))
        assertTrue(body.contains(".wav"))
        assertTrue(body.contains("audio/wav"))
    }

    @Test
    fun `voice-turn crisis flag parses through`() = runTest {
        enqueue(200, """{"transcript":"i want to die","reply":"I'm really glad you told me.","crisis":true,"accepted":true,"reasons":[],"analysis":null,"session_id":"s1"}""")
        val r = repo.voiceTurn("s1", SessionPhase.Pre, AudioPayload(ByteArray(16)), isFinal = false).getOrThrow()
        assertTrue(r.crisis)
        assertNull(r.analysis)
    }
}
