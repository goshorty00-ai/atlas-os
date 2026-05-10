using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Modules.Email.Models;

namespace AtlasAI.Modules.Email.Services;

public sealed class GmailApiEmailProviderService : IEmailProviderService
{
	private const string RequiredScopes = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.compose https://www.googleapis.com/auth/gmail.modify";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private static readonly HttpClient Http = new();

	public static string ResolveOAuthClientSecretsPathPublic() => "gmail-readonly-compose-modify-scope-configured";

	public async Task<EmailAccountSummary> ConnectAccountAsync(string accountId, string email, CancellationToken cancellationToken)
	{
		int unread = await GetUnreadCountAsync(accountId, email, cancellationToken).ConfigureAwait(false);
		string displayName = !string.IsNullOrWhiteSpace(email) && email.Contains('@', StringComparison.Ordinal)
			? email[..email.IndexOf('@')]
			: email;

		return new EmailAccountSummary(accountId, email, displayName, unread, true);
	}

	public async Task<int> GetUnreadCountAsync(string accountId, string email, CancellationToken cancellationToken)
	{
		string accessToken = await GetAccessTokenAsync(accountId, email, cancellationToken).ConfigureAwait(false);
		const string primaryUnreadQuery = "in:inbox category:primary is:unread";
		string encodedQuery = Uri.EscapeDataString(primaryUnreadQuery);
		const int pageSize = 100;
		int totalFetched = 0;
		bool capped = false;
		string? nextPageToken = null;

		do
		{
			string listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?q={encodedQuery}&maxResults={pageSize}";
			if (!string.IsNullOrWhiteSpace(nextPageToken))
			{
				listUrl += $"&pageToken={Uri.EscapeDataString(nextPageToken)}";
			}

			using JsonDocument doc = await GetJsonAsync(listUrl, accessToken, cancellationToken).ConfigureAwait(false);
			int pageCount = 0;
			if (doc.RootElement.TryGetProperty("messages", out JsonElement messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement _ in messagesEl.EnumerateArray())
				{
					pageCount++;
				}
			}

			totalFetched += pageCount;
			nextPageToken = GetString(doc.RootElement, "nextPageToken");
			if (totalFetched >= 100 && !string.IsNullOrWhiteSpace(nextPageToken))
			{
				capped = true;
				break;
			}
		}
		while (!string.IsNullOrWhiteSpace(nextPageToken));

		int unread = capped ? 99 : Math.Min(totalFetched, 99);
		LogMessageList($"[EmailGmailUnread] accountId={accountId} source=primary-query unread={unread} fetched={totalFetched} capped={capped}");
		return unread;
	}

