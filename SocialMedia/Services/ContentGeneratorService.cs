using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.AI;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.Services
{
    /// <summary>
    /// AI-powered content generation for social media platforms
    /// Uses Atlas's existing AI providers to generate content
    /// </summary>
    public class ContentGeneratorService
    {
        private readonly SocialMediaMemoryService _memoryService;
        
        public ContentGeneratorService(SocialMediaMemoryService memoryService)
        {
            _memoryService = memoryService;
        }
        
        /// <summary>
        /// Generate social media post content
        /// </summary>
        public async Task<List<SocialContent>> GeneratePostAsync(ContentGenerationRequest request)
        {
            var brand = await _memoryService.GetBrandAsync(request.BrandId);
            var results = new List<SocialContent>();
            
            var prompt = BuildPostPrompt(request, brand);
            var response = await CallAIAsync(
                "social_content_posts",
                prompt,
                $"Operation: generate social posts. Platforms: {string.Join(", ", request.Platforms)}. Topic: {request.Topic}. Variants: {request.VariantCount}. Tone: {request.Tone}. Brand: {brand?.Name ?? "unknown"}.",
                "Generate platform-ready post variants with clear hook, body, CTA, and hashtag structure.");
            
            if (string.IsNullOrEmpty(response)) return results;
            
            // Parse response into content objects
            for (int i = 0; i < request.VariantCount; i++)
            {
                var content = new SocialContent
                {
                    BrandId = request.BrandId,
                    Type = request.Type,
                    Tone = request.Tone,
                    TargetPlatforms = request.Platforms,
                    Status = ContentStatus.Draft,
                    CreatedAt = DateTime.Now
                };
                
                // Parse the AI response
                ParsePostResponse(response, content, i);
                
                if (request.IncludeHashtags && brand != null)
                {
                    content.Hashtags = await GenerateHashtagsForContent(content, brand);
                }
                
                results.Add(content);
            }
            
            return results;
        }
        
        /// <summary>
        /// Generate short-form video script (TikTok, Reels, Shorts)
        /// </summary>
        public async Task<VideoScript> GenerateVideoScriptAsync(
            string brandId,
            string topic,
            SocialPlatform platform,
            ContentTone tone,
            int targetDurationSeconds = 30)
        {
            var brand = await _memoryService.GetBrandAsync(brandId);
            
            var prompt = $@"Create a {targetDurationSeconds}-second video script for {platform}.

Topic: {topic}
Tone: {tone}
{(brand != null ? $"Brand Voice: {string.Join(", ", brand.VoiceKeywords)}" : "")}
{(brand?.DoNotSayPhrases.Any() == true ? $"Avoid: {string.Join(", ", brand.DoNotSayPhrases)}" : "")}

Structure the script with:
1. HOOK (first 3 seconds) - grab attention immediately
2. MAIN CONTENT (middle sections) - deliver value
3. CTA (last 3-5 seconds) - clear call to action

For each section provide:
- Narration/dialogue
- Visual description
- On-screen text suggestions
- Estimated duration

Format as structured sections.";

            var response = await CallAIAsync(
                "social_video_script",
                prompt,
                $"Operation: generate video script. Platform: {platform}. Topic: {topic}. Tone: {tone}. Duration seconds: {targetDurationSeconds}. Brand: {brand?.Name ?? "unknown"}.",
                "Generate a structured short-form video script with hook, main content, CTA, visual guidance, and timing.");
            
            var script = new VideoScript
            {
                BrandId = brandId,
                Title = topic,
                TargetPlatform = platform,
                Tone = tone,
                EstimatedDurationSeconds = targetDurationSeconds,
                CreatedAt = DateTime.Now
            };
            
            ParseVideoScriptResponse(response, script);
            
            return script;
        }
        
        /// <summary>
        /// Generate Meta-style ad copy with variants for A/B testing
        /// </summary>
        public async Task<AdCopy> GenerateAdCopyAsync(
            string brandId,
            string productOrService,
            CampaignGoal goal,
            SocialPlatform platform,
            ContentTone tone,
            string targetAudience,
            int variantCount = 3)
        {
            var brand = await _memoryService.GetBrandAsync(brandId);
            
            var prompt = $@"Create {variantCount} ad copy variants for {platform} advertising.

Product/Service: {productOrService}
Goal: {goal}
Target Audience: {targetAudience}
Tone: {tone}
{(brand != null ? $"Brand Voice: {string.Join(", ", brand.VoiceKeywords)}" : "")}

For each variant, provide:
1. PRIMARY TEXT (main ad copy, 125 chars optimal for feed)
2. HEADLINE (bold text, 40 chars max)
3. DESCRIPTION (link description, 30 chars)
4. CTA suggestion

Make each variant distinctly different:
- Variant 1: Benefit-focused
- Variant 2: Problem-solution
- Variant 3: Social proof/urgency

Format clearly with labels.";

            var response = await CallAIAsync(
                "social_ad_copy",
                prompt,
                $"Operation: generate ad copy. Platform: {platform}. Goal: {goal}. Product/service: {productOrService}. Audience: {targetAudience}. Tone: {tone}. Brand: {brand?.Name ?? "unknown"}.",
                "Generate multiple ad variants with distinct angles and clearly labeled sections for A/B testing.");
            
            var adCopy = new AdCopy
            {
                BrandId = brandId,
                Platform = platform,
                Tone = tone,
                Goal = goal,
                TargetAudience = targetAudience,
                CreatedAt = DateTime.Now
            };
            
            ParseAdCopyResponse(response, adCopy, variantCount);
            
            return adCopy;
        }
        
        /// <summary>
        /// Generate hashtag sets (broad + niche)
        /// </summary>
        public async Task<(List<string> Broad, List<string> Niche)> GenerateHashtagsAsync(HashtagGenerationRequest request)
        {
            var prompt = $@"Generate hashtags for {request.Platform} about: {request.Topic}
{(request.Industry != null ? $"Industry: {request.Industry}" : "")}

Provide two sets:
1. BROAD HASHTAGS ({request.BroadCount}): High-volume, general audience
2. NICHE HASHTAGS ({request.NicheCount}): Targeted, specific community

Rules:
- No spaces in hashtags
- Mix of popular and medium-competition tags
- Relevant to the topic
- Platform-appropriate (e.g., TikTok uses different tags than LinkedIn)

Format as two lists with # prefix.";

            var response = await CallAIAsync(
                "social_hashtags",
                prompt,
                $"Operation: generate hashtags. Platform: {request.Platform}. Topic: {request.Topic}. Industry: {request.Industry ?? "unknown"}. Broad count: {request.BroadCount}. Niche count: {request.NicheCount}.",
                "Generate broad and niche hashtag sets only, with platform-appropriate tagging.");
            
            return ParseHashtagResponse(response);
        }
        
        /// <summary>
        /// Generate caption with hook and CTA
        /// </summary>
        public async Task<(string Hook, string Body, string CTA)> GenerateCaptionAsync(
            string brandId,
            string topic,
            SocialPlatform platform,
            ContentTone tone)
        {
            var brand = await _memoryService.GetBrandAsync(brandId);
            
            var prompt = $@"Write a {platform} caption about: {topic}

Tone: {tone}
{(brand != null ? $"Brand Voice: {string.Join(", ", brand.VoiceKeywords)}" : "")}

Structure:
1. HOOK: First line that stops the scroll (emoji optional)
2. BODY: Main message (2-3 short paragraphs)
3. CTA: Clear call to action

Keep it {platform}-appropriate in length and style.";

            var response = await CallAIAsync(
                "social_caption",
                prompt,
                $"Operation: generate caption. Platform: {platform}. Topic: {topic}. Tone: {tone}. Brand: {brand?.Name ?? "unknown"}.",
                "Generate a scroll-stopping caption with hook, body, and CTA structure.");
            
            return ParseCaptionResponse(response);
        }
        
        #region Private Methods
        
        private string BuildPostPrompt(ContentGenerationRequest request, BrandProfile? brand)
        {
            var platformList = string.Join(", ", request.Platforms);
            
            return $@"Create {request.VariantCount} social media post(s) for {platformList}.

Type: {request.Type}
Topic: {request.Topic}
Tone: {request.Tone}
{(request.ProductOrService != null ? $"Product/Service: {request.ProductOrService}" : "")}
{(request.TargetAudience != null ? $"Target Audience: {request.TargetAudience}" : "")}
{(request.Goal.HasValue ? $"Goal: {request.Goal}" : "")}
{(brand != null ? $"Brand Voice: {string.Join(", ", brand.VoiceKeywords)}" : "")}
{(brand?.DoNotSayPhrases.Any() == true ? $"DO NOT use these phrases: {string.Join(", ", brand.DoNotSayPhrases)}" : "")}
{(request.AdditionalInstructions != null ? $"Additional: {request.AdditionalInstructions}" : "")}

For each post provide:
- HOOK: Attention-grabbing first line
- BODY: Main content
- CTA: Call to action
{(request.IncludeHashtags ? "- HASHTAGS: 5-10 relevant hashtags" : "")}

Make each variant unique if multiple requested.";
        }
        
        private async Task<string> CallAIAsync(string module, string prompt, string moduleState, string additionalInstructions, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var messages = new List<object>
                {
                    new { role = "system", content = "You are a social media marketing expert. Create engaging, platform-optimized content." },
                    new { role = "user", content = prompt }
                };
                
                var response = await AIManager.SendMessageAsync(new AIManager.AIRoutingRequest
                {
                    Module = module,
                    Messages = messages,
                    MaxTokens = 2000,
                    BucketHint = AIManager.AITaskBucket.Generation,
                    RuntimeContext = new AIManager.AIRuntimeContext
                    {
                        ActiveModule = module,
                        ActivePage = "social",
                        ToolContext = "Social generation runtime can produce posts, captions, hashtags, ad copy, and short-form video scripts for platform publishing workflows.",
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
        
        private void ParsePostResponse(string response, SocialContent content, int variantIndex)
        {
            // Simple parsing - extract sections
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var currentSection = "";
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();
                
                if (upper.Contains("HOOK"))
                    currentSection = "hook";
                else if (upper.Contains("BODY") || upper.Contains("CONTENT"))
                    currentSection = "body";
                else if (upper.Contains("CTA") || upper.Contains("CALL TO ACTION"))
                    currentSection = "cta";
                else if (upper.Contains("HASHTAG"))
                    currentSection = "hashtags";
                else
                {
                    switch (currentSection)
                    {
                        case "hook":
                            content.Hook = trimmed.TrimStart('-', '*', ' ');
                            break;
                        case "body":
                            content.Body += (string.IsNullOrEmpty(content.Body) ? "" : "\n") + trimmed.TrimStart('-', '*', ' ');
                            break;
                        case "cta":
                            content.CallToAction = trimmed.TrimStart('-', '*', ' ');
                            break;
                        case "hashtags":
                            var tags = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Where(t => t.StartsWith("#"))
                                .ToList();
                            content.Hashtags.AddRange(tags);
                            break;
                    }
                }
            }
            
            // Build caption from parts
            content.Caption = $"{content.Hook}\n\n{content.Body}\n\n{content.CallToAction}";
        }
        
        private void ParseVideoScriptResponse(string response, VideoScript script)
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var currentSection = "";
            var sectionOrder = 0;
            ScriptSection? currentScriptSection = null;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();
                
                if (upper.Contains("HOOK"))
                {
                    currentSection = "hook";
                }
                else if (upper.Contains("CTA") || upper.Contains("CALL TO ACTION"))
                {
                    currentSection = "cta";
                }
                else if (upper.Contains("SECTION") || upper.Contains("MAIN"))
                {
                    currentSection = "section";
                    sectionOrder++;
                    currentScriptSection = new ScriptSection { Order = sectionOrder };
                    script.Sections.Add(currentScriptSection);
                }
                else if (upper.Contains("VISUAL"))
                {
                    if (currentScriptSection != null)
                        currentScriptSection.VisualDescription = trimmed.Split(':').LastOrDefault()?.Trim() ?? "";
                    else
                        script.VisualSuggestions.Add(trimmed.Split(':').LastOrDefault()?.Trim() ?? "");
                }
                else if (upper.Contains("NARRATION") || upper.Contains("DIALOGUE"))
                {
                    if (currentScriptSection != null)
                        currentScriptSection.Narration = trimmed.Split(':').LastOrDefault()?.Trim() ?? "";
                }
                else
                {
                    switch (currentSection)
                    {
                        case "hook":
                            script.Hook = trimmed.TrimStart('-', '*', ' ');
                            break;
                        case "cta":
                            script.CallToAction = trimmed.TrimStart('-', '*', ' ');
                            break;
                    }
                }
            }
        }
        
        private void ParseAdCopyResponse(string response, AdCopy adCopy, int variantCount)
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            AdCopyVariant? currentVariant = null;
            var currentField = "";
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();
                
                if (upper.Contains("VARIANT"))
                {
                    currentVariant = new AdCopyVariant();
                    adCopy.Variants.Add(currentVariant);
                }
                else if (upper.Contains("PRIMARY"))
                {
                    currentField = "primary";
                }
                else if (upper.Contains("HEADLINE"))
                {
                    currentField = "headline";
                }
                else if (upper.Contains("DESCRIPTION"))
                {
                    currentField = "description";
                }
                else if (upper.Contains("CTA"))
                {
                    currentField = "cta";
                }
                else if (currentVariant != null && !string.IsNullOrWhiteSpace(trimmed))
                {
                    var value = trimmed.TrimStart('-', '*', ':', ' ');
                    switch (currentField)
                    {
                        case "primary":
                            currentVariant.PrimaryText = value;
                            break;
                        case "headline":
                            currentVariant.Headline = value;
                            break;
                        case "description":
                            currentVariant.Description = value;
                            break;
                    }
                }
            }
            
            // Set main ad copy from first variant
            if (adCopy.Variants.Any())
            {
                var first = adCopy.Variants.First();
                adCopy.PrimaryText = first.PrimaryText;
                adCopy.Headline = first.Headline;
                adCopy.Description = first.Description;
            }
        }
        
        private (List<string> Broad, List<string> Niche) ParseHashtagResponse(string response)
        {
            var broad = new List<string>();
            var niche = new List<string>();
            var currentList = "broad";
            
            foreach (var line in response.Split('\n'))
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();
                
                if (upper.Contains("BROAD"))
                    currentList = "broad";
                else if (upper.Contains("NICHE"))
                    currentList = "niche";
                else
                {
                    var tags = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.StartsWith("#"))
                        .Select(t => t.Trim(',', '.'))
                        .ToList();
                    
                    if (currentList == "broad")
                        broad.AddRange(tags);
                    else
                        niche.AddRange(tags);
                }
            }
            
            return (broad.Distinct().ToList(), niche.Distinct().ToList());
        }
        
        private (string Hook, string Body, string CTA) ParseCaptionResponse(string response)
        {
            var hook = "";
            var body = "";
            var cta = "";
            var currentSection = "";
            
            foreach (var line in response.Split('\n'))
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();
                
                if (upper.Contains("HOOK"))
                    currentSection = "hook";
                else if (upper.Contains("BODY"))
                    currentSection = "body";
                else if (upper.Contains("CTA"))
                    currentSection = "cta";
                else
                {
                    var value = trimmed.TrimStart('-', '*', ' ');
                    switch (currentSection)
                    {
                        case "hook":
                            hook = value;
                            break;
                        case "body":
                            body += (string.IsNullOrEmpty(body) ? "" : "\n") + value;
                            break;
                        case "cta":
                            cta = value;
                            break;
                    }
                }
            }
            
            return (hook, body, cta);
        }
        
        private async Task<List<string>> GenerateHashtagsForContent(SocialContent content, BrandProfile brand)
        {
            var hashtags = new List<string>();
            
            // Start with brand's hashtag library
            if (brand.HashtagLibrary.Any())
            {
                hashtags.AddRange(brand.HashtagLibrary.Take(3));
            }
            
            // Generate topic-specific hashtags
            var request = new HashtagGenerationRequest
            {
                Topic = content.Body,
                Industry = brand.Industry,
                Platform = content.TargetPlatforms.FirstOrDefault(),
                BroadCount = 5,
                NicheCount = 5
            };
            
            var (broad, niche) = await GenerateHashtagsAsync(request);
            hashtags.AddRange(broad.Take(3));
            hashtags.AddRange(niche.Take(4));
            
            return hashtags.Distinct().Take(10).ToList();
        }
        
        #endregion
    }
}
