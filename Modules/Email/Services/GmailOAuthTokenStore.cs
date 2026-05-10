using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Modules.Email.Services;

/// <summary>
/// Full OAuth token data stored as JSON. All fields are optional for
/// backwards-compatibility with callers that only have an access token.
/// </summary>
internal sealed class GmailTokenData
{
	[JsonPropertyName("access_token")]
	public string AccessToken { get; set; } = string.Empty;

	[JsonPropertyName("refresh_token")]
	public string RefreshToken { get; set; } = string.Empty;

	[JsonPropertyName("expires_at_utc")]
	public DateTime ExpiresAtUtc { get; set; } = DateTime.MinValue;

	[JsonPropertyName("scope")]
	public string Scope { get; set; } = string.Empty;

	[JsonPropertyName("token_type")]
	public string TokenType { get; set; } = string.Empty;
}

/// <summary>
/// File-based store for Gmail OAuth tokens. Supports both a legacy plain-text
/// access-token file and a richer JSON format (<see cref="GmailTokenData"/>).
/// </summary>
internal static class GmailOAuthTokenStore
{
	private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

	private static string TokenDirectory() => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"AtlasOS", "Email", "tokens");

	private static string TokenPath(string accountId)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string safe = string.Join("_", accountId.Split(invalid));
		return Path.Combine(TokenDirectory(), $"{safe}.token");
	}

	// ── JSON token data ────────────────────────────────────────────────────

	/// <summary>Persists full <see cref="GmailTokenData"/> for <paramref name="accountId"/>.</summary>
	public static void SaveTokenData(string accountId, GmailTokenData data)
	{
		if (string.IsNullOrWhiteSpace(accountId) || data == null || string.IsNullOrWhiteSpace(data.AccessToken))
			return;
		try
		{
			Directory.CreateDirectory(TokenDirectory());
			string json = JsonSerializer.Serialize(data, JsonOpts);
			File.WriteAllText(TokenPath(accountId), json);
		}
		catch { /* best-effort */ }
	}

	/// <summary>
	/// Returns stored <see cref="GmailTokenData"/> for <paramref name="accountId"/>,
	/// or null if absent, unreadable, or legacy plain-text format.
	/// </summary>
	public static GmailTokenData? LoadTokenData(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId)) return null;
		try
		{
			string path = TokenPath(accountId);
			if (!File.Exists(path)) return null;
			string text = File.ReadAllText(path).Trim();
			if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("{", StringComparison.Ordinal))
				return null;
			GmailTokenData? data = JsonSerializer.Deserialize<GmailTokenData>(text, JsonOpts);
			if (data == null || string.IsNullOrWhiteSpace(data.AccessToken))
				return null;
			return data;
		}
		catch { return null; }
	}

	// ── Legacy plain-text access token API (preserved for existing callers) ─

	/// <summary>Persists <paramref name="accessToken"/> for <paramref name="accountId"/>.</summary>
	public static void Save(string accountId, string accessToken)
	{
		if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
			return;
		try
		{
			Directory.CreateDirectory(TokenDirectory());
			File.WriteAllText(TokenPath(accountId), accessToken.Trim());
		}
		catch { /* best-effort */ }
	}

	/// <summary>
	/// Returns the stored access token for <paramref name="accountId"/>, or null if absent.
	/// Tries JSON format first; falls back to plain-text for legacy files.
	/// </summary>
	public static string? Load(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId)) return null;
		try
		{
			// Try rich JSON format first.
			GmailTokenData? data = LoadTokenData(accountId);
			if (data != null && !string.IsNullOrWhiteSpace(data.AccessToken))
				return data.AccessToken;

			// Fall back to legacy plain-text.
			string path = TokenPath(accountId);
			if (!File.Exists(path)) return null;
			string token = File.ReadAllText(path).Trim();
			return string.IsNullOrWhiteSpace(token) ? null : token;
		}
		catch { return null; }
	}

	/// <summary>Removes the stored token for <paramref name="accountId"/>.</summary>
	public static void Delete(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId)) return;
		try
		{
			string path = TokenPath(accountId);
			if (File.Exists(path)) File.Delete(path);
		}
		catch { /* best-effort */ }
	}
}
