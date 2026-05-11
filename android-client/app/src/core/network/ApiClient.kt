package com.example.android_client.core.network

import android.util.Log
import com.example.android_client.BuildConfig
import com.example.android_client.core.storage.AuthRepository
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.cio.CIO
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.delete
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.headers
import io.ktor.client.request.post
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.flow.first
import kotlinx.serialization.json.Json

class ApiClient(private val authRepository: AuthRepository) {

    companion object {
        private const val TAG = "ApiClient"
    }

    private val client = HttpClient(CIO) {
        engine {
            https {
                if (BuildConfig.INSECURE_TLS) {
                    // Only enabled in debug builds for local development with self-signed certs.
                    trustManager = object : javax.net.ssl.X509TrustManager {
                        override fun checkClientTrusted(p0: Array<out java.security.cert.X509Certificate>?, p1: String?) { }
                        override fun checkServerTrusted(p0: Array<out java.security.cert.X509Certificate>?, p1: String?) { }
                        override fun getAcceptedIssuers(): Array<out java.security.cert.X509Certificate>? = null
                    }
                }
            }
        }
        install(ContentNegotiation) {
            json(Json {
                ignoreUnknownKeys = true
            })
        }
    }

    private fun getUrl(serverDomain: String, path: String): String {
        // Use http only for explicit localhost development, https for everything else
        val protocol = if (serverDomain.startsWith("localhost:") || serverDomain.startsWith("127.0.0.1:") || serverDomain.startsWith("10.0.2.2:")) "http" else "https"
        return "$protocol://$serverDomain$path"
    }

    // ── Auth ────────────────────────────────────────────────────

    suspend fun login(serverDomain: String, loginRequest: LoginRequest): LoginResponse {
        Log.d(TAG, "login() called for serverDomain: $serverDomain")
        val response: LoginResponse = client.post(getUrl(serverDomain, "/Auth/login")) {
            contentType(ContentType.Application.Json)
            setBody(loginRequest)
        }.body()
        Log.d(TAG, "login() completed successfully")
        return response
    }

    suspend fun refreshToken(serverDomain: String, refreshToken: String): LoginResponse {
        Log.d(TAG, "refreshToken() called for serverDomain: $serverDomain")
        val response: LoginResponse = client.post(getUrl(serverDomain, "/Auth/refresh")) {
            contentType(ContentType.Application.Json)
            setBody(RefreshTokenRequest(refreshToken))
        }.body()
        Log.d(TAG, "refreshToken() completed successfully")
        return response
    }

    // ── Content API (plugin-based) ──────────────────────────────

    /**
     * List available server plugins.
     */
    suspend fun getPlugins(serverDomain: String): List<PluginInfo> {
        val token = authRepository.token.first()
        return client.get(getUrl(serverDomain, "/content/plugins")) {
            header("Authorization", "Bearer $token")
        }.body()
    }

    /**
     * Get content items (metadata only) for a given content type.
     */
    suspend fun getContentItems(
        serverDomain: String,
        contentType: String,
        page: Int? = null,
        pageSize: Int? = null,
        titlePrefix: String? = null,
        lastModifiedSince: String? = null,
        extraParams: Map<String, String> = emptyMap()
    ): List<ContentItem> {
        Log.d(TAG, "getContentItems($contentType) page=$page pageSize=$pageSize")
        val token = authRepository.token.first()
        val response: List<ContentItem> = client.get(getUrl(serverDomain, "/content/$contentType/items")) {
            header("Authorization", "Bearer $token")
            url {
                page?.let { parameters.append("page", it.toString()) }
                pageSize?.let { parameters.append("pageSize", it.toString()) }
                titlePrefix?.let { parameters.append("titlePrefix", it) }
                lastModifiedSince?.let { parameters.append("lastModifiedSince", it) }
                extraParams.forEach { (k, v) -> if (v.isNotBlank()) parameters.append(k, v) }
            }
        }.body()
        Log.d(TAG, "getContentItems($contentType) => ${response.size} items")
        return response
    }

