package com.mindsyncvr.core.unity

import android.content.Context
import android.view.View
import android.widget.FrameLayout
import com.mindsyncvr.core.model.LiveSessionState
import com.mindsyncvr.core.model.OnboardingProfile
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow

sealed interface UnityEvent {
    data class Ready(val headsetId: String) : UnityEvent
    data class SessionCompleted(val sessionId: String) : UnityEvent
    data class DiscomfortReported(val severity: String) : UnityEvent
    data class EnvironmentChanged(val environment: String) : UnityEvent
}

interface UnityBridge {
    val events: SharedFlow<UnityEvent>
    fun createUnityView(context: Context): View
    fun attachSession(sessionId: String, profile: OnboardingProfile)
    fun sendLiveState(state: LiveSessionState)
    fun pause()
    fun resume()
    fun stop()
}

class MockUnityBridge : UnityBridge {
    private val mutableEvents = MutableSharedFlow<UnityEvent>(extraBufferCapacity = 8)
    override val events: SharedFlow<UnityEvent> = mutableEvents

    override fun createUnityView(context: Context): View {
        return FrameLayout(context).apply {
            contentDescription = "Unity VR surface placeholder"
            setBackgroundColor(android.graphics.Color.rgb(7, 17, 31))
        }
    }

    override fun attachSession(sessionId: String, profile: OnboardingProfile) {
        mutableEvents.tryEmit(UnityEvent.Ready("mock-unity-headset"))
    }

    override fun sendLiveState(state: LiveSessionState) = Unit
    override fun pause() = Unit
    override fun resume() = Unit
    override fun stop() = Unit
}