	public async Task<IReadOnlyList<EmailMessageSummary>> GetRecentMessagesAsync(string accountId, string email, int maxResults, CancellationToken cancellationToken)
	{
		string accessToken = await GetAccessTokenAsync(accountId, email, cancellationToken).ConfigureAwait(false);
		string listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={Math.Max(1, maxResults)}";
		using JsonDocument listDoc = await GetJsonAsync(listUrl, accessToken, cancellationToken).ConfigureAwait(false);

		if (!listDoc.RootElement.TryGetProperty("messages", out JsonElement messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<EmailMessageSummary>();
		}

		var results = new List<EmailMessageSummary>();
		foreach (JsonElement item in messagesEl.EnumerateArray())
		{
			string messageId = GetString(item, "id");
			if (string.IsNullOrWhiteSpace(messageId))
			{
				continue;
			}

			string detailUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date";
			using JsonDocument detailDoc = await GetJsonAsync(detailUrl, accessToken, cancellationToken).ConfigureAwait(false);
			JsonElement root = detailDoc.RootElement;
			JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

			IReadOnlyList<string> labels = GetStringArray(root, "labelIds");
			bool unread = labels.Any(l => string.Equals(l, "UNREAD", StringComparison.OrdinalIgnoreCase));

			results.Add(new EmailMessageSummary(
				Id: messageId,
				ThreadId: GetString(root, "threadId"),
				From: GetHeader(payload, "From"),
				Subject: GetHeader(payload, "Subject"),
				Snippet: GetString(root, "snippet"),
				Date: NormalizeDate(GetHeader(payload, "Date")),
				Unread: unread));
		}

		return results;
	}

	public async Task<IReadOnlyList<GmailEmailMessageSummary>> GetInboxMessagesAsync(
		string accountId,
		int maxResults = 25,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing accountId");
		}

		int cappedMax = Math.Max(1, maxResults);
		string accessToken = await GetAccessTokenAsync(accountId, string.Empty, cancellationToken).ConfigureAwait(false);
		const string primaryInboxQuery = "in:inbox category:primary";
		string encodedQuery = Uri.EscapeDataString(primaryInboxQuery);
		string listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?q={encodedQuery}&maxResults={cappedMax}";
		using JsonDocument listDoc = await GetJsonAsync(listUrl, accessToken, cancellationToken).ConfigureAwait(false);

		if (!listDoc.RootElement.TryGetProperty("messages", out JsonElement messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
		{
			LogMessageList($"[EmailGmailMessages] accountId={accountId} step=list count=0");
			return Array.Empty<GmailEmailMessageSummary>();
		}

		var messageIds = messagesEl.EnumerateArray()
			.Select(item => GetString(item, "id"))
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.ToList();

		LogMessageList($"[EmailGmailMessages] accountId={accountId} step=list q=\"in:inbox category:primary\" count={messageIds.Count}");

		var results = new List<GmailEmailMessageSummary>(messageIds.Count);
		foreach (string messageId in messageIds)
		{
			string detailUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date";
			using JsonDocument detailDoc = await GetJsonAsync(detailUrl, accessToken, cancellationToken).ConfigureAwait(false);
			JsonElement root = detailDoc.RootElement;
			JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

			IReadOnlyList<string> labels = GetStringArray(root, "labelIds");
			bool isUnread = labels.Any(l => string.Equals(l, "UNREAD", StringComparison.OrdinalIgnoreCase));
			string subject = GetHeader(payload, "Subject");

			results.Add(new GmailEmailMessageSummary
			{
				Id = GetString(root, "id"),
				ThreadId = GetString(root, "threadId"),
				From = GetHeader(payload, "From"),
				Subject = subject,
				Date = GetHeader(payload, "Date"),
				Snippet = GetString(root, "snippet"),
				InternalDateUtc = ParseInternalDateUtc(root),
				IsUnread = isUnread,
				LabelIds = labels,
			});

			LogMessageList($"[EmailGmailMessages] accountId={accountId} step=metadata id={messageId} unread={isUnread} subjectLength={subject.Length}");
		}

		return results;
	}

	public async Task<EmailMessageDetail> GetMessageAsync(string accountId, string messageId, CancellationToken cancellationToken)
	{
		return await GetMessageDetailAsync(accountId, messageId, cancellationToken).ConfigureAwait(false);
	}

	public async Task<EmailMessageDetail> GetMessageDetailAsync(string accountId, string messageId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing accountId");
		}

		if (string.IsNullOrWhiteSpace(messageId))
		{
			throw new InvalidOperationException("Missing messageId");
		}

		string accessToken = await GetAccessTokenAsync(accountId, string.Empty, cancellationToken).ConfigureAwait(false);
		string url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=full";
		using JsonDocument doc = await GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);

		JsonElement root = doc.RootElement;
		JsonElement payload = root.TryGetProperty("payload", out JsonElement p) ? p : default;

		string snippet = GetString(root, "snippet");
		var parse = new MessageParseState();
		await ParsePartRecursiveAsync(payload, accessToken, messageId, parse, cancellationToken).ConfigureAwait(false);

		string plainBodyRaw = SelectBestBody(parse.PlainBodies);
		string htmlBodyRaw = SelectBestBody(parse.HtmlBodies);
		string htmlBodyWithCid = ReplaceCidReferences(htmlBodyRaw, parse.InlineImagesByContentId);
		string safeHtmlBody = ApplyReaderHtmlStyles(SanitizeHtml(htmlBodyWithCid));
		string plainText = !string.IsNullOrWhiteSpace(plainBodyRaw)
			? CleanPlainText(plainBodyRaw)
			: (!string.IsNullOrWhiteSpace(safeHtmlBody) ? StripHtml(safeHtmlBody) : CleanPlainText(snippet));
		string bodyText = plainText;

		return new EmailMessageDetail
		{
			Id = GetString(root, "id"),
			ThreadId = GetString(root, "threadId"),
			From = GetHeader(payload, "From"),
			To = GetHeader(payload, "To"),
			Subject = GetHeader(payload, "Subject"),
			Date = NormalizeDate(GetHeader(payload, "Date")),
			Snippet = snippet,
			BodyText = bodyText,
			PlainText = plainText,
			HtmlBody = safeHtmlBody,
			InlineImagesCount = parse.InlineImagesByContentId.Count,
			Attachments = parse.Attachments,
			Labels = GetStringArray(root, "labelIds"),
		};
	}