    /**
     * Download a single content item descriptor by id (metadata + presigned URL).
     */
    suspend fun downloadContentItem(
        serverDomain: String,
        contentType: String,
        id: String
    ): ContentDownloadResponse? {
        Log.d(TAG, "downloadContentItem($contentType, $id)")
        val token = authRepository.token.first()
        val response: HttpResponse = client.get(getUrl(serverDomain, "/content/$contentType/download/$id")) {
            header("Authorization", "Bearer $token")
        }
        if (!response.status.isSuccess()) {
            Log.e(TAG, "downloadContentItem failed: ${response.status}")
            return null
        }
        return response.body<ContentDownloadResponse>()
    }

    /**
     * Bulk download content item descriptors (metadata + presigned URLs).
     */
    suspend fun downloadContentItems(
        serverDomain: String,
        contentType: String,
        page: Int? = null,
        pageSize: Int? = null,
        titlePrefix: String? = null,
        lastModifiedSince: String? = null,
        extraParams: Map<String, String> = emptyMap()
    ): List<ContentDownloadResponse> {
        Log.d(TAG, "downloadContentItems($contentType) page=$page")
        val token = authRepository.token.first()
        return client.get(getUrl(serverDomain, "/content/$contentType/download")) {
            header("Authorization", "Bearer $token")
            url {
                page?.let { parameters.append("page", it.toString()) }
                pageSize?.let { parameters.append("pageSize", it.toString()) }
                titlePrefix?.let { parameters.append("titlePrefix", it) }
                lastModifiedSince?.let { parameters.append("lastModifiedSince", it) }
                extraParams.forEach { (k, v) -> if (v.isNotBlank()) parameters.append(k, v) }
            }
        }.body()
    }

    /**
     * Download raw bytes from a presigned URL (S3 direct download).
     */
    suspend fun downloadBytes(downloadUrl: String): ByteArray? {
        val response = client.get(downloadUrl)
        if (!response.status.isSuccess()) {
            Log.e(TAG, "downloadBytes failed: ${response.status}")
            return null
        }
        return response.body()
    }

    /**
     * Initialize upload and receive presigned URL for direct object storage upload.
     */
    suspend fun uploadInit(
        serverDomain: String,
        contentType: String,
        request: ContentUploadInitRequest
    ): ContentUploadInitResponse {
        val token = authRepository.token.first()
        return client.post(getUrl(serverDomain, "/content/$contentType/upload-init")) {
            header("Authorization", "Bearer $token")
            contentType(ContentType.Application.Json)
            setBody(request)
        }.body()
    }

    /**
     * Upload binary payload directly to storage using a pre-signed URL.
     */
    suspend fun uploadBinary(uploadUrl: String, bytes: ByteArray, headersFromServer: Map<String, String>) {
        val providedContentType = headersFromServer[HttpHeaders.ContentType] ?: headersFromServer["content-type"]
        val resolvedContentType = providedContentType?.let { ContentType.parse(it) } ?: ContentType.Application.OctetStream

        val response = client.put(uploadUrl) {
            contentType(resolvedContentType)
            headers {
                headersFromServer.forEach { (k, v) ->
                    // Ktor sets Content-Type separately; avoid duplicate header entries.
                    if (!k.equals(HttpHeaders.ContentType, ignoreCase = true)) {
                        append(k, v)
                    }
                }
            }
            setBody(bytes)
        }

        if (!response.status.isSuccess()) {
            error("Direct upload failed with status ${response.status}")
        }
    }

    /**
     * Finalize upload metadata in the sync server after the binary upload succeeds.
     */
    suspend fun uploadComplete(
        serverDomain: String,
        contentType: String,
        request: ContentUploadCompleteRequest
    ): ContentUploadCompleteResponse {
        val token = authRepository.token.first()
        return client.post(getUrl(serverDomain, "/content/$contentType/upload-complete")) {
            header("Authorization", "Bearer $token")
            contentType(ContentType.Application.Json)
            setBody(request)
        }.body()
    }

    /**
     * Delete content items from the server (S3 object + DynamoDB metadata).
     * Server endpoint: DELETE /content/{contentType}/delete
     */
    suspend fun deleteItems(
        serverDomain: String,
        contentType: String,
        items: List<ContentItem>
    ): ContentDeleteResponse {
        val token = authRepository.token.first()
        return client.delete(getUrl(serverDomain, "/content/$contentType/delete")) {
            header("Authorization", "Bearer $token")
            contentType(ContentType.Application.Json)
            setBody(ContentDeleteRequest(items = items))
        }.body()
    }
}
