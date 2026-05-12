package com.example.android_client.core.network

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * User profile returned by GET /User/me and admin user-list endpoints.
 * Roles are serialised by the server as strings ("User", "Admin", "Root").
 */
@Serializable
data class UserProfile(
    @SerialName("id") val id: String,
    @SerialName("username") val username: String,
    @SerialName("roles") val roles: List<String>? = null,
    @SerialName("createdAt") val createdAt: String? = null,
    @SerialName("updatedAt") val updatedAt: String? = null
)

@Serializable
data class ChangeUsernameRequest(val username: String)

@Serializable
data class ChangePasswordRequest(val newPassword: String)

/**
 * Admin-only user creation. Roles are sent as the server's enum-string values
 * ("User", "Admin"); the server rejects "Root" and Admin callers may only create
 * plain `User` accounts.
 */
@Serializable
data class CreateUserRequest(
    val username: String,
    val password: String,
    val roles: List<String>
)
