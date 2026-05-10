using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Email.Services;

internal sealed class GmailOAuthFlow
{
    private const string Scope = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.compose https://www.googleapis.com/auth/gmail.modify";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly HttpClient Http = new();

    public GmailOAuthSession CreateAuthorizationSession(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Missing account id");
        }

        GmailClientSecrets secrets = LoadClientSecrets();
        if (string.IsNullOrWhiteSpace(secrets.ClientId))
        {
            throw new InvalidOperationException("Missing OAuth client id");
        }

        int port = PickFreePort();
        string redirectUri = $"http://localhost:{port}/";
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = CreateCodeChallenge(codeVerifier);
        string state = CreateState();

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = secrets.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };

        string authorizationUrl = BuildUrl(AuthEndpoint, query);

        return new GmailOAuthSession
        {
            AuthorizationUrl = authorizationUrl,
            RedirectUri = redirectUri,
            CodeVerifier = codeVerifier,
            Port = port,
            State = state,
        };
    }

    public async Task<string> WaitForCallbackAsync(
        GmailOAuthSession session,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (session == null)
        {
            throw new InvalidOperationException("Missing OAuth session");
        }

        if (string.IsNullOrWhiteSpace(session.RedirectUri))
        {
            throw new InvalidOperationException("Missing redirect uri");
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(session.RedirectUri);
        listener.Start();

        try
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            string responseHtml = "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#1f2937;\"><h2>Atlas Gmail reconnect complete</h2><p>You can close this browser tab and return to Atlas.</p></body></html>";

            try
            {
                string? errorName = context.Request.QueryString["error"];
                if (!string.IsNullOrWhiteSpace(errorName))
                {
                    WriteHtmlResponse(context.Response, "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#991b1b;\"><h2>Gmail reconnect failed</h2><p>You can close this tab and retry from Atlas.</p></body></html>");
                    throw new InvalidOperationException(errorName);
                }

                string? returnedState = context.Request.QueryString["state"];
                if (!string.Equals(session.State, returnedState, StringComparison.Ordinal))
                {
                    WriteHtmlResponse(context.Response, "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#991b1b;\"><h2>State validation failed</h2><p>You can close this tab and retry from Atlas.</p></body></html>");
                    throw new InvalidOperationException("invalid_state");
                }

                string? code = context.Request.QueryString["code"];
                if (string.IsNullOrWhiteSpace(code))
                {
                    WriteHtmlResponse(context.Response, "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#991b1b;\"><h2>Missing authorization code</h2><p>You can close this tab and retry from Atlas.</p></body></html>");
                    throw new InvalidOperationException("missing_code");
                }

                WriteHtmlResponse(context.Response, responseHtml);
                return code;
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("timeout");
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    public async Task<string> FetchProfileEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        const string profileUrl = "https://gmail.googleapis.com/gmail/v1/users/me/profile";

        using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("profile_fetch_failed");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("emailAddress", out JsonElement emailEl))
        {
            return emailEl.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("profile_email_missing");
    }

    public async Task<GmailTokenData> RefreshAccessTokenAsync(
        GmailTokenData existing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(existing?.RefreshToken))
            throw new InvalidOperationException("No refresh token available.");

        GmailClientSecrets secrets = LoadClientSecrets();

        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", secrets.ClientId),
            new("refresh_token", existing.RefreshToken),
            new("grant_type", "refresh_token"),
        };

        if (!string.IsNullOrWhiteSpace(secrets.ClientSecret))
            form.Add(new("client_secret", secrets.ClientSecret));

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed {(int)response.StatusCode}: {body}");

        GmailOAuthTokenResponse? token = JsonSerializer.Deserialize<GmailOAuthTokenResponse>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Invalid refresh token response.");

        DateTime expiresAtUtc = token.ExpiresIn > 0
            ? DateTime.UtcNow.AddSeconds(token.ExpiresIn)
            : DateTime.UtcNow.AddHours(1);

        return new GmailTokenData
        {
            AccessToken = token.AccessToken,
            RefreshToken = !string.IsNullOrWhiteSpace(token.RefreshToken) ? token.RefreshToken : existing.RefreshToken,
            ExpiresAtUtc = expiresAtUtc,
            Scope = !string.IsNullOrWhiteSpace(token.Scope) ? token.Scope : existing.Scope,
            TokenType = !string.IsNullOrWhiteSpace(token.TokenType) ? token.TokenType : existing.TokenType,
        };
    }

    public async Task<GmailOAuthTokenResponse> ExchangeCodeAsync(
        GmailOAuthSession session,
        string code,
        CancellationToken cancellationToken)
    {
        if (session == null)
        {
            throw new InvalidOperationException("Missing OAuth session");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Missing authorization code");
        }

        GmailClientSecrets secrets = LoadClientSecrets();
        if (string.IsNullOrWhiteSpace(secrets.ClientId))
        {
            throw new InvalidOperationException("Missing OAuth client id");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("code", code),
            new("client_id", secrets.ClientId),
            new("code_verifier", session.CodeVerifier),
            new("redirect_uri", session.RedirectUri),
            new("grant_type", "authorization_code"),
        };

        if (!string.IsNullOrWhiteSpace(secrets.ClientSecret))
        {
            form.Add(new KeyValuePair<string, string>("client_secret", secrets.ClientSecret));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("token_exchange_failed");
        }

        GmailOAuthTokenResponse? token = JsonSerializer.Deserialize<GmailOAuthTokenResponse>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("invalid_token_response");
        }

        return token;
    }

    private static string ResolveClientSecretsPath()
    {
        const string fileName = "google-gmail-oauth-client.json";

        string[] directCandidates =
        {
            @"D:\My Apps\AOS\Atlas.OS\Secrets\google-gmail-oauth-client.json",
        };

        foreach (string candidate in directCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrWhiteSpace(current); i++)
        {
            string candidate = Path.Combine(current, "Secrets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("OAuth client config not found");
    }

    private static GmailClientSecrets LoadClientSecrets()
    {
        string path = ResolveClientSecretsPath();
        string json = File.ReadAllText(path);

        GoogleInstalledRoot? root = JsonSerializer.Deserialize<GoogleInstalledRoot>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (root?.Installed == null || string.IsNullOrWhiteSpace(root.Installed.ClientId))
        {
            throw new InvalidOperationException("Invalid OAuth client config");
        }

        return new GmailClientSecrets
        {
            ClientId = root.Installed.ClientId,
            ClientSecret = root.Installed.ClientSecret ?? string.Empty,
        };
    }

    private static int PickFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string CreateState()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string> query)
    {
        string qs = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{baseUrl}?{qs}";
    }

    private static void WriteHtmlResponse(HttpListenerResponse response, string html)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private sealed class GmailClientSecrets
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }

    private sealed class GoogleInstalledRoot
    {
        [JsonPropertyName("installed")]
        public GoogleInstalledConfig? Installed { get; set; }
    }

    private sealed class GoogleInstalledConfig
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }
    }
}

internal sealed class GmailOAuthSession
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public int Port { get; set; }
    public string State { get; set; } = string.Empty;
}

internal sealed class GmailOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}
