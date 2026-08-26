package com.mindsyncvr

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.mindsyncvr.core.bluetooth.WearableHealthBleController
import com.mindsyncvr.core.data.MindSyncRepository
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.core.model.OnboardingProfile
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class MindSyncViewModel(application: Application) : AndroidViewModel(application), MindSyncActions {
    private val repository = MindSyncRepository(
        ble = WearableHealthBleController(application)
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
}
