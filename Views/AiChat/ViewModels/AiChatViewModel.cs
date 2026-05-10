using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AtlasAI.AI;
using AtlasAI.Conversation.Models;
using AtlasAI.Conversation.Services;
using AtlasAI.Services;
using AtlasAI.Agent;
using AtlasAI.Coding;
using AtlasAI.Core;
using AtlasAI.Integrations;
using AtlasAI.SmartHome;
using AtlasAI.Tools;
using AtlasAI.UI;
using AtlasAI.Voice;
using AtlasAI.Views.AiChat.Services;
using AtlasAI.Views.ViewModels;
using Microsoft.Win32;

namespace AtlasAI.Views.AiChat.ViewModels;

internal sealed record AiChatTurnRoutingPreference(
    bool HasExplicitProviderPin,
    AIProviderType? PreferredProviderOverride,
    string PreferredModelOverride)
{
    public static AiChatTurnRoutingPreference None { get; } = new(false, null, string.Empty);
}

internal static class AiChatTurnRouting
{
    public static AiChatTurnRoutingPreference ResolveTurnRoutingPreference(
        string? userMessage,
        AIProviderType selectedProvider,
        AIModel? selectedModel,
        Func<AIProviderType, string>? manualModelResolver = null)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return AiChatTurnRoutingPreference.None;

        if (TryResolveExplicitProviderPin(text, selectedProvider, selectedModel, manualModelResolver, out var providerOverride, out var modelOverride))
            return new AiChatTurnRoutingPreference(true, providerOverride, modelOverride ?? string.Empty);

