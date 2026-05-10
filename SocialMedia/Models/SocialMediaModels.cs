using System;
using System.Collections.Generic;

namespace AtlasAI.SocialMedia.Models
{
    // ==================== ENUMS ====================
    
    public enum SocialPlatform
    {
        TikTok,
        Instagram,
        Facebook,
        YouTube
    }
    
    public enum ContentType
    {
        Post,
        Story,
        Reel,
        Short,
        Video,
        Ad,
        Carousel
    }
    
    public enum ContentTone
    {
        Professional,
        Friendly,
        Viral,
        Premium,
        Casual,
        Humorous,
        Educational,
        Inspirational
    }
    
    public enum CampaignGoal
    {
        Sales,
        Traffic,
        Followers,
        Engagement,
        BrandAwareness,
        LeadGeneration
    }
    
    public enum ContentStatus
    {
        Draft,
        PendingReview,
        Approved,
        Scheduled,
        Published,
        Failed,
        Archived
    }
    
    // ==================== BRAND PROFILE ====================
    
    public class BrandProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ContentTone DefaultTone { get; set; } = ContentTone.Professional;
        public List<string> VoiceKeywords { get; set; } = new();
        public List<string> DoNotSayPhrases { get; set; } = new();
        public List<string> HashtagLibrary { get; set; } = new();
        public Dictionary<SocialPlatform, string> PlatformHandles { get; set; } = new();
        public string TargetAudience { get; set; } = "";
        public string Industry { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }
    
    // ==================== CONTENT ====================
    
    public class SocialContent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BrandId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string Caption { get; set; } = "";
        public string Hook { get; set; } = "";
        public string CallToAction { get; set; } = "";
        public List<string> Hashtags { get; set; } = new();
        public ContentType Type { get; set; }
        public ContentTone Tone { get; set; }
        public List<SocialPlatform> TargetPlatforms { get; set; } = new();
        public ContentStatus Status { get; set; } = ContentStatus.Draft;
        public DateTime? ScheduledTime { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string? CampaignId { get; set; }
        public List<string> MediaUrls { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
    
    // ==================== VIDEO SCRIPT ====================
    
    public class VideoScript
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BrandId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Hook { get; set; } = "";  // First 3 seconds
        public List<ScriptSection> Sections { get; set; } = new();
        public string CallToAction { get; set; } = "";
        public int EstimatedDurationSeconds { get; set; }
        public SocialPlatform TargetPlatform { get; set; }
        public ContentTone Tone { get; set; }
        public List<string> VisualSuggestions { get; set; } = new();
        public List<string> SoundSuggestions { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
    
    public class ScriptSection
    {
        public int Order { get; set; }
        public string Narration { get; set; } = "";
        public string VisualDescription { get; set; } = "";
        public int DurationSeconds { get; set; }
        public string OnScreenText { get; set; } = "";
    }
    
    // ==================== AD COPY ====================
    
    public class AdCopy
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BrandId { get; set; } = "";
        public string PrimaryText { get; set; } = "";      // Main ad text
        public string Headline { get; set; } = "";          // Bold headline
        public string Description { get; set; } = "";       // Link description
        public string CallToAction { get; set; } = "";
        public SocialPlatform Platform { get; set; }
        public ContentTone Tone { get; set; }
        public string TargetAudience { get; set; } = "";
        public CampaignGoal Goal { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<AdCopyVariant> Variants { get; set; } = new();
    }
    
    public class AdCopyVariant
    {
        public string VariantId { get; set; } = Guid.NewGuid().ToString();
        public string PrimaryText { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";  // Why this variant differs
    }
    
    // ==================== CAMPAIGN ====================
    
    public class SocialCampaign
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BrandId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public CampaignGoal Goal { get; set; }
        public List<SocialPlatform> Platforms { get; set; } = new();
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> ContentIds { get; set; } = new();
        public ContentPlan? Plan { get; set; }
        public CampaignStatus Status { get; set; } = CampaignStatus.Planning;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public Dictionary<string, string> Notes { get; set; } = new();
    }
    
    public enum CampaignStatus
    {
        Planning,
        Active,
        Paused,
        Completed,
        Archived
    }
    
    public class ContentPlan
    {
        public List<PlannedPost> Posts { get; set; } = new();
        public string PostingStrategy { get; set; } = "";
        public List<string> AssetSuggestions { get; set; } = new();
        public string CTAStrategy { get; set; } = "";
        public Dictionary<SocialPlatform, int> PostsPerWeek { get; set; } = new();
    }
    
    public class PlannedPost
    {
        public DateTime ScheduledDate { get; set; }
        public SocialPlatform Platform { get; set; }
        public ContentType Type { get; set; }
        public string TopicSuggestion { get; set; } = "";
        public string? ContentId { get; set; }  // Linked when content is created
    }
    
    // ==================== SCHEDULING ====================
    
    public class ScheduledPost
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ContentId { get; set; } = "";
        public SocialPlatform Platform { get; set; }
        public DateTime ScheduledTime { get; set; }
        public ScheduleStatus Status { get; set; } = ScheduleStatus.Pending;
        public bool RequiresApproval { get; set; } = true;
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? PublishResult { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
    
    public enum ScheduleStatus
    {
        Pending,
        Approved,
        Publishing,
        Published,
        Failed,
        Cancelled
    }
    
    // ==================== ANALYTICS (Phase 2 Ready) ====================
    
    public class ContentAnalytics
    {
        public string ContentId { get; set; } = "";
        public SocialPlatform Platform { get; set; }
        public int Impressions { get; set; }
        public int Reach { get; set; }
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int Shares { get; set; }
        public int Saves { get; set; }
        public int Clicks { get; set; }
        public double EngagementRate { get; set; }
        public TimeSpan? AverageWatchTime { get; set; }
        public double? WatchThroughRate { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.Now;
    }
    
    // ==================== GENERATION REQUESTS ====================
    
    public class ContentGenerationRequest
    {
        public string BrandId { get; set; } = "";
        public ContentType Type { get; set; }
        public List<SocialPlatform> Platforms { get; set; } = new();
        public ContentTone Tone { get; set; }
        public string Topic { get; set; } = "";
        public string? ProductOrService { get; set; }
        public string? TargetAudience { get; set; }
        public CampaignGoal? Goal { get; set; }
        public int VariantCount { get; set; } = 1;
        public bool IncludeHashtags { get; set; } = true;
        public bool IncludeCTA { get; set; } = true;
        public string? AdditionalInstructions { get; set; }
    }
    
    public class HashtagGenerationRequest
    {
        public string Topic { get; set; } = "";
        public string? Industry { get; set; }
        public SocialPlatform Platform { get; set; }
        public int BroadCount { get; set; } = 10;
        public int NicheCount { get; set; } = 10;
        public bool IncludeTrending { get; set; } = true;
    }
}
