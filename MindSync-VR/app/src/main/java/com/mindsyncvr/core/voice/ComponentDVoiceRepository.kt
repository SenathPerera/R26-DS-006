package com.mindsyncvr.core.voice

import com.mindsyncvr.core.voice.dto.CompanionRequestDto
import com.mindsyncvr.core.voice.dto.ErrorDetailDto
import com.mindsyncvr.core.voice.dto.FullSessionRequestDto
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.HttpException
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLException

/**
 * Live Component D client. Translates domain calls to [ComponentDApi] and maps
 * every failure to a typed [VoiceError]. Build with [create]; inject the mock
 * ([MockVoiceStressRepository]) instead when no backend is running.
 */
class ComponentDVoiceRepository(
    private val api: ComponentDApi,
    private val json: Json,
) : VoiceStressRepository {

    override suspend fun health(): Result<BackendHealth> =
        call { api.health().toDomain() }

    override suspend fun ambientCheck(audio: AudioPayload): Result<AmbientResult> =
        call { api.ambientCheck(audio.toPart()).toDomain() }

    override suspend fun analyzeVoice(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        userId: String?,
        language: String?,
        pollB: Boolean,
        log: Boolean,
    ): Result<VoiceAnalysis> = call {
        api.infer(
            file = audio.toPart(),
            sessionId = sessionId,
            phase = phase.wire,
            pollB = pollB,
            log = log,
            userId = userId,
            language = language,
        ).toDomain()
    }

    override suspend fun companionMessage(sessionId: String, text: String, phase: SessionPhase): Result<CompanionReply> =
        call { api.companion(phase.wire, CompanionRequestDto(sessionId, text)).toDomain() }

    override suspend fun voiceTurn(
        sessionId: String,
        phase: SessionPhase,
        audio: AudioPayload,
        isFinal: Boolean,
        userId: String?,
        language: String?,
        pollB: Boolean,
        log: Boolean,
    ): Result<VoiceTurnResult> = call {
        api.companionVoiceTurn(
            file = audio.toPart(),
            sessionId = sessionId,
            phase = phase.wire,
            isFinal = isFinal,
            pollB = pollB,
            log = log,
            userId = userId,
            language = language,
        ).toDomain()
    }

    override suspend fun completeSession(
        sessionId: String,
        userId: String?,
        useMockHrv: Boolean,
        language: String?,
        selfReportPre: Double?,
        selfReportPost: Double?,
        log: Boolean,
    ): Result<SessionReport> = call {
        api.fullSession(
            FullSessionRequestDto(
                sessionId = sessionId,
                userId = userId ?: "default",
                useMockHrv = useMockHrv,
                language = language,
                selfReportPre = selfReportPre,
                selfReportPost = selfReportPost,
                log = log,
            ),
        ).toDomain()
    }

    private fun AudioPayload.toPart(): MultipartBody.Part {
        val body = bytes.toRequestBody(mimeType.toMediaType())
        return MultipartBody.Part.createFormData("file", fileName, body)
    }

    /** Run one API call, converting any throwable to a [VoiceStressException]. */
    private suspend fun <T> call(block: suspend () -> T): Result<T> =
        try {
            Result.success(block())
        } catch (e: Throwable) {
            if (e is kotlinx.coroutines.CancellationException) throw e
            Result.failure(VoiceStressException(e.toVoiceError()))
        }

    private fun Throwable.toVoiceError(): VoiceError = when (this) {
        is HttpException -> mapHttp(this)
        is SocketTimeoutException -> VoiceError.Timeout
        is UnknownHostException, is ConnectException, is SSLException -> VoiceError.NetworkUnavailable
        is IOException -> VoiceError.NetworkUnavailable
        is SerializationException -> VoiceError.Malformed
        else -> VoiceError.Unknown(message)
    }

    private fun mapHttp(e: HttpException): VoiceError {
        val raw = runCatching { e.response()?.errorBody()?.string() }.getOrNull()
        return when (e.code()) {
            400 -> VoiceError.NoAudioCaptured(parseRejectReasons(raw))
            401, 403 -> VoiceError.Unauthorized
            404 -> VoiceError.SessionIncomplete
            422 -> VoiceError.AudioRejected(parseRejectReasons(raw))
            503 -> VoiceError.BackendUnavailable(parseDetailString(raw))
            in 500..599 -> VoiceError.ServerError(e.code())
            else -> VoiceError.Unknown(parseDetailString(raw) ?: "HTTP ${e.code()}")
        }
    }

    /** 422 detail is an object {error, reasons[]}; pull the reasons. */
    private fun parseRejectReasons(raw: String?): List<String> {
        val detail = raw?.let { runCatching { json.parseToJsonElement(it).jsonObject["detail"] }.getOrNull() }
            ?: return emptyList()
        return runCatching { json.decodeFromJsonElement(ErrorDetailDto.serializer(), detail).reasons }
            .getOrDefault(emptyList())
    }

    /** Non-422 errors send detail as a plain string. */
    private fun parseDetailString(raw: String?): String? =
        raw?.let { runCatching { json.parseToJsonElement(it).jsonObject["detail"]?.toString()?.trim('"') }.getOrNull() }

    companion object {
        /** Wire up Retrofit/OkHttp for the given [config]. Logging is BASIC only
         *  (method, URL, status) so audio bytes and stress payloads are never logged. */
        fun create(config: ComponentDConfig): ComponentDVoiceRepository {
            val json = Json { ignoreUnknownKeys = true }
            val logging = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC }
            val client = OkHttpClient.Builder()
                .connectTimeout(config.connectTimeoutSeconds, TimeUnit.SECONDS)
                .readTimeout(config.readTimeoutSeconds, TimeUnit.SECONDS)
                .writeTimeout(config.readTimeoutSeconds, TimeUnit.SECONDS)
                .addInterceptor(logging)
                .build()
            val retrofit = Retrofit.Builder()
                .baseUrl(config.baseUrl)
                .client(client)
                .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
                .build()
            return ComponentDVoiceRepository(retrofit.create(ComponentDApi::class.java), json)
        }
    }
}
