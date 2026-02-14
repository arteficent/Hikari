package com.example.android_client

import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.cio.CIO
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.flow.first
import kotlinx.serialization.json.Json

class ApiClient(private val authRepository: AuthRepository) {

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
        return client.post(getUrl(serverDomain, "/Auth/login")) {
            contentType(ContentType.Application.Json)
            setBody(loginRequest)
        }.body()
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
        val token = authRepository.token.first()
        return client.get(getUrl(serverDomain, "/Get/songs")) {
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
        val token = authRepository.token.first()
        return client.get(getUrl(serverDomain, "/Download/songs")) {
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
    }

    suspend fun downloadSongById(serverDomain: String, songId: String): DownloadResponse {
        val token = authRepository.token.first()
        return client.get(getUrl(serverDomain, "/Download/song")) {
            header("Authorization", "Bearer $token")
            url {
                parameters.append("id", songId)
            }
        }.body()
    }
}
