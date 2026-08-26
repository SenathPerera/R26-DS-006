package com.mindsyncvr.core.realtime

import com.mindsyncvr.core.data.MockData
import com.mindsyncvr.core.model.LiveSessionState
import com.mindsyncvr.core.model.SessionStatus
import com.mindsyncvr.core.model.StressBand
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow

interface RealtimeSessionClient {
    fun subscribe(sessionId: String): Flow<LiveSessionState>
}

class MockRealtimeSessionClient : RealtimeSessionClient {
    override fun subscribe(sessionId: String): Flow<LiveSessionState> = flow {
        var elapsed = 0
        while (true) {
            delay(1500)
            elapsed += 5
            val stress = (38 - elapsed / 15).coerceAtLeast(18)
            emit(
                LiveSessionState(
                    sessionId = sessionId,
                    elapsedSeconds = elapsed,
                    status = SessionStatus.Active,
                    wearableConnected = true,
                    vrConnected = true,
                    research = MockData.researchState.copy(
                        stressLevel = stress,
                        stressBand = when {
                            stress < 25 -> StressBand.Restorative
                            stress < 45 -> StressBand.Balanced
                            stress < 70 -> StressBand.Elevated
                            else -> StressBand.High
                        },
                        stressSummary = if (stress < 25) "Settling into a restorative zone" else "Balanced with mild activation",
                        vrAdaptationState = if (stress < 25) "Maintaining soft flow" else "Reducing scene intensity"
                    )
                )
            )
        }
    }
}
