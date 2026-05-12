package com.example.android_client.core.network

import android.util.Log
import com.example.android_client.BuildConfig
import com.example.android_client.core.storage.AuthRepository
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.cio.CIO
import io.ktor.client.plugins.ResponseException
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
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json

/**
 * Thrown when the user's session can no longer be recovered (refresh token is
 * missing, expired, or revoked). The UI layer should react by clearing local
 * auth state — but `ApiClient` already does that itself before throwing this,
 * so the auth Flow in `AuthRepository` will emit `null` and `MainActivity`
 * will route to the login screen automatically.
 */
class AuthExpiredException(message: String = "Session expired, please log in again.") : Exception(message)

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

    // ── Auth helpers ────────────────────────────────────────────

    /**
     * Serialises concurrent refresh attempts so two parallel API calls don't
     * each consume the (single-use, rotating) refresh token.
     */
    private val refreshMutex = Mutex()

    /**
     * Read the current access token, run [block] with it, and on a 401 try to
     * refresh once and retry. If refresh itself fails, clear local auth state
     * (so the UI flips back to the login screen) and surface
     * [AuthExpiredException].
     */
    private suspend fun <T> executeAuthed(
        serverDomain: String,
        block: suspend (token: String) -> T
    ): T {
        val token = authRepository.token.first()
            ?: run {
                authRepository.clearTokens()
                throw AuthExpiredException()
            }

        return try {
            block(token)
        } catch (e: ResponseException) {
            if (e.response.status != HttpStatusCode.Unauthorized) throw e
            val refreshed = tryRefresh(serverDomain) ?: throw AuthExpiredException()
            block(refreshed)
        }
    }

    /**
     * Attempt to use the stored refresh token to mint a new access token.
     * Returns the new access token on success; null (and clears local auth
     * state) on failure. Concurrency-safe via [refreshMutex] — if multiple
     * callers race to refresh, only the first hits the network and the rest
     * pick up the freshly stored token.
     */
    private suspend fun tryRefresh(serverDomain: String): String? = refreshMutex.withLock {
        // Another coroutine may have already refreshed while we were waiting
        // on the lock — pick up the freshly stored token if so.
        val refreshTokenValue = authRepository.refreshToken.first()
        if (refreshTokenValue == null) {
            authRepository.clearTokens()
            return@withLock null
        }
        try {
            val response = refreshToken(serverDomain, refreshTokenValue)
            authRepository.saveTokens(response.token, response.refreshToken)
            response.token
        } catch (e: Exception) {
            // Refresh failed (rejected, network error, anything). Treat the
            // session as gone: clear local auth so MainActivity routes back to
            // the login screen on the next recomposition.
            Log.w(TAG, "Refresh failed (${e.message}); clearing local auth.")
            authRepository.clearTokens()
            null
        }
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
    suspend fun getPlugins(serverDomain: String): List<PluginInfo> = executeAuthed(serverDomain) { token ->
        client.get(getUrl(serverDomain, "/content/plugins")) {
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
    ): List<ContentItem> = executeAuthed(serverDomain) { token ->
        Log.d(TAG, "getContentItems($contentType) page=$page pageSize=$pageSize")
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
        response
    }

    /**
     * Download a single content item descriptor by id (metadata + presigned URL).
     */
    suspend fun downloadContentItem(
        serverDomain: String,
        contentType: String,
        id: String
    ): ContentDownloadResponse? = executeAuthed(serverDomain) { token ->
        Log.d(TAG, "downloadContentItem($contentType, $id)")
        val response: HttpResponse = client.get(getUrl(serverDomain, "/content/$contentType/download/$id")) {
            header("Authorization", "Bearer $token")
        }
        if (!response.status.isSuccess()) {
            Log.e(TAG, "downloadContentItem failed: ${response.status}")
            null
        } else {
            response.body<ContentDownloadResponse>()
        }
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
    ): List<ContentDownloadResponse> = executeAuthed(serverDomain) { token ->
        Log.d(TAG, "downloadContentItems($contentType) page=$page")
        client.get(getUrl(serverDomain, "/content/$contentType/download")) {
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
    ): ContentUploadInitResponse = executeAuthed(serverDomain) { token ->
        client.post(getUrl(serverDomain, "/content/$contentType/upload-init")) {
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
    ): ContentUploadCompleteResponse = executeAuthed(serverDomain) { token ->
        client.post(getUrl(serverDomain, "/content/$contentType/upload-complete")) {
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
    ): ContentDeleteResponse = executeAuthed(serverDomain) { token ->
        client.delete(getUrl(serverDomain, "/content/$contentType/delete")) {
            header("Authorization", "Bearer $token")
            contentType(ContentType.Application.Json)
            setBody(ContentDeleteRequest(items = items))
        }.body()
    }

    // ── User / Admin ────────────────────────────────────────────

    /** Returns the currently-authenticated user's profile (server reload of token claims). */
    suspend fun getCurrentUser(serverDomain: String): UserProfile = executeAuthed(serverDomain) { token ->
        client.get(getUrl(serverDomain, "/User/me")) {
            header("Authorization", "Bearer $token")
        }.body()
    }

    /** Change a user's username. Self-or-admin enforced server-side. */
    suspend fun changeUsername(serverDomain: String, userId: String, newUsername: String) {
        executeAuthed(serverDomain) { token ->
            val resp = client.post(getUrl(serverDomain, "/User/$userId/change-username")) {
                header("Authorization", "Bearer $token")
                contentType(ContentType.Application.Json)
                setBody(ChangeUsernameRequest(newUsername))
            }
            if (!resp.status.isSuccess()) {
                error("Username update failed: ${resp.status} ${resp.body<String>()}")
            }
        }
    }

    /** Change a user's password. Self-or-admin enforced server-side. */
    suspend fun changePassword(serverDomain: String, userId: String, newPassword: String) {
        executeAuthed(serverDomain) { token ->
            val resp = client.post(getUrl(serverDomain, "/User/$userId/change-password")) {
                header("Authorization", "Bearer $token")
                contentType(ContentType.Application.Json)
                setBody(ChangePasswordRequest(newPassword))
            }
            if (!resp.status.isSuccess()) {
                error("Password update failed: ${resp.status} ${resp.body<String>()}")
            }
        }
    }

    /** Admin only: list all users in the system. */
    suspend fun listUsers(serverDomain: String): List<UserProfile> = executeAuthed(serverDomain) { token ->
        client.get(getUrl(serverDomain, "/Admin/users")) {
            header("Authorization", "Bearer $token")
        }.body()
    }

    /** Admin only: create a new user with the given roles. */
    suspend fun createUser(
        serverDomain: String,
        username: String,
        password: String,
        roles: List<String>
    ): UserProfile = executeAuthed(serverDomain) { token ->
        val resp = client.post(getUrl(serverDomain, "/User")) {
            header("Authorization", "Bearer $token")
            contentType(ContentType.Application.Json)
            setBody(CreateUserRequest(username, password, roles))
        }
        if (!resp.status.isSuccess()) {
            error("Create user failed: ${resp.status} ${resp.body<String>()}")
        }
        resp.body()
    }

    /** Admin only: replace a user's role list. Roles are sent as strings. */
    suspend fun setUserRoles(serverDomain: String, userId: String, roles: List<String>) {
        executeAuthed(serverDomain) { token ->
            val resp = client.post(getUrl(serverDomain, "/Admin/users/$userId/roles")) {
                header("Authorization", "Bearer $token")
                contentType(ContentType.Application.Json)
                setBody(roles)
            }
            if (!resp.status.isSuccess()) {
                error("Role update failed: ${resp.status} ${resp.body<String>()}")
            }
        }
    }

    /** Admin only: remove a user from the system. */
    suspend fun deleteUser(serverDomain: String, userId: String) {
        executeAuthed(serverDomain) { token ->
            val resp = client.delete(getUrl(serverDomain, "/Admin/users/$userId")) {
                header("Authorization", "Bearer $token")
            }
            if (!resp.status.isSuccess()) {
                error("Delete user failed: ${resp.status} ${resp.body<String>()}")
            }
        }
    }
}
