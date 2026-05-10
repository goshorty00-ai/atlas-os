using System;
using System.Collections.Generic;

namespace AtlasAI.Modules.Email.Models;

public sealed class EmailMessageDetail
{
	public string Id { get; set; } = string.Empty;
	public string ThreadId { get; set; } = string.Empty;
	public string From { get; set; } = string.Empty;
	public string To { get; set; } = string.Empty;
	public string Subject { get; set; } = string.Empty;
	public string Date { get; set; } = string.Empty;
	public string Snippet { get; set; } = string.Empty;
	public string BodyText { get; set; } = string.Empty;
	public string PlainText { get; set; } = string.Empty;
	public string HtmlBody { get; set; } = string.Empty;
	public int InlineImagesCount { get; set; }
	public IReadOnlyList<EmailAttachmentDetail> Attachments { get; set; } = Array.Empty<EmailAttachmentDetail>();
	public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();
}

public sealed class EmailAttachmentDetail
{
	public string FileName { get; set; } = string.Empty;
	public string MimeType { get; set; } = string.Empty;
	public string AttachmentId { get; set; } = string.Empty;
	public string ContentId { get; set; } = string.Empty;
	public int Size { get; set; }
	public bool IsInline { get; set; }
}