	/// <summary>Returns the Gmail label id for <paramref name="labelName"/>, creating it if it does not exist.</summary>
	public async Task<string> EnsureLabelAsync(string accountId, string labelName, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(labelName))
			throw new InvalidOperationException("Label name is required.");

		string accessToken = await GetAccessTokenAsync(accountId, string.Empty, cancellationToken).ConfigureAwait(false);

		// List existing labels.
		using JsonDocument listDoc = await GetJsonAsync(
			"https://gmail.googleapis.com/gmail/v1/users/me/labels",
			accessToken,
			cancellationToken).ConfigureAwait(false);

		if (listDoc.RootElement.TryGetProperty("labels", out JsonElement labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement lbl in labelsEl.EnumerateArray())
			{
				string existing = GetString(lbl, "name");
				if (string.Equals(existing, labelName, StringComparison.OrdinalIgnoreCase))
					return GetString(lbl, "id");
			}
		}

		// Create new user label.
		string created = await PostJsonAsync(
			"https://gmail.googleapis.com/gmail/v1/users/me/labels",
			accessToken,
			new { name = labelName, labelListVisibility = "labelShow", messageListVisibility = "show" },
			cancellationToken).ConfigureAwait(false);

		using JsonDocument createdDoc = JsonDocument.Parse(created);
		string newId = GetString(createdDoc.RootElement, "id");
		if (string.IsNullOrWhiteSpace(newId))
			throw new InvalidOperationException($"Gmail label creation returned no id for '{labelName}'.");

