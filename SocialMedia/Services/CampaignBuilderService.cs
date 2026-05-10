using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.AI;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// Guided campaign builder workflow
    /// Creates content plans, posting schedules, and asset suggestions
    /// </summary>
    public class CampaignBuilderService
    {
        private readonly SocialMediaMemoryService _memoryService;
        private readonly ContentGeneratorService _contentGenerator;
        
        public CampaignBuilderService(
            SocialMediaMemoryService memoryService,
            ContentGeneratorService contentGenerator)
        {
            _memoryService = memoryService;
            _contentGenerator = contentGenerator;
        }
        
        /// <summary>
        /// Create a new campaign with AI-generated content plan
        /// </summary>
        public async Task<SocialCampaign> CreateCampaignAsync(
            string brandId,
            string name,
            CampaignGoal goal,
            List<SocialPlatform> platforms,
            DateTime startDate,
            DateTime? endDate = null,
            int weeksToplan = 4)
        {
            var brand = await _memoryService.GetBrandAsync(brandId);
            
            var campaign = new SocialCampaign
            {
                BrandId = brandId,
                Name = name,
                Goal = goal,
                Platforms = platforms,
                StartDate = startDate,
                EndDate = endDate ?? startDate.AddDays(weeksToplan * 7),
                Status = CampaignStatus.Planning,
                CreatedAt = DateTime.Now
            };
            
            // Generate content plan
            campaign.Plan = await GenerateContentPlanAsync(brand, campaign, weeksToplan);
            
            await _memoryService.SaveCampaignAsync(campaign);
            
            return campaign;
        }
        
        /// <summary>
        /// Generate a content plan for a campaign
        /// </summary>
        public async Task<ContentPlan> GenerateContentPlanAsync(
            BrandProfile? brand,
            SocialCampaign campaign,
            int weeks)
        {
            var plan = new ContentPlan();
            
            // Determine posting frequency per platform
            plan.PostsPerWeek = GetRecommendedPostingFrequency(campaign.Goal, campaign.Platforms);
            
            // Generate posting strategy
            plan.PostingStrategy = await GeneratePostingStrategyAsync(brand, campaign);
            
            // Generate CTA strategy
            plan.CTAStrategy = await GenerateCTAStrategyAsync(campaign.Goal);
            
            // Generate asset suggestions
            plan.AssetSuggestions = await GenerateAssetSuggestionsAsync(brand, campaign);
            
            // Create planned posts
            plan.Posts = GeneratePlannedPosts(campaign, plan.PostsPerWeek, weeks);
            
            return plan;
        }
        
        /// <summary>
        /// Get recommended posting frequency based on goal and platforms
        /// </summary>
        public Dictionary<SocialPlatform, int> GetRecommendedPostingFrequency(
            CampaignGoal goal,
            List<SocialPlatform> platforms)
        {
            var frequency = new Dictionary<SocialPlatform, int>();
            
            foreach (var platform in platforms)
            {
                frequency[platform] = (platform, goal) switch
                {
                    // TikTok - high frequency works best
                    (SocialPlatform.TikTok, CampaignGoal.Followers) => 7,
                    (SocialPlatform.TikTok, CampaignGoal.Engagement) => 5,
                    (SocialPlatform.TikTok, _) => 4,
                    
                    // Instagram - moderate frequency
                    (SocialPlatform.Instagram, CampaignGoal.Followers) => 5,
                    (SocialPlatform.Instagram, CampaignGoal.Engagement) => 4,
                    (SocialPlatform.Instagram, _) => 3,
                    
                    // Facebook - lower frequency, quality over quantity
                    (SocialPlatform.Facebook, CampaignGoal.Traffic) => 4,
                    (SocialPlatform.Facebook, _) => 3,
                    
                    // YouTube - consistency matters more than frequency
                    (SocialPlatform.YouTube, _) => 2,
                    
                    _ => 3
                };
            }
            
            return frequency;
        }
        
        /// <summary>
        /// Get optimal posting times for a platform
        /// </summary>
        public List<TimeSpan> GetOptimalPostingTimes(SocialPlatform platform)
        {
            return platform switch
            {
                SocialPlatform.TikTok => new List<TimeSpan>
                {
                    new TimeSpan(7, 0, 0),   // 7 AM
                    new TimeSpan(12, 0, 0),  // 12 PM
                    new TimeSpan(19, 0, 0),  // 7 PM
                    new TimeSpan(21, 0, 0)   // 9 PM
                },
                SocialPlatform.Instagram => new List<TimeSpan>
                {
                    new TimeSpan(8, 0, 0),   // 8 AM
                    new TimeSpan(11, 0, 0),  // 11 AM
                    new TimeSpan(14, 0, 0),  // 2 PM
                    new TimeSpan(19, 0, 0)   // 7 PM
                },
                SocialPlatform.Facebook => new List<TimeSpan>
                {
                    new TimeSpan(9, 0, 0),   // 9 AM
                    new TimeSpan(13, 0, 0),  // 1 PM
                    new TimeSpan(16, 0, 0)   // 4 PM
                },
                SocialPlatform.YouTube => new List<TimeSpan>
                {
                    new TimeSpan(12, 0, 0),  // 12 PM
                    new TimeSpan(15, 0, 0),  // 3 PM
                    new TimeSpan(18, 0, 0)   // 6 PM
                },
                _ => new List<TimeSpan> { new TimeSpan(12, 0, 0) }
            };
        }
        
        /// <summary>
        /// Generate topic suggestions for a campaign
        /// </summary>
        public async Task<List<string>> GenerateTopicSuggestionsAsync(
            BrandProfile? brand,
            CampaignGoal goal,
            int count = 10)
        {
            var prompt = $@"Generate {count} content topic ideas for a social media campaign.

Goal: {goal}
{(brand != null ? $"Brand: {brand.Name}" : "")}
{(brand != null ? $"Industry: {brand.Industry}" : "")}
{(brand != null ? $"Target Audience: {brand.TargetAudience}" : "")}

Provide diverse topics that:
- Align with the campaign goal
- Are engaging and shareable
- Mix educational, entertaining, and promotional content
- Follow the 80/20 rule (80% value, 20% promotional)

List topics as bullet points.";

            var response = await CallAIAsync(
                "social_campaign_topics",
                prompt,
                $"Operation: generate campaign topic suggestions. Goal: {goal}. Requested count: {count}. Brand: {brand?.Name ?? "unknown"}. Industry: {brand?.Industry ?? "unknown"}.",
                "Generate diverse campaign content topics aligned to platform growth and marketing execution.");
            
            return response.Split('\n')
                .Where(l => l.Trim().StartsWith("-") || l.Trim().StartsWith("•"))
                .Select(l => l.Trim().TrimStart('-', '•', ' '))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(count)
                .ToList();
        }
        
        /// <summary>
        /// Activate a campaign (change status to Active)
        /// </summary>
        public async Task<SocialCampaign> ActivateCampaignAsync(string campaignId)
        {
            var campaign = await _memoryService.GetCampaignAsync(campaignId);
            if (campaign == null)
                throw new ArgumentException("Campaign not found");
            
            campaign.Status = CampaignStatus.Active;
            await _memoryService.SaveCampaignAsync(campaign);
            
            return campaign;
        }
        
        /// <summary>
        /// Pause a campaign
        /// </summary>
        public async Task<SocialCampaign> PauseCampaignAsync(string campaignId)
        {
            var campaign = await _memoryService.GetCampaignAsync(campaignId);
            if (campaign == null)
                throw new ArgumentException("Campaign not found");
            
            campaign.Status = CampaignStatus.Paused;
            await _memoryService.SaveCampaignAsync(campaign);
            
            return campaign;
        }
        
        #region Private Methods
        
        private async Task<string> GeneratePostingStrategyAsync(BrandProfile? brand, SocialCampaign campaign)
        {
            var prompt = $@"Create a brief posting strategy for this campaign:

Goal: {campaign.Goal}
Platforms: {string.Join(", ", campaign.Platforms)}
Duration: {(campaign.EndDate - campaign.StartDate)?.Days ?? 28} days
{(brand != null ? $"Brand: {brand.Name}" : "")}

Provide a 2-3 sentence strategy covering:
- Content mix (educational, entertaining, promotional)
- Posting rhythm
- Key themes to emphasize";

            return await CallAIAsync(
                "social_campaign_strategy",
                prompt,
                $"Operation: generate posting strategy. Goal: {campaign.Goal}. Platforms: {string.Join(", ", campaign.Platforms)}. Duration days: {(campaign.EndDate - campaign.StartDate)?.Days ?? 28}. Brand: {brand?.Name ?? "unknown"}.",
                "Generate a concise platform-aware campaign strategy that can be used directly in the campaign planning workflow.");
        }
        
        private async Task<string> GenerateCTAStrategyAsync(CampaignGoal goal)
        {
            var ctaMap = new Dictionary<CampaignGoal, string>
            {
                [CampaignGoal.Sales] = "Focus on urgency and value. Use CTAs like 'Shop Now', 'Get Yours', 'Limited Time'. Include discount codes where possible.",
                [CampaignGoal.Traffic] = "Drive clicks with curiosity. Use 'Learn More', 'Read the Full Story', 'Link in Bio'. Tease content value.",
                [CampaignGoal.Followers] = "Build community. Use 'Follow for More', 'Join Us', 'Turn on Notifications'. Emphasize ongoing value.",
                [CampaignGoal.Engagement] = "Encourage interaction. Use 'Comment Below', 'Share Your Thoughts', 'Tag a Friend'. Ask questions.",
                [CampaignGoal.BrandAwareness] = "Maximize reach. Use 'Share This', 'Save for Later', 'Tell Your Friends'. Focus on memorable content.",
                [CampaignGoal.LeadGeneration] = "Capture interest. Use 'Sign Up', 'Get Free Guide', 'Join Waitlist'. Offer value exchange."
            };
            
            return ctaMap.GetValueOrDefault(goal, "Use clear, action-oriented CTAs that align with your content.");
        }
        
        private async Task<List<string>> GenerateAssetSuggestionsAsync(BrandProfile? brand, SocialCampaign campaign)
        {
            var suggestions = new List<string>();
            
            foreach (var platform in campaign.Platforms)
            {
                var platformSuggestions = platform switch
                {
                    SocialPlatform.TikTok => new[]
                    {
                        "Vertical videos (9:16 ratio)",
                        "Trending audio clips",
                        "Behind-the-scenes footage",
                        "Quick tutorials under 60 seconds"
                    },
                    SocialPlatform.Instagram => new[]
                    {
                        "High-quality photos (1:1 or 4:5)",
                        "Reels (9:16 vertical video)",
                        "Carousel posts (up to 10 slides)",
                        "Stories with interactive stickers"
                    },
                    SocialPlatform.Facebook => new[]
                    {
                        "Eye-catching images",
                        "Short videos (under 3 minutes)",
                        "Link preview images",
                        "Event graphics"
                    },
                    SocialPlatform.YouTube => new[]
                    {
                        "Custom thumbnails (1280x720)",
                        "Shorts (vertical, under 60 seconds)",
                        "End screens and cards",
                        "Channel banner"
                    },
                    _ => Array.Empty<string>()
                };
                
                suggestions.AddRange(platformSuggestions.Select(s => $"{platform}: {s}"));
            }
            
            return suggestions;
        }
        
        private List<PlannedPost> GeneratePlannedPosts(
            SocialCampaign campaign,
            Dictionary<SocialPlatform, int> postsPerWeek,
            int weeks)
        {
            var posts = new List<PlannedPost>();
            var currentDate = campaign.StartDate;
            
            for (int week = 0; week < weeks; week++)
            {
                foreach (var platform in campaign.Platforms)
                {
                    var count = postsPerWeek.GetValueOrDefault(platform, 3);
                    var optimalTimes = GetOptimalPostingTimes(platform);
                    
                    // Distribute posts across the week
                    var daysToPost = DistributePostsAcrossWeek(count);
                    
                    foreach (var dayOffset in daysToPost)
                    {
                        var postDate = currentDate.AddDays(dayOffset);
                        var postTime = optimalTimes[new Random().Next(optimalTimes.Count)];
                        
                        posts.Add(new PlannedPost
                        {
                            ScheduledDate = postDate.Date.Add(postTime),
                            Platform = platform,
                            Type = GetContentTypeForPlatform(platform),
                            TopicSuggestion = "" // Will be filled by topic generator
                        });
                    }
                }
                
                currentDate = currentDate.AddDays(7);
            }
            
            return posts.OrderBy(p => p.ScheduledDate).ToList();
        }
        
        private List<int> DistributePostsAcrossWeek(int count)
        {
            // Distribute posts evenly across the week
            var days = new List<int>();
            var interval = 7.0 / count;
            
            for (int i = 0; i < count; i++)
            {
                days.Add((int)(i * interval));
            }
            
            return days;
        }
        
        private ContentType GetContentTypeForPlatform(SocialPlatform platform)
        {
            return platform switch
            {
                SocialPlatform.TikTok => ContentType.Short,
                SocialPlatform.Instagram => ContentType.Reel,
                SocialPlatform.Facebook => ContentType.Post,
                SocialPlatform.YouTube => ContentType.Video,
                _ => ContentType.Post
            };
        }
        
        private async Task<string> CallAIAsync(string module, string prompt, string moduleState, string additionalInstructions, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var messages = new List<object>
                {
                    new { role = "system", content = "You are a social media marketing strategist. Provide practical, actionable advice." },
                    new { role = "user", content = prompt }
                };
                
                var response = await AIManager.SendMessageAsync(new AIManager.AIRoutingRequest
                {
                    Module = module,
                    Messages = messages,
                    MaxTokens = 1000,
                    BucketHint = AIManager.AITaskBucket.Generation,
                    RuntimeContext = new AIManager.AIRuntimeContext
                    {
                        ActiveModule = module,
                        ActivePage = "social",
                        ToolContext = "Campaign runtime can generate topic ideas, posting strategies, and reusable content planning guidance for social campaigns.",
                        ModuleState = moduleState,
                        AdditionalInstructions = additionalInstructions,
                    },
                }, ct);
                return response.Success ? response.Content ?? "" : "";
            }
            catch
            {
                return "";
            }
        }
        
        #endregion
    }
}
