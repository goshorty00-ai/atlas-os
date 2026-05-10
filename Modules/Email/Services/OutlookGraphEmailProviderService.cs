using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Modules.Email.Models;

namespace AtlasAI.Modules.Email.Services;

public sealed class OutlookGraphEmailProviderService : IEmailProviderService
{
	private static readonly HttpClient Http = new();

	public static string ResolveOAuthClientSecretsPathPublic() => OutlookOAuthFlow.ResolveClientSecretsPathPublic();

	public async Task<EmailAccountSummary> ConnectAccountAsync(string accountId, string email, CancellationToken cancellationToken)
	{
		string accessToken = await GetAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
		OutlookProfileData profile = await FetchProfileAsync(accessToken, cancellationToken).ConfigureAwait(false);
		int unread = await GetUnreadCountCoreAsync(accessToken, cancellationToken).ConfigureAwait(false);
		string resolvedEmail = !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email : (email ?? string.Empty).Trim();
		string displayName = !string.IsNullOrWhiteSpace(profile.DisplayName)
			? profile.DisplayName
			: (!string.IsNullOrWhiteSpace(resolvedEmail) && resolvedEmail.Contains('@', StringComparison.Ordinal)
				? resolvedEmail[..resolvedEmail.IndexOf('@')]
				: resolvedEmail);

		return new EmailAccountSummary(accountId, resolvedEmail, displayName, unread, true);
	}

	public async Task<int> GetUnreadCountAsync(string accountId, CancellationToken cancellationToken)
	{
		string accessToken = await GetAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
		return await GetUnreadCountCoreAsync(accessToken, cancellationToken).ConfigureAwait(false);
	}

	public Task<EmailMessageDetail> GetMessageAsync(string accountId, string messageId, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Outlook message detail is not wired yet.");
	}

	public async Task<OutlookMessageDetailResult> GetMessageDetailAsync(
		string accountId,
		string email,
		string messageId,
		CancellationToken cancellationToken)
	{
		_ = email;

		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing accountId");
		}

		if (string.IsNullOrWhiteSpace(messageId))
		{
			throw new InvalidOperationException("Missing messageId");
		}

		string accessToken = await GetAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
		string detailUrl =
			$"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}?$select=id,subject,from,receivedDateTime,isRead,bodyPreview,body,importance,webLink";