		return newId;
	}

	/// <summary>Applies <paramref name="labelId"/> to a single message using messages.modify (addLabelIds only, no removals).</summary>
	public async Task<GmailApplyLabelResult> ApplyLabelToMessageAsync(
		string accountId,
		string messageId,
		string labelId,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(accountId))
			throw new InvalidOperationException("Missing accountId.");
		if (string.IsNullOrWhiteSpace(messageId))
			throw new InvalidOperationException("Missing messageId.");
		if (string.IsNullOrWhiteSpace(labelId))
			throw new InvalidOperationException("Missing labelId.");

		string accessToken = await GetAccessTokenAsync(accountId, string.Empty, cancellationToken).ConfigureAwait(false);
		string url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}/modify";

		string response = await PostJsonAsync(
			url,
			accessToken,
			new { addLabelIds = new[] { labelId }, removeLabelIds = Array.Empty<string>() },
			cancellationToken).ConfigureAwait(false);

		using JsonDocument doc = JsonDocument.Parse(response);
		return new GmailApplyLabelResult
		{
			MessageId = messageId,
			LabelId = labelId,
			Status = "applied",
			AppliedLabelIds = GetStringArray(doc.RootElement, "labelIds").ToList(),
		};
	}

	public async Task<GmailDraftCreateResult> CreateDraftAsync(
		string accountId,
		string email,
		GmailDraftCreateRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(accountId))
		{
			throw new InvalidOperationException("Missing accountId");
		}

		if (request == null)
		{
			throw new InvalidOperationException("Missing draft request");
		}

		if (string.IsNullOrWhiteSpace(request.To))
		{
			throw new InvalidOperationException("Draft recipient is missing.");
		}

		string accessToken = await GetAccessTokenAsync(accountId, email, cancellationToken).ConfigureAwait(false);
		string rawMessage = BuildDraftMimeMessage(request);
		string rawEncoded = EncodeBase64Url(Encoding.UTF8.GetBytes(rawMessage));

		var payload = new
		{
			message = new
			{
				raw = rawEncoded,
				threadId = string.IsNullOrWhiteSpace(request.ThreadId) ? null : request.ThreadId,
			},
		};

		string response = await PostJsonAsync(
			"https://gmail.googleapis.com/gmail/v1/users/me/drafts",
			accessToken,
			payload,
			cancellationToken).ConfigureAwait(false);

		GmailDraftCreateResponse? parsed = JsonSerializer.Deserialize<GmailDraftCreateResponse>(response, JsonOptions);
		if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
		{
			throw new InvalidOperationException("Gmail draft creation returned no draft id.");
		}

		return new GmailDraftCreateResult
		{
			DraftId = parsed.Id,
			MessageId = parsed.Message?.Id ?? string.Empty,
			ThreadId = parsed.Message?.ThreadId ?? request.ThreadId ?? string.Empty,
			Status = "created",
		};
	}

	private sealed class MessageParseState
	{
		public List<string> PlainBodies { get; } = new();
		public List<string> HtmlBodies { get; } = new();
		public Dictionary<string, string> InlineImagesByContentId { get; } = new(StringComparer.OrdinalIgnoreCase);
		public List<EmailAttachmentDetail> Attachments { get; } = new();
	}

	private async Task ParsePartRecursiveAsync(
		JsonElement part,
		string accessToken,
		string messageId,
		MessageParseState state,
		CancellationToken cancellationToken)
	{
		if (part.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		string mimeType = GetString(part, "mimeType");
		string fileName = GetString(part, "filename");
		Dictionary<string, string> headers = GetHeadersMap(part);
		string contentIdRaw = headers.TryGetValue("Content-ID", out string? cid) ? cid : string.Empty;
		string contentId = NormalizeContentId(contentIdRaw);
		string contentDisposition = headers.TryGetValue("Content-Disposition", out string? disposition) ? disposition : string.Empty;

		if (part.TryGetProperty("parts", out JsonElement nestedParts) && nestedParts.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement child in nestedParts.EnumerateArray())
			{
				await ParsePartRecursiveAsync(child, accessToken, messageId, state, cancellationToken).ConfigureAwait(false);
			}
		}

		string attachmentId = GetBodyAttachmentId(part);
		int size = GetBodySize(part);
		byte[]? bodyBytes = TryGetBodyDataBytes(part);
		bool isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
		bool isInline = !string.IsNullOrWhiteSpace(contentId)
			|| contentDisposition.Contains("inline", StringComparison.OrdinalIgnoreCase);

		if (string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
		{
			string text = TryDecodeUtf8(bodyBytes);
			if (!string.IsNullOrWhiteSpace(text))
			{
				state.PlainBodies.Add(text);
			}
		}

		if (string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase))
		{
			string html = TryDecodeUtf8(bodyBytes);
			if (!string.IsNullOrWhiteSpace(html))
			{
				state.HtmlBodies.Add(html);
			}
		}

		if (isImage && isInline)
		{
			byte[] inlineBytes = bodyBytes ?? (await TryGetAttachmentBytesAsync(accessToken, messageId, attachmentId, cancellationToken).ConfigureAwait(false) ?? Array.Empty<byte>());
			if (!string.IsNullOrWhiteSpace(contentId) && inlineBytes.Length > 0)
			{
				string dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(inlineBytes)}";
				state.InlineImagesByContentId[contentId] = dataUrl;
			}
		}

		if (!string.IsNullOrWhiteSpace(attachmentId)
			|| !string.IsNullOrWhiteSpace(fileName)
			|| isImage)
		{
			state.Attachments.Add(new EmailAttachmentDetail
			{
				FileName = fileName,
				MimeType = mimeType,
				AttachmentId = attachmentId,
				ContentId = contentId,
				Size = size,
				IsInline = isInline,
			});
		}
	}

	private static Dictionary<string, string> GetHeadersMap(JsonElement part)
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!part.TryGetProperty("headers", out JsonElement headers) || headers.ValueKind != JsonValueKind.Array)
		{
			return map;
		}

		foreach (JsonElement header in headers.EnumerateArray())
		{
			string name = GetString(header, "name");
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			map[name] = GetString(header, "value");
		}

		return map;
	}

	private static string GetBodyAttachmentId(JsonElement part)
	{
		if (!part.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
		{
			return string.Empty;
		}

		return GetString(body, "attachmentId");
	}

	private static int GetBodySize(JsonElement part)
	{
		if (!part.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
		{
			return 0;
		}

		if (!body.TryGetProperty("size", out JsonElement sizeEl))
		{
			return 0;
		}

		return sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt32(out int size) ? size : 0;
	}

	private static byte[]? TryGetBodyDataBytes(JsonElement part)
	{
		if (!part.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		string data = GetString(body, "data");
		return string.IsNullOrWhiteSpace(data) ? null : DecodeBase64UrlToBytes(data);
	}

	private static string TryDecodeUtf8(byte[]? bytes)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return string.Empty;
		}

		return Encoding.UTF8.GetString(bytes);
	}

	private async Task<byte[]?> TryGetAttachmentBytesAsync(string accessToken, string messageId, string attachmentId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(attachmentId))
		{
			return null;
		}

		string url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}";
		using JsonDocument doc = await GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
		string data = GetString(doc.RootElement, "data");
		return string.IsNullOrWhiteSpace(data) ? null : DecodeBase64UrlToBytes(data);
	}

	private static string SelectBestBody(IReadOnlyList<string> bodies)
	{
		if (bodies.Count == 0)
		{
			return string.Empty;
		}

		return bodies.OrderByDescending(v => v.Length).FirstOrDefault() ?? string.Empty;
	}

	private static string NormalizeContentId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string trimmed = value.Trim();
		if (trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.EndsWith(">", StringComparison.Ordinal) && trimmed.Length > 2)
		{
			trimmed = trimmed[1..^1];
		}

		if (trimmed.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed[4..];
		}

		return trimmed;
	}

	private static string ReplaceCidReferences(string htmlBody, IReadOnlyDictionary<string, string> inlineByCid)
	{
		if (string.IsNullOrWhiteSpace(htmlBody) || inlineByCid.Count == 0)
		{
			return htmlBody;
		}

		return Regex.Replace(
			htmlBody,
			"cid:([^\"'>\\s]+)",
			match =>
			{
				string raw = match.Groups[1].Value;
				string cid = NormalizeContentId(Uri.UnescapeDataString(raw));
				return inlineByCid.TryGetValue(cid, out string? dataUrl) ? dataUrl : match.Value;
			},
			RegexOptions.IgnoreCase);
	}

	private static string SanitizeHtml(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}

		string sanitized = html;
		sanitized = Regex.Replace(sanitized, "<script\\b[^>]*>[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "<iframe\\b[^>]*>[\\s\\S]*?</iframe>", string.Empty, RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "<object\\b[^>]*>[\\s\\S]*?</object>", string.Empty, RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "<embed\\b[^>]*>(?:</embed>)?", string.Empty, RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "\\son[a-z]+\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)", string.Empty, RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "\\s(href|src)\\s*=\\s*\"\\s*javascript:[^\"]*\"", " $1=\"#\"", RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "\\s(href|src)\\s*=\\s*'\\s*javascript:[^']*'", " $1='#'", RegexOptions.IgnoreCase);
		sanitized = Regex.Replace(sanitized, "\\s(href|src)\\s*=\\s*javascript:[^\\s>]+", " $1=#", RegexOptions.IgnoreCase);
		return sanitized;
	}

	private static string ApplyReaderHtmlStyles(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}

		const string styleBlock = "<style>.atlas-email-html img{max-width:100%;height:auto;border-radius:12px}.atlas-email-html table{max-width:100%;display:block;overflow-x:auto}.atlas-email-html a{color:rgba(34,211,238,.9);overflow-wrap:anywhere;word-break:break-word}</style>";
		return styleBlock + "<div class=\"atlas-email-html\">" + html + "</div>";
	}

	private static async Task<JsonDocument> GetJsonAsync(string url, string accessToken, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Gmail API error {(int)response.StatusCode}: {content}");
		}

		return JsonDocument.Parse(content);
	}

	private static async Task<string> PostJsonAsync(string url, string accessToken, object payload, CancellationToken cancellationToken)
	{
		string json = JsonSerializer.Serialize(payload);
		using var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Gmail API error {(int)response.StatusCode}: {content}");
		}

		return content;
	}

	private static string BuildDraftMimeMessage(GmailDraftCreateRequest request)
	{
		var builder = new StringBuilder();
		builder.Append("To: ").Append(request.To.Trim()).Append("\r\n");
		builder.Append("Subject: ").Append((request.Subject ?? string.Empty).Trim()).Append("\r\n");
		if (!string.IsNullOrWhiteSpace(request.InReplyTo))
		{
			builder.Append("In-Reply-To: ").Append(request.InReplyTo.Trim()).Append("\r\n");
			builder.Append("References: ").Append(request.InReplyTo.Trim()).Append("\r\n");
		}
		builder.Append("Content-Type: text/plain; charset=\"UTF-8\"\r\n");
		builder.Append("MIME-Version: 1.0\r\n");
		builder.Append("\r\n");
		builder.Append(NormalizeMimeBody(request.Body));
		return builder.ToString();
	}

	private static string NormalizeMimeBody(string body)
	{
		string normalized = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		return normalized.Replace("\n", "\r\n", StringComparison.Ordinal);
	}

	private static string EncodeBase64Url(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}

	private static string GetString(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out JsonElement value))
		{
			return string.Empty;
		}

		return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
	}

	private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<string>();
		}

		var list = new List<string>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				list.Add(item.GetString() ?? string.Empty);
			}
		}

		return list;
	}

	private static string GetHeader(JsonElement payload, string headerName)
	{
		if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("headers", out JsonElement headers) || headers.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}

		foreach (JsonElement header in headers.EnumerateArray())
		{
			string name = GetString(header, "name");
			if (!string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			return GetString(header, "value");
		}

		return string.Empty;
	}

	private static string DecodeBase64Url(string value)
	{
		byte[] data = DecodeBase64UrlToBytes(value);
		return Encoding.UTF8.GetString(data);
	}

	private static byte[] DecodeBase64UrlToBytes(string value)
	{
		string normalized = value.Replace('-', '+').Replace('_', '/');
		int padding = 4 - (normalized.Length % 4);
		if (padding is > 0 and < 4)
		{
			normalized = normalized.PadRight(normalized.Length + padding, '=');
		}

		return Convert.FromBase64String(normalized);
	}

	private static string CleanPlainText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		// Gmail embeds inline image alt text as [image: alt]. Strip these markers.
		string cleaned = Regex.Replace(text, @"\[image:[^\]]*\]", string.Empty, RegexOptions.IgnoreCase);
		return Regex.Replace(cleaned, @"\s+", " ").Trim();
	}

	private static string StripHtml(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}

		string noTags = Regex.Replace(html, "<.*?>", " ", RegexOptions.Singleline);
		string decoded = System.Net.WebUtility.HtmlDecode(noTags);
		return Regex.Replace(decoded, "\\s+", " ").Trim();
	}

	private static string NormalizeDate(string dateHeader)
	{
		if (DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset dto))
		{
			return dto.ToString("o", CultureInfo.InvariantCulture);
		}

		return dateHeader;
	}

	private static DateTime? ParseInternalDateUtc(JsonElement root)
	{
		if (!root.TryGetProperty("internalDate", out JsonElement value))
		{
			return null;
		}

		long millis;
		if (value.ValueKind == JsonValueKind.String)
		{
			string raw = value.GetString() ?? string.Empty;
			if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out millis))
			{
				return null;
			}
		}
		else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long numeric))
		{
			millis = numeric;
		}
		else
		{
			return null;
		}

		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
		}
		catch
		{
			return null;
		}
	}

	private static void LogMessageList(string message)
	{
		try
		{
			System.Diagnostics.Debug.WriteLine(message);
		}
		catch
		{
		}
	}

	private static async Task<string> GetAccessTokenAsync(string accountId, string email, CancellationToken cancellationToken)
	{
		bool hasExpectedEmail = !string.IsNullOrWhiteSpace(email);
		string normalizedExpectedEmail = (email ?? string.Empty).Trim();

		// Try persisted rich token first (includes expiry + refresh_token).
		GmailTokenData? tokenData = GmailOAuthTokenStore.LoadTokenData(accountId);
		if (tokenData != null && !string.IsNullOrWhiteSpace(tokenData.AccessToken))
		{
			// Auto-refresh if expired or expiring within 5 minutes.
			bool needsRefresh = tokenData.ExpiresAtUtc != DateTime.MinValue
				&& DateTime.UtcNow >= tokenData.ExpiresAtUtc.AddMinutes(-5);

			if (needsRefresh && !string.IsNullOrWhiteSpace(tokenData.RefreshToken))
			{
				try
				{
					var flow = new GmailOAuthFlow();
					GmailTokenData refreshed = await flow.RefreshAccessTokenAsync(tokenData, cancellationToken).ConfigureAwait(false);
					GmailOAuthTokenStore.SaveTokenData(accountId, refreshed);
					tokenData = refreshed;
				}
				catch
				{
					// Refresh failed — continue with existing token; will fail at API call if truly expired.
				}
			}

			string stored = tokenData.AccessToken;

			if (hasExpectedEmail)
			{
				string actualEmail = await GetProfileEmailAsync(stored, cancellationToken).ConfigureAwait(false);
				if (!string.Equals(actualEmail.Trim(), normalizedExpectedEmail, StringComparison.OrdinalIgnoreCase))
				{
					GmailOAuthTokenStore.Delete(accountId);
					throw new InvalidOperationException($"Gmail token profile mismatch for account '{accountId}': requested={normalizedExpectedEmail} actual={actualEmail}");
				}
			}

			return stored;
		}

		// Fall back to env var (first-run / manual override).
		string token = Environment.GetEnvironmentVariable("ATLAS_GMAIL_ACCESS_TOKEN") ?? string.Empty;
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException(
				$"Gmail access token is missing for account '{accountId}'. " +
				$"Set ATLAS_GMAIL_ACCESS_TOKEN ({RequiredScopes}).");
		}

		if (hasExpectedEmail)
		{
			string actualEmail = await GetProfileEmailAsync(token, cancellationToken).ConfigureAwait(false);
			if (!string.Equals(actualEmail.Trim(), normalizedExpectedEmail, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Gmail token profile mismatch for account '{accountId}': requested={normalizedExpectedEmail} actual={actualEmail}");
			}
		}

		// Persist so subsequent restarts don't require the env var.
		GmailOAuthTokenStore.Save(accountId, token);
		return token;
	}

	private static async Task<string> GetProfileEmailAsync(string accessToken, CancellationToken cancellationToken)
	{
		const string profileUrl = "https://gmail.googleapis.com/gmail/v1/users/me/profile";
		using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Gmail profile error {(int)response.StatusCode}: {body}");
		}

		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("emailAddress", out JsonElement emailEl) && emailEl.ValueKind == JsonValueKind.String)
		{
			return emailEl.GetString() ?? string.Empty;
		}

		throw new InvalidOperationException("Gmail profile email is missing");
	}
}

