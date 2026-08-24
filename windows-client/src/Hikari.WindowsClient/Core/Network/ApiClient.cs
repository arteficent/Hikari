using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Core.Network;

/// <summary>
/// Thrown when the user's session can no longer be recovered (refresh token is
/// missing, expired, or revoked). <see cref="ApiClient"/> already clears local
/// auth state before throwing, so the app shell routes back to the login screen.
/// </summary>
public sealed class AuthExpiredException : Exception
{
    public AuthExpiredException(string message = "Session expired, please log in again.")
        : base(message) { }
}

/// <summary>An HTTP call returned a non-success status code.</summary>
public sealed class ApiStatusException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Body { get; }

    public ApiStatusException(HttpStatusCode statusCode, string body, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Body = body;
    }
}

/// <summary>
/// HTTP surface for the Hikari sync-server. Mirrors
/// <c>android-client/app/src/core/network/ApiClient.kt</c> endpoint for endpoint.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly AuthRepository _authRepository;
    private readonly HttpClient _client;

    /// <summary>Serialises concurrent refresh attempts so two parallel API calls
    /// don't each consume the single-use, rotating refresh token.</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ApiClient(AuthRepository authRepository)
    {
        _authRepository = authRepository;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
#if DEBUG
        // Debug builds only — lets developers point the client at a local server
        // using a self-signed certificate, matching the android client's
        // INSECURE_TLS debug flag. Never enabled in Release.
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif

        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("HikariWindowsClient/1.0");
    }

    /// <summary>
    /// Build an absolute URL. An explicit scheme typed by the user wins; otherwise
    /// plain http is used for loopback addresses and https for everything else.
    /// </summary>
    public static string GetUrl(string serverDomain, string path)
    {
        var domain = serverDomain.Trim().TrimEnd('/');

        if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return domain + path;
        }

        var isLoopback =
            domain.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
            domain.StartsWith("127.0.0.1", StringComparison.Ordinal) ||
            domain.StartsWith("10.0.2.2", StringComparison.Ordinal);

        return $"{(isLoopback ? "http" : "https")}://{domain}{path}";
    }

    // ── Auth helpers ────────────────────────────────────────────

    /// <summary>
    /// Read the current access token, run <paramref name="block"/> with it, and on a
    /// 401 try to refresh once and retry. If the refresh itself fails, local auth
    /// state is cleared (so the UI flips back to login) and
    /// <see cref="AuthExpiredException"/> surfaces.
    /// </summary>
    private async Task<T> ExecuteAuthedAsync<T>(
        string serverDomain,
        Func<string, Task<T>> block)
    {
        var token = _authRepository.Token;
        if (string.IsNullOrEmpty(token))
        {
            _authRepository.ClearTokens();
            throw new AuthExpiredException();
        }

        try
        {
            return await block(token).ConfigureAwait(false);
        }
        catch (ApiStatusException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshAsync(serverDomain).ConfigureAwait(false)
                            ?? throw new AuthExpiredException();
            return await block(refreshed).ConfigureAwait(false);
        }
    }

    private Task ExecuteAuthedAsync(string serverDomain, Func<string, Task> block) =>
        ExecuteAuthedAsync<object?>(serverDomain, async token =>
        {
            await block(token).ConfigureAwait(false);
            return null;
        });

    /// <summary>
    /// Attempt to mint a new access token from the stored refresh token. Returns the
    /// new access token, or null (and clears local auth state) on failure.
    /// Concurrency-safe: if callers race, only the first hits the network and the
    /// rest pick up the freshly stored token.
    /// </summary>
    private async Task<string?> TryRefreshAsync(string serverDomain)
    {
        await _refreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var refreshTokenValue = _authRepository.RefreshToken;
            if (string.IsNullOrEmpty(refreshTokenValue))
            {
                _authRepository.ClearTokens();
                return null;
            }

            try
            {
                var response = await RefreshTokenAsync(serverDomain, refreshTokenValue).ConfigureAwait(false);
                _authRepository.SaveTokens(response.Token, response.RefreshToken);
                return response.Token;
            }
            catch
            {
                // Refresh failed (rejected, network error, anything). Treat the session
                // as gone so the shell routes back to the login screen.
                _authRepository.ClearTokens();
                return null;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ── Low-level plumbing ──────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string? token,
        HttpContent? content,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (content is not null)
        {
            request.Content = content;
        }

        return await _client.SendAsync(request, completion, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;

        var body = string.Empty;
        try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
        catch { /* body is best-effort context for the error message */ }

        throw new ApiStatusException(
            response.StatusCode,
            body,
            $"{what} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body)}");
    }

    private static string Truncate(string value, int max = 400) =>
        value.Length <= max ? value : value[..max] + "…";

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException($"Server returned an empty {typeof(T).Name} payload.");
    }

    private async Task<T> GetJsonAsync<T>(string url, string token, string what, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, url, token, null, ct: ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, what).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response).ConfigureAwait(false);
    }

    private static StringContent JsonBody<T>(T value) =>
        new(JsonSerializer.Serialize(value, Json), System.Text.Encoding.UTF8, "application/json");

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var pairs = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    private static List<KeyValuePair<string, string?>> ListQuery(
        int? page,
        int? pageSize,
        string? titlePrefix,
        string? lastModifiedSince,
        IReadOnlyDictionary<string, string>? extraParams)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("page", page?.ToString()),
            new("pageSize", pageSize?.ToString()),
            new("titlePrefix", titlePrefix),
            new("lastModifiedSince", lastModifiedSince),
        };

        if (extraParams is not null)
        {
            query.AddRange(extraParams
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
        }

        return query;
    }

    // ── Auth ────────────────────────────────────────────────────

    public async Task<LoginResponse> LoginAsync(string serverDomain, LoginRequest loginRequest, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post, GetUrl(serverDomain, "/Auth/login"), null, JsonBody(loginRequest), ct: ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Login").ConfigureAwait(false);
        return await ReadJsonAsync<LoginResponse>(response).ConfigureAwait(false);
    }

    public async Task<LoginResponse> RefreshTokenAsync(string serverDomain, string refreshToken, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post, GetUrl(serverDomain, "/Auth/refresh"), null,
            JsonBody(new RefreshTokenRequest(refreshToken)), ct: ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Token refresh").ConfigureAwait(false);
        return await ReadJsonAsync<LoginResponse>(response).ConfigureAwait(false);
    }

    // ── Content API (plugin-based) ──────────────────────────────

    /// <summary>List the plugins the server has registered.</summary>
    public Task<List<PluginInfo>> GetPluginsAsync(string serverDomain, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, token =>
            GetJsonAsync<List<PluginInfo>>(GetUrl(serverDomain, "/content/plugins"), token, "List plugins", ct));

    /// <summary>Get content items (metadata only) for a given content type.</summary>
    public Task<List<ContentItem>> GetContentItemsAsync(
        string serverDomain,
        string contentType,
        int? page = null,
        int? pageSize = null,
        string? titlePrefix = null,
        string? lastModifiedSince = null,
        IReadOnlyDictionary<string, string>? extraParams = null,
        CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, token =>
        {
            var query = BuildQuery(ListQuery(page, pageSize, titlePrefix, lastModifiedSince, extraParams));
            return GetJsonAsync<List<ContentItem>>(
                GetUrl(serverDomain, $"/content/{contentType}/items") + query, token, "List content", ct);
        });

    /// <summary>Download a single content descriptor by id (metadata + presigned URL).</summary>
    public Task<ContentDownloadResponse?> DownloadContentItemAsync(
        string serverDomain, string contentType, string id, CancellationToken ct = default) =>
        ExecuteAuthedAsync<ContentDownloadResponse?>(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Get, GetUrl(serverDomain, $"/content/{contentType}/download/{Uri.EscapeDataString(id)}"),
                token, null, ct: ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await EnsureSuccessAsync(response, "Download descriptor").ConfigureAwait(false);
            }

            return response.IsSuccessStatusCode
                ? await ReadJsonAsync<ContentDownloadResponse>(response).ConfigureAwait(false)
                : null;
        });

    /// <summary>Bulk download content descriptors (metadata + presigned URLs).</summary>
    public Task<List<ContentDownloadResponse>> DownloadContentItemsAsync(
        string serverDomain,
        string contentType,
        int? page = null,
        int? pageSize = null,
        string? titlePrefix = null,
        string? lastModifiedSince = null,
        IReadOnlyDictionary<string, string>? extraParams = null,
        CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, token =>
        {
            var query = BuildQuery(ListQuery(page, pageSize, titlePrefix, lastModifiedSince, extraParams));
            return GetJsonAsync<List<ContentDownloadResponse>>(
                GetUrl(serverDomain, $"/content/{contentType}/download") + query, token, "Bulk download", ct);
        });

    /// <summary>
    /// Open a read stream over a presigned URL. Streaming (rather than buffering to a
    /// byte[]) keeps memory flat for multi-gigabyte video and book files.
    /// </summary>
    public async Task<HttpResponseMessage> OpenDownloadAsync(string downloadUrl, CancellationToken ct = default)
    {
        var response = await SendAsync(
            HttpMethod.Get, downloadUrl, null, null, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            using (response) { await EnsureSuccessAsync(response, "Download").ConfigureAwait(false); }
        }

        return response;
    }

    /// <summary>Download raw bytes from a presigned URL. Use only for small payloads.</summary>
    public async Task<byte[]?> DownloadBytesAsync(string downloadUrl, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, downloadUrl, null, null, ct: ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>Initialise an upload and receive a presigned URL for direct object storage upload.</summary>
    public Task<ContentUploadInitResponse> UploadInitAsync(
        string serverDomain, string contentType, ContentUploadInitRequest request, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, $"/content/{contentType}/upload-init"),
                token, JsonBody(request), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Upload init").ConfigureAwait(false);
            return await ReadJsonAsync<ContentUploadInitResponse>(response).ConfigureAwait(false);
        });

    /// <summary>Upload a binary payload directly to object storage using the presigned URL.</summary>
    public async Task UploadBinaryAsync(
        string uploadUrl,
        Stream payload,
        IReadOnlyDictionary<string, string> headersFromServer,
        string? fallbackContentType = null,
        CancellationToken ct = default)
    {
        var content = new StreamContent(payload);

        var providedContentType =
            headersFromServer.FirstOrDefault(h =>
                string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value;

        // Prefer what the server signed the URL with; fall back to the plugin's MIME
        // type so object stores that validate Content-Type still accept the PUT.
        content.Headers.ContentType =
            MediaTypeHeaderValue.TryParse(providedContentType, out var parsed) ? parsed
            : MediaTypeHeaderValue.TryParse(fallbackContentType, out var fallback) ? fallback
            : new MediaTypeHeaderValue("application/octet-stream");

        foreach (var (key, value) in headersFromServer)
        {
            // Content-Type is set above; avoid duplicate header entries.
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (!content.Headers.TryAddWithoutValidation(key, value))
            {
                content.Headers.Add(key, value);
            }
        }

        using (content)
        {
            using var response = await SendAsync(HttpMethod.Put, uploadUrl, null, content, ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Direct upload").ConfigureAwait(false);
        }
    }

    /// <summary>Finalise upload metadata in the sync server after the binary upload succeeds.</summary>
    public Task<ContentUploadCompleteResponse> UploadCompleteAsync(
        string serverDomain, string contentType, ContentUploadCompleteRequest request, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, $"/content/{contentType}/upload-complete"),
                token, JsonBody(request), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Upload complete").ConfigureAwait(false);
            return await ReadJsonAsync<ContentUploadCompleteResponse>(response).ConfigureAwait(false);
        });

    /// <summary>Delete content items from the server (object storage + database).</summary>
    public Task<ContentDeleteResponse> DeleteItemsAsync(
        string serverDomain, string contentType, IReadOnlyList<ContentItem> items, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Delete, GetUrl(serverDomain, $"/content/{contentType}/delete"),
                token, JsonBody(new ContentDeleteRequest { Items = items.ToList() }), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Delete content").ConfigureAwait(false);
            return await ReadJsonAsync<ContentDeleteResponse>(response).ConfigureAwait(false);
        });

    /// <summary>Metadata-only update for an existing item; the binary is left untouched.</summary>
    public Task EditContentAsync(string serverDomain, string contentType, ContentItem item, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Put, GetUrl(serverDomain, $"/content/{contentType}/edit"),
                token, JsonBody(item), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Edit").ConfigureAwait(false);
        });

    // ── User / Admin ────────────────────────────────────────────

    /// <summary>The currently-authenticated user's profile (server reload of token claims).</summary>
    public Task<UserProfile> GetCurrentUserAsync(string serverDomain, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, token =>
            GetJsonAsync<UserProfile>(GetUrl(serverDomain, "/User/me"), token, "Load profile", ct));

    /// <summary>Change a user's username. Self-or-admin is enforced server-side.</summary>
    public Task ChangeUsernameAsync(string serverDomain, string userId, string newUsername, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, $"/User/{Uri.EscapeDataString(userId)}/change-username"),
                token, JsonBody(new ChangeUsernameRequest(newUsername)), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Username update").ConfigureAwait(false);
        });

    /// <summary>Change a user's password. Self-or-admin is enforced server-side.</summary>
    public Task ChangePasswordAsync(string serverDomain, string userId, string newPassword, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, $"/User/{Uri.EscapeDataString(userId)}/change-password"),
                token, JsonBody(new ChangePasswordRequest(newPassword)), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Password update").ConfigureAwait(false);
        });

    /// <summary>Admin only: list all users in the system.</summary>
    public Task<List<UserProfile>> ListUsersAsync(string serverDomain, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, token =>
            GetJsonAsync<List<UserProfile>>(GetUrl(serverDomain, "/Admin/users"), token, "List users", ct));

    /// <summary>Admin only: create a new user with the given roles.</summary>
    public Task<UserProfile> CreateUserAsync(
        string serverDomain, string username, string password, List<string> roles, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, "/User"), token,
                JsonBody(new CreateUserRequest(username, password, roles)), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Create user").ConfigureAwait(false);
            return await ReadJsonAsync<UserProfile>(response).ConfigureAwait(false);
        });

    /// <summary>Admin only: replace a user's role list. Roles are sent as strings.</summary>
    public Task SetUserRolesAsync(string serverDomain, string userId, List<string> roles, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Post, GetUrl(serverDomain, $"/Admin/users/{Uri.EscapeDataString(userId)}/roles"),
                token, JsonBody(roles), ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Role update").ConfigureAwait(false);
        });

    /// <summary>Admin only: remove a user from the system.</summary>
    public Task DeleteUserAsync(string serverDomain, string userId, CancellationToken ct = default) =>
        ExecuteAuthedAsync(serverDomain, async token =>
        {
            using var response = await SendAsync(
                HttpMethod.Delete, GetUrl(serverDomain, $"/Admin/users/{Uri.EscapeDataString(userId)}"),
                token, null, ct: ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "Delete user").ConfigureAwait(false);
        });

    public void Dispose()
    {
        _client.Dispose();
        _refreshLock.Dispose();
    }
}
