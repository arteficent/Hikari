package com.example.android_client.core.network

import kotlinx.serialization.Serializable

@Serializable
data class LoginRequest(val email: String, val password: String)

@Serializable
data class LoginResponse(val token: String, val refreshToken: String)

@Serializable
data class RefreshTokenRequest(val refreshToken: String)