public sealed class GmailDraftCreateRequest
{
	public string To { get; set; } = string.Empty;
	public string Subject { get; set; } = string.Empty;
	public string Body { get; set; } = string.Empty;
	public string ThreadId { get; set; } = string.Empty;
	public string InReplyTo { get; set; } = string.Empty;
}

public sealed class GmailDraftCreateResult
{
	public string DraftId { get; set; } = string.Empty;
	public string MessageId { get; set; } = string.Empty;
	public string ThreadId { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
}

internal sealed class GmailDraftCreateResponse
{
	public string Id { get; set; } = string.Empty;
	public GmailDraftCreateMessage? Message { get; set; }
}

internal sealed class GmailDraftCreateMessage
{
	public string Id { get; set; } = string.Empty;
	public string ThreadId { get; set; } = string.Empty;
}

public sealed class GmailApplyLabelResult
{
	public string MessageId { get; set; } = string.Empty;
	public string LabelId { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string Error { get; set; } = string.Empty;
	public List<string> AppliedLabelIds { get; set; } = new();
}

public sealed record EmailAccountSummary(
	string AccountId,
	string Email,
	string DisplayName,
	int UnreadCount,
	bool Connected);

public sealed record EmailMessageSummary(
	string Id,
	string ThreadId,
	string From,
	string Subject,
	string Snippet,
	string Date,
	bool Unread);

public sealed class GmailEmailMessageSummary
{
	public string Id { get; set; } = string.Empty;
	public string ThreadId { get; set; } = string.Empty;
	public string From { get; set; } = string.Empty;
	public string Subject { get; set; } = string.Empty;
	public string Date { get; set; } = string.Empty;
	public string Snippet { get; set; } = string.Empty;
	public DateTime? InternalDateUtc { get; set; }
	public bool IsUnread { get; set; }
	public IReadOnlyList<string> LabelIds { get; set; } = Array.Empty<string>();
}
