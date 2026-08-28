package com.mindsyncvr.core.network

import kotlinx.coroutines.delay

data class ApiConfig(
    val baseUrl: String = "https://api.mindsync.local/v1",
    val realtimeUrl: String = "wss://realtime.mindsync.local/session",
    val useMocks: Boolean = true
)

interface ApiClient {
    suspend fun post(path: String, body: Any): Result<Unit>
    suspend fun get(path: String): Result<String>
}

class MockApiClient : ApiClient {
    override suspend fun post(path: String, body: Any): Result<Unit> {
        delay(180)
        return Result.success(Unit)
    }

    override suspend fun get(path: String): Result<String> {
        delay(180)
        return Result.success("""{"path":"$path","mock":true}""")
    }
}
