package com.mindsyncvr

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.mindsyncvr.core.bluetooth.WearableHealthBleController
import com.mindsyncvr.core.data.MindSyncRepository
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.OnboardingProfile
import com.mindsyncvr.core.model.VoiceStage
import com.mindsyncvr.core.voice.AudioPayload
import com.mindsyncvr.core.voice.ComponentDConfig
import com.mindsyncvr.core.voice.ComponentDVoiceRepository
import com.mindsyncvr.core.voice.SessionPhase
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class MindSyncViewModel(application: Application) : AndroidViewModel(application), MindSyncActions {
    private val repository = MindSyncRepository(
        ble = WearableHealthBleController(application),
        // Real Component D client (voice stress). Base URL comes from BuildConfig:
        // the debug dev host, or the HTTPS production host in release.
        voice = ComponentDVoiceRepository.create(
            ComponentDConfig(baseUrl = BuildConfig.COMPONENT_D_BASE_URL, useMock = false)
        )
    )
    val state: StateFlow<AppState> = repository.state

    override fun login(email: String, password: String) {
        viewModelScope.launch { repository.login(email, password) }
    }

    override fun register(name: String, email: String, password: String) {
        viewModelScope.launch { repository.register(name, email, password) }
    }

    override fun updateOnboarding(profile: OnboardingProfile) {
        repository.updateOnboarding(profile)
    }

    override fun completeOnboarding() {
        repository.completeOnboarding()
    }

    override fun scanWearables() {
        viewModelScope.launch { repository.scanWearables() }
    }

    override fun connectWearable(id: String) {
        viewModelScope.launch { repository.connectWearable(id) }
    }

    override fun disconnectWearable() {
        viewModelScope.launch { repository.disconnectWearable() }
    }

    override fun pairVr() {
        viewModelScope.launch { repository.pairVr() }
    }

    override fun createSession(): String {
        return repository.createSession().id
    }

    override fun startLiveSession(sessionId: String) {
        repository.startLiveSession(sessionId)
    }

    override fun submitQuestionnaire(templateId: String, sessionId: String?, answers: Map<String, String>) {
        viewModelScope.launch { repository.submitQuestionnaire(templateId, sessionId, answers) }
    }

    override fun startVoiceCheckIn() = repository.startVoiceCheckIn()

    override fun beginEnvironmentCheck(name: String, language: String) = repository.beginEnvironmentCheck(name, language)

    override fun submitAmbientClip(audio: AudioPayload?) = repository.submitAmbientClip(audio)

    override fun continueFromEnvironment() = repository.continueFromEnvironment()

    override fun submitVoiceCapture(phase: SessionPhase, audio: AudioPayload?, pcm: ByteArray?, speechSec: Int) =
        repository.submitVoiceCapture(phase, audio, pcm, speechSec)

    override fun sendCompanionMessage(phase: SessionPhase, text: String) =
        repository.sendCompanionMessage(phase, text)

    override fun advanceVoiceStage(stage: VoiceStage) = repository.advanceVoiceStage(stage)

    override fun completeVoiceCheckIn() = repository.completeVoiceCheckIn()

    override fun setDebugMockHrv(enabled: Boolean) = repository.setDebugMockHrv(enabled)

    override fun endVoiceCheckIn() = repository.endVoiceCheckIn()
}

interface MindSyncActions {
    fun login(email: String, password: String)
    fun register(name: String, email: String, password: String)
    fun updateOnboarding(profile: OnboardingProfile)
    fun completeOnboarding()
    fun scanWearables()
    fun connectWearable(id: String)
    fun disconnectWearable()
    fun pairVr()
    fun createSession(): String
    fun startLiveSession(sessionId: String)
    fun submitQuestionnaire(templateId: String, sessionId: String?, answers: Map<String, String>)
    fun startVoiceCheckIn()
    fun beginEnvironmentCheck(name: String, language: String)
    fun submitAmbientClip(audio: AudioPayload?)
    fun continueFromEnvironment()
    fun submitVoiceCapture(phase: SessionPhase, audio: AudioPayload?, pcm: ByteArray?, speechSec: Int)
    fun sendCompanionMessage(phase: SessionPhase, text: String)
    fun advanceVoiceStage(stage: VoiceStage)
    fun completeVoiceCheckIn()
    fun setDebugMockHrv(enabled: Boolean)
    fun endVoiceCheckIn()
}
