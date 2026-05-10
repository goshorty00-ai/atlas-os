using System;
using System.Collections.Generic;

namespace AtlasAI.Canva
{
    /// <summary>
    /// Canva integration mode - API preferred, UI automation as fallback
    /// </summary>
    public enum CanvaIntegrationMode
    {
        /// <summary>Use Canva Connect API (requires API key)</summary>
        Api,
        /// <summary>Use UI automation via browser/desktop app</summary>
        UiAutomation,
        /// <summary>Auto-select: API if configured, otherwise UI automation</summary>
        Auto
    }

    /// <summary>
    /// Design types supported by Canva
    /// </summary>
    public enum CanvaDesignType
    {
        InstagramPost,
        InstagramStory,
        FacebookPost,
        TwitterPost,
        LinkedInPost,
        YouTubeThumbnail,
        Presentation,
        Logo,
        BusinessCard,
        Flyer,
        Poster,
        Resume,
        Infographic,
        CustomSize
    }

    /// <summary>
    /// Design style presets
    /// </summary>
    public enum CanvaDesignStyle
    {
        Modern,
        Minimal,
        Luxury,
        Bold,
        Playful,
        Corporate,
        Vintage,
        Elegant,
        Artistic,
        Tech,
        Natural,
        Custom
    }

    /// <summary>
    /// Represents a design specification for Canva
    /// </summary>
    public class CanvaDesignSpec
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CanvaDesignType DesignType { get; set; } = CanvaDesignType.InstagramPost;
        public CanvaDesignStyle Style { get; set; } = CanvaDesignStyle.Modern;
        public int Width { get; set; } = 1080;
        public int Height { get; set; } = 1080;
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> TextElements { get; set; } = new();
        public ColorPalette Colors { get; set; } = new();
        public FontPairing Fonts { get; set; } = new();
        public List<string> ImageSuggestions { get; set; } = new();
        public string LayoutDescription { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Color palette for design
    /// </summary>
    public class ColorPalette
    {
        public string Primary { get; set; } = "#000000";
        public string Secondary { get; set; } = "#FFFFFF";
        public string Accent { get; set; } = "#6366F1";
        public string Background { get; set; } = "#FFFFFF";
        public string Text { get; set; } = "#1A1A1A";
        public List<string> AdditionalColors { get; set; } = new();

        public static ColorPalette FromStyle(CanvaDesignStyle style) => style switch
        {
            CanvaDesignStyle.Modern => new ColorPalette
            {
                Primary = "#1A1A2E", Secondary = "#16213E", Accent = "#0F3460",
                Background = "#FFFFFF", Text = "#1A1A1A"
            },
            CanvaDesignStyle.Minimal => new ColorPalette
            {
                Primary = "#2D2D2D", Secondary = "#F5F5F5", Accent = "#E0E0E0",
                Background = "#FFFFFF", Text = "#333333"
            },
            CanvaDesignStyle.Luxury => new ColorPalette
            {
                Primary = "#1A1A1A", Secondary = "#D4AF37", Accent = "#C9A227",
                Background = "#0D0D0D", Text = "#F5F5F5"
            },
            CanvaDesignStyle.Bold => new ColorPalette
            {
                Primary = "#FF6B6B", Secondary = "#4ECDC4", Accent = "#FFE66D",
                Background = "#2C3E50", Text = "#FFFFFF"
            },
            CanvaDesignStyle.Playful => new ColorPalette
            {
                Primary = "#FF6B9D", Secondary = "#C44569", Accent = "#F8B500",
                Background = "#FFF8E7", Text = "#2D3436"
            },
            CanvaDesignStyle.Corporate => new ColorPalette
            {
                Primary = "#003366", Secondary = "#336699", Accent = "#0066CC",
                Background = "#F8F9FA", Text = "#212529"
            },
            CanvaDesignStyle.Vintage => new ColorPalette
            {
                Primary = "#8B4513", Secondary = "#D2691E", Accent = "#F4A460",
                Background = "#FDF5E6", Text = "#3E2723"
            },
            CanvaDesignStyle.Elegant => new ColorPalette
            {
                Primary = "#2C3E50", Secondary = "#BDC3C7", Accent = "#9B59B6",
                Background = "#ECF0F1", Text = "#2C3E50"
            },
            CanvaDesignStyle.Tech => new ColorPalette
            {
                Primary = "#00D9FF", Secondary = "#7B2CBF", Accent = "#E100FF",
                Background = "#0A0A0A", Text = "#FFFFFF"
            },
            CanvaDesignStyle.Natural => new ColorPalette
            {
                Primary = "#2D5016", Secondary = "#5C8A2F", Accent = "#8BC34A",
                Background = "#F1F8E9", Text = "#33691E"
            },
            _ => new ColorPalette()
        };
    }

    /// <summary>
    /// Font pairing for design
    /// </summary>
    public class FontPairing
    {
        public string HeadingFont { get; set; } = "Montserrat";
        public string BodyFont { get; set; } = "Open Sans";
        public string AccentFont { get; set; } = "Playfair Display";

        public static FontPairing FromStyle(CanvaDesignStyle style) => style switch
        {
            CanvaDesignStyle.Modern => new FontPairing { HeadingFont = "Montserrat", BodyFont = "Open Sans", AccentFont = "Roboto" },
            CanvaDesignStyle.Minimal => new FontPairing { HeadingFont = "Helvetica Neue", BodyFont = "Arial", AccentFont = "Futura" },
            CanvaDesignStyle.Luxury => new FontPairing { HeadingFont = "Playfair Display", BodyFont = "Lato", AccentFont = "Cormorant Garamond" },
            CanvaDesignStyle.Bold => new FontPairing { HeadingFont = "Bebas Neue", BodyFont = "Oswald", AccentFont = "Impact" },
            CanvaDesignStyle.Playful => new FontPairing { HeadingFont = "Pacifico", BodyFont = "Quicksand", AccentFont = "Comic Neue" },
            CanvaDesignStyle.Corporate => new FontPairing { HeadingFont = "Roboto", BodyFont = "Source Sans Pro", AccentFont = "Merriweather" },
            CanvaDesignStyle.Vintage => new FontPairing { HeadingFont = "Abril Fatface", BodyFont = "Libre Baskerville", AccentFont = "Old Standard TT" },
            CanvaDesignStyle.Elegant => new FontPairing { HeadingFont = "Cormorant", BodyFont = "Raleway", AccentFont = "Great Vibes" },
            CanvaDesignStyle.Tech => new FontPairing { HeadingFont = "Orbitron", BodyFont = "Exo 2", AccentFont = "Share Tech Mono" },
            CanvaDesignStyle.Natural => new FontPairing { HeadingFont = "Amatic SC", BodyFont = "Josefin Sans", AccentFont = "Sacramento" },
            _ => new FontPairing()
        };
    }

    /// <summary>
    /// Analysis result from an image/screenshot
    /// </summary>
    public class DesignAnalysis
    {
        public List<string> DetectedColors { get; set; } = new();
        public List<string> DetectedFonts { get; set; } = new();
        public string LayoutType { get; set; } = "";
        public List<string> TextContent { get; set; } = new();
        public List<string> ImageDescriptions { get; set; } = new();
        public string OverallStyle { get; set; } = "";
        public int EstimatedWidth { get; set; }
        public int EstimatedHeight { get; set; }
        public List<string> DesignElements { get; set; } = new();
    }

    /// <summary>
    /// Automation action for Canva
    /// </summary>
    public class CanvaAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CanvaActionType Type { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool RequiresConfirmation { get; set; } = true;
        public bool IsCompleted { get; set; } = false;
        public string Result { get; set; } = "";
    }

    public enum CanvaActionType
    {
        OpenCanva,
        CreateNewDesign,
        SelectTemplate,
        AddText,
        AddImage,
        AddShape,
        ChangeBackground,
        ApplyColor,
        ChangeFont,
        ResizeElement,
        MoveElement,
        DuplicateElement,
        DeleteElement,
        GroupElements,
        AlignElements,
        AddEffect,
        Export,
        Save
    }

    /// <summary>
    /// Build session state
    /// </summary>
    public class CanvaBuildSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public CanvaDesignSpec Spec { get; set; } = new();
        public List<CanvaAction> ActionQueue { get; set; } = new();
        public int CurrentActionIndex { get; set; } = 0;
        public CanvaBuildState State { get; set; } = CanvaBuildState.NotStarted;
        public DateTime StartedAt { get; set; }
        public string LastError { get; set; } = "";
        public bool IsPaused { get; set; } = false;
    }

