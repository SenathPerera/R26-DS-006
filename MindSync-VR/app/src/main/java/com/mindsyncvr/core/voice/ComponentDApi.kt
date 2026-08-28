package com.mindsyncvr.core.voice

import com.mindsyncvr.core.voice.dto.AmbientDto
import com.mindsyncvr.core.voice.dto.CompanionReplyDto
import com.mindsyncvr.core.voice.dto.CompanionRequestDto
import com.mindsyncvr.core.voice.dto.FullSessionDto
import com.mindsyncvr.core.voice.dto.FullSessionRequestDto
import com.mindsyncvr.core.voice.dto.HealthDto
import com.mindsyncvr.core.voice.dto.InferDto
import okhttp3.MultipartBody
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Query

/**
 * Retrofit surface for Component D (:8010). Mirrors the proven web contract in
 * component-d/clients/web/src/api.js. This is the only place that names raw HTTP;
 * everything above it speaks domain types via [VoiceStressRepository].
 */
interface ComponentDApi {

    @GET("health")
    suspend fun health(): HealthDto

    @Multipart
    @POST("ambient-check")
    suspend fun ambientCheck(@Part file: MultipartBody.Part): AmbientDto

    @Multipart
    @POST("infer")
    suspend fun infer(
        @Part file: MultipartBody.Part,
        @Query("session_id") sessionId: String,
        @Query("phase") phase: String,
        @Query("poll_b") pollB: Boolean,
        @Query("log") log: Boolean,
        @Query("user_id") userId: String?,
        @Query("language") language: String?,
    ): InferDto

    @POST("companion/message")
    suspend fun companion(
        @Query("phase") phase: String,
        @Body body: CompanionRequestDto,
    ): CompanionReplyDto

    @POST("full-session")
    suspend fun fullSession(@Body body: FullSessionRequestDto): FullSessionDto
}
