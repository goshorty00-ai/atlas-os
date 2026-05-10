using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.SocialMedia.Models;
using AtlasAI.SocialMedia.Services;

namespace AtlasAI.SocialMedia
{
    /// <summary>
    /// Voice command integration for Social Media Console
    /// Handles natural language commands for social media tasks
    /// </summary>
    public static class SocialMediaToolExecutor
    {
        private static SocialMediaMemoryService? _memoryService;
        private static ContentGeneratorService? _contentGenerator;
        private static CampaignBuilderService? _campaignBuilder;
        
        private static void EnsureInitialized()
        {
            if (_memoryService == null)
            {
                _memoryService = new SocialMediaMemoryService();
                _contentGenerator = new ContentGeneratorService(_memoryService);
                _campaignBuilder = new CampaignBuilderService(_memoryService, _contentGenerator);
            }
        }
        
        /// <summary>
        /// Check if message is a social media command and execute it
        /// Returns result or null if not a social media command
        /// </summary>
        public static async Task<string?> TryExecuteAsync(string message)
        {
            var msg = message.ToLower().Trim();
            
            // Open Social Media Console
            if (ContainsAny(msg, "social media console", "open social media", "social media panel",
                "social console", "content console", "open content creator"))
            {
                return "__OPEN_SOCIAL_MEDIA_CONSOLE__";
            }
            
            // Generate social media post
            if (ContainsAny(msg, "create post", "generate post", "write post", "make a post",
                "social media post", "create content", "generate content"))
            {
                EnsureInitialized();
                var topic = ExtractTopic(message);
                var platforms = ExtractPlatforms(msg);
                var tone = ExtractTone(msg);
                
                if (string.IsNullOrEmpty(topic))
                {
                    return "What would you like the post to be about? Tell me the topic.";
                }
                
                var posts = await _contentGenerator!.GeneratePostAsync(new ContentGenerationRequest
                {
                    Topic = topic,
                    Platforms = platforms,
                    Tone = tone,
                    Type = ContentType.Post,
                    IncludeHashtags = true,
                    IncludeCTA = true
                });
                
                if (posts.Any())
                {
                    var post = posts.First();
                    await _memoryService!.SaveContentAsync(post);
                    
                    return $"📱 Generated post for {string.Join(", ", platforms)}:\n\n" +
                           $"**Hook:** {post.Hook}\n\n" +
                           $"{post.Body}\n\n" +
                           $"**CTA:** {post.CallToAction}\n\n" +
                           $"**Hashtags:** {string.Join(" ", post.Hashtags)}\n\n" +
                           $"_Saved as draft. Say 'open social media console' to schedule it._";
                }
                
                return "I couldn't generate the post. Please try again with more details.";
            }
            
            // Generate video script
            if (ContainsAny(msg, "video script", "tiktok script", "reel script", "short script",
                "create script", "generate script"))
            {
                EnsureInitialized();
                var topic = ExtractTopic(message);
                var platform = ExtractPlatforms(msg).FirstOrDefault();
                
                if (string.IsNullOrEmpty(topic))
                {
                    return "What's the video about? Give me a topic or idea.";
                }
                
                var script = await _contentGenerator!.GenerateVideoScriptAsync(
                    "", topic, platform, ContentTone.Friendly, 30);
                
                return $"🎬 Video Script ({script.EstimatedDurationSeconds}s):\n\n" +
                       $"**HOOK (0-3s):** {script.Hook}\n\n" +
                       string.Join("\n", script.Sections.Select(s => 
                           $"**Section {s.Order}:**\n" +
                           $"  📝 {s.Narration}\n" +
                           $"  🎥 Visual: {s.VisualDescription}")) +
                       $"\n\n**CTA:** {script.CallToAction}";
            }
            
            // Generate hashtags
            if (ContainsAny(msg, "generate hashtags", "hashtags for", "get hashtags", "suggest hashtags"))
            {
                EnsureInitialized();
                var topic = ExtractTopic(message);
                var platform = ExtractPlatforms(msg).FirstOrDefault();
                
                if (string.IsNullOrEmpty(topic))
                {
                    return "What topic do you need hashtags for?";
                }
                
                var (broad, niche) = await _contentGenerator!.GenerateHashtagsAsync(new HashtagGenerationRequest
                {
                    Topic = topic,
                    Platform = platform,
                    BroadCount = 10,
                    NicheCount = 10
                });
                
                return $"#️⃣ Hashtags for '{topic}':\n\n" +
                       $"**Broad (high reach):**\n{string.Join(" ", broad)}\n\n" +
                       $"**Niche (targeted):**\n{string.Join(" ", niche)}";
            }
            
            // Generate ad copy
            if (ContainsAny(msg, "ad copy", "create ad", "generate ad", "facebook ad", "instagram ad",
                "write ad", "advertising copy"))
            {
                EnsureInitialized();
                var topic = ExtractTopic(message);
                var platform = ExtractPlatforms(msg).FirstOrDefault();
                
                if (string.IsNullOrEmpty(topic))
                {
                    return "What product or service is the ad for?";
                }
                
                var adCopy = await _contentGenerator!.GenerateAdCopyAsync(
                    "", topic, CampaignGoal.Engagement, platform, ContentTone.Professional, "", 3);
                
                var output = $"📢 Ad Copy Variants:\n\n";
                for (int i = 0; i < adCopy.Variants.Count; i++)
                {
                    var v = adCopy.Variants[i];
                    output += $"**Variant {i + 1}:**\n" +
                              $"  Primary: {v.PrimaryText}\n" +
                              $"  Headline: {v.Headline}\n" +
                              $"  Description: {v.Description}\n\n";
                }
                
                return output;
            }
            
            // List brands
            if (ContainsAny(msg, "my brands", "list brands", "show brands", "what brands"))
            {
                EnsureInitialized();
                var brands = await _memoryService!.GetAllBrandsAsync();
                
                if (!brands.Any())
                {
                    return "You haven't set up any brand profiles yet. Open the Social Media Console to add one.";
                }
                
                return "🏷️ Your Brands:\n\n" +
                       string.Join("\n", brands.Select(b => $"• **{b.Name}** - {b.Industry}"));
            }
            
            // List campaigns
            if (ContainsAny(msg, "my campaigns", "list campaigns", "show campaigns", "active campaigns"))
            {
                EnsureInitialized();
                var campaigns = await _memoryService!.GetAllCampaignsAsync();
                
                if (!campaigns.Any())
                {
                    return "No campaigns yet. Open the Social Media Console to create one.";
                }
                
                return "📊 Your Campaigns:\n\n" +
                       string.Join("\n", campaigns.Select(c => 
                           $"• **{c.Name}** ({c.Status}) - {c.Goal} on {string.Join(", ", c.Platforms)}"));
            }
            
            // List scheduled posts
            if (ContainsAny(msg, "scheduled posts", "upcoming posts", "what's scheduled", "posting schedule"))
            {
                EnsureInitialized();
                var schedules = await _memoryService!.GetPendingSchedulesAsync();
                
                if (!schedules.Any())
                {
                    return "No posts scheduled. Generate content and schedule it from the Social Media Console.";
                }
                
                return "📅 Upcoming Posts:\n\n" +
                       string.Join("\n", schedules.Take(5).Select(s => 
                           $"• {s.Platform} - {s.ScheduledTime:MMM dd HH:mm} ({s.Status})"));
            }
            
            // Not a social media command
            return null;
        }
        