    public enum CanvaBuildState
    {
        NotStarted,
        GatheringRequirements,
        PlanningActions,
        WaitingForConfirmation,
        Executing,
        Paused,
        Completed,
        Failed
    }

    /// <summary>
    /// Standard Canva design sizes
    /// </summary>
    public static class CanvaDesignSizes
    {
        public static readonly Dictionary<CanvaDesignType, (int Width, int Height)> Sizes = new()
        {
            { CanvaDesignType.InstagramPost, (1080, 1080) },
            { CanvaDesignType.InstagramStory, (1080, 1920) },
            { CanvaDesignType.FacebookPost, (1200, 630) },
            { CanvaDesignType.TwitterPost, (1200, 675) },
            { CanvaDesignType.LinkedInPost, (1200, 627) },
            { CanvaDesignType.YouTubeThumbnail, (1280, 720) },
            { CanvaDesignType.Presentation, (1920, 1080) },
            { CanvaDesignType.Logo, (500, 500) },
            { CanvaDesignType.BusinessCard, (1050, 600) },
            { CanvaDesignType.Flyer, (1275, 1650) },
            { CanvaDesignType.Poster, (1587, 2245) },
            { CanvaDesignType.Resume, (816, 1056) },
            { CanvaDesignType.Infographic, (800, 2000) }
        };

        public static (int Width, int Height) GetSize(CanvaDesignType type)
        {
            return Sizes.TryGetValue(type, out var size) ? size : (1080, 1080);
        }
    }
}
