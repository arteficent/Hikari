package com.example.android_client

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "settings")

class SettingsRepository(private val context: Context) {

    private val serverDomainKey = stringPreferencesKey("server_domain")

    val serverDomain: Flow<String?> = context.dataStore.data.map {
        it[serverDomainKey]
    }

    suspend fun saveServerDomain(domain: String) {
        context.dataStore.edit {
            it[serverDomainKey] = domain
        }
    }

    suspend fun clearServerDomain() {
        context.dataStore.edit {
            it.remove(serverDomainKey)
        }
    }
}