		using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Outlook message detail error {(int)response.StatusCode}: {body}");
		}

		OutlookMessageDetailResponse? parsed = JsonSerializer.Deserialize<OutlookMessageDetailResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
		{
			throw new InvalidOperationException("Outlook message detail payload is missing id");
		}

		DateTime? internalDateUtc = null;
		if (!string.IsNullOrWhiteSpace(parsed.ReceivedDateTime)
			&& DateTime.TryParse(parsed.ReceivedDateTime, out DateTime parsedReceived))
		{
			internalDateUtc = parsedReceived.ToUniversalTime();
		}

		string htmlBody = string.Empty;
		string plainText = string.Empty;
		string bodyText = string.Empty;
		string bodyPreview = parsed.BodyPreview ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(parsed.Body?.Content))
		{
			bool isHtml = string.Equals(parsed.Body.ContentType, "html", StringComparison.OrdinalIgnoreCase);
			if (isHtml)
			{
				htmlBody = parsed.Body.Content;
				plainText = StripHtml(parsed.Body.Content);
				bodyText = !string.IsNullOrWhiteSpace(bodyPreview) ? bodyPreview : plainText;
			}
			else
			{
				plainText = parsed.Body.Content;
				bodyText = !string.IsNullOrWhiteSpace(parsed.Body.Content) ? parsed.Body.Content : bodyPreview;
			}
		}

		if (string.IsNullOrWhiteSpace(plainText))
		{
			plainText = bodyPreview;
		}

		if (string.IsNullOrWhiteSpace(bodyText))
		{
			bodyText = !string.IsNullOrWhiteSpace(plainText) ? plainText : bodyPreview;
		}

		var detail = new EmailMessageDetail
		{
			Id = parsed.Id,
			ThreadId = string.Empty,
			From = FormatFrom(parsed.From),
			To = string.Empty,
			Subject = parsed.Subject ?? string.Empty,
			Date = parsed.ReceivedDateTime ?? string.Empty,
			Snippet = bodyPreview,
			BodyText = bodyText,
			PlainText = plainText,
			HtmlBody = htmlBody,
			InlineImagesCount = 0,
			Attachments = Array.Empty<EmailAttachmentDetail>(),
			Labels = Array.Empty<string>(),
		};

		return new OutlookMessageDetailResult
		{
			Detail = detail,
			Provider = "outlook",
			WebLink = parsed.WebLink ?? string.Empty,
			InternalDateUtc = internalDateUtc,
			IsUnread = !parsed.IsRead,
		};
	}

	public async Task<IReadOnlyList<GmailEmailMessageSummary>> GetInboxMessagesAsync(
		string accountId,
		string email,
		int maxResults,
		CancellationToken cancellationToken)
	{
		_ = email;

		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing accountId");
		}

		int cappedMax = Math.Max(1, maxResults);
		string accessToken = await GetAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
		string listUrl =
			$"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$top={cappedMax}&$orderby=receivedDateTime desc&$select=id,subject,from,receivedDateTime,isRead,bodyPreview,importance,webLink";

		using var request = new HttpRequestMessage(HttpMethod.Get, listUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Outlook inbox messages error {(int)response.StatusCode}: {body}");
		}

		OutlookMessagesResponse? parsed = JsonSerializer.Deserialize<OutlookMessagesResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (parsed?.Value == null || parsed.Value.Count == 0)
		{
			return Array.Empty<GmailEmailMessageSummary>();
		}

		var results = new List<GmailEmailMessageSummary>(parsed.Value.Count);
		foreach (OutlookInboxMessage item in parsed.Value)
		{
			if (string.IsNullOrWhiteSpace(item.Id))
			{
				continue;
			}

			DateTime? internalDateUtc = null;
			if (!string.IsNullOrWhiteSpace(item.ReceivedDateTime)
				&& DateTime.TryParse(item.ReceivedDateTime, out DateTime parsedReceived))
			{
				internalDateUtc = parsedReceived.ToUniversalTime();
			}

			results.Add(new GmailEmailMessageSummary
			{
				Id = item.Id,
				ThreadId = string.Empty,
				From = FormatFrom(item.From),
				Subject = item.Subject ?? string.Empty,
				Date = item.ReceivedDateTime ?? string.Empty,
				Snippet = item.BodyPreview ?? string.Empty,
				InternalDateUtc = internalDateUtc,
				IsUnread = !item.IsRead,
				LabelIds = Array.Empty<string>(),
			});
		}

		return results;
	}

	private static async Task<string> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
	{
		OutlookTokenData? tokenData = OutlookOAuthTokenStore.LoadTokenData(accountId);
		if (tokenData != null && !string.IsNullOrWhiteSpace(tokenData.AccessToken))
		{
			bool needsRefresh = tokenData.ExpiresAtUtc != DateTime.MinValue
				&& DateTime.UtcNow >= tokenData.ExpiresAtUtc.AddMinutes(-5);

			if (needsRefresh && !string.IsNullOrWhiteSpace(tokenData.RefreshToken))
			{
				try
				{
					var flow = new OutlookOAuthFlow();
					OutlookTokenData refreshed = await flow.RefreshAccessTokenAsync(tokenData, cancellationToken).ConfigureAwait(false);
					OutlookOAuthTokenStore.SaveTokenData(accountId, refreshed);
					tokenData = refreshed;
				}
				catch
				{
				}
			}

			return tokenData.AccessToken;
		}

		throw new InvalidOperationException($"Outlook access token is missing for account '{accountId}'. Connect Microsoft Graph first.");
	}

	private static async Task<OutlookProfileData> FetchProfileAsync(string accessToken, CancellationToken cancellationToken)
	{
		const string profileUrl = "https://graph.microsoft.com/v1.0/me?$select=displayName,mail,userPrincipalName";
		using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Outlook profile error {(int)response.StatusCode}: {body}");
		}

		OutlookMeResponse? profile = JsonSerializer.Deserialize<OutlookMeResponse>(
			body,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		return new OutlookProfileData
		{
			Email = !string.IsNullOrWhiteSpace(profile?.Mail) ? profile.Mail : profile?.UserPrincipalName ?? string.Empty,
			DisplayName = profile?.DisplayName ?? string.Empty,
		};
	}

	private static async Task<int> GetUnreadCountCoreAsync(string accessToken, CancellationToken cancellationToken)
	{
		const string inboxUrl = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox?$select=unreadItemCount";
		using var request = new HttpRequestMessage(HttpMethod.Get, inboxUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Outlook inbox error {(int)response.StatusCode}: {body}");
		}

		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("unreadItemCount", out JsonElement unreadEl) && unreadEl.ValueKind == JsonValueKind.Number)
		{
			return unreadEl.GetInt32();
		}

		throw new InvalidOperationException("Outlook unread count is missing");
	}

	private static string FormatFrom(OutlookRecipient? from)
	{
		string name = from?.EmailAddress?.Name?.Trim() ?? string.Empty;
		string address = from?.EmailAddress?.Address?.Trim() ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(address))
		{
			return $"{name} <{address}>";
		}

		if (!string.IsNullOrWhiteSpace(address))
		{
			return address;
		}

		return name;
	}

	private static string StripHtml(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string withoutTags = Regex.Replace(value, "<[^>]+>", " ");
		string decoded = WebUtility.HtmlDecode(withoutTags);
		return Regex.Replace(decoded, "\\s+", " ").Trim();
	}

	private sealed class OutlookMessagesResponse
	{
		public List<OutlookInboxMessage> Value { get; set; } = new();
	}

	private sealed class OutlookInboxMessage
	{
		public string Id { get; set; } = string.Empty;
		public string Subject { get; set; } = string.Empty;
		public OutlookRecipient? From { get; set; }
		public string ReceivedDateTime { get; set; } = string.Empty;
		public bool IsRead { get; set; }
		public string BodyPreview { get; set; } = string.Empty;
		public string Importance { get; set; } = string.Empty;
		public string WebLink { get; set; } = string.Empty;
	}

	private sealed class OutlookMessageDetailResponse
	{
		public string Id { get; set; } = string.Empty;
		public string Subject { get; set; } = string.Empty;
		public OutlookRecipient? From { get; set; }
		public string ReceivedDateTime { get; set; } = string.Empty;
		public bool IsRead { get; set; }
		public string BodyPreview { get; set; } = string.Empty;
		public OutlookMessageBody? Body { get; set; }
		public string Importance { get; set; } = string.Empty;
		public string WebLink { get; set; } = string.Empty;
	}

	private sealed class OutlookMessageBody
	{
		public string ContentType { get; set; } = string.Empty;
		public string Content { get; set; } = string.Empty;
	}

	private sealed class OutlookRecipient
	{
		public OutlookEmailAddress? EmailAddress { get; set; }
	}

	private sealed class OutlookEmailAddress
	{
		public string Name { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
	}
}

public sealed class OutlookMessageDetailResult
{
	public EmailMessageDetail Detail { get; set; } = new();
	public string Provider { get; set; } = "outlook";
	public string WebLink { get; set; } = string.Empty;
	public DateTime? InternalDateUtc { get; set; }
	public bool IsUnread { get; set; }
}