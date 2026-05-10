using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Modules.Email.Services;

internal sealed class OutlookOAuthFlow
{
	private const string DefaultTenant = "common";
	private static readonly HttpClient Http = new();

	public static string ResolveClientSecretsPathPublic() => ResolveClientSecretsPath();

	public OutlookOAuthSession CreateAuthorizationSession(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing account id");
		}

		OutlookClientSecrets secrets = LoadClientSecrets();
		if (string.IsNullOrWhiteSpace(secrets.ClientId))
		{
			throw new InvalidOperationException("Missing OAuth client id");
		}

		int port = PickFreePort();
		string redirectUri = BuildRedirectUri(secrets.RedirectUri, port);
		string codeVerifier = GenerateCodeVerifier();
		string codeChallenge = CreateCodeChallenge(codeVerifier);
		string state = CreateState();
		string scope = string.Join(" ", secrets.Scopes);

		var query = new Dictionary<string, string>
		{
			["client_id"] = secrets.ClientId,
			["response_type"] = "code",
			["redirect_uri"] = redirectUri,
			["response_mode"] = "query",
			["scope"] = scope,
			["code_challenge"] = codeChallenge,
			["code_challenge_method"] = "S256",
			["state"] = state,
			["prompt"] = "select_account",
		};

		string tenant = string.IsNullOrWhiteSpace(secrets.TenantId) ? DefaultTenant : secrets.TenantId;
		string authEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize";
		string authorizationUrl = BuildUrl(authEndpoint, query);

