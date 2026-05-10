using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace AtlasAI.Canva
{
    /// <summary>
    /// Canva Assistant - Helps users create designs in Canva
    /// 
    /// MODES:
    /// 1. API Mode (requires user's own Canva API key) - Fast, reliable, programmatic
    /// 2. UI Automation Mode (free) - Opens Canva and guides user step-by-step
    /// 3. Guidance Mode (always free) - Provides instructions, color palettes, font suggestions
    /// 
    /// Users can get their own Canva API key from: https://www.canva.com/developers/
    /// </summary>
    public class CanvaAssistant
    {
        private readonly CanvaApiClient _apiClient;
        private readonly string _settingsPath;
        private CanvaBuildSession? _currentSession;
        
        public event Action<string>? StatusChanged;
        public event Action<CanvaAction>? ActionPending;
        public event Action<string>? StepCompleted;

        public bool HasApiKey => _apiClient.IsConfigured;
        public bool IsSessionActive => _currentSession != null && _currentSession.State != CanvaBuildState.Completed;
        public CanvaIntegrationMode PreferredMode { get; set; } = CanvaIntegrationMode.Auto;

        public CanvaAssistant()
        {
            _apiClient = new CanvaApiClient();
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "canva_settings.json");
            
            LoadSettings();
        }

        #region Configuration

        /// <summary>
        /// Configure Canva API with user's own credentials
        /// Users get their key from https://www.canva.com/developers/
        /// </summary>
        public void ConfigureApi(string accessToken)
        {
            _apiClient.ConfigureWithToken(accessToken);
            SaveSettings();
            StatusChanged?.Invoke("Canva API configured successfully");
        }

        /// <summary>
        /// Configure with OAuth credentials for full API access
        /// </summary>
        public void ConfigureOAuth(string clientId, string clientSecret)
        {
            _apiClient.Configure(clientId, clientSecret);
            SaveSettings();
            StatusChanged?.Invoke("Canva OAuth configured - authorization required");
        }

        /// <summary>
        /// Get the current integration mode based on configuration
        /// </summary>
        public CanvaIntegrationMode GetActiveMode()
        {
            if (PreferredMode == CanvaIntegrationMode.Api && HasApiKey)
                return CanvaIntegrationMode.Api;
            if (PreferredMode == CanvaIntegrationMode.UiAutomation)
                return CanvaIntegrationMode.UiAutomation;
            // Auto mode: use API if available, otherwise UI automation
            return HasApiKey ? CanvaIntegrationMode.Api : CanvaIntegrationMode.UiAutomation;
        }

        #endregion

        #region Design Creation

        /// <summary>
        /// Start a new design build session
        /// </summary>
        public async Task<CanvaBuildSession> StartDesignSessionAsync(
            CanvaDesignType designType,
            CanvaDesignStyle style,
            string title,
            string description = "",
            CancellationToken ct = default)
        {
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

            _currentSession = new CanvaBuildSession
            {
                Spec = spec,
                State = CanvaBuildState.GatheringRequirements,
                StartedAt = DateTime.Now
            };

            StatusChanged?.Invoke($"Starting {designType} design: {title}");

            // Determine which mode to use
            var mode = GetActiveMode();
            
            if (mode == CanvaIntegrationMode.Api)
            {
                return await StartApiSessionAsync(spec, ct);
            }
            else
            {
                return await StartUiAutomationSessionAsync(spec, ct);
            }
        }

        /// <summary>
        /// Create design using Canva API (requires user's API key)
        /// </summary>
        private async Task<CanvaBuildSession> StartApiSessionAsync(CanvaDesignSpec spec, CancellationToken ct)
        {
            _currentSession!.State = CanvaBuildState.Executing;
            StatusChanged?.Invoke("Creating design via Canva API...");

            try
            {
                var design = await _apiClient.CreateDesignAsync(spec, ct);
                if (design != null)
                {
                    _currentSession.State = CanvaBuildState.Completed;
                    StatusChanged?.Invoke($"Design created! Opening in Canva...");
                    
                    // Open the design in browser
                    if (!string.IsNullOrEmpty(design.EditUrl))
                    {
                        Process.Start(new ProcessStartInfo(design.EditUrl) { UseShellExecute = true });
                    }
                    
                    return _currentSession;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAssistant] API error: {ex.Message}");
                StatusChanged?.Invoke($"API error: {ex.Message}. Falling back to UI automation...");
            }

            // Fallback to UI automation
            return await StartUiAutomationSessionAsync(spec, ct);
        }

        /// <summary>
        /// Create design using UI automation (free, no API key needed)
        /// </summary>
        private async Task<CanvaBuildSession> StartUiAutomationSessionAsync(CanvaDesignSpec spec, CancellationToken ct)
        {
            _currentSession!.State = CanvaBuildState.PlanningActions;
            StatusChanged?.Invoke("Planning design steps...");

            // Build action queue
            var actions = BuildActionQueue(spec);
            _currentSession.ActionQueue = actions;
            _currentSession.State = CanvaBuildState.WaitingForConfirmation;

            // First action: Open Canva
            var openAction = new CanvaAction
            {
                Type = CanvaActionType.OpenCanva,
                Description = "I will open Canva in your browser",
                RequiresConfirmation = true
            };
            
            ActionPending?.Invoke(openAction);
            
            return _currentSession;
        }

        /// <summary>
        /// Build the queue of actions needed to create the design
        /// </summary>
        private List<CanvaAction> BuildActionQueue(CanvaDesignSpec spec)
        {
            var actions = new List<CanvaAction>();

            // 1. Open Canva
            actions.Add(new CanvaAction
            {
                Type = CanvaActionType.OpenCanva,
                Description = "Open Canva in browser",
                Parameters = new() { ["url"] = GetCanvaCreateUrl(spec.DesignType) }
            });

            // 2. Create new design with size
            actions.Add(new CanvaAction
            {
                Type = CanvaActionType.CreateNewDesign,
                Description = $"Create new {spec.DesignType} ({spec.Width}x{spec.Height})",
                Parameters = new() { ["width"] = spec.Width, ["height"] = spec.Height, ["type"] = spec.DesignType.ToString() }
            });

            // 3. Set background color
            actions.Add(new CanvaAction
            {
                Type = CanvaActionType.ChangeBackground,
                Description = $"Set background color to {spec.Colors.Background}",
                Parameters = new() { ["color"] = spec.Colors.Background }
            });

            // 4. Add text elements
            foreach (var text in spec.TextElements)
            {
                actions.Add(new CanvaAction
                {
                    Type = CanvaActionType.AddText,
                    Description = $"Add text: \"{text}\"",
                    Parameters = new() { ["text"] = text, ["font"] = spec.Fonts.HeadingFont }
                });
            }

            return actions;
        }

        #endregion

        #region Session Control

        /// <summary>
        /// Confirm and execute the next pending action
        /// </summary>
        public async Task<bool> ConfirmAndExecuteNextAsync(CancellationToken ct = default)
        {
            if (_currentSession == null || _currentSession.CurrentActionIndex >= _currentSession.ActionQueue.Count)
                return false;

            var action = _currentSession.ActionQueue[_currentSession.CurrentActionIndex];
            _currentSession.State = CanvaBuildState.Executing;

            StatusChanged?.Invoke($"Executing: {action.Description}");

            bool success = await ExecuteActionAsync(action, ct);
            
            if (success)
            {
                action.IsCompleted = true;
                _currentSession.CurrentActionIndex++;
                StepCompleted?.Invoke(action.Description);

                if (_currentSession.CurrentActionIndex >= _currentSession.ActionQueue.Count)
                {
                    _currentSession.State = CanvaBuildState.Completed;
                    StatusChanged?.Invoke("Design session completed!");
                }
                else
                {
                    _currentSession.State = CanvaBuildState.WaitingForConfirmation;
                    ActionPending?.Invoke(_currentSession.ActionQueue[_currentSession.CurrentActionIndex]);
                }
            }
            else
            {
                action.Result = "Failed";
                _currentSession.State = CanvaBuildState.Failed;
            }

            return success;
        }

        /// <summary>
        /// Pause the current session
        /// </summary>
        public void PauseSession()
        {
            if (_currentSession != null)
            {
                _currentSession.IsPaused = true;
                _currentSession.State = CanvaBuildState.Paused;
                StatusChanged?.Invoke("Session paused");
            }
        }

        /// <summary>
        /// Resume a paused session
        /// </summary>
        public void ResumeSession()
        {
            if (_currentSession != null && _currentSession.IsPaused)
            {
                _currentSession.IsPaused = false;
                _currentSession.State = CanvaBuildState.WaitingForConfirmation;
                
                if (_currentSession.CurrentActionIndex < _currentSession.ActionQueue.Count)
                {
                    ActionPending?.Invoke(_currentSession.ActionQueue[_currentSession.CurrentActionIndex]);
                }
                StatusChanged?.Invoke("Session resumed");
            }
        }

        /// <summary>
        /// Cancel the current session
        /// </summary>
        public void CancelSession()
        {
            _currentSession = null;
            StatusChanged?.Invoke("Session cancelled");
        }

        #endregion

        #region Action Execution

        private async Task<bool> ExecuteActionAsync(CanvaAction action, CancellationToken ct)
        {
            try
            {
                switch (action.Type)
                {
                    case CanvaActionType.OpenCanva:
                        return await OpenCanvaAsync(action, ct);
                    
                    case CanvaActionType.CreateNewDesign:
                        return await CreateNewDesignUiAsync(action, ct);
                    
                    default:
                        // For other actions, provide guidance
                        StatusChanged?.Invoke($"Please manually: {action.Description}");
                        await Task.Delay(2000, ct); // Give user time to read
                        return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAssistant] Action error: {ex.Message}");
                action.Result = ex.Message;
                return false;
            }
        }

        private async Task<bool> OpenCanvaAsync(CanvaAction action, CancellationToken ct)
        {
            var url = action.Parameters.TryGetValue("url", out var u) ? u?.ToString() : "https://www.canva.com/";
            
            try
            {
                Process.Start(new ProcessStartInfo(url ?? "https://www.canva.com/") { UseShellExecute = true });
                await Task.Delay(2000, ct); // Wait for browser to open
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CreateNewDesignUiAsync(CanvaAction action, CancellationToken ct)
        {
            // For UI automation, we guide the user
            StatusChanged?.Invoke("In Canva, click 'Create a design' and select your design type");
            await Task.Delay(1000, ct);
            return true;
        }

        private string GetCanvaCreateUrl(CanvaDesignType type)
        {
            // Canva direct create URLs
            return type switch
            {
                CanvaDesignType.InstagramPost => "https://www.canva.com/create/instagram-posts/",
                CanvaDesignType.InstagramStory => "https://www.canva.com/create/instagram-stories/",
                CanvaDesignType.FacebookPost => "https://www.canva.com/create/facebook-posts/",
                CanvaDesignType.TwitterPost => "https://www.canva.com/create/twitter-posts/",
                CanvaDesignType.LinkedInPost => "https://www.canva.com/create/linkedin-posts/",
                CanvaDesignType.YouTubeThumbnail => "https://www.canva.com/create/youtube-thumbnails/",
                CanvaDesignType.Presentation => "https://www.canva.com/create/presentations/",
                CanvaDesignType.Logo => "https://www.canva.com/create/logos/",
                CanvaDesignType.BusinessCard => "https://www.canva.com/create/business-cards/",
                CanvaDesignType.Flyer => "https://www.canva.com/create/flyers/",
                CanvaDesignType.Poster => "https://www.canva.com/create/posters/",
                CanvaDesignType.Resume => "https://www.canva.com/create/resumes/",
                CanvaDesignType.Infographic => "https://www.canva.com/create/infographics/",
                _ => "https://www.canva.com/"
            };
        }

        #endregion

        #region Design Guidance (Always Free)

        /// <summary>
        /// Generate design guidance without API - always free
        /// Returns color palette, font suggestions, layout tips
        /// </summary>
        public DesignGuidance GenerateDesignGuidance(CanvaDesignSpec spec)
        {
            var guidance = new DesignGuidance
            {
                Title = spec.Title,
                DesignType = spec.DesignType.ToString(),
                Dimensions = $"{spec.Width} x {spec.Height} pixels"
            };

            // Color palette
            guidance.ColorPalette = new List<ColorSuggestion>
            {
                new() { Name = "Primary", HexCode = spec.Colors.Primary, Usage = "Main headings, key elements" },
                new() { Name = "Secondary", HexCode = spec.Colors.Secondary, Usage = "Supporting elements, borders" },
                new() { Name = "Accent", HexCode = spec.Colors.Accent, Usage = "Call-to-action buttons, highlights" },
                new() { Name = "Background", HexCode = spec.Colors.Background, Usage = "Canvas background" },
                new() { Name = "Text", HexCode = spec.Colors.Text, Usage = "Body text, paragraphs" }
            };

            // Font pairing
            guidance.FontPairing = new FontPairingSuggestion
            {
                HeadingFont = spec.Fonts.HeadingFont,
                BodyFont = spec.Fonts.BodyFont,
                AccentFont = spec.Fonts.AccentFont,
                HeadingSizeRange = "24-48pt",
                BodySizeRange = "12-16pt"
            };

            // Layout tips based on design type
            guidance.LayoutTips = GetLayoutTips(spec.DesignType, spec.Style);

            // Canva search terms to find relevant templates
            guidance.CanvaSearchTerms = GetCanvaSearchTerms(spec);

            return guidance;
        }

        private List<string> GetLayoutTips(CanvaDesignType type, CanvaDesignStyle style)
        {
            var tips = new List<string>();

            // General tips
            tips.Add("Use the rule of thirds for balanced composition");
            tips.Add("Maintain consistent margins (at least 20px from edges)");
            tips.Add("Limit to 2-3 fonts maximum");

            // Type-specific tips
            switch (type)
            {
                case CanvaDesignType.InstagramPost:
                    tips.Add("Keep text minimal - Instagram is visual-first");
                    tips.Add("Use high-contrast colors for readability");
                    tips.Add("Consider how it looks in the grid (square crop)");
                    break;
                case CanvaDesignType.Presentation:
                    tips.Add("One main idea per slide");
                    tips.Add("Use bullet points sparingly (max 5-6 per slide)");
                    tips.Add("Leave 40% white space for breathing room");
                    break;
                case CanvaDesignType.Logo:
                    tips.Add("Design should work in black & white");
                    tips.Add("Test at small sizes (favicon, app icon)");
                    tips.Add("Avoid trendy elements that will date quickly");
                    break;
                case CanvaDesignType.Poster:
                    tips.Add("Hierarchy: headline > subhead > details");
                    tips.Add("Make the main message readable from 3 feet away");
                    tips.Add("Include clear call-to-action");
                    break;
            }

            // Style-specific tips
            switch (style)
            {
                case CanvaDesignStyle.Minimal:
                    tips.Add("Embrace white space - less is more");
                    tips.Add("Use thin, elegant typography");
                    break;
                case CanvaDesignStyle.Bold:
                    tips.Add("Use large, impactful typography");
                    tips.Add("High contrast color combinations work well");
                    break;
                case CanvaDesignStyle.Luxury:
                    tips.Add("Use serif fonts for elegance");
                    tips.Add("Gold/black combinations convey premium feel");
                    break;
            }

            return tips;
        }

        private List<string> GetCanvaSearchTerms(CanvaDesignSpec spec)
        {
            var terms = new List<string>();
            
            terms.Add($"{spec.Style.ToString().ToLower()} {spec.DesignType.ToString().ToLower()}");
            terms.Add($"{spec.Style.ToString().ToLower()} template");
            
            if (!string.IsNullOrEmpty(spec.Description))
            {
                terms.Add(spec.Description.ToLower());
            }

            return terms;
        }

        #endregion

        #region Image Analysis (for rebuild feature)

        /// <summary>
        /// Analyze an image/screenshot to extract design elements
        /// Uses AI vision to identify colors, layout, typography
        /// </summary>
        public async Task<DesignAnalysis> AnalyzeImageAsync(byte[] imageData, CancellationToken ct = default)
        {
            var analysis = new DesignAnalysis();

            // Extract dominant colors from image
            analysis.DetectedColors = ExtractDominantColors(imageData);
            
            // Estimate dimensions
            using var ms = new MemoryStream(imageData);
            // Note: Would need System.Drawing or ImageSharp for actual dimension detection
            analysis.EstimatedWidth = 1080;
            analysis.EstimatedHeight = 1080;

            // Layout detection would require AI vision API
            analysis.LayoutType = "grid"; // Placeholder
            analysis.OverallStyle = "modern"; // Placeholder

            return analysis;
        }

        private List<string> ExtractDominantColors(byte[] imageData)
        {
            // Simplified color extraction - in production would use proper image analysis
            return new List<string> { "#FFFFFF", "#000000", "#6366F1" };
        }

        #endregion

        #region Settings Persistence

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("accessToken", out var token))
                    {
                        var tokenStr = token.GetString();
                        if (!string.IsNullOrEmpty(tokenStr))
                        {
                            _apiClient.ConfigureWithToken(tokenStr);
                        }
                    }

                    if (root.TryGetProperty("preferredMode", out var mode) &&
                        Enum.TryParse<CanvaIntegrationMode>(mode.GetString(), out var modeEnum))
                    {
                        PreferredMode = modeEnum;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAssistant] Error loading settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                var settings = new
                {
                    preferredMode = PreferredMode.ToString(),
                    // Note: In production, encrypt the token before saving
                    accessToken = _apiClient.HasValidToken ? "configured" : ""
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanvaAssistant] Error saving settings: {ex.Message}");
            }
        }

        #endregion
    }

    #region Guidance Models

    public class DesignGuidance
    {
        public string Title { get; set; } = "";
        public string DesignType { get; set; } = "";
        public string Dimensions { get; set; } = "";
        public List<ColorSuggestion> ColorPalette { get; set; } = new();
        public FontPairingSuggestion FontPairing { get; set; } = new();
        public List<string> LayoutTips { get; set; } = new();
        public List<string> CanvaSearchTerms { get; set; } = new();
    }

    public class ColorSuggestion
    {
        public string Name { get; set; } = "";
        public string HexCode { get; set; } = "";
        public string Usage { get; set; } = "";
    }

    public class FontPairingSuggestion
    {
        public string HeadingFont { get; set; } = "";
        public string BodyFont { get; set; } = "";
        public string AccentFont { get; set; } = "";
        public string HeadingSizeRange { get; set; } = "";
        public string BodySizeRange { get; set; } = "";
    }

    #endregion
}
