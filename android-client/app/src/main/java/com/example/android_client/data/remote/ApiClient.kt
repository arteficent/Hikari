package com.example.android_client.data.remote

import android.util.Log
import com.example.android_client.data.local.AuthRepository
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.cio.CIO
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.flow.first
import kotlinx.serialization.json.Json

class ApiClient(private val authRepository: AuthRepository) {

    private val TAG = "ApiClient"

    private val client = HttpClient(CIO) {
        engine {
            https {
                // WARNING: This is insecure and should not be used in production.
                // It trusts all certificates, which is useful for local development with self-signed certs.
                trustManager = object : javax.net.ssl.X509TrustManager {
                    override fun checkClientTrusted(p0: Array<out java.security.cert.X509Certificate>?, p1: String?) { }
                    override fun checkServerTrusted(p0: Array<out java.security.cert.X509Certificate>?, p1: String?) { }
                    override fun getAcceptedIssuers(): Array<out java.security.cert.X509Certificate>? = null
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
        val protocol = if (serverDomain.contains("3446")) "http" else "https"
        return "$protocol://$serverDomain$path"
    }

    suspend fun login(serverDomain: String, loginRequest: LoginRequest): LoginResponse {
        Log.d(TAG, "login() called with serverDomain: $serverDomain, loginRequest: $loginRequest")
        val response: LoginResponse = client.post(getUrl(serverDomain, "/Auth/login")) {
            contentType(ContentType.Application.Json)
            setBody(loginRequest)
        }.body()
        Log.d(TAG, "login() response: $response")
        return response
    }

    suspend fun refreshToken(serverDomain: String, refreshToken: String): LoginResponse {
        Log.d(TAG, "refreshToken() called with serverDomain: $serverDomain, refreshToken: $refreshToken")
        val response: LoginResponse = client.post(getUrl(serverDomain, "/Auth/refresh")) {
            contentType(ContentType.Application.Json)
            setBody(RefreshTokenRequest(refreshToken))
        }.body()
        Log.d(TAG, "refreshToken() response: $response")
        return response
    }

    suspend fun getSongs(
        serverDomain: String,
        page: Int? = null,
        pageSize: Int? = null,
        genre: String? = null,
        album: String? = null,
        artist: String? = null,
        titlePrefix: String? = null,
        playlist: String? = null,
        releaseFrom: String? = null,
        releaseTo: String? = null,
        lastModifiedSince: String? = null
    ): List<Music> {
        Log.d(TAG, "getSongs() called with serverDomain: $serverDomain, page: $page, pageSize: $pageSize, genre: $genre, album: $album, artist: $artist, titlePrefix: $titlePrefix, playlist: $playlist, releaseFrom: $releaseFrom, releaseTo: $releaseTo, lastModifiedSince: $lastModifiedSince")
        val token = authRepository.token.first()
        val response: List<Music> = client.get(getUrl(serverDomain, "/Get/songs")) {
            header("Authorization", "Bearer $token")
            url {
                page?.let { parameters.append("page", it.toString()) }
                pageSize?.let { parameters.append("pageSize", it.toString()) }
                genre?.let { parameters.append("genre", it) }
                album?.let { parameters.append("album", it) }
                artist?.let { parameters.append("artist", it) }
                titlePrefix?.let { parameters.append("titlePrefix", it) }
                playlist?.let { parameters.append("playlist", it) }
                releaseFrom?.let { parameters.append("releaseFrom", it) }
                releaseTo?.let { parameters.append("releaseTo", it) }
                lastModifiedSince?.let { parameters.append("lastModifiedSince", it) }
            }
        }.body()
        Log.d(TAG, "getSongs() response: ${response.size} songs")
        return response
    }

    suspend fun downloadSongs(
        serverDomain: String,
        page: Int? = null,
        pageSize: Int? = null,
        genre: String? = null,
        album: String? = null,
        artist: String? = null,
        titlePrefix: String? = null,
        playlist: String? = null,
        releaseFrom: String? = null,
        releaseTo: String? = null,
        lastModifiedSince: String? = null
    ): List<DownloadResponse> {
        Log.d(TAG, "downloadSongs() called with serverDomain: $serverDomain, page: $page, pageSize: $pageSize, genre: $genre, album: $album, artist: $artist, titlePrefix: $titlePrefix, playlist: $playlist, releaseFrom: $releaseFrom, releaseTo: $releaseTo, lastModifiedSince: $lastModifiedSince")
        val token = authRepository.token.first()
        val response: List<DownloadResponse> = client.get(getUrl(serverDomain, "/Download/songs")) {
            header("Authorization", "Bearer $token")
            url {
                page?.let { parameters.append("page", it.toString()) }
                pageSize?.let { parameters.append("pageSize", it.toString()) }
                genre?.let { parameters.append("genre", it) }
                album?.let { parameters.append("album", it) }
                artist?.let { parameters.append("artist", it) }
                titlePrefix?.let { parameters.append("titlePrefix", it) }
                playlist?.let { parameters.append("playlist", it) }
                releaseFrom?.let { parameters.append("releaseFrom", it) }
                releaseTo?.let { parameters.append("releaseTo", it) }
                lastModifiedSince?.let { parameters.append("lastModifiedSince", it) }
            }
        }.body()
        Log.d(TAG, "downloadSongs() response: ${response.size} songs")
        return response
    }

    suspend fun downloadSongById(serverDomain: String, songId: String): DownloadResponse? {
        Log.d(TAG, "downloadSongById() called with serverDomain: $serverDomain, songId: $songId")
        val token = authRepository.token.first()
        val response: HttpResponse = client.get(getUrl(serverDomain, "/Download/song")) {
            header("Authorization", "Bearer $token")
            url {
                parameters.append("id", songId)
            }
        }
        if (!response.status.isSuccess()) {
            Log.e(TAG, "downloadSongById() failed with status: ${response.status}")
            return null
        }
        val responseObject: DownloadResponse = response.body()
        Log.d(TAG, "downloadSongById() response: $responseObject")
        return responseObject
    }
}
