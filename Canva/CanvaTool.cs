using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Canva
{
    /// <summary>
    /// Canva Tool - Integrates Canva design assistance into Atlas AI
    /// 
    /// Commands:
    /// - "build this in canva" / "create a canva design"
    /// - "make an instagram post about..."
    /// - "design a logo for..."
    /// - "recreate this design" (with image)
    /// - "configure canva api" / "set canva api key"
    /// 
    /// IMPORTANT: 
    /// - API mode requires user's own Canva API key (free tier available)
    /// - UI automation mode is always free but requires user interaction
    /// - Guidance mode (color palettes, fonts, tips) is always free
    /// </summary>
    public class CanvaTool
    {
        private readonly CanvaAssistant _assistant;
        private static CanvaTool? _instance;
        
        public static CanvaTool Instance => _instance ??= new CanvaTool();

        public CanvaTool()
        {
            _assistant = new CanvaAssistant();
            _assistant.StatusChanged += status => Debug.WriteLine($"[CanvaTool] {status}");
        }

        /// <summary>
        /// Check if a user message is a Canva-related request
        /// </summary>
        public static bool IsCanvaRequest(string input)
        {
            var lower = input.ToLowerInvariant();

            if (lower.Contains("figma") ||
                lower.Contains("domain") ||
                lower.Contains("website") ||
                lower.Contains("web site") ||
                lower.Contains("hosting") ||
                lower.Contains("deploy") ||
                lower.Contains("publish") ||
                lower.Contains("connect my") ||
                lower.Contains("connect to my"))
            {
                return false;
            }

            if (lower.Contains("canva"))
                return true;

            var explicitDesignPatterns = new[]
            {
                @"\b(create|make|design|build)\b.*\b(instagram post|facebook post|twitter post|linkedin post|youtube thumbnail|logo|flyer|poster|business card|presentation|slide deck|infographic|social media post)\b",
                @"\b(recreate|rebuild)\b.*\b(design|poster|logo|post|thumbnail)\b",
                @"\b(create|make)\s+a\s+post\b",
                @"\bbuild\s+this\s+in\s+canva\b",
                @"\bcreate\s+in\s+canva\b",
                @"\bdesign\s+this\b"
            };

            return explicitDesignPatterns.Any(pattern => Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Process a Canva-related request
        /// </summary>
        public async Task<string> ProcessRequestAsync(string input, byte[]? imageData = null, CancellationToken ct = default)
        {
            var lower = input.ToLower();

            // Check for API configuration request
            if (lower.Contains("configure canva") || lower.Contains("canva api key") || 
                lower.Contains("set canva key") || lower.Contains("add canva api"))
            {
                return GetApiConfigurationInstructions();
            }

            // Check for status request
            if (lower.Contains("canva status") || lower.Contains("canva mode"))
            {
                return GetStatusMessage();
            }

            // Parse the design request
            var designType = ParseDesignType(input);
            var style = ParseDesignStyle(input);
            var title = ExtractTitle(input);
            var description = ExtractDescription(input);

            // If image provided, analyze and rebuild
            if (imageData != null && imageData.Length > 0)
            {
                return await HandleImageRebuildAsync(imageData, title, ct);
            }

            // Start design session
            return await HandleDesignRequestAsync(designType, style, title, description, ct);
        }

        /// <summary>
        /// Handle a design creation request
        /// </summary>
        private async Task<string> HandleDesignRequestAsync(
            CanvaDesignType designType,
            CanvaDesignStyle style,
            string title,
            string description,
            CancellationToken ct)
        {
            var sb = new StringBuilder();
            var mode = _assistant.GetActiveMode();

            sb.AppendLine($"🎨 **Creating {designType} Design**");
            sb.AppendLine();

            if (mode == CanvaIntegrationMode.Api)
            {
                sb.AppendLine("Using Canva API (your API key)...");
            }
            else
            {
                sb.AppendLine("Using guided mode (no API key configured)");
                sb.AppendLine("*Tip: Add your own Canva API key in Settings for faster creation*");
            }
            sb.AppendLine();

            // Generate design spec
            var size = CanvaDesignSizes.GetSize(designType);
            var spec = new CanvaDesignSpec
            {
                DesignType = designType,
                Style = style,
                Title = title,
                Description = description,
                Width = size.Width,
                Height = size.Height,
                Colors = ColorPalette.FromStyle(style),
                Fonts = FontPairing.FromStyle(style)
            };

            // Generate guidance (always free)
            var guidance = _assistant.GenerateDesignGuidance(spec);

            sb.AppendLine($"**Design Specifications:**");
            sb.AppendLine($"- Type: {designType}");
            sb.AppendLine($"- Size: {guidance.Dimensions}");
            sb.AppendLine($"- Style: {style}");
            sb.AppendLine();

            sb.AppendLine("**Color Palette:**");
            foreach (var color in guidance.ColorPalette)
            {
                sb.AppendLine($"- {color.Name}: `{color.HexCode}` - {color.Usage}");
            }
            sb.AppendLine();

            sb.AppendLine("**Font Pairing:**");
            sb.AppendLine($"- Headings: {guidance.FontPairing.HeadingFont} ({guidance.FontPairing.HeadingSizeRange})");
            sb.AppendLine($"- Body: {guidance.FontPairing.BodyFont} ({guidance.FontPairing.BodySizeRange})");
            sb.AppendLine($"- Accent: {guidance.FontPairing.AccentFont}");
            sb.AppendLine();

            sb.AppendLine("**Layout Tips:**");
            foreach (var tip in guidance.LayoutTips.Take(5))
            {
                sb.AppendLine($"- {tip}");
            }
            sb.AppendLine();

            // Start the session
            try
            {
                var session = await _assistant.StartDesignSessionAsync(designType, style, title, description, ct);
                
                if (session.State == CanvaBuildState.Completed)
                {
                    sb.AppendLine("✅ Design created and opened in Canva!");
                }
                else if (session.State == CanvaBuildState.WaitingForConfirmation)
                {
                    sb.AppendLine("**Next Steps:**");
                    sb.AppendLine("1. I'll open Canva for you");
                    sb.AppendLine("2. Search for: " + string.Join(", ", guidance.CanvaSearchTerms.Take(2)));
                    sb.AppendLine("3. Apply the color palette and fonts above");
                    sb.AppendLine();
                    sb.AppendLine("Say **'continue'** to open Canva, or **'cancel'** to stop.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"⚠️ Error starting session: {ex.Message}");
                sb.AppendLine();
                sb.AppendLine("You can still use the design guidance above to create your design manually in Canva.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Handle image-based design rebuild
        /// </summary>
        private async Task<string> HandleImageRebuildAsync(byte[] imageData, string title, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🔍 **Analyzing Design...**");
            sb.AppendLine();

            try
            {
                var analysis = await _assistant.AnalyzeImageAsync(imageData, ct);

                sb.AppendLine("**Detected Elements:**");
                sb.AppendLine($"- Estimated Size: {analysis.EstimatedWidth} x {analysis.EstimatedHeight}");
                sb.AppendLine($"- Layout Style: {analysis.LayoutType}");
                sb.AppendLine($"- Overall Style: {analysis.OverallStyle}");
                sb.AppendLine();

                sb.AppendLine("**Detected Colors:**");
                foreach (var color in analysis.DetectedColors)
                {
                    sb.AppendLine($"- `{color}`");
                }
                sb.AppendLine();

                sb.AppendLine("**To Recreate This Design:**");
                sb.AppendLine("1. Open Canva and create a custom size design");
                sb.AppendLine("2. Use the detected colors above");
                sb.AppendLine("3. Match the layout structure");
                sb.AppendLine();
                sb.AppendLine("*Note: For more accurate analysis, add your OpenAI API key for vision capabilities*");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"⚠️ Analysis error: {ex.Message}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get API configuration instructions
        /// </summary>
        private string GetApiConfigurationInstructions()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🔧 **Canva API Configuration**");
            sb.AppendLine();
            sb.AppendLine("To enable fast API-based design creation, you need your own Canva API key.");
            sb.AppendLine();
            sb.AppendLine("**How to get your Canva API key:**");
            sb.AppendLine("1. Go to https://www.canva.com/developers/");
            sb.AppendLine("2. Sign in with your Canva account");
            sb.AppendLine("3. Create a new app/integration");
            sb.AppendLine("4. Copy your API access token");
            sb.AppendLine();
            sb.AppendLine("**To configure in Atlas:**");
            sb.AppendLine("1. Open Settings (gear icon)");
            sb.AppendLine("2. Go to 'Integrations' or 'API Keys'");
            sb.AppendLine("3. Paste your Canva API key");
            sb.AppendLine();
            sb.AppendLine("**Without API key:**");
            sb.AppendLine("- Design guidance (colors, fonts, tips) - ✅ Always free");
            sb.AppendLine("- Open Canva with correct template - ✅ Always free");
            sb.AppendLine("- Automated design creation - ❌ Requires API key");
            sb.AppendLine();
            sb.AppendLine("*Your API key is stored locally and never shared.*");

            return sb.ToString();
        }

        /// <summary>
        /// Get current Canva integration status
        /// </summary>
        private string GetStatusMessage()
        {
            var mode = _assistant.GetActiveMode();
            var hasKey = _assistant.HasApiKey;

            var sb = new StringBuilder();
            sb.AppendLine("📊 **Canva Integration Status**");
            sb.AppendLine();
            sb.AppendLine($"- API Key Configured: {(hasKey ? "✅ Yes" : "❌ No")}");
            sb.AppendLine($"- Active Mode: {mode}");
            sb.AppendLine($"- Session Active: {(_assistant.IsSessionActive ? "Yes" : "No")}");
            sb.AppendLine();

            if (!hasKey)
            {
                sb.AppendLine("*Add your Canva API key for automated design creation*");
                sb.AppendLine("Say 'configure canva api' for instructions.");
            }

            return sb.ToString();
        }

        #region Parsing Helpers

        private CanvaDesignType ParseDesignType(string input)
        {
            var lower = input.ToLower();

            if (lower.Contains("instagram story")) return CanvaDesignType.InstagramStory;
            if (lower.Contains("instagram") || lower.Contains("insta post")) return CanvaDesignType.InstagramPost;
            if (lower.Contains("facebook")) return CanvaDesignType.FacebookPost;
            if (lower.Contains("twitter") || lower.Contains("tweet")) return CanvaDesignType.TwitterPost;
            if (lower.Contains("linkedin")) return CanvaDesignType.LinkedInPost;
            if (lower.Contains("youtube") || lower.Contains("thumbnail")) return CanvaDesignType.YouTubeThumbnail;
            if (lower.Contains("presentation") || lower.Contains("slide")) return CanvaDesignType.Presentation;
            if (lower.Contains("logo")) return CanvaDesignType.Logo;
            if (lower.Contains("business card")) return CanvaDesignType.BusinessCard;
            if (lower.Contains("flyer")) return CanvaDesignType.Flyer;
            if (lower.Contains("poster")) return CanvaDesignType.Poster;
            if (lower.Contains("resume") || lower.Contains("cv")) return CanvaDesignType.Resume;
            if (lower.Contains("infographic")) return CanvaDesignType.Infographic;

            return CanvaDesignType.InstagramPost; // Default
        }

        private CanvaDesignStyle ParseDesignStyle(string input)
        {
            var lower = input.ToLower();

            if (lower.Contains("minimal") || lower.Contains("minimalist") || lower.Contains("clean")) 
                return CanvaDesignStyle.Minimal;
            if (lower.Contains("luxury") || lower.Contains("premium") || lower.Contains("elegant")) 
                return CanvaDesignStyle.Luxury;
            if (lower.Contains("bold") || lower.Contains("vibrant") || lower.Contains("colorful")) 
                return CanvaDesignStyle.Bold;
            if (lower.Contains("playful") || lower.Contains("fun") || lower.Contains("cute")) 
                return CanvaDesignStyle.Playful;
            if (lower.Contains("corporate") || lower.Contains("professional") || lower.Contains("business")) 
                return CanvaDesignStyle.Corporate;
            if (lower.Contains("vintage") || lower.Contains("retro") || lower.Contains("classic")) 
                return CanvaDesignStyle.Vintage;
            if (lower.Contains("tech") || lower.Contains("futuristic") || lower.Contains("cyber")) 
                return CanvaDesignStyle.Tech;
            if (lower.Contains("natural") || lower.Contains("organic") || lower.Contains("eco")) 
                return CanvaDesignStyle.Natural;

            return CanvaDesignStyle.Modern; // Default
        }

        private string ExtractTitle(string input)
        {
            // Try to extract title from quotes
            var quoteMatch = Regex.Match(input, "\"([^\"]+)\"");
            if (quoteMatch.Success) return quoteMatch.Groups[1].Value;

            // Try to extract from "about X" or "for X"
            var aboutMatch = Regex.Match(input, @"(?:about|for|called|titled)\s+(.+?)(?:\s+in\s+|\s+with\s+|$)", RegexOptions.IgnoreCase);
            if (aboutMatch.Success) return aboutMatch.Groups[1].Value.Trim();

            return "Untitled Design";
        }

        private string ExtractDescription(string input)
        {
            // Remove common command words to get the description
            var description = Regex.Replace(input, 
                @"^(create|make|design|build)\s+(a|an|the)?\s*(canva)?\s*", "", 
                RegexOptions.IgnoreCase);
            
            return description.Trim();
        }

        #endregion

        #region Session Control

        /// <summary>
        /// Continue the current design session
        /// </summary>
        public async Task<string> ContinueSessionAsync(CancellationToken ct = default)
        {
            if (!_assistant.IsSessionActive)
            {
                return "No active design session. Start a new design with 'create a canva design'.";
            }

            var success = await _assistant.ConfirmAndExecuteNextAsync(ct);
            return success 
                ? "Step completed. Say 'continue' for the next step or 'cancel' to stop."
                : "Step failed. You can try again or cancel the session.";
        }

        /// <summary>
        /// Cancel the current session
        /// </summary>
        public string CancelSession()
        {
            _assistant.CancelSession();
            return "Design session cancelled.";
        }

        /// <summary>
        /// Pause the current session
        /// </summary>
        public string PauseSession()
        {
            _assistant.PauseSession();
            return "Design session paused. Say 'resume' to continue.";
        }

        /// <summary>
        /// Resume a paused session
        /// </summary>
        public string ResumeSession()
        {
            _assistant.ResumeSession();
            return "Design session resumed.";
        }

        #endregion

        /// <summary>
        /// Configure the Canva API with user's token
        /// </summary>
        public void ConfigureApi(string accessToken)
        {
            _assistant.ConfigureApi(accessToken);
        }
    }
}
