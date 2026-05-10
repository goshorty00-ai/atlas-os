using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasAI.Modules.Email.Services;

internal sealed class OutlookTokenData
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

internal static class OutlookOAuthTokenStore
{
	private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

	private static string TokenDirectory() => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"AtlasOS", "Email", "tokens");

	private static string TokenPath(string accountId)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string safe = string.Join("_", accountId.Split(invalid));
		return Path.Combine(TokenDirectory(), $"{safe}.outlook.token");
	}

	public static void SaveTokenData(string accountId, OutlookTokenData data)
	{
		if (string.IsNullOrWhiteSpace(accountId) || data == null || string.IsNullOrWhiteSpace(data.AccessToken))
			return;

		try
		{
			Directory.CreateDirectory(TokenDirectory());
			string json = JsonSerializer.Serialize(data, JsonOpts);
			File.WriteAllText(TokenPath(accountId), json);
		}
		catch
		{
		}
	}

	public static OutlookTokenData? LoadTokenData(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId))
			return null;

		try
		{
			string path = TokenPath(accountId);
			if (!File.Exists(path))
				return null;

			string text = File.ReadAllText(path).Trim();
			if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("{", StringComparison.Ordinal))
				return null;

			OutlookTokenData? data = JsonSerializer.Deserialize<OutlookTokenData>(text, JsonOpts);
			if (data == null || string.IsNullOrWhiteSpace(data.AccessToken))
				return null;

			return data;
		}
		catch
		{
			return null;
		}
	}

	public static string? Load(string accountId)
	{
		return LoadTokenData(accountId)?.AccessToken;
	}

	public static void Delete(string accountId)
	{
		if (string.IsNullOrWhiteSpace(accountId))
			return;

		try
		{
			string path = TokenPath(accountId);
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
		}
	}
}