        return AiChatTurnRoutingPreference.None;
    }

    private static bool TryResolveExplicitProviderPin(
        string userMessage,
        AIProviderType selectedProvider,
        AIModel? selectedModel,
        Func<AIProviderType, string>? manualModelResolver,
        out AIProviderType providerOverride,
        out string modelOverride)
    {
        providerOverride = default;
        modelOverride = string.Empty;

        if (!TryMatchExplicitProvider(userMessage, out var matchedProvider))
            return false;

        providerOverride = matchedProvider;
        modelOverride = ResolvePinnedModelId(matchedProvider, selectedProvider, selectedModel, manualModelResolver);
        return true;
    }

    private static bool TryMatchExplicitProvider(string userMessage, out AIProviderType providerType)
    {
        providerType = default;
        var text = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var candidate in new[]
        {
            (AIProviderType.OpenAI, @"(?:openai|chatgpt|gpt(?:-\d+(?:\.\d+)?)?)"),
            (AIProviderType.Claude, @"(?:claude|anthropic)"),
            (AIProviderType.Gemini, @"(?:gemini|google\s+gemini)")
        })
        {
            var providerPattern = candidate.Item2;
            if (Regex.IsMatch(text, $@"(?ix)
                (?:^|\b)
                (?:@{providerPattern}|(?:use|using|with|via|through|on|route\s+to|answer\s+with|reply\s+with|run\s+on|try)\s+(?:the\s+)??{providerPattern})
                (?:\b|$)") ||
                Regex.IsMatch(text, $@"(?ix)
                    (?:^|\b)
                    {providerPattern}
                    \s+(?:only|specifically|for\s+this\s+turn|for\s+this\s+one)
                    (?:\b|$)"))
            {
                providerType = candidate.Item1;
                return true;
            }
        }

        return false;
    }

    private static string ResolvePinnedModelId(
        AIProviderType pinnedProvider,
        AIProviderType selectedProvider,
        AIModel? selectedModel,
        Func<AIProviderType, string>? manualModelResolver)
    {
        if (pinnedProvider == selectedProvider && !string.IsNullOrWhiteSpace(selectedModel?.Id))
            return selectedModel.Id.Trim();

        var resolved = manualModelResolver?.Invoke(pinnedProvider) ?? string.Empty;
        return (resolved ?? string.Empty).Trim();
    }
}

public sealed class AiChatViewModel : INotifyPropertyChanged
    {
    private string NormalizeResponse(string response)
    {
        var t = (response ?? "").Trim();
        t = StripRefusalAndMoralizing(t);
        return t.Trim();
    }

    private static string StripRefusalAndMoralizing(string text)
    {
        var t = text ?? "";
        if (string.IsNullOrWhiteSpace(t)) return "";

        t = Regex.Replace(t, @"^\s*[-•]?\s*(I\s+apologize,\s+but\s+)?I\s+(do not|don't)\s+feel\s+comfortable\s+responding.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*[-•]?\s*Perhaps\s+we\s+could\s+(have\s+)?start\s+our\s+conversation\s+in\s+a\s+more\s+respectful\s+manner.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*[-•]?\s*Let'?s\s+keep\s+(it\s+)?(friendly|respectful|civil).*?(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*[-•]?\s*Please\s+(refrain|avoid)\s+from.*?(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*[-•]?\s*I\s+can'?t\s+help\s+with\s+that\s+kind\s+of\s+language.*?(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        return t.Trim();
    }

    private bool ExtractPlanJson(string response, out string displayText, out string planJson)
    {
        displayText = response;
        planJson = "";
        return false;
    }

    private void LoadPersonalityFromSettings()
    {
        var prefs = PreferencesStore.Instance.Current;

        var rawBanter = prefs.ChatBanterLevel > 0 ? prefs.ChatBanterLevel : prefs.ChatHumanLevel;
        _banterLevel = Math.Clamp(rawBanter, 1, 5);
        _chatPreferredName = (prefs.ChatPreferredName ?? string.Empty).Trim();
        _chatAllowProfanity = prefs.ChatAllowProfanity;
        _chatAllowPlayfulRoast = prefs.ChatAllowPlayfulRoast;

        AvailableChatPersonalities.Clear();
        foreach (var def in AtlasAI.Personality.PersonalityRegistry.GetAvailable(includeHidden: false))
            AvailableChatPersonalities.Add(def.Id);

        var personality = string.IsNullOrWhiteSpace(prefs.ChatPersonality) ? "Buddy" : prefs.ChatPersonality.Trim();
        if (AtlasAI.Personality.PersonalityRegistry.GetById(personality) == null)
            personality = "Buddy";

        SelectedChatPersonality = personality;

        OnPropertyChanged(nameof(SelectedChatPersonality));
        OnPropertyChanged(nameof(BanterLevel));
        OnPropertyChanged(nameof(AllowProfanity));
        OnPropertyChanged(nameof(PersonalityPillText));
        OnPropertyChanged(nameof(UserDisplayName));
    }

        private readonly VoiceManager? _voiceManager;
    private readonly PersonalityEngine _personalityEngine = new();
    private readonly CodeAssistantService _codeAssistant = new();
    private readonly CodeToolExecutor _codeToolExecutor;
    private readonly SmartHomeTextCommandService _smartHomeTextCommandService = new();
    private readonly IContextService _contextService;
    private readonly IMemoryService _memoryService;
    private readonly string _agentWorkspaceRoot;
    private CancellationTokenSource? _currentCts;
    private CancellationTokenSource? _readAloudCts;
    private string? _pendingReadAloudText;
    private bool _awaitingReadAloud;
    private int _readAloudToken;

    private bool _isToolsEnabled = true;

    private bool _isSending;
    private bool _isTyping;
    private bool _isListening;
    private string _inputMessage = "";
    private string _statusText = "Neural Network Active";
    private string _voiceDebugText = "";
    private bool _isVoiceDebugVisible;
    private PresenceMode _currentMode = PresenceMode.Idle;

    private bool _isInputEnabled = true;

    private string _lastAiProviderUsed = "";
    private string _lastAiModelUsed = "";
    private string _lastAiTaskBucket = "";

    private readonly WorkingSessionState _workingSession = new();

    private string _selectedChatPersonality = "Buddy";
    private int _banterLevel = 2;
    private string _chatPreferredName = "";

    private LottieChoice? _selectedHeaderLottie;

    private string _selectedWallpaperMode = "Video";
    private WallpaperChoice? _selectedVideoWallpaper;
    private WallpaperChoice? _selectedStillWallpaper;

    private bool _chatAllowProfanity;
    private bool _chatAllowPlayfulRoast = true;
    private System.Windows.Threading.DispatcherTimer? _checkInTimer;
    private DateTime _nextCheckInUtc = DateTime.MinValue;
    private DateTime _lastUserActivityUtc = DateTime.UtcNow;
    private readonly Random _checkInRng = new(unchecked(Environment.TickCount));
    private static bool _startupGreetingShown;
    private static string _lastStartupGreeting = "";

    private AIProviderType _selectedAiProvider;
    private bool _autoModeEnabled;
    private AIModel? _selectedModel;
    private bool _suppressAiProviderChange;
    private int _aiModelsLoadRequestId;
    private VoiceProviderType _selectedVoiceProvider;
    private VoiceInfo? _selectedVoice;
    private bool _suppressVoiceSelection;
    private CancellationTokenSource? _previewVoiceCts;

    public ObservableCollection<AtlasAI.Models.ChatMessage> Messages { get; } = new();
    public ObservableCollection<string> Attachments { get; } = new();

    public ObservableCollection<string> AvailableChatPersonalities { get; } = new();
    public ObservableCollection<LottieChoice> AvailableHeaderLotties { get; } = new();

    public ObservableCollection<string> AvailableWallpaperModes { get; } = new() { "Video", "Still" };
    public ObservableCollection<WallpaperChoice> AvailableVideoWallpapers { get; } = new();
    public ObservableCollection<WallpaperChoice> AvailableStillWallpapers { get; } = new();
    public ObservableCollection<AIProviderType> AvailableAiProviders { get; } = new();
    public ObservableCollection<AIModel> AvailableModels { get; } = new();
    public ObservableCollection<VoiceProviderType> AvailableVoiceProviders { get; } = new();
    public ObservableCollection<VoiceInfo> AvailableVoices { get; } = new();

    public async Task LoadSessionAsync(string sessionId)
    {
        var store = new SessionStore();
        var session = await store.LoadSessionAsync(sessionId);
        if (session == null)
            return;

        ResetWorkingSessionState();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Messages.Clear();
            foreach (var message in session.Messages.OrderBy(m => m.Timestamp))
            {
                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = message.Content ?? string.Empty,
                    IsUser = message.Role == MessageRole.User,
                    Timestamp = message.Timestamp == default ? DateTime.Now : message.Timestamp,
                });
            }
        });
    }

    public Task StartNewSessionAsync()
    {
        ResetWorkingSessionState();
        return Application.Current.Dispatcher.InvokeAsync(() => Messages.Clear()).Task;
    }

    public Task PrepareRemoteConversationAsync(bool startNewConversation)
    {
        if (startNewConversation)
            ResetWorkingSessionState();

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (startNewConversation)
            {
                Messages.Clear();
            }
        }).Task;
    }

    public Task PresentRemoteConversationTurnAsync(string userMessage, string assistantReply, bool startNewConversation)
    {
        if (startNewConversation)
            ResetWorkingSessionState();

        return Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (startNewConversation)
            {
                Messages.Clear();
            }

            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = userMessage,
                    IsUser = true,
                    Timestamp = DateTime.Now,
                });
            }

            if (!string.IsNullOrWhiteSpace(assistantReply))
            {
                await AddAssistantResponseAsync(assistantReply, speakWhileTyping: true, CancellationToken.None);
            }
        }).Task.Unwrap();
    }

    public bool IsToolsEnabled
    {
        get => _isToolsEnabled;
        set => SetProperty(ref _isToolsEnabled, value);
    }

    public string VoiceDebugText
    {
        get => _voiceDebugText;
        private set => SetProperty(ref _voiceDebugText, value ?? string.Empty);
    }

    public bool IsVoiceDebugVisible
    {
        get => _isVoiceDebugVisible;
        private set => SetProperty(ref _isVoiceDebugVisible, value);
    }

    public string SelectedChatPersonality
    {
        get => _selectedChatPersonality;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Buddy" : value.Trim();
            if (AvailableChatPersonalities.Count > 0 && !AvailableChatPersonalities.Contains(v)) v = "Buddy";
            if (!SetProperty(ref _selectedChatPersonality, v)) return;
            PreferencesStore.Instance.Update(p =>
            {
                p.ChatPersonality = v;
                p.SelectedPersonalityId = v;
            });
            if (string.Equals(v, "Unfiltered", StringComparison.OrdinalIgnoreCase))
            {
                if (_banterLevel != 5)
                {
                    _banterLevel = 5;
                    PreferencesStore.Instance.Update(p =>
                    {
                        p.ChatBanterLevel = 5;
                        p.SavageLevel = 5;
                    });
                    OnPropertyChanged(nameof(BanterLevel));
                }

                if (!_chatAllowProfanity)
                {
                    _chatAllowProfanity = true;
                    PreferencesStore.Instance.Update(p => p.ChatAllowProfanity = true);
                    OnPropertyChanged(nameof(AllowProfanity));
                }

                if (!_chatAllowPlayfulRoast)
                {
                    _chatAllowPlayfulRoast = true;
                    PreferencesStore.Instance.Update(p => p.ChatAllowPlayfulRoast = true);
                    OnPropertyChanged(nameof(AllowPlayfulRoast));
                }
            }
            try { PreferencesStore.Instance.SavePreferences(PreferencesStore.Instance.Current); } catch { }
            OnPropertyChanged(nameof(PersonalityPillText));
        }
    }

    public int BanterLevel
    {
        get => _banterLevel;
        set
        {
            var v = Math.Clamp(value, 1, 5);
            if (!SetProperty(ref _banterLevel, v)) return;
            PreferencesStore.Instance.Update(p =>
            {
                p.ChatBanterLevel = v;
                p.SavageLevel = v;
            });
            OnPropertyChanged(nameof(PersonalityPillText));
        }
    }

    public string UserDisplayName => string.IsNullOrWhiteSpace(_chatPreferredName) ? "YOU" : _chatPreferredName.ToUpperInvariant();

    public bool AllowProfanity
    {
        get => _chatAllowProfanity;
        set
        {
            if (!SetProperty(ref _chatAllowProfanity, value)) return;
            PreferencesStore.Instance.Update(p => p.ChatAllowProfanity = value);
        }
    }

    public bool AllowPlayfulRoast
    {
        get => _chatAllowPlayfulRoast;
        set
        {
            if (!SetProperty(ref _chatAllowPlayfulRoast, value)) return;
            PreferencesStore.Instance.Update(p => p.ChatAllowPlayfulRoast = value);
        }
    }

    public string PersonalityPillText => $"{SelectedChatPersonality} · {BanterLevel}";

    public LottieChoice? SelectedHeaderLottie
    {
        get => _selectedHeaderLottie;
        set
        {
            if (!SetProperty(ref _selectedHeaderLottie, value)) return;
            try
            {
                PreferencesStore.Instance.Update(p => p.ChatHeaderLottie = value?.Label ?? "");
                ToastInfo($"Lottie: {value?.Label ?? "(none)"}");
            }
            catch (Exception ex)
            {
                ToastError($"Lottie failed: {ex.Message}");
            }
            OnPropertyChanged(nameof(HeaderLottieFilePath));
        }
    }

    public string HeaderLottieFilePath => SelectedHeaderLottie?.Path ?? string.Empty;

    public string SelectedWallpaperMode
    {
        get => _selectedWallpaperMode;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Video" : value.Trim();
            if (!AvailableWallpaperModes.Contains(v)) v = "Video";
            if (!SetProperty(ref _selectedWallpaperMode, v)) return;

            // Atomic update: mode + best matching path in one write (prevents flash/revert).
            try
            {
                var targetPath = IsVideoWallpaperMode
                    ? (_selectedVideoWallpaper?.Path ?? string.Empty).Trim()
                    : (_selectedStillWallpaper?.Path ?? string.Empty).Trim();

                PreferencesStore.Instance.Update(p =>
                {
                    p.CommandCenterWallpaperMode = v;
                    if (!string.IsNullOrWhiteSpace(targetPath))
                        p.CommandCenterWallpaperPath = targetPath;
                });

                ToastInfo($"Wallpaper mode: {v}");
            }
            catch (Exception ex)
            {
                ToastError($"Wallpaper mode failed: {ex.Message}");
                try { PreferencesStore.Instance.Update(p => p.CommandCenterWallpaperMode = v); } catch { }
            }

            OnPropertyChanged(nameof(IsVideoWallpaperMode));
            OnPropertyChanged(nameof(IsStillWallpaperMode));
        }
    }

    public bool IsVideoWallpaperMode => string.Equals(SelectedWallpaperMode, "Video", StringComparison.OrdinalIgnoreCase);
    public bool IsStillWallpaperMode => string.Equals(SelectedWallpaperMode, "Still", StringComparison.OrdinalIgnoreCase);

    public WallpaperChoice? SelectedVideoWallpaper
    {
        get => _selectedVideoWallpaper;
        set
        {
            if (!SetProperty(ref _selectedVideoWallpaper, value)) return;
            var path = (value?.Path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path)) return;

            // Atomic update: always enforce Video mode when selecting from video list.
            try
            {
                PreferencesStore.Instance.Update(p =>
                {
                    p.CommandCenterWallpaperMode = "Video";
                    p.CommandCenterWallpaperPath = path;
                });

                ToastInfo($"Wallpaper: {value?.Label ?? Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ToastError($"Wallpaper failed: {ex.Message}");
                try { PreferencesStore.Instance.Update(p => p.CommandCenterWallpaperPath = path); } catch { }
            }
        }
    }

    public WallpaperChoice? SelectedStillWallpaper
    {
        get => _selectedStillWallpaper;
        set
        {
            if (!SetProperty(ref _selectedStillWallpaper, value)) return;
            var path = (value?.Path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path)) return;

            // Atomic update: always enforce Still mode when selecting from still list.
            try
            {
                PreferencesStore.Instance.Update(p =>
                {
                    p.CommandCenterWallpaperMode = "Still";
                    p.CommandCenterWallpaperPath = path;
                });

                ToastInfo($"Wallpaper: {value?.Label ?? Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ToastError($"Wallpaper failed: {ex.Message}");
                try { PreferencesStore.Instance.Update(p => p.CommandCenterWallpaperPath = path); } catch { }
            }
        }
    }

    // Back-compat: some bindings/older code may still reference `HumanLevel`.
    public int HumanLevel
    {
        get => BanterLevel;
        set => BanterLevel = value;
    }

    public AIProviderType SelectedAiProvider
    {
        get => _selectedAiProvider;
        set
        {
            if (!SetProperty(ref _selectedAiProvider, value)) return;
            _workingSession.SelectedProvider = value.ToString();
            if (_suppressAiProviderChange) return;
            ToastInfo($"AI provider: {value}");
            _ = SetAiProviderAndReloadAsync(value);
        }
    }

    public bool AutoModeEnabled
    {
        get => _autoModeEnabled;
        set
        {
            if (!SetProperty(ref _autoModeEnabled, value)) return;
            AIManager.SetAutoModeEnabled(value);
        }
    }

    public string InputMessage
    {
        get => _inputMessage;
        set
        {
            if (SetProperty(ref _inputMessage, value ?? string.Empty))
            {
                CommandManager.InvalidateRequerySuggested();
                try
                {
                    var t = (_inputMessage ?? "").Trim();
                    if (!_isSending && !IsListening && CurrentMode != PresenceMode.Error)
                    {
                        if (!string.IsNullOrWhiteSpace(t))
                            CurrentMode = PresenceMode.Typing;
                        else if (CurrentMode == PresenceMode.Typing)
                            CurrentMode = PresenceMode.Idle;
                    }
                }
                catch
                {
                }
            }
        }
    }

    public PresenceMode CurrentMode
    {
        get => _currentMode;
        set => SetProperty(ref _currentMode, value);
    }

    public bool IsTyping
    {
        get => _isTyping;
        private set
        {
            if (SetProperty(ref _isTyping, value))
            {
                OnPropertyChanged(nameof(IsInputHintVisible));
                CommandManager.InvalidateRequerySuggested();
                try
                {
                    if (CurrentMode == PresenceMode.Error) return;
                    if (_isTyping && !IsListening)
                        CurrentMode = PresenceMode.Thinking;
                    else if (!_isTyping && CurrentMode == PresenceMode.Thinking)
                        CurrentMode = PresenceMode.Idle;
                }
                catch
                {
                }
            }
        }
    }

    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (SetProperty(ref _isListening, value))
            {
                OnPropertyChanged(nameof(InputHintText));
                OnPropertyChanged(nameof(IsInputHintVisible));
                CommandManager.InvalidateRequerySuggested();
                try
                {
                    if (CurrentMode == PresenceMode.Error) return;
                    if (_isListening)
                        CurrentMode = PresenceMode.Listening;
                    else if (CurrentMode == PresenceMode.Listening)
                        CurrentMode = PresenceMode.Idle;
                }
                catch
                {
                }
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value ?? string.Empty);
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        private set
        {
            if (SetProperty(ref _isInputEnabled, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasAttachments => Attachments.Count > 0;

    public string InputHintText
    {
        get
        {
            if (IsListening) return "LISTENING...";
            if (IsTyping) return "PROCESSING...";
            return "";
        }
    }

    public bool IsInputHintVisible => string.IsNullOrWhiteSpace(InputMessage) && (IsListening || IsTyping);

    public bool IsAgentModeEnabled
    {
        get => AgentModeManager.IsAgentModeEnabled;
        set
        {
            if (AgentModeManager.IsAgentModeEnabled == value) return;
            AgentModeManager.IsAgentModeEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool SpeechEnabled
    {
        get => _voiceManager?.SpeechEnabled ?? false;
        set
        {
            if (_voiceManager == null) return;
            if (_voiceManager.SpeechEnabled == value) return;
            _voiceManager.SpeechEnabled = value;
            OnPropertyChanged();
        }
    }

    public AIModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value)) return;
            if (!string.IsNullOrWhiteSpace(_selectedModel?.Id))
            {
                try
                {
                    if (_autoModeEnabled)
                    {
                        AIManager.SetAutoCheapModel(_selectedAiProvider, _selectedModel.Id);
                        AIManager.SetAutoSmartModel(_selectedAiProvider, _selectedModel.Id);
                        ToastInfo($"Auto model: {_selectedModel.DisplayName}");
                    }
                    else
                    {
                        // Persist the manual model against the provider selected in the settings UI.
                        AIManager.SetSelectedModel(_selectedAiProvider, _selectedModel.Id);
                        ToastInfo($"Model: {_selectedModel.DisplayName}");
                    }
                }
                catch (Exception ex)
                {
                    ToastError($"Model failed: {ex.Message}");
                }
            }
        }
    }

    public VoiceProviderType SelectedVoiceProvider
    {
        get => _selectedVoiceProvider;
        set
        {
            if (!SetProperty(ref _selectedVoiceProvider, value)) return;
            _ = SetVoiceProviderAndReloadAsync(value);
        }
    }

    public VoiceInfo? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (!SetProperty(ref _selectedVoice, value)) return;
            if (_suppressVoiceSelection) return;
            if (_voiceManager == null) return;
            if (string.IsNullOrWhiteSpace(_selectedVoice?.Id)) return;

            var voiceId = _selectedVoice.Id;
            var provider = _selectedVoice.Provider;
            var displayName = _selectedVoice.DisplayName;
            ToastInfo($"Voice: {displayName}");
            _ = ApplySelectedVoiceAsync(voiceId, provider, displayName);
            QueuePreviewVoice();
        }
    }

    private async Task ApplySelectedVoiceAsync(string voiceId, VoiceProviderType provider, string displayName)
    {
        try
        {
            if (_voiceManager == null) return;
            try { await _voiceManager.WaitForInitializationAsync(); } catch { }

            // Ensure the voice's provider is active; otherwise SelectVoiceAsync will fail silently.
            if (_voiceManager.ActiveProviderType != provider)
            {
                var providerOk = await _voiceManager.SetProviderAsync(provider);
                if (!providerOk)
                {
                    ToastError($"Voice provider unavailable: {provider}");
                    return;
                }

                try
                {
                    _selectedVoiceProvider = provider;
                    OnPropertyChanged(nameof(SelectedVoiceProvider));
                }
                catch { }

                try { await LoadVoicesAsync(); } catch { }
            }

            var ok = await _voiceManager.SelectVoiceAsync(voiceId);
            if (!ok)
            {
                ToastError("Voice unavailable");
                try
                {
                    var current = _voiceManager.SelectedVoice;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _selectedVoice = current != null
                            ? AvailableVoices.FirstOrDefault(v => string.Equals(v.Id, current.Id, StringComparison.Ordinal))
                            : AvailableVoices.FirstOrDefault();
                        OnPropertyChanged(nameof(SelectedVoice));
                    });
                }
                catch { }
                return;
            }

            // CRITICAL: VoiceManager.SpeakAsync selects via VoiceSelectionService which reads VoicePreferences.
            try { VoicePreferences.Current.SetGlobalVoice(voiceId); } catch { }

            // Ensure the voice catalog knows about this voice/provider so VoiceSelectionService can resolve it.
            // (Without this, non-static ElevenLabs voices may resolve as "Custom Voice" with no provider, and TTS can fall back.)
            if (provider == VoiceProviderType.ElevenLabs)
            {
                try { await VoiceCatalogService.Instance.RefreshAsync(); } catch { }
            }

            // Audible confirmation (only if speech is enabled).
            // DISABLED: User report "saying the name of the voice" issue during startup.
            /*
            try
            {
                if (_voiceManager.SpeechEnabled)
                {
                    if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;
                    var phrase = string.IsNullOrWhiteSpace(displayName)
                        ? "Voice selected."
                        : $"Voice set to {displayName}.";
                    _ = _voiceManager.SpeakAsync(phrase, ResponseType.Normal);
                }
            }
            catch { }
            */
        }
        catch (Exception ex)
        {
            ToastError($"Voice failed: {ex.Message}");
        }
    }

    public ICommand SendCommand { get; }
    public ICommand ToggleMicCommand { get; }
    public ICommand AttachCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand AddWallpaperCommand { get; }
    public ICommand PreviewVoiceCommand { get; }

    public AiChatViewModel(VoiceManager? voiceManager)
    {
        _voiceManager = voiceManager;
        Attachments.CollectionChanged += Attachments_CollectionChanged;

        _contextService = AtlasAI.App.GetService<IContextService>() ?? new ContextService();
        _memoryService = AtlasAI.App.GetService<IMemoryService>() ?? new MemoryService();
        try { _memoryService.Initialize(); } catch { }

        try { PreferencesStore.Instance.PreferencesChanged += PreferencesStore_PreferencesChanged; } catch { }
        try { AtlasAI.Settings.SettingsStore.SettingsChanged += (s, e) => Application.Current?.Dispatcher?.Invoke(LoadPersonalityFromSettings); } catch { }

        _agentWorkspaceRoot = TryFindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(_agentWorkspaceRoot))
        {
            try { _codeAssistant.SetWorkspace(_agentWorkspaceRoot); } catch { }
        }
        _codeToolExecutor = new CodeToolExecutor(_codeAssistant);

        // persisted chat personality settings
        var prefs = PreferencesStore.Instance.Current;
        LoadPersonalityFromSettings();

        // If the chat preferred name hasn't been set in settings yet, use local memory profile.
        try
        {
            if (string.IsNullOrWhiteSpace(_chatPreferredName))
            {
                var snap = _memoryService.GetSnapshot();
                var name = (snap?.PreferredName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _chatPreferredName = name;
                    try { PreferencesStore.Instance.Update(p => p.ChatPreferredName = name); } catch { }
                }
            }
        }
        catch
        {
        }

        LoadHeaderLotties(prefs.ChatHeaderLottie);

        _selectedWallpaperMode = string.IsNullOrWhiteSpace(prefs.CommandCenterWallpaperMode) ? "Video" : prefs.CommandCenterWallpaperMode.Trim();
        if (!AvailableWallpaperModes.Contains(_selectedWallpaperMode)) _selectedWallpaperMode = "Video";
        LoadWallpapers(prefs.CommandCenterWallpaperPath);

        OnPropertyChanged(nameof(SelectedChatPersonality));
        OnPropertyChanged(nameof(BanterLevel));
        OnPropertyChanged(nameof(AllowProfanity));
        OnPropertyChanged(nameof(AllowPlayfulRoast));
        OnPropertyChanged(nameof(PersonalityPillText));
        OnPropertyChanged(nameof(SelectedHeaderLottie));
        OnPropertyChanged(nameof(HeaderLottieFilePath));

        OnPropertyChanged(nameof(SelectedWallpaperMode));
        OnPropertyChanged(nameof(IsVideoWallpaperMode));
        OnPropertyChanged(nameof(IsStillWallpaperMode));
        OnPropertyChanged(nameof(SelectedVideoWallpaper));
        OnPropertyChanged(nameof(SelectedStillWallpaper));

        if (!prefs.ChatOnboardingComplete || !prefs.FirstRunWizardComplete)
        {
            ShowStartupSequence(false);
            try
            {
                PreferencesStore.Instance.Update(p =>
                {
                    p.ChatOnboardingComplete = true;
                    p.FirstRunWizardComplete = true;
                });
                PreferencesStore.Instance.SaveNow();
            }
            catch { }
        }
        EnsureStartupGreeting();

        // Disable unsolicited ambient check-ins (water/joke prompts) to keep chat signal clean.

        SendCommand = new RelayCommand(TrySend, CanSend);
        ToggleMicCommand = new RelayCommand(ToggleMic);
        AttachCommand = new RelayCommand(OpenAttachments);
        RemoveAttachmentCommand = new RelayCommand<string?>(RemoveAttachment);
        AddWallpaperCommand = new RelayCommand(AddWallpaper);
        PreviewVoiceCommand = new RelayCommand(PreviewVoice);

        // AI provider + models
        var providerTypes = AIManager.GetAllProviders()
            .Select(p => p.ProviderType)
            .Distinct()
            .ToList();
        foreach (var pt in providerTypes)
            AvailableAiProviders.Add(pt);

        // Reflect the user's selected provider from settings.
        // (Don't auto-snapback here; users need to be able to select providers even before configuring keys.)
        _selectedAiProvider = AIManager.GetActiveProvider();
        _autoModeEnabled = AIManager.GetAutoModeEnabled();
        ResetWorkingSessionState();
        OnPropertyChanged(nameof(SelectedAiProvider));
        OnPropertyChanged(nameof(AutoModeEnabled));

        // Models
        _ = LoadModelsAsync();

        // Voice settings
        foreach (var p in Enum.GetValues(typeof(VoiceProviderType)).Cast<VoiceProviderType>())
            AvailableVoiceProviders.Add(p);

        _selectedVoiceProvider = _voiceManager?.ActiveProviderType ?? VoiceProviderType.ElevenLabs;
        OnPropertyChanged(nameof(SelectedVoiceProvider));

        _ = LoadVoicesAsync();

        // Voice status wiring (helps debug when speech isn't audible)
        if (_voiceManager != null)
        {
            _voiceManager.SpeechError += (_, msg) =>
            {
                try
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        var m = (msg ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(m) &&
                            (m.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                             m.Contains("canceled", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Cancellation is expected when speech is interrupted by a newer request.
                            OnPropertyChanged(nameof(SpeechEnabled));
                            return;
                        }

                        if (!IsTyping && !IsListening)
                            StatusText = string.IsNullOrWhiteSpace(m) ? "Voice error" : $"Voice error: {m}";
                        OnPropertyChanged(nameof(SpeechEnabled));
                        try { CurrentMode = PresenceMode.Error; } catch { }

                        try
                        {
                            if (!string.IsNullOrWhiteSpace(m))
                                ToastError($"Voice error: {m}");
                            else
                                ToastError("Voice error");
                        }
                        catch { }

                        // Auto-reset from Error back to Idle after a short delay
                        // so a transient startup error doesn't permanently lock the UI red.
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            await System.Threading.Tasks.Task.Delay(3500);
                            try
                            {
                                Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    if (CurrentMode == PresenceMode.Error)
                                        CurrentMode = PresenceMode.Idle;
                                });
                            }
                            catch { }
                        });
                    });
                }
                catch { }
            };

            _voiceManager.SpeechStarted += (_, __) =>
            {
                try
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            if (CurrentMode != PresenceMode.Error)
                                CurrentMode = PresenceMode.Talking;
                        }
                        catch { }
                        if (!IsTyping && !IsListening)
                            StatusText = "Speaking...";
                        OnPropertyChanged(nameof(SpeechEnabled));
                    });
                }
                catch { }
            };

            _voiceManager.SpeechEnded += (_, __) =>
            {
                try
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            // Always reset mode on speech end, including clearing Error state
                            CurrentMode = IsListening ? PresenceMode.Listening : (IsTyping ? PresenceMode.Thinking : PresenceMode.Idle);
                        }
                        catch { }
                        if (!IsTyping && !IsListening)
                            StatusText = "Neural Network Active";
                        OnPropertyChanged(nameof(SpeechEnabled));
                    });
                }
                catch { }
            };
        }

        // Voice orchestrator wiring (push-to-talk)
        var orchestrator = VoiceSystemOrchestrator.Instance;
        if (_voiceManager != null)
            orchestrator.SetVoiceManager(_voiceManager);
        orchestrator.ListeningStarted += Orchestrator_ListeningStarted;
        orchestrator.ListeningStopped += Orchestrator_ListeningStopped;
        orchestrator.Error += Orchestrator_Error;

        if (orchestrator.PushToTalkCommandHandler == null)
        {
            orchestrator.PushToTalkCommandHandler = text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    InputMessage = text;
                    TrySend();
                });
            };
        }

        if (orchestrator.SubmitMessageHandler == null)
        {
            orchestrator.SubmitMessageHandler = text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    InputMessage = text;
                    TrySend();
                });
            };
        }
    }

    private void StartCheckIns()
    {
        try { _checkInTimer?.Stop(); } catch { }

        _checkInTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _checkInTimer.Tick += (_, __) =>
        {
            try { MaybeEmitCheckIn(); } catch { }
        };

        ScheduleNextCheckIn();
        _checkInTimer.Start();
    }

    private void EnsureStartupGreeting()
    {
        // Disabled: CommandCenterWindow.TrySpeakChatGreetingAsync() handles both
        // the text greeting AND the voice greeting on startup. Having this too
        // caused duplicate text bubbles and voice collisions.
    }

    private string BuildStartupGreeting()
    {
        try
        {
            var prefs = PreferencesStore.Instance.Current;
            var name = (_chatPreferredName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = (prefs.ChatPreferredName ?? "").Trim();

            var persona = (SelectedChatPersonality ?? "").Trim();
            if (string.IsNullOrWhiteSpace(persona)) persona = "Buddy";

            var hasName = !string.IsNullOrWhiteSpace(name);
            var greeting = PickFromPool(BuildGreetingPool(persona, hasName), avoid: _lastStartupGreeting);
            if (string.IsNullOrWhiteSpace(greeting))
                greeting = PickFromPool(BuildGreetingPool("Buddy", hasName), avoid: _lastStartupGreeting);

            if (!string.IsNullOrWhiteSpace(greeting))
                _lastStartupGreeting = greeting;

            if (!hasName && !string.IsNullOrWhiteSpace(greeting))
                greeting = greeting.Replace("{name}", ""); // no-op safety
            else if (hasName && !string.IsNullOrWhiteSpace(greeting))
                greeting = greeting.Replace("{name}", name);

            return (greeting ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private string[] BuildGreetingPool(string persona, bool hasName)
    {
        var id = (persona ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id)) id = "Buddy";

        if (string.Equals(id, "Unfiltered", StringComparison.OrdinalIgnoreCase))
        {
            return hasName
                ? new[]
                {
                    "Alright, {name}. What’s the damage?",
                    "{name}. You here to fix something, or just talk shite?",
                    "Oi {name} — what’s broken now?",
                    "Good. You’re back. What do you need?",
                    "{name}, give me the short version."
                }
                : new[]
                {
                    "Alright. Who am I calling you?",
                    "You got a name, or are we doing this on hard mode?",
                    "Right then — what should I call you?",
                    "Alright. Name?",
                    "Okay. Who’s this?"
                };
        }

        if (string.Equals(id, "Sarcasm", StringComparison.OrdinalIgnoreCase))
        {
            return hasName
                ? new[]
                {
                    "Oh look, {name} is back. What’s the plan?",
                    "Alright {name}. What’s broken this time?",
                    "Hi {name}. Try not to set anything on fire. What do you need?",
                    "{name}. Hit me. What are we fixing?",
                    "Welcome back, {name}. Let’s pretend this will be painless."
                }
                : new[]
                {
                    "Oh good. A mystery person. What should I call you?",
                    "Alright — name, then tell me what you want.",
                    "Before we begin: what’s your name?",
                    "Okay. Who am I roasting today?",
                    "Hi. Name?"
                };
        }

        if (string.Equals(id, "Funny", StringComparison.OrdinalIgnoreCase))
        {
            return hasName
                ? new[]
                {
                    "Alright {name} — what chaos are we turning into progress today?",
                    "{name}, what do you need? Try not to break spacetime.",
                    "Hey {name}. Give me the problem; I’ll bring the bad jokes.",
                    "{name}! What’s up? What are we doing?",
                    "Okay {name} — hit me."
                }
                : new[]
                {
                    "Hey. What should I call you?",
                    "Alright, who’s this? Name first, then we’ll fix stuff.",
                    "Hi! Name?",
                    "Okay — what do you want me to call you?",
                    "Before we start: what’s your name?"
                };
        }

        if (string.Equals(id, "Professional", StringComparison.OrdinalIgnoreCase))
        {
            return hasName
                ? new[]
                {
                    "Good day, {name}. How can I help?",
                    "Hello {name}. What can I assist with?",
                    "Welcome back, {name}. What would you like to do?",
                    "{name}, what are we working on?",
                    "Hi {name}. What’s the objective?"
                }
                : new[]
                {
                    "Hello. What should I call you?",
                    "Good day. Your name, please?",
                    "Hi. What name should I use?",
                    "Hello. How would you like to be addressed?",
                    "Good day. What should I call you?"
                };
        }

        if (string.Equals(id, "Romantic", StringComparison.OrdinalIgnoreCase))
        {
            return hasName
                ? new[]
                {
                    "Hey {name}. You alright? What do you need?",
                    "Hi {name}. Tell me what’s up.",
                    "{name}, I’m here. What can I do for you today?",
                    "Hey you, {name}. What are we doing?",
                    "Hi {name}. What’s on your mind?"
                }
                : new[]
                {
                    "Hey. What should I call you?",
                    "Hi. What’s your name?",
                    "Hello. What do you want me to call you?",
                    "Hey. Name?",
                    "Hi there. What’s your name?"
                };
        }

        return hasName
            ? new[]
            {
                "Alright {name}. What do you need?",
                "Hey {name}. What’s up?",
                "{name} — what are we doing?",
                "Hi {name}. Hit me.",
                "Okay {name}. What’s the goal?"
            }
            : new[]
            {
                "Alright. What should I call you?",
                "Hey. Name?",
                "Hello. What’s your name?",
                "Okay — what do you want me to call you?",
                "Hi. What should I call you?"
            };
    }

    private string PickFromPool(string[] pool, string avoid)
    {
        if (pool == null || pool.Length == 0) return "";
        var tries = Math.Min(8, pool.Length * 2);
        for (var i = 0; i < tries; i++)
        {
            var pick = pool[_checkInRng.Next(pool.Length)];
            if (string.IsNullOrWhiteSpace(pick)) continue;
            if (!string.IsNullOrWhiteSpace(avoid) && string.Equals(pick, avoid, StringComparison.Ordinal))
                continue;
            return pick;
        }
        return pool[0] ?? "";
    }

    private void ScheduleNextCheckIn()
    {
        try
        {
            var prefs = PreferencesStore.Instance.Current;
            var min = Math.Clamp(prefs.ChatCheckInMinMinutes, 5, 360);
            var max = Math.Clamp(prefs.ChatCheckInMaxMinutes, min, 360);
            var minutes = _checkInRng.Next(min, max + 1);
            _nextCheckInUtc = DateTime.UtcNow.AddMinutes(minutes);
        }
        catch
        {
            _nextCheckInUtc = DateTime.UtcNow.AddMinutes(30);
        }
    }

    private void MaybeEmitCheckIn()
    {
        // Ambient check-ins are intentionally disabled to avoid unsolicited chatter.
        return;

        var prefs = PreferencesStore.Instance.Current;
        if (!prefs.AliveModeEnabled) return;
        if (!prefs.ChatCheckInsEnabled) return;
        if (prefs.AmbientDimEnabled) return;
        if (DateTime.UtcNow < _nextCheckInUtc) return;

        var idleMin = Math.Max(0, prefs.ChatCheckInIdleMinutes);
        if ((DateTime.UtcNow - _lastUserActivityUtc) < TimeSpan.FromMinutes(idleMin))
        {
            ScheduleNextCheckIn();
            return;
        }

        if (_voiceManager?.IsSpeaking == true)
        {
            _nextCheckInUtc = DateTime.UtcNow.AddMinutes(5);
            return;
        }

        try
        {
            if (VoiceSystemOrchestrator.Instance.IsListening)
            {
                _nextCheckInUtc = DateTime.UtcNow.AddMinutes(3);
                return;
            }
        }
        catch
        {
        }

        var msg = BuildCheckInMessage();
        if (string.IsNullOrWhiteSpace(msg))
        {
            ScheduleNextCheckIn();
            return;
        }

        Messages.Add(new AtlasAI.Models.ChatMessage
        {
            Content = msg,
            IsUser = false,
            Timestamp = DateTime.Now
        });

        ScheduleNextCheckIn();
    }

    private string BuildCheckInMessage()
    {
        var prefs = PreferencesStore.Instance.Current;
        var name = (_chatPreferredName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = (prefs.ChatPreferredName ?? "").Trim();

        var friendlyName = string.IsNullOrWhiteSpace(name) ? "" : $", {name}";
        var level = Math.Clamp(BanterLevel, 1, 5);

        var pick = _checkInRng.Next(0, 10);
        if (pick <= 4)
        {
            if (level >= 4)
                return $"Oi{friendlyName}. Quick one: have you had any water, or are we running on pure chaos again?";
            return $"Quick check-in{friendlyName}: you had some water lately?";
        }

        if (pick <= 7)
        {
            if (level >= 4)
                return $"You all good{friendlyName}? Blink twice if you're about to throw the PC out the window.";
            return $"You alright{friendlyName}? Just checking in.";
        }

        var jokes = new[]
        {
            $"Random one{friendlyName}: why don’t skeletons fight each other? They don’t have the guts.",
            $"Quick joke{friendlyName}: I told my PC I needed a break… now it won’t stop sending me KitKats.",
            $"Tiny joke{friendlyName}: parallel lines have so much in common. Shame they’ll never meet."
        };
        return jokes[_checkInRng.Next(jokes.Length)];
    }

    public AiChatViewModel() : this(null)
    {
    }

    private void LoadHeaderLotties(string? preferredLabel)
    {
        try
        {
            AvailableHeaderLotties.Clear();

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Directory.GetCurrentDirectory();

            var candidates = new List<LottieChoice>();

            void AddFromFolder(string folderPath, string labelPrefix)
            {
                if (!Directory.Exists(folderPath)) return;

                foreach (var file in Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    candidates.Add(new LottieChoice($"{labelPrefix}/{name}", file));
                }
            }

            AddFromFolder(Path.Combine(baseDir, "Assets", "Animations", "Lottie"), "Assets/Animations/Lottie");
            AddFromFolder(Path.Combine(baseDir, "Animations"), "Animations");

            foreach (var c in candidates.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase))
                AvailableHeaderLotties.Add(c);

            _selectedHeaderLottie = AvailableHeaderLotties.FirstOrDefault(x =>
                string.Equals(x.Label, preferredLabel ?? "", StringComparison.OrdinalIgnoreCase))
                ?? AvailableHeaderLotties.FirstOrDefault();
        }
        catch
        {
            _selectedHeaderLottie = null;
        }
    }

    public sealed record LottieChoice(string Label, string Path);

    public sealed record WallpaperChoice(string Label, string Path);

    private void LoadWallpapers(string? preferredPath)
    {
        try
        {
            AvailableVideoWallpapers.Clear();
            AvailableStillWallpapers.Clear();

            var preferred = (preferredPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            {
                var name = Path.GetFileName(preferred);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var ext = Path.GetExtension(name) ?? "";
                    if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase))
                        AvailableVideoWallpapers.Add(new WallpaperChoice($"{name}", preferred));
                    else
                        AvailableStillWallpapers.Add(new WallpaperChoice($"{name}", preferred));
                }
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Directory.GetCurrentDirectory();

            // IMPORTANT: avoid duplicates by *not* scanning both output + source trees.
            // Assets are copied into output; use baseDir primarily, fall back to CWD only
            // if output assets aren't present.
            IEnumerable<string> CandidateRoots()
            {
                var roots = new List<string>();
                var cwd = "";
                try { cwd = Directory.GetCurrentDirectory(); } catch { }

                bool HasAssets(string root)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(root)) return false;
                        return Directory.Exists(Path.Combine(root, "Assets", "Video_Wallpaper")) ||
                               Directory.Exists(Path.Combine(root, "Assets", "Wallpaper"));
                    }
                    catch { return false; }
                }

                if (!string.IsNullOrWhiteSpace(baseDir))
                    roots.Add(baseDir);

                if (!HasAssets(baseDir) && !string.IsNullOrWhiteSpace(cwd))
                    roots.Add(cwd);

                return roots
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }

            void AddFromFolder(string folder, string labelPrefix)
            {
                if (!Directory.Exists(folder)) return;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(file);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var ext = Path.GetExtension(name) ?? "";
                        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase))
                            AvailableVideoWallpapers.Add(new WallpaperChoice(name, file));
                        else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                 ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                            AvailableStillWallpapers.Add(new WallpaperChoice(name, file));
                    }
                }
                catch
                {
                }
            }

            foreach (var root in CandidateRoots())
            {
                AddFromFolder(Path.Combine(root, "Assets", "Video_Wallpaper"), "Assets/Video_Wallpaper");
                AddFromFolder(Path.Combine(root, "Assets", "Wallpaper"), "Assets/Wallpaper");
            }

            // De-dupe by filename (prefer output/baseDir copies) and sort.
            bool PreferAOverB(string a, string b)
            {
                try
                {
                    var ad = (baseDir ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(ad))
                    {
                        var aIn = a.StartsWith(ad, StringComparison.OrdinalIgnoreCase);
                        var bIn = b.StartsWith(ad, StringComparison.OrdinalIgnoreCase);
                        if (aIn != bIn) return aIn;
                    }
                }
                catch { }
                return true;
            }

            var video = AvailableVideoWallpapers
                .GroupBy(x => Path.GetFileName(x.Path) ?? x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Aggregate((best, next) => PreferAOverB(best.Path, next.Path) ? best : next))
                .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var still = AvailableStillWallpapers
                .GroupBy(x => Path.GetFileName(x.Path) ?? x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Aggregate((best, next) => PreferAOverB(best.Path, next.Path) ? best : next))
                .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AvailableVideoWallpapers.Clear();
            foreach (var c in video) AvailableVideoWallpapers.Add(c);

            AvailableStillWallpapers.Clear();
            foreach (var c in still) AvailableStillWallpapers.Add(c);

            _selectedVideoWallpaper = AvailableVideoWallpapers.FirstOrDefault(x =>
                string.Equals(x.Path, preferred, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVideoWallpapers.FirstOrDefault();

            _selectedStillWallpaper = AvailableStillWallpapers.FirstOrDefault(x =>
                string.Equals(x.Path, preferred, StringComparison.OrdinalIgnoreCase))
                ?? AvailableStillWallpapers.FirstOrDefault();
        }
        catch
        {
            _selectedVideoWallpaper = null;
            _selectedStillWallpaper = null;
        }
    }

    private void AddWallpaper()
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = IsVideoWallpaperMode ? "Add video wallpaper" : "Add still wallpaper",
                Filter = IsVideoWallpaperMode
                    ? "Video (*.mp4;*.wmv)|*.mp4;*.wmv|All files (*.*)|*.*"
                    : "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;
            var path = (dlg.FileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            PreferencesStore.Instance.Update(p => p.CommandCenterWallpaperPath = path);
            LoadWallpapers(path);
            OnPropertyChanged(nameof(AvailableVideoWallpapers));
            OnPropertyChanged(nameof(AvailableStillWallpapers));
            OnPropertyChanged(nameof(SelectedVideoWallpaper));
            OnPropertyChanged(nameof(SelectedStillWallpaper));
        }
        catch
        {
        }
    }

    private void Attachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAttachments));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Orchestrator_ListeningStarted(object? sender, ListeningSource e)
    {
        _lastUserActivityUtc = DateTime.UtcNow;
        IsListening = true;
        IsInputEnabled = false;
        StatusText = "Listening… (click mic to stop)";
        VoiceDebugText = $"Listening ({e})";
        IsVoiceDebugVisible = true;
        try
        {
            if (CurrentMode != PresenceMode.Error)
                CurrentMode = PresenceMode.Listening;
        }
        catch { }
    }

    private void Orchestrator_ListeningStopped(object? sender, EventArgs e)
    {
        IsListening = false;
        IsInputEnabled = true;
        StatusText = "Neural Network Active";
        VoiceDebugText = "";
        IsVoiceDebugVisible = false;
        try
        {
            if (CurrentMode != PresenceMode.Error)
                CurrentMode = IsTyping ? PresenceMode.Thinking : PresenceMode.Idle;
        }
        catch { }
    }

    private void Orchestrator_Error(object? sender, string msg)
    {
        try
        {
            var m = (msg ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(m)) return;

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    VoiceDebugText = m;
                    // Keep visible while listening; allow it to persist briefly even if state is noisy.
                    if (IsListening)
                        IsVoiceDebugVisible = true;
                }
                catch { }
            });
        }
        catch
        {
        }
    }

    private void ToggleMic()
    {
        var prefs = PreferencesStore.Instance.Current;
        if (!prefs.EnableMicrophone)
        {
            StatusText = "Microphone disabled";
            return;
        }

        var orchestrator = VoiceSystemOrchestrator.Instance;
        if (orchestrator.IsListening)
        {
            orchestrator.StopListening();
            return;
        }

        orchestrator.BeginListening(ListeningSource.PushToTalk, continuous: true);
    }

    private void OpenAttachments()
    {
        // OpenFileDialog is a WPF UI component; kept here to avoid introducing a new dialog-service layer.
        var dlg = new OpenFileDialog
        {
            Title = "Attach files",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp|Archives (*.zip)|*.zip|Text/Code (*.txt;*.md;*.json;*.xml;*.yml;*.yaml;*.cs;*.xaml)|*.txt;*.md;*.json;*.xml;*.yml;*.yaml;*.cs;*.xaml|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true) return;

        AddAttachments(dlg.FileNames);
    }

    private bool CanSend()
    {
        if (_isSending) return false;
        if (IsTyping) return false;
        if (IsListening) return false;

        return !string.IsNullOrWhiteSpace(InputMessage) || HasAttachments;
    }

    public void TrySend()
    {
        if (!CanSend()) return;
        if (_isSending) return;

        SendAsync();
    }

    private async void SendAsync()
    {
        var text = (InputMessage ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text) && Attachments.Count == 0) return;
        AtlasAI.Models.ChatMessage? pendingAssistantMessage = null;

        if (HandleReadAloudResponse(text))
        {
            InputMessage = "";
            return;
        }

        var lowerInput = text.ToLowerInvariant();
        if (lowerInput == "startup sequence" || lowerInput == "start up sequence")
        {
            Messages.Add(new AtlasAI.Models.ChatMessage
            {
                Content = text,
                IsUser = true,
                Timestamp = DateTime.Now
            });
            InputMessage = "";
            ShowStartupSequence(true);
            return;
        }

        _lastUserActivityUtc = DateTime.UtcNow;
        try { ScheduleNextCheckIn(); } catch { }

        _isSending = true;
        _currentCts = new CancellationTokenSource();
        CommandManager.InvalidateRequerySuggested();

        try
        {
            var attachmentPaths = Attachments.ToList();
            var combinedText = BuildUserPayload(text, attachmentPaths);

            Messages.Add(new AtlasAI.Models.ChatMessage
            {
                Content = combinedText,
                IsUser = true,
                Timestamp = DateTime.Now
            });

            InputMessage = "";
            Attachments.Clear();

            IsTyping = true;
            StatusText = "Atlas is thinking...";
            pendingAssistantMessage = new AtlasAI.Models.ChatMessage
            {
                Content = "Atlas is thinking...",
                IsUser = false,
                Timestamp = DateTime.Now
            };
            Messages.Add(pendingAssistantMessage);
            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = PresenceMode.Thinking;
            }
            catch { }

            using var timeoutCts = new CancellationTokenSource(120000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_currentCts.Token, timeoutCts.Token);
            var ct = linkedCts.Token;

            // Built-in deterministic tool pipeline tests (safe/local).
            // Must run BEFORE any ToolExecutor or AI call.
            var cmd = (text ?? "").Trim();
            if (string.Equals(cmd, "/tooltest", StringComparison.Ordinal))
            {
                IsTyping = false;
                StatusText = "Neural Network Active";
                if (pendingAssistantMessage != null)
                    pendingAssistantMessage.Content = "✅ Tools pipeline is working.";
                return;
            }

            if (string.Equals(cmd, "/toollist", StringComparison.Ordinal))
            {
                IsTyping = false;
                StatusText = "Neural Network Active";
                if (pendingAssistantMessage != null)
                    pendingAssistantMessage.Content = BuildToolListText();
                return;
            }

            // LocalPreferenceMemory commands (safe/local).
            // Must run BEFORE ToolExecutor or AI call.
            if (TryHandleLocalPreferenceMemoryCommand(cmd, out var memoryReply))
            {
                IsTyping = false;
                StatusText = "Neural Network Active";
                if (pendingAssistantMessage != null)
                    pendingAssistantMessage.Content = memoryReply;

                if (_voiceManager != null)
                    _ = TrySpeakAsync("Okay.");

                return;
            }

            // Observe the user's message to infer verbosity (only) and remember a small set of explicit preferences.
            // Skip slash-commands.
            if (!cmd.StartsWith("/", StringComparison.Ordinal))
            {
                LocalPreferenceMemoryStore.Instance.ObserveUserMessage(cmd);
                TryRememberExplicitPreferences(cmd);

                try { _memoryService.ObserveUserMessage(cmd); } catch { }

                try
                {
                    var changed = PersonalityLearningService.ObserveUserMessageForBanter(cmd);
                    if (changed.HasValue && changed.Value != _banterLevel)
                    {
                        _banterLevel = Math.Clamp(changed.Value, 1, 5);
                        OnPropertyChanged(nameof(BanterLevel));
                        OnPropertyChanged(nameof(PersonalityPillText));

                        try { _memoryService.Update(p => p.BanterLevel = _banterLevel); } catch { }
                    }
                }
                catch
                {
                }
            }

            var smartHomeCommandResult = await _smartHomeTextCommandService.ExecuteAsync(cmd, ct, bypassVoiceCommandToggle: true);
            if (smartHomeCommandResult.Matched)
            {
                if (smartHomeCommandResult.Ok)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(smartHomeCommandResult.RequestedPage) &&
                            string.Equals(smartHomeCommandResult.RequestedPage, "smarthome", StringComparison.OrdinalIgnoreCase))
                        {
                            var handled = await ShowSmartHomeIntentAsync(smartHomeCommandResult, ct);
                            if (!handled)
                            {
                                smartHomeCommandResult = new SmartHomeTextCommandResult
                                {
                                    Matched = true,
                                    Ok = false,
                                    Message = "Atlas could not open the Smart Home page.",
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        smartHomeCommandResult = new SmartHomeTextCommandResult
                        {
                            Matched = true,
                            Ok = false,
                            Message = ex.Message,
                        };
                    }
                }

                IsTyping = false;
                StatusText = "Neural Network Active";

                var responseText = smartHomeCommandResult.Ok
                    ? smartHomeCommandResult.Message
                    : $"Smart Home command failed: {smartHomeCommandResult.Message}";

                UpdateWorkingSessionAfterTurn(
                    new AssistantExecutionPlan(AIManager.AITaskBucket.Chat, AssistantExecutionMode.PlainAi, "smart-home-direct", StructuredTaskKind.SmartHomeAction),
                    "smart-home runtime",
                    attachmentPaths,
                    responseText,
                    smartHomeCommandResult.Ok,
                    smartHomeCommandResult.Ok ? null : responseText);

                await AddAssistantResponseAsync(responseText, speakWhileTyping: true, ct, pendingAssistantMessage);

                return;
            }

            await Task.Delay(60, ct);

            var executionPlan = BuildAssistantExecutionPlan(combinedText);
            UpdateWorkingSessionBeforeTurn(combinedText, executionPlan, attachmentPaths);
            Debug.WriteLine($"[Chat] Execution plan: mode={executionPlan.Mode}; bucket={executionPlan.Bucket}; reason={executionPlan.Reason}");

            // 1) Fast-path: try tool/skill execution BEFORE AI. If handled, short-circuit.
            string? toolResult = null;

            if (IsToolsEnabled)
            {
                try
                {
                    try
                    {
                        if (CurrentMode != PresenceMode.Error)
                            CurrentMode = PresenceMode.Working;
                    }
                    catch { }
                    toolResult = await Tools.ToolExecutor.TryExecuteToolWithCancellationAsync(combinedText, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Chat] ToolExecutor exception: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (CurrentMode != PresenceMode.Error)
                            CurrentMode = PresenceMode.Thinking;
                    }
                    catch { }
                }
            }

            Debug.WriteLine($"[Chat] ToolExecutor handled: {toolResult != null}");

            if (toolResult != null)
            {
                var toolOutput = await HandleToolResultAsync(toolResult, combinedText, ct);
                var structuredTask = ClassifyStructuredTaskKind(combinedText);

                IsTyping = false;
                StatusText = "Neural Network Active";

                if (!string.IsNullOrWhiteSpace(toolOutput))
                {
                    var messageContent = RequiresStructuredOutput(structuredTask)
                        ? BuildStructuredResponse(
                            structuredTask,
                            "local tool pipeline",
                            toolOutput,
                            !toolOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) && !toolOutput.Contains("error", StringComparison.OrdinalIgnoreCase),
                            extraFilesInspected: attachmentPaths,
                            fallbackActionTaken: "Executed a direct local tool action before calling the model.")
                        : FormatToolOutputForChat(toolOutput);

                    UpdateWorkingSessionAfterTurn(
                        new AssistantExecutionPlan(
                            structuredTask == StructuredTaskKind.CodeTask || structuredTask == StructuredTaskKind.FileAnalysis ? AIManager.AITaskBucket.Code : AIManager.AITaskBucket.Chat,
                            AssistantExecutionMode.PlainAi,
                            "direct-tool",
                            structuredTask),
                        "local tool pipeline",
                        ExtractLikelyPaths(new[] { toolOutput }).Concat(attachmentPaths),
                        toolOutput,
                        !toolOutput.Contains("failed", StringComparison.OrdinalIgnoreCase) && !toolOutput.Contains("error", StringComparison.OrdinalIgnoreCase),
                        null);

                    await AddAssistantResponseAsync(messageContent, speakWhileTyping: toolResult != "__STOP_VOICE__", ct, pendingAssistantMessage);
                }

                return;
            }

            if (TryParseMediaCentreQuery(combinedText, out var searchQuery, out var typeId))
            {
                var vm = MediaCentreViewModel.Instance;
                var lines = vm != null
                    ? (await Task.Run(() => vm.FindLibraryItems(searchQuery, typeId, 500), ct))
                        .Select(r =>
                        {
                            var t = (r.Title ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(t)) return t;
                            var p = (r.FilePath ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(p)) return "";
                            var n = Path.GetFileNameWithoutExtension(p) ?? "";
                            return (n ?? "").Trim();
                        })
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : await Task.Run(() => MediaCentreViewModel.FindPersistedLibraryTitles(searchQuery, typeId, 500), ct);

                var label = string.IsNullOrWhiteSpace(typeId) ? "ITEMS" : typeId.ToUpperInvariant();
                var qLabel = string.IsNullOrWhiteSpace(searchQuery) ? "(no query)" : searchQuery;

                var localAssistantText =
                    lines.Count == 0
                        ? $"NO MATCHES · {label} · {qLabel}"
                        : $"FOUND {lines.Count} · {label} · {qLabel}\n" + string.Join("\n", lines.Select(t => $"• {t}"));

                var structuredMediaResponse = BuildStructuredResponse(
                    StructuredTaskKind.MediaAction,
                    "media centre library",
                    localAssistantText,
                    true,
                    fallbackActionTaken: $"Searched the local media catalogue for '{qLabel}' in {label.ToLowerInvariant()}.",
                    fallbackNextStep: lines.Count == 0 ? "refine the title or media type and search again" : "pick a returned title or refine the query for another media action");

                UpdateWorkingSessionAfterTurn(
                    new AssistantExecutionPlan(AIManager.AITaskBucket.Chat, AssistantExecutionMode.PlainAi, "media-direct", StructuredTaskKind.MediaAction),
                    "media centre library",
                    attachmentPaths,
                    localAssistantText,
                    true,
                    null);

                IsTyping = false;
                StatusText = "Neural Network Active";

                await AddAssistantResponseAsync(structuredMediaResponse, speakWhileTyping: true, ct, pendingAssistantMessage);

                return;
            }

            // 2) Agent mode: run AgentOrchestrator loop (tools + AI) instead of plain AI call.
            if (IsAgentModeEnabled || (IsToolsEnabled && executionPlan.Mode == AssistantExecutionMode.AgentTools))
            {
                var root = string.IsNullOrWhiteSpace(_agentWorkspaceRoot)
                    ? (Environment.CurrentDirectory ?? "")
                    : _agentWorkspaceRoot;

                var agent = new AgentOrchestrator(string.IsNullOrWhiteSpace(root) ? "." : root);
                agent.OnThinking += (_, msg) =>
                {
                    try
                    {
                        StatusText = msg;
                        if (CurrentMode != PresenceMode.Error)
                            CurrentMode = PresenceMode.Thinking;
                    }
                    catch { }
                };
                agent.OnToolExecuting += (_, desc) =>
                {
                    try
                    {
                        if (CurrentMode != PresenceMode.Error)
                            CurrentMode = PresenceMode.Working;
                        var toolName = TryInferAgentToolName(desc);
                        if (!string.IsNullOrWhiteSpace(toolName))
                            Debug.WriteLine($"[Chat] AgentOrchestrator tool call: {toolName}");
                    }
                    catch { }
                };

                var agentText = await agent.RunAsync(combinedText, ct);
                if (string.IsNullOrWhiteSpace(agentText))
                    agentText = "NO RESPONSE · RETRY";

                // Post-agent: allow coding tool hooks if the agent outputs them.
                var agentCoding = await ExecuteCodingToolsIfPresentAsync(agentText, ct);
                var agentFilesInspected = ExtractAgentPaths(agent.ActionHistory, changedOnly: false);
                agentFilesInspected.AddRange(attachmentPaths);
                var agentFilesChanged = ExtractAgentPaths(agent.ActionHistory, changedOnly: true);
                var agentProviderUsed = FormatProviderUsed(agent.LastProviderUsed, agent.LastModelUsed);
                var structuredAgentResponse = RequiresStructuredOutput(executionPlan.StructuredTask)
                    ? BuildStructuredResponse(
                        executionPlan.StructuredTask,
                        agentProviderUsed,
                        agentCoding.CleanedText,
                        !agentCoding.CleanedText.Contains("error", StringComparison.OrdinalIgnoreCase),
                        extraFilesInspected: agentFilesInspected,
                        extraFilesChanged: agentFilesChanged,
                        fallbackActionTaken: agent.ActionHistory.Count == 0
                            ? "Completed the agent reasoning pass without executing workspace tools."
                            : $"Completed {agent.ActionHistory.Count} agent action(s) while working the task.")
                    : agentCoding.CleanedText;
                        structuredAgentResponse = EnforceAtlasIdentity(structuredAgentResponse);

                UpdateWorkingSessionAfterTurn(
                    executionPlan,
                    agentProviderUsed,
                    agentFilesChanged.Concat(agentFilesInspected),
                    agentCoding.CleanedText,
                    !agentCoding.CleanedText.Contains("error", StringComparison.OrdinalIgnoreCase),
                    null);

                IsTyping = false;
                StatusText = "Neural Network Active";

                if (!string.IsNullOrWhiteSpace(structuredAgentResponse))
                    await AddAssistantResponseAsync(structuredAgentResponse, speakWhileTyping: true, ct, pendingAssistantMessage);

                if (agentCoding.ToolOutput != null && !RequiresStructuredOutput(executionPlan.StructuredTask))
                {
                    Messages.Add(new AtlasAI.Models.ChatMessage
                    {
                        Content = FormatToolOutputForChat(agentCoding.ToolOutput),
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                }

                return;
            }

            var context = BuildContext(combinedText);
            AIResponse? response = null;
            string assistantText;

            if (IsToolsEnabled && executionPlan.Mode == AssistantExecutionMode.AutomaticWebSearch)
            {
                assistantText = await TryGenerateGroundedWebResponseAsync(context, combinedText, executionPlan, null, ct);
                if (string.IsNullOrWhiteSpace(assistantText))
                    assistantText = "I couldn't ground that answer with live results. Please retry in a moment.";
            }
            else
            {
                response = await AIManager.SendMessageAsync(BuildRoutingRequest(context, executionPlan), ct);
                assistantText = response.Success ? response.Content : response.Error;

                if (IsToolsEnabled && ShouldTriggerGroundedWebFallback(executionPlan, combinedText, assistantText))
                {
                    var groundedAssistantText = await TryGenerateGroundedWebResponseAsync(context, combinedText, executionPlan, assistantText, ct);
                    if (!string.IsNullOrWhiteSpace(groundedAssistantText))
                        assistantText = groundedAssistantText;
                }
            }
            
            // --- Legacy Personality Pipeline ---
            assistantText = NormalizeResponse(assistantText);
            var parsed = ExtractPlanJson(assistantText, out var displayText, out var planJson);
            
            #if PERSONAL_BUILD
            if (SelectedChatPersonality == "Unfiltered" || SelectedChatPersonality == "ChaosTesting")
            {
                try
                {
                    // Map settings
                    var settings = AtlasAI.Settings.SettingsStore.Current;
                    
                    // Construct legacy profile manually (since we are in ViewModel)
                    var profile = new AtlasAI.Personality.PersonalityProfile 
                    { 
                        Type = AtlasAI.Personality.PersonalityType.Unfiltered,
                        VerbosityLevel = AtlasAI.Personality.VerbosityLevel.Medium,
                    };

                    var userInput = combinedText;
                    // ShortTermMemory for variation engine
                    var memory = new AtlasAI.Brain.ShortTermMemory(); 

                    // EXACT LEGACY PIPELINE
                    var mood = AtlasAI.Personality.MoodEngine.FromUserText(userInput, DateTime.Now);
                    // Legacy pipeline requires presence
                    AtlasAI.Brain.PresenceState? presence = null; 
                    
                    var styled = AtlasAI.Personality.PersonalityEngine.Apply(profile, displayText, presence, mood, userInput);
                    var emotion = AtlasAI.Personality.EmotionalStateEngine.Evaluate(userInput, DateTime.Now);
                    var tuned = AtlasAI.Personality.EmotionalStateEngine.Apply(styled, emotion);
                    var varied = AtlasAI.Personality.ResponseVariationEngine.Apply(profile, tuned, memory);
                    var filtered = AtlasAI.Personality.ResponseMemoryFilter.Apply(profile, varied, memory);
                    
                    assistantText = filtered;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LegacyPipeline] Error: {ex}");
                }
            }
            #endif
            // -----------------------------------

            if (IsIrishMode())
                assistantText = ApplyIrishSwearSwap(assistantText);

            if (string.IsNullOrWhiteSpace(assistantText))
                assistantText = "NO RESPONSE · RETRY";

            // 3) Post-AI: execute coding tool calls if present.
            // Append tool output as a separate assistant message ONLY if a tool executed.
            var coding = await ExecuteCodingToolsIfPresentAsync(assistantText, ct);

            IsTyping = false;
            if (response != null)
                UpdateAiRouteStatus(response);

            var structuredAiResponse = RequiresStructuredOutput(executionPlan.StructuredTask)
                ? BuildStructuredResponse(
                    executionPlan.StructuredTask,
                    FormatProviderUsed(
                        response?.Provider.ToString() ?? _lastAiProviderUsed,
                        response?.Model ?? _lastAiModelUsed),
                    coding.CleanedText,
                    response?.Success ?? !coding.CleanedText.Contains("error", StringComparison.OrdinalIgnoreCase),
                    extraFilesInspected: attachmentPaths,
                    extraFilesChanged: coding.ToolOutput == null ? Array.Empty<string>() : ExtractLikelyPaths(new[] { coding.ToolOutput }),
                    fallbackActionTaken: coding.ToolOutput == null
                        ? "Completed the routed AI response for the requested technical or operational task."
                        : "Completed the routed AI response and then executed a coding tool step.")
                : coding.CleanedText;
                    structuredAiResponse = EnforceAtlasIdentity(structuredAiResponse);

            var providerUsed = FormatProviderUsed(
                response?.Provider.ToString() ?? _lastAiProviderUsed,
                response?.Model ?? _lastAiModelUsed);
            var filesTouched = (coding.ToolOutput == null ? Enumerable.Empty<string>() : ExtractLikelyPaths(new[] { coding.ToolOutput }))
                .Concat(attachmentPaths);
            UpdateWorkingSessionAfterTurn(
                executionPlan,
                providerUsed,
                filesTouched,
                coding.CleanedText,
                response?.Success ?? !coding.CleanedText.Contains("error", StringComparison.OrdinalIgnoreCase),
                response?.Success == false ? response.Error : null);

            if (!string.IsNullOrWhiteSpace(structuredAiResponse))
                await AddAssistantResponseAsync(structuredAiResponse, speakWhileTyping: true, ct, pendingAssistantMessage);

            if (coding.ToolOutput != null && !RequiresStructuredOutput(executionPlan.StructuredTask))
            {
                Messages.Add(new AtlasAI.Models.ChatMessage
                {
                    Content = FormatToolOutputForChat(coding.ToolOutput),
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
        }
        catch (OperationCanceledException)
        {
            IsTyping = false;
            StatusText = "Neural Network Active";
            UpdateWorkingSessionAfterTurn(
                new AssistantExecutionPlan(AIManager.AITaskBucket.Chat, AssistantExecutionMode.PlainAi, "cancelled", StructuredTaskKind.None),
                _workingSession.SelectedProvider,
                Attachments.ToList(),
                "CANCELLED · OPERATION STOPPED",
                false,
                "CANCELLED · OPERATION STOPPED");
            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = IsListening ? PresenceMode.Listening : PresenceMode.Idle;
            }
            catch { }
            if (pendingAssistantMessage != null)
                pendingAssistantMessage.Content = "CANCELLED · OPERATION STOPPED";
        }
        catch
        {
            IsTyping = false;
            StatusText = "Neural Network Active";
            UpdateWorkingSessionAfterTurn(
                new AssistantExecutionPlan(AIManager.AITaskBucket.Chat, AssistantExecutionMode.PlainAi, "failed", StructuredTaskKind.None),
                _workingSession.SelectedProvider,
                Attachments.ToList(),
                "ERROR · REQUEST FAILED",
                false,
                "ERROR · REQUEST FAILED");
            try { CurrentMode = PresenceMode.Error; } catch { }
            if (pendingAssistantMessage != null)
                pendingAssistantMessage.Content = "ERROR · REQUEST FAILED";
        }
        finally
        {
            _isSending = false;
            _currentCts?.Dispose();
            _currentCts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static async Task<bool> ShowSmartHomeIntentAsync(SmartHomeTextCommandResult result, CancellationToken cancellationToken)
    {
        CommandCenterWindow? commandCenterWindow = null;
        try
        {
            commandCenterWindow = Application.Current?.Windows
                .OfType<CommandCenterWindow>()
                .OrderByDescending(window => window.IsActive)
                .FirstOrDefault();
        }
        catch
        {
        }

        if (commandCenterWindow == null)
            return false;

        return await commandCenterWindow.ShowSmartHomeIntentAsync(result, cancellationToken);
    }

    private static bool TryHandleLocalPreferenceMemoryCommand(string raw, out string reply)
    {
        reply = "";
        var t = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;

        var lower = t.ToLowerInvariant();

        if (lower == "show what you remember" || lower == "show what you remember about me")
        {
            reply = LocalPreferenceMemoryStore.Instance.BuildShowText();
            return true;
        }

        var m = Regex.Match(t, "^forget\\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var query = (m.Groups[1].Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                reply = "FORGET: Please specify what to forget (e.g., \"forget preferred_name\").";
                return true;
            }

            LocalPreferenceMemoryStore.Instance.Forget(query, out var forgotten, out var verbosityReset);

            if (forgotten.Count == 0 && !verbosityReset)
                reply = $"FORGET: Nothing matched '{query}'.";
            else
            {
                var parts = new List<string>();
                if (verbosityReset) parts.Add("verbosity");
                if (forgotten.Count > 0) parts.AddRange(forgotten);
                reply = "FORGOT: " + string.Join(", ", parts);
            }

            return true;
        }

        return false;
    }

    private void ShowStartupSequence(bool requested)
    {
        var name = string.IsNullOrWhiteSpace(_chatPreferredName) ? "there" : _chatPreferredName;
        var lines = new[]
        {
            $"Startup sequence: alright {name}, quick tour so you stop breaking things.",
            "1) Chat: type or talk, I’ll answer fast and take the piss if you deserve it. 2) Voice: pick a voice in Settings; I’ll remember it. 3) Mic: choose your input device if voice chat acts up.",
            "4) Personalities: Irish Mate is the default — witty, sarcastic, swears when it fits. 5) Commands: tell me what you want, I’ll do it. Say “startup sequence” any time for a refresh."
        };
        foreach (var line in lines)
        {
            Messages.Add(new AtlasAI.Models.ChatMessage
            {
                Content = line,
                IsUser = false,
                Timestamp = DateTime.Now
            });
        }
        if (!requested)
        {
            try
            {
                PreferencesStore.Instance.Update(p =>
                {
                    p.ChatOnboardingComplete = true;
                    p.FirstRunWizardComplete = true;
                });
                PreferencesStore.Instance.SaveNow();
            }
            catch { }
        }
    }

    private bool IsIrishMode()
    {
        try
        {
            if (string.Equals(SelectedChatPersonality, "Irish Mate", StringComparison.OrdinalIgnoreCase))
                return true;
            var accent = (PreferencesStore.Instance.Current.ChatAccent ?? "").Trim();
            return string.Equals(accent, "Irish", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ApplyIrishSwearSwap(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var t = text;
        t = Regex.Replace(t, @"\bfook(?:ing|in)?\b", "fecking", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bfuck(?:ing)?\b", "fecking", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bidiot\b", "eejit", RegexOptions.IgnoreCase);
        return t;
    }

    private bool HandleReadAloudResponse(string input)
    {
        if (!_awaitingReadAloud) return false;
        var t = (input ?? "").Trim();
        var lower = t.ToLowerInvariant();
        var yes = lower == "yes" || lower == "yeah" || lower == "yep" || lower == "ok" || lower == "okay" || lower == "read it" || lower == "go on" || lower == "do it";
        var no = lower == "no" || lower == "nope" || lower == "nah" || lower == "don't" || lower == "do not";

        _readAloudCts?.Cancel();
        _awaitingReadAloud = false;
        var pending = _pendingReadAloudText;
        _pendingReadAloudText = null;

        if (yes && !string.IsNullOrWhiteSpace(pending) && _voiceManager != null)
        {
            _ = TrySpeakAsync(pending);
            return true;
        }

        if (no) return true;
        return false;
    }

    private void MaybeOfferReadAloud(string text)
    {
        if (_voiceManager == null || !_voiceManager.SpeechEnabled) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Always read the full response immediately to avoid clipped half-sentence playback.
        _readAloudCts?.Cancel();
        _awaitingReadAloud = false;
        _pendingReadAloudText = text;
        _ = TrySpeakAsync(text);
    }

    private static string EnforceAtlasIdentity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text.Trim();

        // Normalize common self-identification leaks from upstream models.
        cleaned = Regex.Replace(
            cleaned,
            "(?im)^\\s*(?:mate[,!\\s-]*)?(?:i\\s*(?:am|'m)\\s+)(?:google\\s+)?gemini\\b[^.!?\\r\\n]*[.!?]?\\s*",
            "I'm Atlas, your AI assistant. ");

        cleaned = Regex.Replace(
            cleaned,
            "(?im)^\\s*(?:i\\s*(?:am|'m)\\s+)(?:chatgpt|claude|openai(?:'s)?\\s+assistant|anthropic\\s+assistant)\\b[^.!?\\r\\n]*[.!?]?\\s*",
            "I'm Atlas, your AI assistant. ");

        cleaned = Regex.Replace(
            cleaned,
            "(?im)\\b(?:google\\s+gemini|gemini|chatgpt|claude|openai\\s+assistant|anthropic\\s+assistant)\\b",
            "Atlas");

        return cleaned.Trim();
    }

    private static void TryRememberExplicitPreferences(string raw)
    {
        var t = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return;

        // Prefer name: "call me X" / "my name is X" / "you can call me X"
        var m = Regex.Match(t, "^(call me|my name is|you can call me)\\s+(?<v>.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var v = (m.Groups["v"].Value ?? "").Trim();
            v = v.Trim().Trim('"', '\'');
            while (v.EndsWith(".") || v.EndsWith("!") || v.EndsWith("?"))
                v = v.Substring(0, v.Length - 1).Trim();

            if (!string.IsNullOrWhiteSpace(v))
            {
                if (v.Length > 64) v = v.Substring(0, 64).Trim();
                LocalPreferenceMemoryStore.Instance.RememberExplicit("preferred_name", v, "explicit_name_phrase");
            }
        }

        // Accent preference: "Irish accent" / "use an Irish accent" / "talk in an Irish accent"
        var a = Regex.Match(t, "(?i)\\b(irish|scottish|british|english|american|aussie|australian)\\s+accent\\b");
        if (a.Success)
        {
            var v = (a.Groups[1].Value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(v))
            {
                var accent = char.ToUpperInvariant(v[0]) + v.Substring(1).ToLowerInvariant();
                LocalPreferenceMemoryStore.Instance.RememberExplicit("accent", accent, "explicit_accent_phrase");
                try { PreferencesStore.Instance.Update(p => p.ChatAccent = accent); } catch { }
            }
        }
    }

    private async Task<string> HandleToolResultAsync(string toolResult, string originalUserText, CancellationToken ct)
    {
        // Mirror ChatWindow.xaml.cs special markers so tool execution behaves consistently.
        if (toolResult == "__STOP_VOICE__")
        {
            try
            {
                _voiceManager?.Stop();
                VoiceSystemOrchestrator.Instance.Stop();
            }
            catch
            {
            }
            return "🔇 Stopped.";
        }

        if (toolResult == "__OPEN_INTEGRATION_HUB__")
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var hubWindow = new AtlasAI.Integrations.IntegrationHubWindow();
                    hubWindow.Owner = Application.Current?.MainWindow;
                    hubWindow.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IntegrationHub2] Error: {ex}");
                    MessageBox.Show($"Integration Hub error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            return "🔌 Opening Integration Hub - see all available apps and services Atlas can connect to!";
        }

        if (toolResult == "__OPEN_SOCIAL_MEDIA_CONSOLE__")
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var consoleWindow = new AtlasAI.SocialMedia.SocialMediaConsoleWindow();
                    consoleWindow.Owner = Application.Current?.MainWindow;
                    consoleWindow.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SocialMedia2] Error: {ex}");
                    MessageBox.Show($"Social Media Console error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            return "📱 Opening Social Media Console - create content, manage campaigns, and schedule posts!";
        }

        if (toolResult == "__OPEN_SECURITY_SUITE__")
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var win = new AtlasAI.SecuritySuite.SecuritySuiteWindow();
                    win.Owner = Application.Current?.MainWindow;
                    win.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SecuritySuite] Error: {ex}");
                    MessageBox.Show($"Security Suite error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            return "🛡️ Opening Security Suite...";
        }

        if (toolResult.StartsWith("__ANALYZE_IMAGE__|", StringComparison.Ordinal))
        {
            var parts = toolResult.Split('|');
            if (parts.Length >= 3)
            {
                var imagePath = parts[1];
                var question = parts[2];
                return await AnalyzeImageWithQuestionAsync(imagePath, question, ct);
            }
        }

        if (toolResult.StartsWith("__GENERATE_IMAGE__|", StringComparison.Ordinal))
        {
            var prompt = toolResult.Substring("__GENERATE_IMAGE__|".Length);
            return await GenerateAndMaybeOpenImageAsync(prompt, ct);
        }

        return toolResult;
    }

    private async Task<string> GenerateAndMaybeOpenImageAsync(string prompt, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var result = await ImageGeneratorTool.GenerateImageAsync(prompt, ct: ct);
            if (!result.Success)
                return $"❌ {result.Error}";

            var response = new System.Text.StringBuilder();
            response.AppendLine("🎨 **Image Generated!**\n");
            response.AppendLine($"**Prompt:** {prompt}");
            if (!string.IsNullOrEmpty(result.RevisedPrompt) && result.RevisedPrompt != prompt)
                response.AppendLine($"**DALL-E enhanced to:** {result.RevisedPrompt}");
            response.AppendLine($"\n📁 **Saved to:** {result.ImagePath}");
            response.AppendLine("\n💡 Say \"open images folder\" to see all your generated images!");

            if (!string.IsNullOrEmpty(result.ImagePath) && File.Exists(result.ImagePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.ImagePath,
                        UseShellExecute = true
                    });
                    response.AppendLine("\n✅ Opened the image for you!");
                }
                catch
                {
                }
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            return $"❌ Image generation failed: {ex.Message}";
        }
    }

    private async Task<string> AnalyzeImageWithQuestionAsync(string imagePath, string question, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(imagePath))
                return $"❌ Image not found: {imagePath}";

            var activeProvider = AIManager.GetActiveProviderInstance();
            if (activeProvider == null || !activeProvider.IsConfigured)
            {
                var fileInfo = new FileInfo(imagePath);
                var fileName = Path.GetFileName(imagePath);
                return $"📸 **Image Analysis (Basic Mode)**\n\n" +
                       $"**File:** {fileName}\n" +
                       $"**Size:** {fileInfo.Length / 1024:N0} KB\n" +
                       $"**Your question:** {question}\n\n" +
                       "💡 **For AI-powered analysis:**\n" +
                       "AI providers and API keys are admin-only on this installation.\n" +
                       "• Detailed image description\n" +
                       "• Answer to your question about the image\n" +
                       "• OCR text extraction\n" +
                       "• Smart insights and suggestions";
            }

            var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
            var base64Image = Convert.ToBase64String(imageBytes);
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            var mimeType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/png"
            };

            var analysisPrompt = string.IsNullOrWhiteSpace(question) || question.Length < 5
                ? "Please analyze this image and describe what you see in detail. Include any text, UI elements, or notable features."
                : $"The user attached this image and asked: \"{question}\"\n\nPlease analyze the image and answer their question. Be helpful and specific.";

            // Minimal, self-contained vision request. Uses the provider's multimodal support if available.
            var messages = new List<object>
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = analysisPrompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }
                }
            };

            var response = await AIManager.SendMessageAsync(messages, 1000, ct);
            if (response.Success)
            {
                UpdateAiRouteStatus(response);
                return $"🔍 **Image Analysis:**\n\n{response.Content}";
            }

            return $"❌ AI analysis failed: {response.Error}";
        }
        catch (Exception ex)
        {
            return $"❌ Image analysis error: {ex.Message}";
        }
    }

    private void UpdateAiRouteStatus(AIResponse response)
    {
        try
        {
            if (response == null || !response.Success)
                return;

            var provider = response.Provider.ToString();
            var model = string.IsNullOrWhiteSpace(response.Model) ? "default model" : response.Model.Trim();
            var bucket = string.IsNullOrWhiteSpace(response.TaskBucket) ? "chat" : response.TaskBucket.Trim();
            _lastAiProviderUsed = provider;
            _lastAiModelUsed = model;
            _lastAiTaskBucket = bucket;
            StatusText = $"Ready · {provider} · {model} · {bucket}";
        }
        catch
        {
            StatusText = "Neural Network Active";
        }
    }

    private enum AssistantExecutionMode
    {
        PlainAi,
        AgentTools,
        AutomaticWebSearch,
    }

    private enum StructuredTaskKind
    {
        None,
        CodeTask,
        FileAnalysis,
        SystemOperation,
        SmartHomeAction,
        MediaAction,
    }

    private sealed class WorkingSessionState
    {
        public string ActiveProject { get; set; } = string.Empty;
        public string ActiveFolder { get; set; } = string.Empty;
        public List<string> LastFilesTouched { get; } = new();
        public string CurrentGoal { get; set; } = string.Empty;
        public List<string> LastErrors { get; } = new();
        public string SelectedProvider { get; set; } = string.Empty;
        public string CurrentTaskMode { get; set; } = "chat";
    }

    private sealed record AssistantExecutionPlan(
        AIManager.AITaskBucket Bucket,
        AssistantExecutionMode Mode,
        string Reason,
        StructuredTaskKind StructuredTask,
        string WebQuery = "",
        string GroundingChecks = "",
        bool AllowConfidenceBasedWebFallback = false,
        AiChatTurnRoutingPreference? TurnRoutingPreference = null);

    private sealed record GroundingDecision(
        bool RequiresWebSearch,
        bool AllowConfidenceFallback,
        string Query,
        string Checks);

    private void ResetWorkingSessionState()
    {
        _workingSession.ActiveProject = DetectActiveProjectName();
        _workingSession.ActiveFolder = DetectDefaultActiveFolder();
        _workingSession.LastFilesTouched.Clear();
        _workingSession.CurrentGoal = string.Empty;
        _workingSession.LastErrors.Clear();
        _workingSession.SelectedProvider = _selectedAiProvider.ToString();
        _workingSession.CurrentTaskMode = "chat";
    }

    private string DetectActiveProjectName()
    {
        try
        {
            var root = !string.IsNullOrWhiteSpace(_agentWorkspaceRoot)
                ? _agentWorkspaceRoot
                : (Environment.CurrentDirectory ?? string.Empty);

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "AtlasAI";

            var solution = Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(solution))
                return Path.GetFileNameWithoutExtension(solution) ?? "AtlasAI";

            var project = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(project))
                return Path.GetFileNameWithoutExtension(project) ?? "AtlasAI";

            return Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "AtlasAI";
        }
        catch
        {
            return "AtlasAI";
        }
    }

    private string DetectDefaultActiveFolder()
    {
        var root = !string.IsNullOrWhiteSpace(_agentWorkspaceRoot)
            ? _agentWorkspaceRoot
            : (Environment.CurrentDirectory ?? string.Empty);

        return string.IsNullOrWhiteSpace(root) ? "." : root;
    }

    private static string NormalizeSessionTaskMode(AssistantExecutionPlan executionPlan)
    {
        if (executionPlan.StructuredTask is StructuredTaskKind.SystemOperation or StructuredTaskKind.SmartHomeAction or StructuredTaskKind.MediaAction)
            return "operations";

        return executionPlan.Bucket switch
        {
            AIManager.AITaskBucket.Code => "code",
            AIManager.AITaskBucket.Generation => "generation",
            _ => "chat",
        };
    }

    private static bool LooksLikeFollowUpRequest(string userMessage)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return false;

        if (lower.Length <= 80 && Regex.IsMatch(lower, @"^(continue|go on|keep going|retry|again|do that|do it|fix it|use that|apply that|same task|same thing|what about that|and now|next|carry on|proceed)\b", RegexOptions.IgnoreCase))
            return true;

        return Regex.IsMatch(lower, @"\b(that|it|those|them|this one|same file|same folder|same project|same task|that error|that file|that folder)\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(lower, @"\b(file|folder|project|error|build|debug|generate|design|chat|code|operation|system)\b.*\b(new|different|another)\b", RegexOptions.IgnoreCase);
    }

    private StructuredTaskKind GetStructuredTaskFromSessionMode()
    {
        return (_workingSession.CurrentTaskMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "code" => StructuredTaskKind.CodeTask,
            "operations" => StructuredTaskKind.SystemOperation,
            _ => StructuredTaskKind.None,
        };
    }

    private void UpdateWorkingSessionBeforeTurn(string userMessage, AssistantExecutionPlan executionPlan, IEnumerable<string>? attachmentPaths)
    {
        _workingSession.ActiveProject = DetectActiveProjectName();
        _workingSession.SelectedProvider = _selectedAiProvider.ToString();

        if (!LooksLikeFollowUpRequest(userMessage) || string.IsNullOrWhiteSpace(_workingSession.CurrentGoal))
            _workingSession.CurrentGoal = (userMessage ?? string.Empty).Trim();

        _workingSession.CurrentTaskMode = NormalizeSessionTaskMode(executionPlan);

        RegisterTouchedFiles(attachmentPaths);

        if (string.IsNullOrWhiteSpace(_workingSession.ActiveFolder))
            _workingSession.ActiveFolder = DetectDefaultActiveFolder();
    }

    private void UpdateWorkingSessionAfterTurn(
        AssistantExecutionPlan executionPlan,
        string? providerUsed,
        IEnumerable<string>? filesTouched,
        string? resultText,
        bool success,
        string? explicitError = null)
    {
        _workingSession.ActiveProject = DetectActiveProjectName();
        _workingSession.SelectedProvider = !string.IsNullOrWhiteSpace(providerUsed)
            ? providerUsed!.Trim()
            : (_selectedAiProvider.ToString() ?? string.Empty);
        _workingSession.CurrentTaskMode = NormalizeSessionTaskMode(executionPlan);

        RegisterTouchedFiles(filesTouched);

        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitError))
            errors.Add(explicitError!.Trim());

        if (!success)
            errors.AddRange(ExtractSessionErrors(resultText));

        if (errors.Count == 0 && !success && !string.IsNullOrWhiteSpace(resultText))
            errors.Add(NormalizeToolOutput(resultText));

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                if (string.IsNullOrWhiteSpace(error))
                    continue;

                _workingSession.LastErrors.Insert(0, error.Trim());
            }

            var deduped = _workingSession.LastErrors
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            _workingSession.LastErrors.Clear();
            _workingSession.LastErrors.AddRange(deduped);
        }
        else if (success)
        {
            _workingSession.LastErrors.Clear();
        }
    }

    private void RegisterTouchedFiles(IEnumerable<string>? paths)
    {
        if (paths == null)
            return;

        var normalized = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            return;

        foreach (var path in normalized)
        {
            _workingSession.LastFilesTouched.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
            _workingSession.LastFilesTouched.Insert(0, path);
        }

        if (_workingSession.LastFilesTouched.Count > 8)
            _workingSession.LastFilesTouched.RemoveRange(8, _workingSession.LastFilesTouched.Count - 8);

        var firstExistingPath = _workingSession.LastFilesTouched.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (!string.IsNullOrWhiteSpace(firstExistingPath))
        {
            try
            {
                var folder = Path.GetDirectoryName(firstExistingPath);
                if (!string.IsNullOrWhiteSpace(folder))
                    _workingSession.ActiveFolder = folder;
            }
            catch
            {
            }
        }
    }

    private static List<string> ExtractSessionErrors(string? text)
    {
        var normalized = NormalizeToolOutput(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<string>();

        var lines = normalized
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(static line => Regex.IsMatch(line, @"\b(error|failed|exception|traceback|unable|cannot|could not|request failed|command failed)\b", RegexOptions.IgnoreCase))
            .Take(3)
            .ToList();

        return lines.Count == 0 ? new List<string> { normalized } : lines;
    }

    private string BuildWorkingSessionMemoryBlock()
    {
        var activeProject = string.IsNullOrWhiteSpace(_workingSession.ActiveProject) ? "none" : _workingSession.ActiveProject;
        var activeFolder = string.IsNullOrWhiteSpace(_workingSession.ActiveFolder) ? "none" : _workingSession.ActiveFolder;
        var currentGoal = string.IsNullOrWhiteSpace(_workingSession.CurrentGoal) ? "none" : _workingSession.CurrentGoal;
        var selectedProvider = string.IsNullOrWhiteSpace(_workingSession.SelectedProvider) ? _selectedAiProvider.ToString() : _workingSession.SelectedProvider;
        var currentTaskMode = string.IsNullOrWhiteSpace(_workingSession.CurrentTaskMode) ? "chat" : _workingSession.CurrentTaskMode;
        var files = _workingSession.LastFilesTouched.Count == 0
            ? "- none"
            : string.Join("\n", _workingSession.LastFilesTouched.Take(6).Select(static path => $"- {path}"));
        var errors = _workingSession.LastErrors.Count == 0
            ? "- none"
            : string.Join("\n", _workingSession.LastErrors.Take(4).Select(static error => $"- {error}"));

        return string.Join("\n", new[]
        {
            "Active session working memory:",
            $"- Active project: {activeProject}",
            $"- Active folder: {activeFolder}",
            $"- Current goal: {currentGoal}",
            $"- Selected provider: {selectedProvider}",
            $"- Current task mode: {currentTaskMode}",
            "- Last files touched:",
            files,
            "- Last errors:",
            errors,
            "Use this working memory only to stay consistent within the current active session and follow-up task."
        });
    }

    private AssistantExecutionPlan BuildAssistantExecutionPlan(string userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        var turnRoutingPreference = AiChatTurnRouting.ResolveTurnRoutingPreference(text, _selectedAiProvider, _selectedModel, AIManager.GetManualSelectedModel);
        if (string.IsNullOrWhiteSpace(text))
            return new AssistantExecutionPlan(AIManager.AITaskBucket.Chat, AssistantExecutionMode.PlainAi, "empty", StructuredTaskKind.None, TurnRoutingPreference: turnRoutingPreference);

        var structuredTask = ClassifyStructuredTaskKind(text);
        var isFollowUpRequest = LooksLikeFollowUpRequest(text);

        if (structuredTask == StructuredTaskKind.None && isFollowUpRequest)
            structuredTask = GetStructuredTaskFromSessionMode();

        if (ShouldUseAgentTools(text))
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Code,
                AssistantExecutionMode.AgentTools,
                "workspace-tool-task",
                structuredTask == StructuredTaskKind.None ? StructuredTaskKind.CodeTask : structuredTask,
                TurnRoutingPreference: turnRoutingPreference);

        var grounding = AssessGroundingNeed(text);
        if (grounding.RequiresWebSearch)
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Chat,
                AssistantExecutionMode.AutomaticWebSearch,
                "grounded-current-or-freshness-required",
                StructuredTaskKind.None,
                grounding.Query,
                grounding.Checks,
                grounding.AllowConfidenceFallback,
                turnRoutingPreference);

        if (LooksLikeGenerationTask(text))
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Generation,
                AssistantExecutionMode.PlainAi,
                "generation-task",
                StructuredTaskKind.None,
                grounding.Query,
                grounding.Checks,
                grounding.AllowConfidenceFallback,
                turnRoutingPreference);

        if (LooksLikeCodeTask(text))
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Code,
                AssistantExecutionMode.PlainAi,
                "code-task",
                structuredTask == StructuredTaskKind.None ? StructuredTaskKind.CodeTask : structuredTask,
                grounding.Query,
                grounding.Checks,
                grounding.AllowConfidenceFallback,
                turnRoutingPreference);

        if (structuredTask == StructuredTaskKind.SystemOperation)
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Chat,
                AssistantExecutionMode.PlainAi,
                isFollowUpRequest ? "session-follow-up-operation" : "system-operation",
                structuredTask,
                grounding.Query,
                grounding.Checks,
                grounding.AllowConfidenceFallback,
                turnRoutingPreference);

        if (structuredTask == StructuredTaskKind.FileAnalysis)
            return new AssistantExecutionPlan(
                AIManager.AITaskBucket.Code,
                AssistantExecutionMode.PlainAi,
                isFollowUpRequest ? "session-follow-up-file-analysis" : "file-analysis",
                structuredTask,
                grounding.Query,
                grounding.Checks,
                grounding.AllowConfidenceFallback,
                turnRoutingPreference);

        if (isFollowUpRequest)
        {
            var sessionTaskMode = (_workingSession.CurrentTaskMode ?? string.Empty).Trim().ToLowerInvariant();
            if (sessionTaskMode == "code")
            {
                return new AssistantExecutionPlan(
                    AIManager.AITaskBucket.Code,
                    AssistantExecutionMode.PlainAi,
                    "session-follow-up-code",
                    structuredTask == StructuredTaskKind.None ? StructuredTaskKind.CodeTask : structuredTask,
                    grounding.Query,
                    grounding.Checks,
                    grounding.AllowConfidenceFallback,
                    turnRoutingPreference);
            }

            if (sessionTaskMode == "generation")
            {
                return new AssistantExecutionPlan(
                    AIManager.AITaskBucket.Generation,
                    AssistantExecutionMode.PlainAi,
                    "session-follow-up-generation",
                    StructuredTaskKind.None,
                    grounding.Query,
                    grounding.Checks,
                    grounding.AllowConfidenceFallback,
                    turnRoutingPreference);
            }
        }

        return new AssistantExecutionPlan(
            AIManager.AITaskBucket.Chat,
            AssistantExecutionMode.PlainAi,
            isFollowUpRequest ? "session-follow-up-chat" : "conversation",
            StructuredTaskKind.None,
            grounding.Query,
            grounding.Checks,
            grounding.AllowConfidenceFallback,
            turnRoutingPreference);
    }

    private static StructuredTaskKind ClassifyStructuredTaskKind(string userMessage)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return StructuredTaskKind.None;

        if (Regex.IsMatch(lower,
            @"\b(read|inspect|analy[sz]e|analyse|review|scan|search|find|list|show|open|check)\b.*\b(file|files|folder|folders|directory|directories|repo|repository|workspace|solution|project|tree|path|paths|xaml|csproj|sln|json|yaml|xml|markdown|log|logs|config)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            return StructuredTaskKind.FileAnalysis;
        }

        if (Regex.IsMatch(lower,
            @"\b(build|rebuild|compile|test|publish|launch|run|start|stop|restart|kill|diagnose|troubleshoot|scan|inspect|check|monitor|flush|repair|fix|service|process|network|dns|firewall|update|powershell|terminal|cmd|command)\b",
            RegexOptions.IgnoreCase))
        {
            return StructuredTaskKind.SystemOperation;
        }

        if (Regex.IsMatch(lower,
            @"\b(code|coding|debug|debugging|bug|exception|stack trace|refactor|implement|implementation|patch|unit test|integration test|method|class|function|compile error|runtime error|root cause)\b",
            RegexOptions.IgnoreCase))
        {
            return StructuredTaskKind.CodeTask;
        }

        return StructuredTaskKind.None;
    }

    private bool ShouldUseAgentTools(string userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        var hasWorkspace = !string.IsNullOrWhiteSpace(_agentWorkspaceRoot);
        if (!hasWorkspace)
            return false;

        var fileOrWorkspaceTask = Regex.IsMatch(lower,
            @"\b(read|open|inspect|show|view|check|scan|search|find|grep|list|locate|write|create|add|append|update|modify|edit|patch|replace|rename|delete|remove|move)\b.*\b(file|folder|directory|repo|repository|project|solution|workspace|code|source|class|method|function|log|config|xaml|csproj|sln|json|yaml|xml|markdown)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (fileOrWorkspaceTask)
            return true;

        var commandTask = Regex.IsMatch(lower,
            @"\b(run|execute|build|rebuild|compile|test|publish|launch|start|stop|restart)\b.*\b(command|powershell|terminal|cmd|script|project|solution|atlas|app|application|server)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (commandTask)
            return true;

        var mentionsIntegration = false;
        try
        {
            IntegrationRegistry.Initialize();
            mentionsIntegration = IntegrationRegistry.GetAll().Any(integration =>
                (!string.IsNullOrWhiteSpace(integration.Id) && lower.Contains(integration.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(integration.Name) && lower.Contains(integration.Name, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
        }

        if (!mentionsIntegration && !Regex.IsMatch(lower, @"\b(api|endpoint|integration|service|provider|connector)\b", RegexOptions.IgnoreCase))
            return false;

        return Regex.IsMatch(lower,
            @"\b(call|use|check|list|show|test|configure|connect|open|run|trigger|invoke)\b.*\b(api|endpoint|integration|service|provider|connector|spotify|youtube|canva|clipboard|security|web search|openai|claude|gemini)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static GroundingDecision AssessGroundingNeed(string userMessage)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return new GroundingDecision(false, false, string.Empty, string.Empty);

        if (Regex.IsMatch(lower, @"\b(file|folder|directory|repo|repository|workspace|solution|project|code|class|method|function|command|powershell|terminal|build|compile|test|patch|edit|write|modify)\b", RegexOptions.IgnoreCase))
            return new GroundingDecision(false, false, string.Empty, string.Empty);

        var checks = new List<string>();
        var requiresWebSearch = false;
        var allowConfidenceFallback = false;

        if (Regex.IsMatch(lower, @"\b(latest|current|today|recent|newest|breaking|this week|this month|news|release notes|release date|version|changelog)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("freshness keywords: latest/current/recent/news/version/release");
        }

        if (Regex.IsMatch(lower, @"\b(doc|docs|documentation|reference|guide|tutorial|readme|quickstart)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("documentation lookup");
        }

        if (Regex.IsMatch(lower, @"\b(api|apis|sdk|endpoint|schema|openapi|graphql|rest api|webhook)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("API surface lookup");
        }

        if (Regex.IsMatch(lower, @"\b(library|libraries|package|packages|dependency|dependencies|nuget|npm|pip|pypi|crate|gem|module)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("library/package lookup");
        }

        if (Regex.IsMatch(lower, @"\b(price|pricing|cost|plan|plans|tier|tiers|subscription|free tier)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("pricing lookup");
        }

        if (Regex.IsMatch(lower, @"\b(event|events|conference|launch|announcement|incident|outage|roadmap)\b", RegexOptions.IgnoreCase))
        {
            requiresWebSearch = true;
            allowConfidenceFallback = true;
            checks.Add("recent event or announcement lookup");
        }

        if (Regex.IsMatch(lower, @"\b(recommend|recommendation|best|top|compare|comparison|versus|\bvs\b|should i use|what should i use|which one should i use)\b", RegexOptions.IgnoreCase))
        {
            allowConfidenceFallback = true;
            checks.Add("recommendation/comparison may have changed");
        }

        if (Regex.IsMatch(lower, @"^(what is|what's|who is|who's|when is|when did|where is|how to|how do i|search for|look up|find online|check online)\b", RegexOptions.IgnoreCase))
        {
            allowConfidenceFallback = true;
            if (!requiresWebSearch)
                checks.Add("explicit lookup wording");
        }

        var query = (requiresWebSearch || allowConfidenceFallback) ? BuildWebSearchQuery(userMessage) : string.Empty;
        var checksText = string.Join("; ", checks.Distinct(StringComparer.OrdinalIgnoreCase));
        return new GroundingDecision(requiresWebSearch, allowConfidenceFallback, query, checksText);
    }

    private static bool ShouldTriggerGroundedWebFallback(AssistantExecutionPlan executionPlan, string userMessage, string assistantText)
    {
        if (executionPlan.Mode == AssistantExecutionMode.AutomaticWebSearch)
            return false;

        if (!executionPlan.AllowConfidenceBasedWebFallback)
            return false;

        if (string.IsNullOrWhiteSpace(executionPlan.WebQuery))
            return false;

        var response = (assistantText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(response))
            return true;

        if (Regex.IsMatch(response,
            @"\b(i do not know|i don't know|i am not sure|i'm not sure|not sure|unclear|uncertain|can't verify|cannot verify|i don't have live|i do not have live|i don't have current|i do not have current|as of my last update|may have changed|might have changed|could be outdated|possibly outdated|you should verify)\b",
            RegexOptions.IgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(userMessage ?? string.Empty,
            @"\b(recommend|recommendation|best|top|compare|comparison|versus|\bvs\b|docs|documentation|pricing|api|package|library)\b",
            RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(response, @"\b(source|sources|grounding|freshness|confidence)\b", RegexOptions.IgnoreCase);
    }

    private static string BuildWebSearchQuery(string userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = Regex.Replace(text, @"^please\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^can you\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^could you\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^look up\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^search for\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^find online\s+", string.Empty, RegexOptions.IgnoreCase);
        return text.Trim();
    }

    private static bool LooksLikeGenerationTask(string userMessage)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return false;

        return Regex.IsMatch(lower,
            @"\b(generate|create image|make image|design|poster|artwork|caption|campaign|social post|thumbnail|storyboard|prompt)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeCodeTask(string userMessage)
    {
        var lower = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return false;

        return Regex.IsMatch(lower,
            @"\b(code|coding|debug|bug|exception|stack trace|refactor|implement|implementation|patch|unit test|integration test|xaml|csproj|sln|json|yaml|xml|markdown|repo|repository|workspace)\b",
            RegexOptions.IgnoreCase);
    }

    private string BuildToolContext(AssistantExecutionPlan executionPlan)
    {
        var parts = new List<string>
        {
            "Atlas can use direct tools for files, folders, command execution, system actions, integrations, and web search.",
        };

        if (!string.IsNullOrWhiteSpace(_agentWorkspaceRoot))
            parts.Add($"Workspace root: {_agentWorkspaceRoot}");

        if (executionPlan.Mode == AssistantExecutionMode.AgentTools)
            parts.Add("This request was pre-classified as a tool-using workspace task. Prefer tool-backed actions over descriptive answers.");
        else if (executionPlan.Mode == AssistantExecutionMode.AutomaticWebSearch)
            parts.Add("This request was pre-classified as current or uncertain online information and was eligible for automatic web lookup.");

        if (!string.IsNullOrWhiteSpace(executionPlan.GroundingChecks))
            parts.Add($"Grounding checks: {executionPlan.GroundingChecks}.");

        if (!string.IsNullOrWhiteSpace(_workingSession.ActiveProject) ||
            !string.IsNullOrWhiteSpace(_workingSession.ActiveFolder) ||
            _workingSession.LastFilesTouched.Count > 0 ||
            !string.IsNullOrWhiteSpace(_workingSession.CurrentGoal))
        {
            parts.Add($"Active session memory: project={_workingSession.ActiveProject}; folder={_workingSession.ActiveFolder}; goal={_workingSession.CurrentGoal}; task-mode={_workingSession.CurrentTaskMode}; recent-files={string.Join(", ", _workingSession.LastFilesTouched.Take(4))}; recent-errors={string.Join(" | ", _workingSession.LastErrors.Take(2))}.");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildAdditionalInstructions(AssistantExecutionPlan executionPlan)
    {
        var modeInstruction = executionPlan.Mode switch
        {
            AssistantExecutionMode.AgentTools => "The runtime classified this as a tool-first task before provider routing. Use concrete workspace-aware reasoning.",
            AssistantExecutionMode.AutomaticWebSearch => "The runtime classified this as current or uncertain information. Prefer grounded, up-to-date answers with explicit freshness and confidence notes.",
            _ => "The runtime classified this request before provider routing. Answer normally unless tools are explicitly required.",
        };

        if (!RequiresStructuredOutput(executionPlan.StructuredTask))
            return modeInstruction;

        return modeInstruction + " Return these headings exactly and keep each section concise: Task Classification, Provider Used, Files Inspected, Plan, Action Taken, Files Changed, Result, Remaining Risks, Next Step. Use 'none' when a section does not apply. Do not reply as loose prose for this task.";
    }

    private AIManager.AIRoutingRequest BuildRoutingRequest(List<object> messages, AssistantExecutionPlan executionPlan)
    {
        var routingPreference = executionPlan.TurnRoutingPreference ?? AiChatTurnRoutingPreference.None;

        return new AIManager.AIRoutingRequest
        {
            Module = "ai_chat",
            Messages = messages,
            MaxTokens = 800,
            BucketHint = executionPlan.Bucket,
            PreferredProviderOverride = routingPreference.PreferredProviderOverride,
            PreferredModelOverride = routingPreference.PreferredModelOverride,
            RuntimeContext = new AIManager.AIRuntimeContext
            {
                ActiveModule = "ai_chat",
                ActivePage = "chat",
                WorkspacePath = _agentWorkspaceRoot,
                ToolContext = BuildToolContext(executionPlan),
                AdditionalInstructions = BuildAdditionalInstructions(executionPlan),
            },
        };
    }

    private async Task<string> TryGenerateGroundedWebResponseAsync(
        List<object> baseContext,
        string originalUserText,
        AssistantExecutionPlan executionPlan,
        string? draftResponse,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executionPlan.WebQuery))
            return string.Empty;

        WebSearchTool.WebSearchExecutionResult? webSearchResult = null;

        try
        {
            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = PresenceMode.Working;
            }
            catch
            {
            }
            webSearchResult = await WebSearchTool.SearchWithMetadataAsync(executionPlan.WebQuery, ct);
        }
        finally
        {
            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = PresenceMode.Thinking;
            }
            catch
            {
            }
        }

        var nonGroundedResult = GetNonGroundedWebResultText(webSearchResult);
        if (!string.IsNullOrWhiteSpace(nonGroundedResult))
            return nonGroundedResult;

        var groundedEvidence = GetGroundedWebEvidenceText(webSearchResult);
        if (string.IsNullOrWhiteSpace(groundedEvidence))
            return string.Empty;

        var groundedContext = new List<object>
        {
            new
            {
                role = "system",
                content = BuildGroundedWebSystemPrompt(executionPlan, draftResponse)
            },
            new
            {
                role = "system",
                content = $"Grounding evidence for this turn:\n{groundedEvidence}"
            }
        };

        groundedContext.AddRange(baseContext);

        var groundedResponse = await AIManager.SendMessageAsync(
            BuildRoutingRequest(groundedContext, executionPlan),
            ct);

        if (!groundedResponse.Success)
            return groundedEvidence;

        return groundedResponse.Content;
    }

    internal static string GetGroundedWebEvidenceText(WebSearchTool.WebSearchExecutionResult? webSearchResult)
    {
        if (webSearchResult == null || !webSearchResult.IsGrounded)
            return string.Empty;

        var evidence = NormalizeToolOutput(webSearchResult.EvidenceText ?? string.Empty);
        return LooksLikeSearchEscalationMessage(evidence) ? string.Empty : evidence;
    }

    internal static string GetNonGroundedWebResultText(WebSearchTool.WebSearchExecutionResult? webSearchResult)
    {
        if (webSearchResult == null)
            return string.Empty;

        if (webSearchResult.IsGrounded)
            return string.Empty;

        return NormalizeToolOutput(webSearchResult.UserFacingText ?? string.Empty);
    }

    private static bool LooksLikeSearchEscalationMessage(string webResult)
    {
        if (string.IsNullOrWhiteSpace(webResult))
            return true;

        if (webResult.StartsWith("WEB_SEARCH_DENIED", StringComparison.OrdinalIgnoreCase))
            return true;

        return webResult.Contains("online access enabled", StringComparison.OrdinalIgnoreCase)
            || webResult.Contains("would you like me to look it up", StringComparison.OrdinalIgnoreCase)
            || webResult.StartsWith("CANCELLED", StringComparison.OrdinalIgnoreCase)
            || webResult.StartsWith("🔍 Search for", StringComparison.OrdinalIgnoreCase)
            || webResult.Contains("Here's a search link", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGroundedWebSystemPrompt(AssistantExecutionPlan executionPlan, string? draftResponse)
    {
        var sections = new List<string>
        {
            "Use the supplied web evidence to answer this turn. Do not guess beyond the evidence.",
            "Preserve the conversation's tone and answer as Atlas, but replace unsupported claims with grounded ones.",
            "Return a structured grounded answer with these headings exactly: Answer, Grounding, Freshness, Confidence.",
            "Under Grounding, cite the most relevant sources as flat bullet points using the titles or URLs present in the evidence.",
            "Under Freshness, state whether the answer depended on current information.",
            "Under Confidence, say high, medium, or low and briefly explain why.",
            "If the evidence is incomplete or conflicting, say that plainly and ask for a narrower follow-up instead of bluffing."
        };

        if (!string.IsNullOrWhiteSpace(executionPlan.GroundingChecks))
            sections.Add($"Grounding checks that triggered this lookup: {executionPlan.GroundingChecks}.");

        if (!string.IsNullOrWhiteSpace(draftResponse))
            sections.Add($"A first-pass draft looked uncertain and must be corrected or replaced, not repeated verbatim:\n{draftResponse.Trim()}");

        return string.Join("\n\n", sections);
    }

    private sealed record CodingExecutionResult(string CleanedText, string? ToolOutput);

    private static string FormatToolOutputForChat(string raw)
    {
        var cleaned = NormalizeToolOutput(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        var suffix = "";
        if (cleaned.Length > 1200)
        {
            cleaned = cleaned.Substring(0, 900).TrimEnd();
            suffix = "\n\n…(truncated) Want the full output?";
        }

        if (IsMultiLine(cleaned))
            return $"Done. Here's what I got:\n```text\n{cleaned}\n```{suffix}";

        return cleaned + suffix;
    }

    private static bool IsMultiLine(string text)
        => text.IndexOf('\n', StringComparison.Ordinal) >= 0;

    private static string NormalizeToolOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();

        if (s.Length == 0)
            return string.Empty;

        // Trim per-line trailing whitespace; keep content otherwise intact.
        var sb = new StringBuilder(s.Length);
        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = (lines[i] ?? string.Empty).TrimEnd();
            sb.Append(line);
            if (i < lines.Length - 1)
                sb.Append('\n');
        }

        s = sb.ToString().Trim();

        // Compact excessive vertical whitespace.
        s = Regex.Replace(s, "\\n{3,}", "\n\n");
        return s;
    }

    private static bool RequiresStructuredOutput(StructuredTaskKind taskKind)
        => taskKind != StructuredTaskKind.None;

    private static string GetStructuredTaskLabel(StructuredTaskKind taskKind)
        => taskKind switch
        {
            StructuredTaskKind.CodeTask => "code/refactor/debug task",
            StructuredTaskKind.FileAnalysis => "file/folder analysis",
            StructuredTaskKind.SystemOperation => "system operation",
            StructuredTaskKind.SmartHomeAction => "smart-home action",
            StructuredTaskKind.MediaAction => "media action",
            _ => "general chat",
        };

    private static string FormatProviderUsed(string provider, string model)
    {
        var normalizedProvider = (provider ?? string.Empty).Trim();
        var normalizedModel = (model ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedProvider) && string.IsNullOrWhiteSpace(normalizedModel))
            return "local-tools";

        if (string.IsNullOrWhiteSpace(normalizedModel))
            return normalizedProvider;

        if (string.IsNullOrWhiteSpace(normalizedProvider))
            return normalizedModel;

        return $"{normalizedProvider} · {normalizedModel}";
    }

    private static bool TryMatchStructuredHeading(string line, out string heading, out string inlineValue)
    {
        var match = Regex.Match(
            line ?? string.Empty,
            @"^\s*(?:[-*]\s*)?(?:\*\*)?(Task Classification|Provider Used|Files Inspected|Plan|Action Taken|Files Changed|Result|Remaining Risks|Next Step)(?:\*\*)?\s*:?\s*(.*)$",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            heading = match.Groups[1].Value.Trim();
            inlineValue = match.Groups[2].Value.Trim();
            return true;
        }

        heading = string.Empty;
        inlineValue = string.Empty;
        return false;
    }

    private static Dictionary<string, string> ParseStructuredSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeToolOutput(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return result;

        string? currentHeading = null;
        var builder = new StringBuilder();

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine ?? string.Empty;
            if (TryMatchStructuredHeading(line, out var heading, out var inlineValue))
            {
                if (!string.IsNullOrWhiteSpace(currentHeading))
                    result[currentHeading] = builder.ToString().Trim();

                currentHeading = heading;
                builder.Clear();
                if (!string.IsNullOrWhiteSpace(inlineValue))
                    builder.Append(inlineValue);
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentHeading))
                continue;

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(line);
        }

        if (!string.IsNullOrWhiteSpace(currentHeading))
            result[currentHeading] = builder.ToString().Trim();

        return result;
    }

    private static List<string> ExtractLikelyPaths(IEnumerable<string?> textBlocks)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new[]
        {
            @"(?<!\w)[A-Za-z]:\\[^\s`""'<>|]+",
            @"(?<!\w)(?:\.?\.?[/\\])?[A-Za-z0-9_./\\ -]+\.(?:cs|xaml|csproj|sln|slnx|json|xml|yml|yaml|md|txt|log|config|ps1|cmd|bat)(?!\w)"
        };

        foreach (var block in textBlocks.Where(static block => !string.IsNullOrWhiteSpace(block)))
        {
            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(block!, pattern, RegexOptions.IgnoreCase))
                {
                    var value = (match.Value ?? string.Empty).Trim().TrimEnd('.', ',', ';', ':');
                    if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                        continue;

                    results.Add(value);
                    if (results.Count >= 10)
                        return results;
                }
            }
        }

        return results;
    }

    private static List<string> ExtractAgentPaths(IEnumerable<Agent.AgentAction> actions, bool changedOnly)
    {
        var changedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "write_file",
            "delete_file",
            "rename_file",
            "move_file",
            "create_file",
            "patch_file",
        };

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in actions ?? Array.Empty<Agent.AgentAction>())
        {
            var isChanged = changedTools.Contains(action.Tool ?? string.Empty);
            if (changedOnly != isChanged)
                continue;

            foreach (var key in new[] { "path", "source", "target", "destination", "new_path", "old_path" })
            {
                if (!action.Params.TryGetValue(key, out var value) || value == null)
                    continue;

                var path = value.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    continue;

                results.Add(path);
                if (results.Count >= 10)
                    return results;
            }
        }

        return results;
    }

    private static string FormatSectionList(IEnumerable<string> values, string emptyValue = "none")
    {
        var items = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return items.Count == 0 ? emptyValue : string.Join("\n", items.Select(static item => $"- {item}"));
    }

    private static string InferRemainingRisks(string resultText, bool success)
    {
        var normalized = NormalizeToolOutput(resultText);
        if (!success)
            return string.IsNullOrWhiteSpace(normalized) ? "the requested action did not complete successfully" : normalized;

        if (Regex.IsMatch(normalized, @"\b(maybe|might|uncertain|verify|check|partial|truncated|warning)\b", RegexOptions.IgnoreCase))
            return "verify the outcome before relying on it fully";

        return "none noted";
    }

    private static string InferNextStep(StructuredTaskKind taskKind, bool success)
    {
        if (!success)
        {
            return taskKind switch
            {
                StructuredTaskKind.CodeTask => "review the reported failure, then retry with the relevant file or error context",
                StructuredTaskKind.FileAnalysis => "refine the scope or name the exact file or folder to inspect next",
                StructuredTaskKind.SystemOperation => "check the reported error and rerun the operation with a narrower command if needed",
                StructuredTaskKind.SmartHomeAction => "confirm the device state or retry the command once the provider is reachable",
                StructuredTaskKind.MediaAction => "refine the title or media type and try the action again",
                _ => "ask a narrower follow-up",
            };
        }

        return taskKind switch
        {
            StructuredTaskKind.CodeTask => "review the result and, if needed, request the next edit or validation step",
            StructuredTaskKind.FileAnalysis => "name the next file or folder if you want a deeper pass",
            StructuredTaskKind.SystemOperation => "confirm the operational state if you want a follow-up validation step",
            StructuredTaskKind.SmartHomeAction => "verify the device responded as expected or issue the next smart-home command",
            StructuredTaskKind.MediaAction => "pick a returned item or refine the query for another media action",
            _ => "continue the conversation normally",
        };
    }

    private static string DefaultPlanFor(StructuredTaskKind taskKind)
    {
        return taskKind switch
        {
            StructuredTaskKind.CodeTask => "Inspect the relevant code context, produce a concrete fix or diagnosis, and report the outcome.",
            StructuredTaskKind.FileAnalysis => "Inspect the requested files or folders and summarize the relevant findings.",
            StructuredTaskKind.SystemOperation => "Resolve the requested operational action, run the relevant local step when possible, and report the outcome.",
            StructuredTaskKind.SmartHomeAction => "Resolve the requested smart-home intent and execute it through the local smart-home runtime.",
            StructuredTaskKind.MediaAction => "Resolve the requested media action against the local media catalogue and return the outcome.",
            _ => "Answer the request directly.",
        };
    }

    private static string DefaultActionTakenFor(StructuredTaskKind taskKind, bool success)
    {
        var outcome = success ? "Completed" : "Attempted";
        return taskKind switch
        {
            StructuredTaskKind.CodeTask => $"{outcome} the requested code, refactor, or debugging pass.",
            StructuredTaskKind.FileAnalysis => $"{outcome} the requested file or folder inspection.",
            StructuredTaskKind.SystemOperation => $"{outcome} the requested system or operational action.",
            StructuredTaskKind.SmartHomeAction => $"{outcome} the requested smart-home command.",
            StructuredTaskKind.MediaAction => $"{outcome} the requested media action.",
            _ => $"{outcome} the requested task.",
        };
    }

    private static string BuildStructuredResponse(
        StructuredTaskKind taskKind,
        string providerUsed,
        string rawText,
        bool success,
        IEnumerable<string>? extraFilesInspected = null,
        IEnumerable<string>? extraFilesChanged = null,
        string? fallbackPlan = null,
        string? fallbackActionTaken = null,
        string? fallbackNextStep = null)
    {
        var normalizedText = NormalizeToolOutput(rawText);
        var sections = ParseStructuredSections(normalizedText);

        var filesInspected = new List<string>();
        if (extraFilesInspected != null)
            filesInspected.AddRange(extraFilesInspected);
        filesInspected.AddRange(ExtractLikelyPaths(new[]
        {
            sections.TryGetValue("Files Inspected", out var inspectedSection) ? inspectedSection : string.Empty,
            normalizedText
        }));

        var filesChanged = new List<string>();
        if (extraFilesChanged != null)
            filesChanged.AddRange(extraFilesChanged);
        filesChanged.AddRange(ExtractLikelyPaths(new[]
        {
            sections.TryGetValue("Files Changed", out var changedSection) ? changedSection : string.Empty
        }));

        var resultText = sections.TryGetValue("Result", out var explicitResult) && !string.IsNullOrWhiteSpace(explicitResult)
            ? explicitResult.Trim()
            : normalizedText;

        var plan = sections.TryGetValue("Plan", out var explicitPlan) && !string.IsNullOrWhiteSpace(explicitPlan)
            ? explicitPlan.Trim()
            : (fallbackPlan ?? DefaultPlanFor(taskKind));

        var actionTaken = sections.TryGetValue("Action Taken", out var explicitAction) && !string.IsNullOrWhiteSpace(explicitAction)
            ? explicitAction.Trim()
            : (fallbackActionTaken ?? DefaultActionTakenFor(taskKind, success));

        var remainingRisks = sections.TryGetValue("Remaining Risks", out var explicitRisks) && !string.IsNullOrWhiteSpace(explicitRisks)
            ? explicitRisks.Trim()
            : InferRemainingRisks(resultText, success);

        var nextStep = sections.TryGetValue("Next Step", out var explicitNextStep) && !string.IsNullOrWhiteSpace(explicitNextStep)
            ? explicitNextStep.Trim()
            : (fallbackNextStep ?? InferNextStep(taskKind, success));

        var builder = new StringBuilder();
        builder.AppendLine("Task Classification");
        builder.AppendLine(GetStructuredTaskLabel(taskKind));
        builder.AppendLine();
        builder.AppendLine("Provider Used");
        builder.AppendLine(string.IsNullOrWhiteSpace(providerUsed) ? "local-tools" : providerUsed.Trim());
        builder.AppendLine();
        builder.AppendLine("Files Inspected");
        builder.AppendLine(FormatSectionList(filesInspected));
        builder.AppendLine();
        builder.AppendLine("Plan");
        builder.AppendLine(string.IsNullOrWhiteSpace(plan) ? "none" : plan);
        builder.AppendLine();
        builder.AppendLine("Action Taken");
        builder.AppendLine(string.IsNullOrWhiteSpace(actionTaken) ? "none" : actionTaken);
        builder.AppendLine();
        builder.AppendLine("Files Changed");
        builder.AppendLine(FormatSectionList(filesChanged));
        builder.AppendLine();
        builder.AppendLine("Result");
        builder.AppendLine(string.IsNullOrWhiteSpace(resultText) ? "none" : resultText);
        builder.AppendLine();
        builder.AppendLine("Remaining Risks");
        builder.AppendLine(string.IsNullOrWhiteSpace(remainingRisks) ? "none" : remainingRisks);
        builder.AppendLine();
        builder.AppendLine("Next Step");
        builder.Append(string.IsNullOrWhiteSpace(nextStep) ? "none" : nextStep);
        return builder.ToString().Trim();
    }

    private async Task<CodingExecutionResult> ExecuteCodingToolsIfPresentAsync(string assistantText, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(assistantText))
                return new CodingExecutionResult("", null);

            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = PresenceMode.Working;
            }
            catch
            {
            }

            var (codeHandled, codeResult) = await _codeToolExecutor.TryExecuteToolAsync(assistantText, ct);
            if (!codeHandled || codeResult == null)
                return new CodingExecutionResult(assistantText, null);

            var cleanedResponse = Regex.Replace(assistantText, @"\[TOOL:[^\]]+\]", "").Trim();
            return new CodingExecutionResult(cleanedResponse, codeResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Chat] CodeToolExecutor error: {ex.Message}");
            return new CodingExecutionResult(assistantText, null);
        }
        finally
        {
            try
            {
                if (CurrentMode != PresenceMode.Error)
                    CurrentMode = IsListening ? PresenceMode.Listening : (IsTyping ? PresenceMode.Thinking : PresenceMode.Idle);
            }
            catch
            {
            }
        }
    }

    private static string BuildToolListText()
    {
        // ToolExecutor doesn't currently expose a public registry; keep a deterministic top list.
        // These phrases map to real ToolExecutor routes (system + app actions) in this project.
        var lines = new[]
        {
            "• open notepad",
            "• open calculator",
            "• open cmd",
            "• ipconfig (or ipconfig /all)",
            "• ping 8.8.8.8",
            "• take screenshot",
            "• open settings (wifi/bluetooth/display/sound/etc)",
            "• scan system",
            "• stop voice",
            "• open security suite"
        };

        return "AVAILABLE TOOL PHRASES (SAMPLE)\n" + string.Join("\n", lines);
    }

    private static string TryInferAgentToolName(string toolExecutingDescription)
    {
        var d = (toolExecutingDescription ?? "").Trim();
        if (string.IsNullOrWhiteSpace(d)) return "";

        // Description is typically "🔧 {GetToolDescription(...)}"
        if (d.StartsWith("🔧", StringComparison.Ordinal))
            d = d.Substring(1).Trim();

        // Map common descriptions back to tool IDs.
        if (d.StartsWith("Delete file:", StringComparison.OrdinalIgnoreCase)) return "delete_file";
        if (d.StartsWith("Uninstall software:", StringComparison.OrdinalIgnoreCase)) return "uninstall_software";
        if (d.StartsWith("Run command:", StringComparison.OrdinalIgnoreCase)) return "run_command";
        if (d.StartsWith("Run PowerShell:", StringComparison.OrdinalIgnoreCase)) return "run_powershell";
        if (d.StartsWith("Write:", StringComparison.OrdinalIgnoreCase)) return "write_file";
        if (d.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase)) return "tool";

        // Default format is "<tool>: ..."
        var idx = d.IndexOf(':');
        if (idx > 0)
            return d.Substring(0, idx).Trim();

        return "";
    }

    private static string TryFindWorkspaceRoot()
    {
        try
        {
            var start = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 10 && dir != null; i++)
            {
                var csproj = Path.Combine(dir.FullName, "AtlasAI.csproj");
                if (File.Exists(csproj))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
        }
        return "";
    }

    public void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (Attachments.Contains(p)) continue;
            Attachments.Add(p);
        }
    }

    public void RemoveAttachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Attachments.Remove(path);
    }

    private static string BuildUserPayload(string text, List<string> attachments)
    {
        if (attachments.Count == 0) return text;

        var normalizedText = string.IsNullOrWhiteSpace(text) ? "(no message)" : text;
        var parts = new List<string> { normalizedText };

        foreach (var path in attachments)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = path;

            try
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    var isText =
                        ext is ".txt" or ".md" or ".cs" or ".json" or ".xml" or ".xaml" or ".yml" or ".yaml" or
                        ".js" or ".ts" or ".tsx" or ".jsx" or ".py" or ".java" or ".kt" or ".cpp" or ".h" or ".hpp" or
                        ".css" or ".scss" or ".html" or ".htm";

                    var info = new FileInfo(path);
                    if (isText && info.Length <= 50_000)
                    {
                        var content = File.ReadAllText(path);
                        parts.Add($"[Attachment: {fileName}]\n{content}");
                        continue;
                    }
                }
            }
            catch
            {
            }

            parts.Add($"[Attachment: {fileName}]");
        }

        return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static bool TryParseMediaCentreQuery(string text, out string query, out string? typeId)
    {
        query = "";
        typeId = null;

        var raw = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("media centre") && !lower.Contains("media center") && !lower.Contains("media centr")) return false;
        if (!lower.Contains("find") && !lower.Contains("search") && !lower.Contains("list") && !lower.Contains("show")) return false;

        if (lower.Contains("movie"))
            typeId = "movies";
        else if (lower.Contains("tv") || lower.Contains("series") || lower.Contains("show"))
            typeId = "tv";
        else if (lower.Contains("music") || lower.Contains("song") || lower.Contains("track"))
            typeId = "music";

        query = raw;
        query = Regex.Replace(query, "\\b(show me|show|list|find|search|in|inside|me|the)\\b", " ", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, "\\b(media centre|media center|media centr|media)\\b", " ", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, "\\b(movies|movie|tv|shows|show|series|music|songs|song|tracks|track)\\b", " ", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, "\\b(all|every)\\b", " ", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, "\\s+", " ").Trim();

        return true;
    }

    private List<object> BuildContext(string latestUserMessage)
    {
        string? accent = null;
        double? verbosity = null;
        try { accent = (PreferencesStore.Instance.Current.ChatAccent ?? string.Empty).Trim(); } catch { }
        try { verbosity = LocalPreferenceMemoryStore.Instance.Current.Verbosity.Score; } catch { }

        var personaId = SelectedChatPersonality;

        var prompt = _personalityEngine.Compose(
            personaId,
            BanterLevel,
            AllowProfanity,
            AllowPlayfulRoast,
            _chatPreferredName,
            Messages.TakeLast(16).Cast<object>(),
            latestUserMessage,
            userAccent: accent,
            verbosityScore: verbosity);

        try
        {
            AppLogger.LogInfo($"[Chat] generation mode='{SelectedChatPersonality}' banter={BanterLevel} profanity={AllowProfanity} roast={AllowPlayfulRoast}");
            AppLogger.LogInfo($"[Chat] system prompt length={prompt.SystemPrompt?.Length ?? 0}");
        }
        catch
        {
        }

        var systemPrompt = prompt.SystemPrompt ?? string.Empty;
        try
        {
            var snap = _memoryService.GetSnapshot();
            var memBlock = _memoryService.BuildPromptMemoryBlock(snap);
            if (!string.IsNullOrWhiteSpace(memBlock))
                systemPrompt = (systemPrompt + "\n\n" + memBlock).Trim();
        }
        catch
        {
        }

        try
        {
            var workingSessionBlock = BuildWorkingSessionMemoryBlock();
            if (!string.IsNullOrWhiteSpace(workingSessionBlock))
                systemPrompt = (systemPrompt + "\n\n" + workingSessionBlock).Trim();
        }
        catch
        {
        }

        var context = new List<object> { new { role = "system", content = systemPrompt } };
        var systemMessagesAttached = 1;

        try
        {
            var snap = _contextService.CaptureSnapshot();
            var ctxBlock = _contextService.BuildPromptContextBlock(snap);
            if (!string.IsNullOrWhiteSpace(ctxBlock))
            {
                context.Add(new { role = "system", content = ctxBlock });
                systemMessagesAttached++;
            }
        }
        catch
        {
        }

        foreach (var msg in Messages.TakeLast(16))
        {
            if (string.IsNullOrWhiteSpace(msg.Content)) continue;
            var content = msg.Content ?? "";
            if (msg.IsUser)
                content = TransformUserContentForModel(content, allowCrudeBanter: AllowProfanity && AllowPlayfulRoast);
            context.Add(new { role = msg.IsUser ? "user" : "assistant", content = content });
        }

        try { AppLogger.LogInfo($"[Chat] system messages attached={systemMessagesAttached}"); } catch { }

        return context;
    }

    private static string TransformUserContentForModel(string content, bool allowCrudeBanter)
    {
        var t = (content ?? "").Trim();
        if (!allowCrudeBanter) return t;
        if (string.IsNullOrWhiteSpace(t)) return t;

        var lower = t.ToLowerInvariant();
        var looksLikeGreeting = Regex.IsMatch(lower, @"\b(hi|hey|hello|yo|sup|morning|afternoon|evening)\b", RegexOptions.IgnoreCase);
        var looksLikeCrude = Regex.IsMatch(lower, @"\b(shit|fuck|fucking|shite|bollocks|cunt|twat|wank|wanker|dick|dickhead|shitlips)\b", RegexOptions.IgnoreCase);

        if (looksLikeGreeting && looksLikeCrude)
            return "User greeted with crude banter. Treat it as playful, reply with cheeky sarcasm and move on to: what do you need?";

        return t;
    }

    private void PreferencesStore_PreferencesChanged(object? sender, UserPreferences prefs)
    {
        try
        {
            var personality = string.IsNullOrWhiteSpace(prefs.ChatPersonality) ? "Buddy" : prefs.ChatPersonality.Trim();
            if (AtlasAI.Personality.PersonalityRegistry.GetById(personality) == null)
                personality = "Buddy";
            if (!string.Equals(_selectedChatPersonality, personality, StringComparison.OrdinalIgnoreCase))
            {
                _selectedChatPersonality = personality;
                OnPropertyChanged(nameof(SelectedChatPersonality));
                OnPropertyChanged(nameof(PersonalityPillText));
            }

            var rawBanter = prefs.ChatBanterLevel > 0 ? prefs.ChatBanterLevel : prefs.ChatHumanLevel;
            var banter = Math.Clamp(rawBanter, 1, 5);
            if (_banterLevel != banter)
            {
                _banterLevel = banter;
                OnPropertyChanged(nameof(BanterLevel));
                OnPropertyChanged(nameof(PersonalityPillText));
            }

            var name = (prefs.ChatPreferredName ?? string.Empty).Trim();
            if (!string.Equals(_chatPreferredName, name, StringComparison.Ordinal))
            {
                _chatPreferredName = name;
                OnPropertyChanged(nameof(UserDisplayName));
            }

            if (_chatAllowProfanity != prefs.ChatAllowProfanity)
            {
                _chatAllowProfanity = prefs.ChatAllowProfanity;
                OnPropertyChanged(nameof(AllowProfanity));
            }

            if (_chatAllowPlayfulRoast != prefs.ChatAllowPlayfulRoast)
            {
                _chatAllowPlayfulRoast = prefs.ChatAllowPlayfulRoast;
                OnPropertyChanged(nameof(AllowPlayfulRoast));
            }
        }
        catch
        {
        }
    }

    private async Task TrySpeakAsync(string text)
    {
        try
        {
            if (_voiceManager == null) return;
            try { await _voiceManager.WaitForInitializationAsync(); } catch { }
            if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;
            if (!_voiceManager.SpeechEnabled) _voiceManager.SpeechEnabled = true;
            if (_voiceManager.ActiveProviderType == VoiceProviderType.ElevenLabs)
            {
                try
                {
                    var active = _voiceManager.GetProvider(_voiceManager.ActiveProviderType);
                    if (active == null || !await active.IsAvailableAsync())
                    {
                        ToastError("ElevenLabs TTS unavailable. Check the API key and voice settings.");
                        return;
                    }
                }
                catch { }
            }
            await _voiceManager.SpeakAsync(text, ResponseType.Normal);
            OnPropertyChanged(nameof(SpeechEnabled));
        }
        catch
        {
        }
    }

    private async Task AddAssistantResponseAsync(string text, bool speakWhileTyping, CancellationToken cancellationToken, AtlasAI.Models.ChatMessage? targetMessage = null)
    {
        var content = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        var message = targetMessage ?? new AtlasAI.Models.ChatMessage
        {
            Content = string.Empty,
            IsUser = false,
            Timestamp = DateTime.Now
        };

        if (targetMessage == null)
            Messages.Add(message);

        if (speakWhileTyping && _voiceManager != null)
            _ = TrySpeakAsync(content);

        var index = 0;
        while (index < content.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = content.Length - index;
            var step = remaining > 240 ? 10 : remaining > 120 ? 6 : 3;
            index = Math.Min(content.Length, index + step);
            message.Content = content.Substring(0, index);

            if (index < content.Length)
                await Task.Delay(content.Length > 280 ? 24 : 32, cancellationToken);
        }
    }

    private async Task SetAiProviderAndReloadAsync(AIProviderType provider)
    {
        try
        {
            await AIManager.SetActiveProviderAsync(provider);

            try
            {
                var inst = AIManager.GetProvider(provider);
                if (inst != null && !inst.IsConfigured)
                    ToastError($"{provider} is not configured. This installation is locked to admin configuration.");
            }
            catch { }

            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            ToastError($"AI provider failed: {ex.Message}");
        }
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var providerType = _selectedAiProvider;
            var requestId = Interlocked.Increment(ref _aiModelsLoadRequestId);

            // Load models for the provider shown in the UI (not the effective provider after fallback).
            // This keeps the Provider + Model dropdowns consistent.
            var provider = AIManager.GetProvider(providerType);
            var models = provider != null
                ? await provider.GetModelsAsync()
                : new List<AIModel>();

            // Out-of-order protection: if another load started, ignore this one.
            if (requestId != _aiModelsLoadRequestId) return;
            if (providerType != _selectedAiProvider) return;

            if (models.Count == 0)
            {
                // fall back to current selected model (if any)
                var currentId = _autoModeEnabled
                    ? AIManager.GetAutoSmartModel(providerType)
                    : AIManager.GetManualSelectedModel(providerType);
                if (!string.IsNullOrWhiteSpace(currentId))
                    models.Add(new AIModel { Id = currentId, DisplayName = currentId, IsAvailable = true });
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AvailableModels.Clear();
                foreach (var m in models.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id)))
                    AvailableModels.Add(m);

                var selected = _autoModeEnabled
                    ? AIManager.GetAutoSmartModel(providerType)
                    : AIManager.GetManualSelectedModel(providerType);
                SelectedModel = AvailableModels.FirstOrDefault(m => string.Equals(m.Id, selected, StringComparison.OrdinalIgnoreCase))
                               ?? AvailableModels.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            ToastError($"Model load failed: {ex.Message}");
        }
    }

    private async Task SetVoiceProviderAndReloadAsync(VoiceProviderType provider)
    {
        if (_voiceManager == null) return;
        try
        {
            var ok = await _voiceManager.SetProviderAsync(provider);
            if (!ok)
            {
                // Revert UI selection to the provider that is actually active.
                _selectedVoiceProvider = _voiceManager.ActiveProviderType;
                OnPropertyChanged(nameof(SelectedVoiceProvider));

                ToastError($"Voice provider unavailable: {provider}");
            }
            else
            {
                try { VoicePreferences.Current.SetGlobalVoice(_voiceManager.SelectedVoice?.Id); } catch { }
                ToastInfo($"Voice provider: {provider}");
            }
            OnPropertyChanged(nameof(SpeechEnabled));
            await LoadVoicesAsync();
        }
        catch (Exception ex)
        {
            ToastError($"Voice provider failed: {ex.Message}");
        }
    }

    private static void ToastInfo(string message)
    {
        try { ToastNotificationManager.Instance.Show(message, ToastType.Info, 1400); } catch { }
    }

    private static void ToastError(string message)
    {
        try { ToastNotificationManager.Instance.Show(message, ToastType.Error, 4000); } catch { }
    }

    private async Task LoadVoicesAsync()
    {
        if (_voiceManager == null) return;

        try
        {
            var voices = await _voiceManager.GetVoicesAsync(CancellationToken.None);

            if (voices.Count == 0 && _voiceManager.ActiveProviderType == VoiceProviderType.ElevenLabs)
            {
                ToastError("No ElevenLabs voices loaded. Check the API key or refresh voices.");
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AvailableVoices.Clear();
                foreach (var v in voices)
                    AvailableVoices.Add(v);

                try
                {
                    _suppressVoiceSelection = true;
                    _selectedVoice = _voiceManager.SelectedVoice != null
                        ? AvailableVoices.FirstOrDefault(v => v.Id == _voiceManager.SelectedVoice.Id)
                        : AvailableVoices.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedVoice));
                }
                finally
                {
                    _suppressVoiceSelection = false;
                }
            });
        }
        catch
        {
        }
    }

    private void PreviewVoice()
    {
        _ = PreviewVoiceAsync();
    }

    private void QueuePreviewVoice()
    {
        try { _previewVoiceCts?.Cancel(); } catch { }
        try { _previewVoiceCts?.Dispose(); } catch { }
        _previewVoiceCts = new CancellationTokenSource();
        var ct = _previewVoiceCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(180, ct); } catch { return; }
            if (ct.IsCancellationRequested) return;
            await PreviewVoiceAsync();
        });
    }

    private async Task PreviewVoiceAsync()
    {
        try
        {
            if (_voiceManager == null) return;
            var v = _selectedVoice;
            if (v == null || string.IsNullOrWhiteSpace(v.Id)) return;

            try { await _voiceManager.WaitForInitializationAsync(); } catch { }

            if (_voiceManager.ActiveProviderType != v.Provider)
            {
                var okProvider = await _voiceManager.SetProviderAsync(v.Provider);
                if (!okProvider) return;
            }

            try { await _voiceManager.SelectVoiceAsync(v.Id); } catch { }

            if (_voiceManager.Volume < 0.05) _voiceManager.Volume = 1.0;
            if (!_voiceManager.SpeechEnabled) _voiceManager.SpeechEnabled = true;

            var sample = "Alright. Quick voice check. If you can hear this, we’re good.";
            await _voiceManager.SpeakAsync(sample, ResponseType.Normal);
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); } catch { }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