        #region Helper Methods
        
        private static bool ContainsAny(string text, params string[] phrases)
        {
            return phrases.Any(p => text.Contains(p));
        }
        
        private static string ExtractTopic(string message)
        {
            // Remove command words and extract the topic
            var cleaners = new[] { 
                "create post about", "generate post about", "write post about", "make a post about",
                "create content about", "generate content about", "video script for", "video script about",
                "hashtags for", "generate hashtags for", "ad copy for", "create ad for",
                "for tiktok", "for instagram", "for facebook", "for youtube",
                "on tiktok", "on instagram", "on facebook", "on youtube"
            };
            
            var topic = message;
            foreach (var cleaner in cleaners)
            {
                topic = topic.Replace(cleaner, "", StringComparison.OrdinalIgnoreCase);
            }
            
            return topic.Trim();
        }
        
        private static List<SocialPlatform> ExtractPlatforms(string message)
        {
            var platforms = new List<SocialPlatform>();
            
            if (message.Contains("tiktok")) platforms.Add(SocialPlatform.TikTok);
            if (message.Contains("instagram") || message.Contains("insta")) platforms.Add(SocialPlatform.Instagram);
            if (message.Contains("facebook") || message.Contains("fb")) platforms.Add(SocialPlatform.Facebook);
            if (message.Contains("youtube") || message.Contains("yt")) platforms.Add(SocialPlatform.YouTube);
            
            // Default to Instagram if none specified
            if (!platforms.Any()) platforms.Add(SocialPlatform.Instagram);
            
            return platforms;
        }
        
        private static ContentTone ExtractTone(string message)
        {
            if (message.Contains("professional") || message.Contains("formal")) return ContentTone.Professional;
            if (message.Contains("casual") || message.Contains("relaxed")) return ContentTone.Casual;
            if (message.Contains("viral") || message.Contains("trending")) return ContentTone.Viral;
            if (message.Contains("funny") || message.Contains("humor")) return ContentTone.Humorous;
            if (message.Contains("educational") || message.Contains("informative")) return ContentTone.Educational;
            
            return ContentTone.Friendly; // Default
        }
        
        #endregion
    }
}