		return new OutlookOAuthSession
		{
			AuthorizationUrl = authorizationUrl,
			RedirectUri = redirectUri,
			CodeVerifier = codeVerifier,
			Port = port,
			State = state,
		};
	}

	public async Task<string> WaitForCallbackAsync(
		OutlookOAuthSession session,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		if (session == null || string.IsNullOrWhiteSpace(session.RedirectUri))
		{
			throw new InvalidOperationException("Missing OAuth session");
		}

		using var listener = new HttpListener();
		listener.Prefixes.Add(session.RedirectUri);
		listener.Start();

		try
		{
			HttpListenerContext context = await listener.GetContextAsync().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

			try
			{
				string? errorName = context.Request.QueryString["error"];
				if (!string.IsNullOrWhiteSpace(errorName))
				{
					WriteHtmlResponse(context.Response, "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#991b1b;\"><h2>Outlook connect failed</h2><p>You can close this tab and retry from Atlas.</p></body></html>");
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

				WriteHtmlResponse(context.Response, "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;color:#1f2937;\"><h2>Atlas Outlook connect complete</h2><p>You can close this browser tab and return to Atlas.</p></body></html>");
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

	public async Task<OutlookOAuthTokenResponse> ExchangeCodeAsync(
		OutlookOAuthSession session,
		string code,
		CancellationToken cancellationToken)
	{
		if (session == null || string.IsNullOrWhiteSpace(code))
		{
			throw new InvalidOperationException("Missing authorization code");
		}

		OutlookClientSecrets secrets = LoadClientSecrets();
		string tenant = string.IsNullOrWhiteSpace(secrets.TenantId) ? DefaultTenant : secrets.TenantId;
		string tokenEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";

		var form = new List<KeyValuePair<string, string>>
		{
			new("client_id", secrets.ClientId),
			new("code", code),
			new("redirect_uri", session.RedirectUri),
			new("grant_type", "authorization_code"),
			new("code_verifier", session.CodeVerifier),
		};

		using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
		{
			Content = new FormUrlEncodedContent(form),
		};

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"token_exchange_failed: {(int)response.StatusCode}");
		}

		OutlookOAuthTokenResponse? token = JsonSerializer.Deserialize<OutlookOAuthTokenResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
		{
			throw new InvalidOperationException("invalid_token_response");
		}

		return token;
	}

	public async Task<OutlookTokenData> RefreshAccessTokenAsync(
		OutlookTokenData existing,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(existing?.RefreshToken))
		{
			throw new InvalidOperationException("No refresh token available.");
		}

		OutlookClientSecrets secrets = LoadClientSecrets();
		string tenant = string.IsNullOrWhiteSpace(secrets.TenantId) ? DefaultTenant : secrets.TenantId;
		string tokenEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";

		var form = new List<KeyValuePair<string, string>>
		{
			new("client_id", secrets.ClientId),
			new("refresh_token", existing.RefreshToken),
			new("grant_type", "refresh_token"),
			new("scope", string.Join(" ", secrets.Scopes)),
		};

		using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
		{
			Content = new FormUrlEncodedContent(form),
		};

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Token refresh failed {(int)response.StatusCode}: {body}");
		}

		OutlookOAuthTokenResponse? token = JsonSerializer.Deserialize<OutlookOAuthTokenResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
		{
			throw new InvalidOperationException("Invalid refresh token response.");
		}

		DateTime expiresAtUtc = token.ExpiresIn > 0
			? DateTime.UtcNow.AddSeconds(token.ExpiresIn)
			: DateTime.UtcNow.AddHours(1);

		return new OutlookTokenData
		{
			AccessToken = token.AccessToken,
			RefreshToken = !string.IsNullOrWhiteSpace(token.RefreshToken) ? token.RefreshToken : existing.RefreshToken,
			ExpiresAtUtc = expiresAtUtc,
			Scope = !string.IsNullOrWhiteSpace(token.Scope) ? token.Scope : existing.Scope,
			TokenType = !string.IsNullOrWhiteSpace(token.TokenType) ? token.TokenType : existing.TokenType,
		};
	}

	public async Task<OutlookProfileData> FetchProfileAsync(string accessToken, CancellationToken cancellationToken)
	{
		const string profileUrl = "https://graph.microsoft.com/v1.0/me?$select=displayName,mail,userPrincipalName";

		using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException("profile_fetch_failed");
		}

		OutlookMeResponse? profile = JsonSerializer.Deserialize<OutlookMeResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		string email = !string.IsNullOrWhiteSpace(profile?.Mail)
			? profile.Mail
			: profile?.UserPrincipalName ?? string.Empty;

		if (string.IsNullOrWhiteSpace(email))
		{
			throw new InvalidOperationException("profile_email_missing");
		}

		return new OutlookProfileData
		{
			Email = email,
			DisplayName = profile?.DisplayName ?? string.Empty,
		};
	}

	private static string ResolveClientSecretsPath()
	{
		const string fileName = "microsoft-outlook-oauth-client.json";

		string[] directCandidates =
		{
			@"D:\My Apps\AOS\Atlas.OS\Secrets\microsoft-outlook-oauth-client.json",
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

	private static OutlookClientSecrets LoadClientSecrets()
	{
		string path = ResolveClientSecretsPath();
		string json = File.ReadAllText(path);

		OutlookSecretsFile? file = JsonSerializer.Deserialize<OutlookSecretsFile>(
			json,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (file == null || string.IsNullOrWhiteSpace(file.ClientId))
		{
			throw new InvalidOperationException("Invalid OAuth client config");
		}

		return new OutlookClientSecrets
		{
			ClientId = file.ClientId,
			TenantId = string.IsNullOrWhiteSpace(file.TenantId) ? DefaultTenant : file.TenantId,
			RedirectUri = string.IsNullOrWhiteSpace(file.RedirectUri) ? "http://localhost" : file.RedirectUri,
			Scopes = file.Scopes is { Length: > 0 } ? file.Scopes : new[] { "offline_access", "User.Read", "Mail.Read" },
		};
	}

	private static string BuildRedirectUri(string configuredRedirectUri, int port)
	{
		if (!Uri.TryCreate(configuredRedirectUri, UriKind.Absolute, out Uri? uri))
		{
			return $"http://localhost:{port}/";
		}

		if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			return configuredRedirectUri.EndsWith("/", StringComparison.Ordinal) ? configuredRedirectUri : configuredRedirectUri + "/";
		}

		return $"{uri.Scheme}://localhost:{port}/";
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
		byte[] bytes = RandomNumberGenerator.GetBytes(24);
		return Base64UrlEncode(bytes);
	}

	private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string> query)
	{
		string qs = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
		return $"{baseUrl}?{qs}";
	}

	private static string Base64UrlEncode(byte[] bytes)
	{
		return Convert.ToBase64String(bytes)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private static void WriteHtmlResponse(HttpListenerResponse response, string html)
	{
		byte[] buffer = Encoding.UTF8.GetBytes(html);
		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength64 = buffer.Length;
		response.OutputStream.Write(buffer, 0, buffer.Length);
	}

	private sealed class OutlookClientSecrets
	{
		public string ClientId { get; init; } = string.Empty;
		public string TenantId { get; init; } = DefaultTenant;
		public string RedirectUri { get; init; } = "http://localhost";
		public string[] Scopes { get; init; } = Array.Empty<string>();
	}
}

internal sealed class OutlookOAuthSession
{
	public string AuthorizationUrl { get; set; } = string.Empty;
	public string RedirectUri { get; set; } = string.Empty;
	public string CodeVerifier { get; set; } = string.Empty;
	public int Port { get; set; }
	public string State { get; set; } = string.Empty;
}

internal sealed class OutlookOAuthTokenResponse
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

internal sealed class OutlookProfileData
{
	public string Email { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
}

internal sealed class OutlookSecretsFile
{
	[JsonPropertyName("client_id")]
	public string ClientId { get; set; } = string.Empty;

	[JsonPropertyName("tenant_id")]
	public string TenantId { get; set; } = string.Empty;

	[JsonPropertyName("redirect_uri")]
	public string RedirectUri { get; set; } = string.Empty;

	[JsonPropertyName("scopes")]
	public string[] Scopes { get; set; } = Array.Empty<string>();
}

internal sealed class OutlookMeResponse
{
	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("mail")]
	public string Mail { get; set; } = string.Empty;

	[JsonPropertyName("userPrincipalName")]
	public string UserPrincipalName { get; set; } = string.Empty;
}