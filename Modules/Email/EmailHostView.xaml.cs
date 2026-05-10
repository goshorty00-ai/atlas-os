using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Modules.Email.Models;
using AtlasAI.Modules.Email.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AtlasAI.Modules.Email;

public partial class EmailHostView : UserControl
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _nativeGmailLock = new(1, 1);
    private readonly GmailApiEmailProviderService _gmailApi = new();
    private readonly OutlookGraphEmailProviderService _outlookApi = new();
    private readonly List<StoredEmailAccount> _accounts = new();
    private readonly Dictionary<string, HostBounds> _boundsByAccount = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _gmailMessagesDebugFetched = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _apiInboxActiveAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _unreadRefreshLock = new(1, 1);
    private readonly Dictionary<string, List<EmailAgentMessageMeta>> _emailAgentInboxCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EmailAgentSelectedContext> _emailAgentSelectedCache = new(StringComparer.OrdinalIgnoreCase);

    private WebView2? _nativeGmailView;
    private string? _activeAccountId;
    private string? _nativeViewAccountId;
    private bool _nativeCoreInitialized;
    private bool _nativeNavigationCompletedSuccess;
    private string _nativeLastSource = string.Empty;
    private bool _settingsPanelOpen;
    private bool _aiPanelOpen;
    private string _activeViewMode = "webview";
    private string? _reactSelectedAccountId;
    private bool _nativeHostAllowed;
    private bool _reactConnectionError;
    private HostBounds? _lastApiLockedCenterBounds;
    private string? _lastApiLockedCenterAccountId;
    private bool _nativeLockedCardActive;

    private const double AiReopenRightGutterWidth = 64;

    public EmailHostView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadPersistedAccounts();
            await EnsureShellInitializedAsync();
        }
        catch (Exception ex)
        {
            LogDebug($"load failed: {ex}");
        }
    }

    private async void ReconnectGmailApiButton_Click(object sender, RoutedEventArgs e)
    {
        await ReconnectGmailApiAsync(null);
    }

    private async Task ReconnectGmailApiAsync(string? requestedAccountId)
    {
        string accountId = requestedAccountId
            ?? _reactSelectedAccountId
            ?? _nativeViewAccountId
            ?? _activeAccountId
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            LogDebug("[EmailGmailReconnect] step=error accountId= type=validation message=Missing selected account id");
            return;
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));

        if (account == null)
        {
            LogDebug($"[EmailGmailReconnect] step=error accountId={accountId} type=validation message=Account not found");
            return;
        }

        if (!string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            LogDebug($"[EmailGmailReconnect] step=error accountId={accountId} type=validation message=Account provider is not gmail");
            return;
        }

        LogDebug($"[EmailGmailReconnect] step=start accountId={account.Id} email={account.Email}");

        try
        {
            var flow = new GmailOAuthFlow();
            GmailOAuthSession session = flow.CreateAuthorizationSession(account.Id);

            Process.Start(new ProcessStartInfo
            {
                FileName = session.AuthorizationUrl,
                UseShellExecute = true,
            });

            string code = await flow.WaitForCallbackAsync(session, TimeSpan.FromMinutes(3), CancellationToken.None);
            GmailOAuthTokenResponse tokenResponse = await flow.ExchangeCodeAsync(session, code, CancellationToken.None);

            DateTime expiresAtUtc = tokenResponse.ExpiresIn > 0
                ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                : DateTime.UtcNow;

            var tokenData = new GmailTokenData
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAtUtc = expiresAtUtc,
                Scope = tokenResponse.Scope,
                TokenType = tokenResponse.TokenType,
            };

            string profileEmail = await flow.FetchProfileEmailAsync(tokenResponse.AccessToken, CancellationToken.None);
            LogDebug($"[EmailGmailReconnect] step=profile accountId={account.Id} requestedEmail={account.Email} actualEmail={profileEmail}");
            if (!string.Equals(profileEmail.Trim(), account.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await ApplyGmailReconnectNeededStateAsync(account.Id, clearToken: true, reason: "account-mismatch");
                LogDebug($"[EmailGmailReconnect] step=account-mismatch requestedEmail={account.Email} actualEmail={profileEmail}");
                string mismatchMsg = $"Google account mismatch. Please sign in as {account.Email}.";
                await PostAsync(new { type = "gmail-reconnect-error", accountId = account.Id, message = mismatchMsg });
                return;
            }

            GmailOAuthTokenStore.SaveTokenData(account.Id, tokenData);
            LogDebug($"[EmailGmailReconnect] step=token-saved accountId={account.Id} expiresAtUtc={expiresAtUtc:o}");

            EmailAccountSummary summary = await _gmailApi.ConnectAccountAsync(account.Id, account.Email, CancellationToken.None);
            account.DisplayName = summary.DisplayName;
            account.UnreadCount = summary.UnreadCount;
            account.Status = "connected";
            account.LastUsedUtc = DateTime.UtcNow;
            PersistAccounts();

            await PostAsync(new { type = "account-status", accountId = account.Id, status = "connected" });
            await PostSavedAccountsAsync();
            _ = TriggerUnreadRefreshBackgroundAsync("reconnect-success");

            SetApiLockedCenterOverlay(account.Id, visible: false, tokenExists: true);
            await HideApiLockedCardViaJsAsync();
            UpdateNativeOverlayInteractivity();
        }
        catch (Exception ex)
        {
            if (IsProfileMismatchOrSuspiciousFailure(ex))
            {
                await ApplyGmailReconnectNeededStateAsync(accountId, clearToken: true, reason: "profile-check-failed");
            }
            LogDebug($"[EmailGmailReconnect] step=error accountId={accountId} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EmailWebView?.CoreWebView2 != null)
            {
                EmailWebView.CoreWebView2.WebMessageReceived -= EmailWebView_WebMessageReceived;
                EmailWebView.CoreWebView2.NavigationCompleted -= EmailWebView_NavigationCompleted;
            }
        }
        catch
        {
        }

        DisposeNativeGmailView();
    }

    private async Task EnsureShellInitializedAsync()
    {
        if (EmailWebView?.CoreWebView2 != null)
        {
            return;
        }

        await EmailWebView.EnsureCoreWebView2Async();

        var settings = EmailWebView.CoreWebView2.Settings;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreDevToolsEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = true;

        EmailWebView.CoreWebView2.WebMessageReceived -= EmailWebView_WebMessageReceived;
        EmailWebView.CoreWebView2.WebMessageReceived += EmailWebView_WebMessageReceived;
        EmailWebView.CoreWebView2.NavigationCompleted -= EmailWebView_NavigationCompleted;
        EmailWebView.CoreWebView2.NavigationCompleted += EmailWebView_NavigationCompleted;

        // Fix cursor-not-allowed on disabled buttons by setting inline style (beats all CSS).
        // MutationObserver on body catches every React render/update.
        await EmailWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            "(function(){" +
            "function fix(){" +
            "  document.querySelectorAll('.cursor-not-allowed').forEach(function(el){" +
            "    el.style.setProperty('cursor','default','important');" +
            "  });" +
            "}" +
            "var ob=new MutationObserver(fix);" +
            "function start(){" +
            "  fix();" +
            "  ob.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['class']});" +
            "}" +
            "if(document.body){start();}" +
            "else{document.addEventListener('DOMContentLoaded',start);}" +
            "})();");

        string? dist = FindEmailDist();
        if (string.IsNullOrWhiteSpace(dist))
        {
            MissingUiOverlay.Visibility = Visibility.Visible;
            LogDebug("email dist not found");
            return;
        }

        MissingUiOverlay.Visibility = Visibility.Collapsed;
        EmailWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "atlas-email-ui",
            dist,
            CoreWebView2HostResourceAccessKind.Allow);

        long indexTicks = 0;
        try
        {
            string indexPath = Path.Combine(dist, "index.html");
            if (File.Exists(indexPath))
            {
                indexTicks = File.GetLastWriteTimeUtc(indexPath).Ticks;
            }
        }
        catch
        {
        }

        string version = (indexTicks != 0 ? indexTicks : DateTime.UtcNow.Ticks).ToString();
        EmailWebView.CoreWebView2.Navigate($"https://atlas-email-ui/index.html?mode=email&v={version}");
    }

    private async void EmailWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            MissingUiOverlay.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
            if (e.IsSuccess)
            {
                await PostSavedAccountsAsync();
                _ = TriggerUnreadRefreshBackgroundAsync("shell-navigation-completed");
                // Auto-restore the previously active account so the user doesn't have to re-click after restart.
                if (!string.IsNullOrWhiteSpace(_activeAccountId))
                {
                    StoredEmailAccount? active = _accounts.FirstOrDefault(
                        a => string.Equals(a.Id, _activeAccountId, StringComparison.OrdinalIgnoreCase));
                    if (active != null)
                    {
                        _ = OpenGmailNativeAsync(active, navigateIfNew: true);
                        if (string.Equals(active.Provider, "gmail", StringComparison.OrdinalIgnoreCase) && HasGmailApiToken(active))
                        {
                            _ = DebugFetchGmailInboxMessagesAsync(active.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"shell navigation completed handler failed: {ex.Message}");
        }
    }

    private async void EmailWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = document.RootElement;
            string type = GetString(root, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            string bridgeProvider = GetString(root, "provider");
            string bridgeAccountId = GetString(root, "accountId");
            string bridgeScope = GetString(root, "scope");

            if (type.StartsWith("email-agent-", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"[EmailAgentBridge] recv type={type} provider={bridgeProvider} accountId={bridgeAccountId} scope={bridgeScope}");
            }

            LogDebug($"WebMessage received type={type}");

            switch (type)
            {
                case "request-saved-accounts":
                    await PostSavedAccountsAsync();
                    break;
                case "open-account":
                case "select-account":
                    await HandleOpenOrSelectAsync(root);
                    break;
                case "remove-account":
                    await HandleRemoveAccountAsync(root);
                    break;
                case "gmail-host-bounds":
                    await HandleGmailHostBoundsAsync(root);
                    break;
                case "gmail-api-connect":
                    await HandleGmailApiConnectAsync(root);
                    break;
                case "gmail-reconnect-request":
                    {
                        string webAccountId = GetString(root, "accountId");
                        LogDebug($"[EmailGmailReconnect] step=web-message accountId={webAccountId}");
                        await ReconnectGmailApiAsync(string.IsNullOrWhiteSpace(webAccountId) ? null : webAccountId);
                        break;
                    }
                case "gmail-api-unread-count":
                    await HandleGmailApiUnreadCountAsync(root);
                    break;
                case "gmail-api-recent-messages":
                    await HandleGmailApiRecentMessagesAsync(root);
                    break;
                case "gmail-api-message-detail":
                    await HandleGmailApiMessageDetailAsync(root);
                    break;
                case "email-host-ui-state":
                    HandleEmailHostUiState(root);
                    break;
                case "active-email-mode":
                    HandleActiveEmailMode(root);
                    break;
                case "open-external-webmail":
                    await HandleOpenExternalWebmailAsync(root);
                    break;
                case "reopen-native-webmail":
                    await HandleReopenNativeWebmailAsync(root);
                    break;
                case "email-agent-command":
                    await HandleEmailAgentCommandAsync(root);
                    break;
                case "email-agent-draft-reply":
                    await HandleEmailAgentDraftReplyAsync(root);
                    break;
                case "email-agent-insert-draft":
                    await HandleEmailAgentInsertDraftAsync(root);
                    break;
                case "email-agent-triage":
                    await HandleEmailAgentTriageAsync(root);
                    break;
                case "email-agent-apply-labels":
                    await HandleEmailAgentApplyLabelsAsync(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"web message parse failed: {ex.Message}");
        }
    }

    private void HandleEmailHostUiState(JsonElement root)
    {
        bool changed = false;

        if (root.TryGetProperty("settingsOpen", out JsonElement settingsOpenProp)
            && (settingsOpenProp.ValueKind == JsonValueKind.True || settingsOpenProp.ValueKind == JsonValueKind.False))
        {
            bool settingsOpen = settingsOpenProp.GetBoolean();
            if (_settingsPanelOpen != settingsOpen)
            {
                _settingsPanelOpen = settingsOpen;
                changed = true;
            }
        }

        if (root.TryGetProperty("aiPanelOpen", out JsonElement aiPanelOpenProp)
            && (aiPanelOpenProp.ValueKind == JsonValueKind.True || aiPanelOpenProp.ValueKind == JsonValueKind.False))
        {
            bool aiPanelOpen = aiPanelOpenProp.GetBoolean();
            if (_aiPanelOpen != aiPanelOpen)
            {
                _aiPanelOpen = aiPanelOpen;
                changed = true;
            }
        }

        if (root.TryGetProperty("selectedAccountId", out JsonElement selectedAccountIdProp))
        {
            string selectedAccountId = selectedAccountIdProp.ValueKind == JsonValueKind.String
                ? selectedAccountIdProp.GetString() ?? string.Empty
                : string.Empty;

            if (!string.Equals(_reactSelectedAccountId ?? string.Empty, selectedAccountId, StringComparison.OrdinalIgnoreCase))
            {
                _reactSelectedAccountId = string.IsNullOrWhiteSpace(selectedAccountId) ? null : selectedAccountId;
                changed = true;
            }
        }

        if (root.TryGetProperty("nativeHostAllowed", out JsonElement nativeHostAllowedProp)
            && (nativeHostAllowedProp.ValueKind == JsonValueKind.True || nativeHostAllowedProp.ValueKind == JsonValueKind.False))
        {
            bool nativeHostAllowed = nativeHostAllowedProp.GetBoolean();
            if (_nativeHostAllowed != nativeHostAllowed)
            {
                _nativeHostAllowed = nativeHostAllowed;
                changed = true;
            }
        }

        if (root.TryGetProperty("connectionError", out JsonElement connectionErrorProp)
            && (connectionErrorProp.ValueKind == JsonValueKind.True || connectionErrorProp.ValueKind == JsonValueKind.False))
        {
            bool connectionError = connectionErrorProp.GetBoolean();
            if (_reactConnectionError != connectionError)
            {
                _reactConnectionError = connectionError;
                changed = true;
            }
        }

        if (changed)
        {
            LogDebug($"email-host-ui-state settingsOpen={_settingsPanelOpen} aiPanelOpen={_aiPanelOpen} selectedAccountId={_reactSelectedAccountId} nativeHostAllowed={_nativeHostAllowed} connectionError={_reactConnectionError}");
        }

        LogNativeOverlayGate("ui-state");

        UpdateNativeOverlayInteractivity();
    }

    private void HandleActiveEmailMode(JsonElement root)
    {
        string mode = GetString(root, "mode");
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        if (!string.Equals(_activeViewMode, mode, StringComparison.OrdinalIgnoreCase))
        {
            _activeViewMode = mode;
            LogDebug($"active-email-mode mode={_activeViewMode}");
        }

        LogNativeOverlayGate("active-mode");

        UpdateNativeOverlayInteractivity();
    }

    private bool ShouldShowNativeOverlay()
    {
        if (!string.IsNullOrWhiteSpace(_nativeViewAccountId)
            && _apiInboxActiveAccounts.Contains(_nativeViewAccountId))
        {
            return false;
        }

        return string.Equals(_activeViewMode, "webview", StringComparison.OrdinalIgnoreCase)
               && !_settingsPanelOpen
               && _nativeHostAllowed
               && !_reactConnectionError
               && !string.IsNullOrWhiteSpace(_nativeViewAccountId)
               && (string.IsNullOrWhiteSpace(_reactSelectedAccountId)
                   || string.Equals(_nativeViewAccountId, _reactSelectedAccountId, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldShowApiLockedCenterOverlay(string accountId, out bool tokenExists)
    {
        tokenExists = false;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (account == null || !string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Don't show the API-locked overlay for accounts that are actively being set up —
        // the user needs to see the Google sign-in page in the WebView2 to authenticate.
        if (string.Equals(account.Status, "setup-pending", StringComparison.OrdinalIgnoreCase))
        {
            tokenExists = false;
            return false;
        }

        tokenExists = HasGmailApiToken(account);
        return !tokenExists;
    }

    private bool ShouldKeepApiLockedCenterOverlayVisible(string accountId, out bool tokenExists)
    {
        if (!ShouldShowApiLockedCenterOverlay(accountId, out tokenExists))
        {
            return false;
        }

        if (!string.Equals(_activeViewMode, "webview", StringComparison.OrdinalIgnoreCase) || _settingsPanelOpen)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_reactSelectedAccountId))
        {
            return false;
        }

        return string.Equals(accountId, _reactSelectedAccountId, StringComparison.OrdinalIgnoreCase);
    }

    private void SetApiLockedCenterOverlay(string accountId, bool visible, bool tokenExists, HostBounds? bounds = null, string? reason = null)
    {
        try
        {
            if (ApiLockedCenterOverlay == null)
            {
                return;
            }

            if (visible)
            {
                HostBounds? effectiveBounds = bounds;
                if (effectiveBounds.HasValue && effectiveBounds.Value.Width > 4 && effectiveBounds.Value.Height > 4)
                {
                    _lastApiLockedCenterBounds = effectiveBounds;
                    _lastApiLockedCenterAccountId = accountId;
                }
                else if (_lastApiLockedCenterBounds.HasValue
                         && string.Equals(_lastApiLockedCenterAccountId, accountId, StringComparison.OrdinalIgnoreCase))
                {
                    effectiveBounds = _lastApiLockedCenterBounds;
                    reason = string.IsNullOrWhiteSpace(reason) ? "last-valid-bounds" : reason;
                }

                if (!effectiveBounds.HasValue || effectiveBounds.Value.Width <= 4 || effectiveBounds.Value.Height <= 4)
                {
                    ApiLockedCenterOverlay.Visibility = Visibility.Collapsed;
                    LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=False bounds=0.0x0.0");
                    return;
                }

                HostBounds b = effectiveBounds.Value;
                Canvas.SetLeft(ApiLockedCenterOverlay, b.Left);
                Canvas.SetTop(ApiLockedCenterOverlay, b.Top);
                ApiLockedCenterOverlay.Width = b.Width;
                ApiLockedCenterOverlay.Height = b.Height;
                ApiLockedCenterOverlay.Visibility = Visibility.Visible;
                Panel.SetZIndex(ApiLockedCenterOverlay, 40);
                NativeOverlayLayer.Visibility = Visibility.Visible;
                NativeOverlayLayer.IsHitTestVisible = false;

                if (string.IsNullOrWhiteSpace(reason))
                {
                    LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=True bounds={b.Width:F1}x{b.Height:F1}");
                }
                else
                {
                    LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=True reason={reason} bounds={b.Width:F1}x{b.Height:F1}");
                }
                return;
            }

            ApiLockedCenterOverlay.Visibility = Visibility.Collapsed;
            _nativeLockedCardActive = false;
            _ = HideApiLockedCardViaJsAsync();
            if (string.Equals(_lastApiLockedCenterAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                _lastApiLockedCenterBounds = null;
                _lastApiLockedCenterAccountId = null;
            }
            if (bounds.HasValue)
            {
                HostBounds b = bounds.Value;
                LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=False bounds={b.Width:F1}x{b.Height:F1}");
            }
            else
            {
                LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=False bounds=n/a");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=False bounds=n/a error={ex.Message}");
        }
    }

    // Injects a DOM overlay into EmailWebView (the React shell) to display the locked card.
    // Using the same WebView2 HWND as the shell guarantees Z-order — no Win32 HWND competition.
    private void ShowApiLockedNativeCard(string accountId, bool tokenExists, HostBounds? bounds)
    {
        HostBounds? effectiveBounds = bounds;
        if (effectiveBounds.HasValue && effectiveBounds.Value.Width > 4 && effectiveBounds.Value.Height > 4)
        {
            _lastApiLockedCenterBounds = effectiveBounds;
            _lastApiLockedCenterAccountId = accountId;
        }
        else if (_lastApiLockedCenterBounds.HasValue
                 && string.Equals(_lastApiLockedCenterAccountId, accountId, StringComparison.OrdinalIgnoreCase))
        {
            effectiveBounds = _lastApiLockedCenterBounds;
        }

        if (!effectiveBounds.HasValue || effectiveBounds.Value.Width <= 4 || effectiveBounds.Value.Height <= 4)
        {
            LogDebug($"[EmailApiLockedCenter] ShowApiLockedNativeCard: no valid bounds, skipping accountId={accountId}");
            return;
        }

        HostBounds b = effectiveBounds.Value;
        _ = ShowApiLockedCardViaJsAsync(accountId, tokenExists, b);

        // Collapse native Gmail view and native overlay layer — the JS card is inside EmailWebView.
        if (_nativeGmailView != null) _nativeGmailView.Visibility = Visibility.Collapsed;
        NativeOverlayLayer.Visibility = Visibility.Collapsed;
        NativeOverlayLayer.IsHitTestVisible = false;
        ApiLockedCenterOverlay.Visibility = Visibility.Collapsed;

        LogDebug($"[EmailApiLockedCenter] accountId={accountId} tokenExists={tokenExists} visible=True js-inject bounds={b.Width:F1}x{b.Height:F1}");
    }

    private async Task ShowApiLockedCardViaJsAsync(string accountId, bool tokenExists, HostBounds b)
    {
        try
        {
            if (EmailWebView?.CoreWebView2 == null) return;

            string js = $$$"""
                (function() {{
                  const id = 'atlas-api-locked-overlay';
                  let el = document.getElementById(id);
                  if (!el) {{
                    el = document.createElement('div');
                    el.id = id;
                    document.body.appendChild(el);
                  }}
                  Object.assign(el.style, {{
                    position: 'fixed',
                    left: '{{{b.Left}}}px',
                    top: '{{{b.Top}}}px',
                    width: '{{{b.Width}}}px',
                    height: '{{{b.Height}}}px',
                    zIndex: '2147483647',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    background: '#0A0A0F',
                    fontFamily: '-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif',
                    color: '#F4F8FF',
                    overflow: 'hidden',
                    pointerEvents: 'all',
                  }});
                  el.innerHTML = `
                    <div style="background:#141A24;border:1px solid rgba(57,87,217,0.5);border-radius:20px;padding:32px;max-width:520px;width:90%;">
                      <div style="height:4px;border-radius:2px;background:linear-gradient(90deg,#669CFF,#7A5DFF);margin-bottom:20px;"></div>
                      <div style="font-size:26px;font-weight:600;margin-bottom:12px;">Gmail API not connected</div>
                      <div style="font-size:14px;color:#C7D3E8;line-height:1.6;margin-bottom:20px;">This account has a webmail session, but Atlas does not have a Gmail API token yet. Reconnect Gmail API to enable live inbox, unread counts, summaries, and AI actions.</div>
                      <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:20px;">
                        <span style="background:#1D2A42;border:1px solid #3B86FF;border-radius:999px;padding:4px 12px;font-size:12px;color:#A9D5FF;">Web session: connected</span>
                        <span style="background:#221A42;border:1px solid #7A5DFF;border-radius:999px;padding:4px 12px;font-size:12px;color:#C9B8FF;">AI token: missing</span>
                        <span style="background:#221A42;border:1px solid #7A5DFF;border-radius:999px;padding:4px 12px;font-size:12px;color:#C9B8FF;">Unread sync: off</span>
                      </div>
                      <button onclick="window.chrome.webview.postMessage(JSON.stringify({type:'gmail-reconnect-request',accountId:'{{{accountId}}}'}));this.disabled=true;this.textContent='Connecting...';" style="background:linear-gradient(135deg,#3957D9,#7A5DFF);border:none;border-radius:10px;color:#fff;font-size:14px;padding:10px 20px;cursor:pointer;font-weight:600;">Reconnect Gmail API</button>
                    </div>`;
                }})();
                """;

            await EmailWebView.CoreWebView2.ExecuteScriptAsync(js);
            LogDebug($"[EmailApiLockedCenter] js-inject done accountId={accountId}");
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailApiLockedCenter] js-inject failed accountId={accountId}: {ex.Message}");
        }
    }

    private async Task HideApiLockedCardViaJsAsync()
    {
        try
        {
            if (EmailWebView?.CoreWebView2 == null) return;
            await EmailWebView.CoreWebView2.ExecuteScriptAsync(
                "var el=document.getElementById('atlas-api-locked-overlay'); if(el) el.remove();");
            LogDebug("[EmailApiLockedCenter] js-inject overlay removed");
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailApiLockedCenter] js-inject remove failed: {ex.Message}");
        }
    }

    private void UpdateNativeOverlayInteractivity()
    {
        try
        {
            if (_nativeGmailView == null || string.IsNullOrWhiteSpace(_nativeViewAccountId))
            {
                SetApiLockedCenterOverlay(_nativeViewAccountId ?? string.Empty, visible: false, tokenExists: false);
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                return;
            }

            if (_apiInboxActiveAccounts.Contains(_nativeViewAccountId))
            {
                _nativeGmailView.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                SetApiLockedCenterOverlay(_nativeViewAccountId, visible: false, tokenExists: false);
                LogDebug($"[EmailApiInboxMode] accountId={_nativeViewAccountId} nativeOverlaySuppressed=True reason=inbox-messages-posted");
                return;
            }

            if (ShouldKeepApiLockedCenterOverlayVisible(_nativeViewAccountId, out bool tokenExists))
            {
                ShowApiLockedNativeCard(_nativeViewAccountId, tokenExists, null);
                return;
            }

            if (!ShouldShowNativeOverlay())
            {
                SetApiLockedCenterOverlay(_nativeViewAccountId, visible: false, tokenExists: false);
                _nativeGmailView.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                return;
            }

            if (_boundsByAccount.TryGetValue(_nativeViewAccountId, out HostBounds bounds))
            {
                ApplyNativeBounds(_nativeViewAccountId, bounds);
            }
            else
            {
                // No known bounds yet; keep input disabled until React posts host bounds.
                SetApiLockedCenterOverlay(_nativeViewAccountId, visible: false, tokenExists: false);
                _nativeGmailView.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"UpdateNativeOverlayInteractivity failed: {ex.Message}");
        }
    }

    private async Task HandleOpenOrSelectAsync(JsonElement root)
    {
        string messageType = GetString(root, "type");
        bool isOpenAccount = string.Equals(messageType, "open-account", StringComparison.OrdinalIgnoreCase);
        string provider = GetString(root, "provider");
        string email = (GetString(root, "email") ?? string.Empty).Trim();
        string accountId = ComputeStableAccountId(provider, email, GetString(root, "accountId"));
        bool isGmail = string.Equals(provider, "gmail", StringComparison.OrdinalIgnoreCase);
        bool isOutlook = string.Equals(provider, "outlook", StringComparison.OrdinalIgnoreCase);
        if (!isGmail && !isOutlook)
        {
            _apiInboxActiveAccounts.Remove(accountId);
            await PostAsync(new { type = "account-status", accountId, status = "error" });
            return;
        }

        if (!IsValidEmailAddress(email))
        {
            LogDebug($"[EmailAddAccountValidation] provider={provider} result=rejected reason=invalid-email inputLength={email.Length}");
            await PostAsync(new
            {
                type = "account-status",
                accountId,
                status = "error",
                message = "Enter a full email address, for example name@outlook.com.",
            });
            return;
        }

        bool profileExists = Directory.Exists(GetAccountProfileDirectory(accountId));
        bool tokenExists = isGmail
            ? GmailOAuthTokenStore.Load(accountId) != null
            : isOutlook && OutlookOAuthTokenStore.Load(accountId) != null;

        if (IsVerifierIdentity(provider, email, accountId) && !profileExists && !tokenExists)
        {
            LogDebug($"[EmailAddAccountValidation] provider={provider} result=rejected reason=verifier-identity accountId={accountId} email={email}");
            await PostAsync(new
            {
                type = "account-status",
                accountId,
                status = "error",
                message = "This account is invalid for production mode. Add a real inbox account.",
            });
            return;
        }
        bool setupPending = isOpenAccount && !profileExists && !tokenExists;

        LogDebug($"select-account received type={messageType} provider={provider} email={email} accountId={accountId} profileExists={profileExists} tokenExists={tokenExists} setupPending={setupPending}");
        var account = UpsertAccount(accountId, provider, email);
        account.Status = setupPending ? "setup-pending" : "loading";
        account.LastUsedUtc = DateTime.UtcNow;
        _activeAccountId = accountId;
        PersistAccounts();

        await PostAsync(new { type = "account-status", accountId, status = account.Status });
        await PostSavedAccountsAsync();

        if (string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase) && HasGmailApiToken(account))
        {
            _gmailMessagesDebugFetched.Remove(account.Id);
            LogDebug($"[EmailGmailMessagesUi] accountId={account.Id} step=fetch-guard-cleared reason=manual-select");
            _ = DebugFetchGmailInboxMessagesAsync(account.Id);
        }

        if (string.Equals(account.Provider, "outlook", StringComparison.OrdinalIgnoreCase) && HasProviderApiToken(account))
        {
            _ = DebugFetchOutlookInboxMessagesAsync(account.Id);
        }

        await OpenGmailNativeAsync(account, navigateIfNew: true);
    }

    private async Task DebugFetchGmailInboxMessagesAsync(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        if (!_gmailMessagesDebugFetched.Add(accountId))
        {
            return;
        }

        // Mark API inbox mode immediately (before any await) so that NavigationCompleted,
        // which fires concurrently, finds the account already in the active set and suppresses
        // native-gmail-mounted false posts before the fetch completes.
        _apiInboxActiveAccounts.Add(accountId);
        LogDebug($"[EmailApiInboxMode] accountId={accountId} step=api-inbox-mode-preemptive-set");

        try
        {
            IReadOnlyList<GmailEmailMessageSummary> messages = await _gmailApi.GetInboxMessagesAsync(accountId, 25, CancellationToken.None);
            LogDebug($"[EmailGmailMessagesUi] accountId={accountId} count={messages.Count}");

            int maxLog = Math.Min(5, messages.Count);
            for (int i = 0; i < maxLog; i++)
            {
                GmailEmailMessageSummary msg = messages[i];
                int fromLength = msg.From?.Length ?? 0;
                int subjectLength = msg.Subject?.Length ?? 0;
                int snippetLength = msg.Snippet?.Length ?? 0;
                string date = msg.InternalDateUtc?.ToString("o") ?? string.Empty;

                LogDebug($"[EmailGmailMessagesUi] index={i} id={msg.Id} unread={msg.IsUnread} fromLength={fromLength} subjectLength={subjectLength} snippetLength={snippetLength} date={date}");
            }

            var payload = messages.Take(25).Select(msg => new
            {
                id = msg.Id,
                threadId = msg.ThreadId,
                from = msg.From,
                subject = msg.Subject,
                snippet = msg.Snippet,
                internalDateUtc = msg.InternalDateUtc?.ToString("o"),
                isUnread = msg.IsUnread,
            }).ToList();

            _emailAgentInboxCache[accountId] = messages
                .Take(100)
                .Select(msg => new EmailAgentMessageMeta
                {
                    Id = msg.Id,
                    ThreadId = msg.ThreadId,
                    From = msg.From,
                    Subject = msg.Subject,
                    Snippet = msg.Snippet,
                    Date = msg.Date,
                    IsUnread = msg.IsUnread,
                    Provider = "gmail",
                    Labels = msg.LabelIds?.ToList() ?? new List<string>(),
                })
                .ToList();

            await PostAsync(new { type = "gmail-inbox-messages", accountId, messages = payload });
            _apiInboxActiveAccounts.Add(accountId);
            LogDebug($"[EmailApiInboxMode] accountId={accountId} nativeOverlaySuppressed=True reason=inbox-messages-posted");
            StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
                string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (account != null && string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
            {
                int freshUnread = messages.Count(msg => msg.IsUnread);

                account.UnreadCount = freshUnread;
                account.Status = "connected";
                account.LastUsedUtc = DateTime.UtcNow;
                PersistAccounts();
                await PostAsync(new { type = "account-status", accountId, status = "connected" });
                await PostSavedAccountsAsync();
                LogDebug($"[EmailGmailMessagesUi] accountId={accountId} step=api-inbox-unread-updated unread={freshUnread}");
                LogDebug($"[EmailGmailMessagesUi] accountId={accountId} step=api-inbox-connected status=connected");
            }

            UpdateNativeOverlayInteractivity();
            LogDebug($"[EmailGmailMessagesUi] accountId={accountId} posted gmail-inbox-messages count={payload.Count}");
        }
        catch (Exception ex)
        {
            if (IsGmailApiUnauthorized(ex))
            {
                _gmailMessagesDebugFetched.Remove(accountId);
                GmailOAuthTokenStore.Delete(accountId);

                StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
                    string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
                if (account != null)
                {
                    account.UnreadCount = 0;
                    if (string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    {
                        account.Status = "connected";
                    }
                }

                PersistAccounts();
                await PostSavedAccountsAsync();

                _reactConnectionError = true;
                if (_boundsByAccount.TryGetValue(accountId, out HostBounds bounds))
                {
                    SetApiLockedCenterOverlay(accountId, visible: true, tokenExists: false, bounds, reason: "gmail-api-401");
                }
                else
                {
                    SetApiLockedCenterOverlay(accountId, visible: true, tokenExists: false, reason: "gmail-api-401-no-bounds");
                }

                if (_nativeGmailView != null)
                {
                    _nativeGmailView.Visibility = Visibility.Collapsed;
                }

                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;

                LogDebug($"[EmailGmailMessagesUi] accountId={accountId} step=api-401-token-cleared reconnectNeeded=True");
                UpdateNativeOverlayInteractivity();
                return;
            }

            LogDebug($"[EmailGmailMessagesUi] accountId={accountId} error type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private async Task DebugFetchOutlookInboxMessagesAsync(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));

        if (account == null || !string.Equals(account.Provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!HasProviderApiToken(account))
        {
            LogDebug($"[EmailOutlookGraph] step=inbox-fetch-skip-no-token accountId={accountId}");
            return;
        }

        _apiInboxActiveAccounts.Add(accountId);
        LogDebug($"[EmailApiInboxMode] accountId={accountId} step=api-inbox-mode-preemptive-set provider=outlook");

        try
        {
            LogDebug($"[EmailOutlookGraph] step=inbox-fetch-start accountId={accountId}");
            IReadOnlyList<GmailEmailMessageSummary> messages = await _outlookApi.GetInboxMessagesAsync(
                accountId,
                account.Email,
                25,
                CancellationToken.None);

            LogDebug($"[EmailOutlookGraph] step=inbox-fetch-success accountId={accountId} count={messages.Count}");
            LogDebug($"[EmailOutlookMessagesUi] accountId={accountId} count={messages.Count}");

            var payload = messages.Take(25).Select(msg => new
            {
                id = msg.Id,
                threadId = msg.ThreadId,
                from = msg.From,
                subject = msg.Subject,
                snippet = msg.Snippet,
                internalDateUtc = msg.InternalDateUtc?.ToString("o"),
                isUnread = msg.IsUnread,
            }).ToList();

            _emailAgentInboxCache[accountId] = messages
                .Take(100)
                .Select(msg => new EmailAgentMessageMeta
                {
                    Id = msg.Id,
                    ThreadId = msg.ThreadId,
                    From = msg.From,
                    Subject = msg.Subject,
                    Snippet = msg.Snippet,
                    Date = msg.Date,
                    IsUnread = msg.IsUnread,
                    Provider = "outlook",
                })
                .ToList();

            await PostAsync(new { type = "gmail-inbox-messages", accountId, messages = payload });

            int freshUnread;
            try
            {
                freshUnread = await _outlookApi.GetUnreadCountAsync(accountId, CancellationToken.None);
            }
            catch
            {
                freshUnread = messages.Count(msg => msg.IsUnread);
            }
            account.UnreadCount = freshUnread;
            account.Status = "connected";
            account.LastUsedUtc = DateTime.UtcNow;

            PersistAccounts();
            await PostAsync(new { type = "account-status", accountId, status = "connected" });
            await PostSavedAccountsAsync();

            UpdateNativeOverlayInteractivity();
            LogDebug($"[EmailOutlookMessagesUi] posted outlook-inbox-messages count={payload.Count}");
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailOutlookGraph] step=inbox-fetch-error accountId={accountId} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private static bool IsGmailApiUnauthorized(Exception ex)
    {
        if (ex == null)
        {
            return false;
        }

        string message = ex.ToString();
        return message.Contains("Gmail API error 401", StringComparison.OrdinalIgnoreCase)
               || message.Contains("\"code\": 401", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authError", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileMismatchOrSuspiciousFailure(Exception ex)
    {
        if (ex == null)
        {
            return false;
        }

        string message = ex.ToString();
        return message.Contains("Gmail token profile mismatch", StringComparison.OrdinalIgnoreCase)
               || message.Contains("profile_fetch_failed", StringComparison.OrdinalIgnoreCase)
               || message.Contains("profile_email_missing", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Gmail profile error", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyGmailReconnectNeededStateAsync(string accountId, bool clearToken, string reason)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        _gmailMessagesDebugFetched.Remove(accountId);
        _apiInboxActiveAccounts.Remove(accountId);
        if (clearToken)
        {
            GmailOAuthTokenStore.Delete(accountId);
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (account != null)
        {
            account.UnreadCount = 0;
            if (string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.Status, "connected", StringComparison.OrdinalIgnoreCase))
            {
                account.Status = "connected";
            }
        }

        PersistAccounts();
        await PostSavedAccountsAsync();

        _reactConnectionError = true;
        if (_boundsByAccount.TryGetValue(accountId, out HostBounds bounds))
        {
            SetApiLockedCenterOverlay(accountId, visible: true, tokenExists: false, bounds, reason: reason);
        }
        else
        {
            SetApiLockedCenterOverlay(accountId, visible: true, tokenExists: false, reason: reason + "-no-bounds");
        }

        if (_nativeGmailView != null)
        {
            _nativeGmailView.Visibility = Visibility.Collapsed;
        }

        NativeOverlayLayer.Visibility = Visibility.Collapsed;
        NativeOverlayLayer.IsHitTestVisible = false;
        UpdateNativeOverlayInteractivity();
    }

    private async Task HandleRemoveAccountAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        bool deleteSession = GetBool(root, "deleteSession");
        _accounts.RemoveAll(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        _apiInboxActiveAccounts.Remove(accountId);
        // Remove persisted API token for the account.
        GmailOAuthTokenStore.Delete(accountId);
        PersistAccounts();

        if (string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase))
        {
            DisposeNativeGmailView();
            _activeAccountId = null;
        }

        if (deleteSession)
        {
            try
            {
                string profileDir = GetAccountProfileDirectory(accountId);
                if (Directory.Exists(profileDir))
                {
                    Directory.Delete(profileDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"delete session failed accountId={accountId}: {ex.Message}");
            }
        }

        await PostSavedAccountsAsync();
    }

    private async Task HandleGmailHostBoundsAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = GetString(root, "id");
        }

        double left = GetDouble(root, "left");
        double top = GetDouble(root, "top");
        double width = GetDouble(root, "width");
        double height = GetDouble(root, "height");

        LogNativeOverlayGate("bounds-received", width, height);

        if (string.IsNullOrWhiteSpace(accountId))
        {
            await PostAsync(new { type = "native-gmail-mounted", accountId = string.Empty, mounted = false, error = "Missing account id" });
            return;
        }

        if (!ShouldShowNativeOverlay())
        {
            if (ShouldKeepApiLockedCenterOverlayVisible(accountId, out bool tokenExists))
            {
                ShowApiLockedNativeCard(accountId, tokenExists, new HostBounds(left, top, width, height));
                LogDebug($"gmail-host-bounds retained api locked overlay accountId={accountId} mode={_activeViewMode} settingsOpen={_settingsPanelOpen} nativeHostAllowed={_nativeHostAllowed} selectedAccountId={_reactSelectedAccountId}");
                return;
            }

            SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: false);
            if (_nativeGmailView != null)
            {
                _nativeGmailView.Visibility = Visibility.Collapsed;
            }
            NativeOverlayLayer.Visibility = Visibility.Collapsed;
            NativeOverlayLayer.IsHitTestVisible = false;
            LogDebug($"gmail-host-bounds ignored accountId={accountId} mode={_activeViewMode} settingsOpen={_settingsPanelOpen} nativeHostAllowed={_nativeHostAllowed} selectedAccountId={_reactSelectedAccountId}");
            return;
        }

        LogDebug($"React posted gmail-host-bounds accountId={accountId} left={left:F1} top={top:F1} width={width:F1} height={height:F1}");
        LogDebug($"WPF received gmail-host-bounds accountId={accountId} left={left:F1} top={top:F1} width={width:F1} height={height:F1}");

        // Zero/tiny bounds indicate the host region is not currently valid.
        // Clear stale cached bounds so old geometry cannot be reapplied.
        if (width <= 4 || height <= 4)
        {
            _boundsByAccount.Remove(accountId);

            if (string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                if (ShouldKeepApiLockedCenterOverlayVisible(accountId, out bool tokenExists))
                {
                    ShowApiLockedNativeCard(accountId, tokenExists, null);
                    LogDebug($"gmail-host-bounds invalid accountId={accountId} w={width:F1} h={height:F1} – kept api locked overlay using last valid bounds");
                    return;
                }

                SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: false);
                if (_nativeGmailView != null)
                {
                    _nativeGmailView.Visibility = Visibility.Collapsed;
                }

                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
            }

            LogDebug($"gmail-host-bounds invalid accountId={accountId} w={width:F1} h={height:F1} – cleared cached bounds and collapsed native overlay");
            return;
        }

        var bounds = new HostBounds(left, top, width, height);
        _boundsByAccount[accountId] = bounds;
        LogNativeOverlayGate("before-apply-bounds", width, height);
        ApplyNativeBounds(accountId, bounds);
    }

    private async Task HandleGmailApiConnectAsync(JsonElement root)
    {
        string requestId = GetString(root, "requestId");
        string accountId = GetString(root, "accountId");
        string email = GetString(root, "email");

        StoredEmailAccount? existingAccount = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (existingAccount != null && string.Equals(existingAccount.Provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            if (HasProviderApiToken(existingAccount))
            {
                LogDebug($"[EmailOutlookGraph] step=inbox-fetch-refresh accountId={existingAccount.Id}");
                await PostAsync(new
                {
                    type = "gmail-api-connected",
                    requestId,
                    accountId = existingAccount.Id,
                    connected = true,
                    email = existingAccount.Email,
                    displayName = existingAccount.DisplayName,
                    unreadCount = existingAccount.UnreadCount,
                });

                _ = DebugFetchOutlookInboxMessagesAsync(existingAccount.Id);
                return;
            }

            await HandleOutlookApiConnectAsync(requestId, accountId, email);
            return;
        }

        LogDebug($"[EmailGmailConnect] step=start requestId={requestId} accountId={accountId} email={email}");

        try
        {
            LogDebug($"gmail-api-connect received requestId={requestId} accountId={accountId} email={email}");
            string resolvedOAuthPath = "(resolving...)";
            try { resolvedOAuthPath = GmailApiEmailProviderService.ResolveOAuthClientSecretsPathPublic(); } catch (Exception pathEx) { resolvedOAuthPath = $"FAILED: {pathEx.Message}"; }
            LogDebug($"OAuth JSON resolved path: {resolvedOAuthPath}");
            LogDebug($"Gmail readonly scope requested: https://www.googleapis.com/auth/gmail.readonly");
            LogDebug($"[EmailGmailConnect] step=before-connect requestId={requestId} accountId={accountId} email={email}");
            EmailAccountSummary summary = await _gmailApi.ConnectAccountAsync(accountId, email, CancellationToken.None);
            LogDebug($"[EmailGmailConnect] step=after-connect requestId={requestId} accountId={summary.AccountId} email={summary.Email} unread={summary.UnreadCount}");
            var account = UpsertAccount(summary.AccountId, "gmail", summary.Email);
            account.DisplayName = summary.DisplayName;
            LogDebug($"[EmailGmailConnect] step=update-account-before requestId={requestId} accountId={summary.AccountId} email={summary.Email} unread={summary.UnreadCount}");
            account.UnreadCount = summary.UnreadCount;
            LogDebug($"[EmailGmailConnect] step=update-account-after requestId={requestId} accountId={summary.AccountId} email={summary.Email} unread={summary.UnreadCount}");
            account.Status = "connected";
            account.LastUsedUtc = DateTime.UtcNow;
            PersistAccounts();

            _gmailMessagesDebugFetched.Remove(summary.AccountId);
            LogDebug($"[EmailGmailMessagesUi] accountId={summary.AccountId} step=fetch-guard-cleared reason=refresh");
            if (HasGmailApiToken(account))
            {
                _ = DebugFetchGmailInboxMessagesAsync(summary.AccountId);
            }

            LogDebug($"[EmailGmailConnect] step=post-success requestId={requestId} accountId={summary.AccountId} email={summary.Email} unread={summary.UnreadCount}");
            await PostAsync(new
            {
                type = "gmail-api-connected",
                requestId,
                accountId = summary.AccountId,
                connected = summary.Connected,
                email = summary.Email,
                displayName = summary.DisplayName,
                unreadCount = summary.UnreadCount,
            });
            await PostAsync(new { type = "account-status", accountId = summary.AccountId, status = "connected" });
            await PostSavedAccountsAsync();
            _ = TriggerUnreadRefreshBackgroundAsync("gmail-connect-success");
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailGmailConnect] step=error requestId={requestId} accountId={accountId} email={email} type={ex.GetType().FullName} message={ex.Message}");
            await PostAsync(new
            {
                type = "gmail-api-connected",
                requestId,
                accountId,
                connected = false,
                error = ex.Message,
            });
        }
    }

    private async Task HandleOutlookApiConnectAsync(string requestId, string accountId, string email)
    {
        LogDebug($"[EmailOutlookConnect] step=start requestId={requestId} accountId={accountId} email={email}");

        try
        {
            StoredEmailAccount account = _accounts.FirstOrDefault(a =>
                    string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase))
                ?? UpsertAccount(accountId, "outlook", email);

            if (!string.Equals(account.Provider, "outlook", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Selected account is not an Outlook account.");
            }

            string resolvedOAuthPath = "(resolving...)";
            try { resolvedOAuthPath = OutlookGraphEmailProviderService.ResolveOAuthClientSecretsPathPublic(); } catch (Exception pathEx) { resolvedOAuthPath = $"FAILED: {pathEx.Message}"; }
            LogDebug($"[EmailOutlookConnect] oauthPath={resolvedOAuthPath}");

            var flow = new OutlookOAuthFlow();
            OutlookOAuthSession session = flow.CreateAuthorizationSession(account.Id);

            Process.Start(new ProcessStartInfo
            {
                FileName = session.AuthorizationUrl,
                UseShellExecute = true,
            });

            string code = await flow.WaitForCallbackAsync(session, TimeSpan.FromMinutes(3), CancellationToken.None);
            OutlookOAuthTokenResponse tokenResponse = await flow.ExchangeCodeAsync(session, code, CancellationToken.None);

            DateTime expiresAtUtc = tokenResponse.ExpiresIn > 0
                ? DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                : DateTime.UtcNow.AddHours(1);

            OutlookProfileData profile = await flow.FetchProfileAsync(tokenResponse.AccessToken, CancellationToken.None);
            LogDebug($"[EmailOutlookConnect] step=profile accountId={account.Id} requestedEmail={account.Email} actualEmail={profile.Email}");

            if (!string.IsNullOrWhiteSpace(account.Email)
                && !string.Equals(profile.Email.Trim(), account.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                OutlookOAuthTokenStore.Delete(account.Id);
                throw new InvalidOperationException($"Microsoft account mismatch. Please sign in as {account.Email}.");
            }

            var tokenData = new OutlookTokenData
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAtUtc = expiresAtUtc,
                Scope = tokenResponse.Scope,
                TokenType = tokenResponse.TokenType,
            };

            OutlookOAuthTokenStore.SaveTokenData(account.Id, tokenData);
            LogDebug($"[EmailOutlookConnect] step=token-saved accountId={account.Id} expiresAtUtc={expiresAtUtc:o}");

            EmailAccountSummary summary = await _outlookApi.ConnectAccountAsync(account.Id, profile.Email, CancellationToken.None);
            account.Email = summary.Email;
            account.DisplayName = summary.DisplayName;
            account.UnreadCount = summary.UnreadCount;
            account.Status = "connected";
            account.LastUsedUtc = DateTime.UtcNow;
            PersistAccounts();

            await PostAsync(new
            {
                type = "gmail-api-connected",
                requestId,
                accountId = summary.AccountId,
                connected = summary.Connected,
                email = summary.Email,
                displayName = summary.DisplayName,
                unreadCount = summary.UnreadCount,
            });
            await PostAsync(new { type = "account-status", accountId = summary.AccountId, status = "connected" });
            await PostSavedAccountsAsync();
            _ = DebugFetchOutlookInboxMessagesAsync(summary.AccountId);
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailOutlookConnect] step=error requestId={requestId} accountId={accountId} email={email} type={ex.GetType().FullName} message={ex.Message}");
            await PostAsync(new
            {
                type = "gmail-api-connected",
                requestId,
                accountId,
                connected = false,
                error = ex.Message,
            });
        }
    }

    private async Task HandleGmailApiUnreadCountAsync(JsonElement root)
    {
        string requestId = GetString(root, "requestId");
        string accountId = GetString(root, "accountId");
        string email = GetString(root, "email");

        try
        {
            int unreadCount = await _gmailApi.GetUnreadCountAsync(accountId, email, CancellationToken.None);
            StoredEmailAccount? account = _accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (account != null)
            {
                account.UnreadCount = unreadCount;
                PersistAccounts();
            }

            await PostAsync(new
            {
                type = "gmail-api-unread-count",
                requestId,
                accountId,
                unreadCount,
            });
        }
        catch (Exception ex)
        {
            await PostAsync(new
            {
                type = "gmail-api-unread-count",
                requestId,
                accountId,
                error = ex.Message,
            });
        }
    }

    private async Task TriggerUnreadRefreshBackgroundAsync(string reason)
    {
        try
        {
            LogDebug($"[EmailUnreadRefresh] trigger reason={reason}");
            await Task.Run(() => RefreshUnreadCountsForConnectedGmailAccountsAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailUnreadRefresh] triggerFailed reason={reason} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private async Task RefreshUnreadCountsForConnectedGmailAccountsAsync(CancellationToken ct)
    {
        await _unreadRefreshLock.WaitAsync(ct);
        try
        {
            List<StoredEmailAccount> gmailAccounts = _accounts
                .Where(a => string.Equals(a.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int changedCount = 0;
            bool anyChanged = false;

            foreach (StoredEmailAccount account in gmailAccounts)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (!HasGmailApiToken(account))
                {
                    continue;
                }

                try
                {
                    int unread = await _gmailApi.GetUnreadCountAsync(account.Id, account.Email, ct);
                    LogDebug($"[EmailUnreadRefresh] accountId={account.Id} unread={unread}");

                    if (account.UnreadCount != unread)
                    {
                        account.UnreadCount = unread;
                        anyChanged = true;
                        changedCount++;
                    }
                }
                catch (Exception ex)
                {
                    if (IsGmailApiUnauthorized(ex))
                    {
                        GmailOAuthTokenStore.Delete(account.Id);

                        bool wasChanged = false;
                        if (account.UnreadCount != 0)
                        {
                            account.UnreadCount = 0;
                            wasChanged = true;
                        }

                        bool hasNativeSession = Directory.Exists(GetAccountProfileDirectory(account.Id))
                            || string.Equals(_nativeViewAccountId, account.Id, StringComparison.OrdinalIgnoreCase);
                        if (hasNativeSession && !string.Equals(account.Status, "connected", StringComparison.OrdinalIgnoreCase))
                        {
                            account.Status = "connected";
                            wasChanged = true;
                        }

                        if (wasChanged)
                        {
                            anyChanged = true;
                            changedCount++;
                        }

                        LogDebug($"[EmailUnreadRefresh] accountId={account.Id} tokenInvalid=True");
                        continue;
                    }

                    LogDebug($"[EmailUnreadRefresh] accountId={account.Id} errorType={ex.GetType().Name} message={ex.Message}");
                }
            }

            if (anyChanged)
            {
                PersistAccounts();
                await PostSavedAccountsAsync();
            }

            LogDebug($"[EmailUnreadRefresh] changed={anyChanged} count={changedCount}");
        }
        finally
        {
            _unreadRefreshLock.Release();
        }
    }

    private async Task HandleGmailApiRecentMessagesAsync(JsonElement root)
    {
        string requestId = GetString(root, "requestId");
        string accountId = GetString(root, "accountId");
        string email = GetString(root, "email");
        int maxResults = GetInt(root, "maxResults", 12);

        try
        {
            IReadOnlyList<EmailMessageSummary> messages = await _gmailApi.GetRecentMessagesAsync(accountId, email, maxResults, CancellationToken.None);
            _emailAgentInboxCache[accountId] = messages
                .Take(100)
                .Select(msg => new EmailAgentMessageMeta
                {
                    Id = msg.Id,
                    ThreadId = msg.ThreadId,
                    From = msg.From,
                    Subject = msg.Subject,
                    Snippet = msg.Snippet,
                    Date = msg.Date,
                    IsUnread = msg.Unread,
                    Provider = "gmail",
                })
                .ToList();
            await PostAsync(new
            {
                type = "gmail-api-recent-messages",
                requestId,
                accountId,
                messages,
            });
        }
        catch (Exception ex)
        {
            await PostAsync(new
            {
                type = "gmail-api-recent-messages",
                requestId,
                accountId,
                error = ex.Message,
            });
        }
    }

    private async Task HandleGmailApiMessageDetailAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        string messageId = GetString(root, "messageId");
        string providerFromPayload = GetString(root, "provider");
        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        string resolvedProvider = !string.IsNullOrWhiteSpace(providerFromPayload)
            ? providerFromPayload
            : account?.Provider ?? string.Empty;

        LogDebug($"gmail-api-message-detail received accountId={accountId} messageId={messageId} provider={resolvedProvider}");

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(messageId))
        {
            await PostAsync(new
            {
                type = "gmail-api-message-detail-result",
                accountId,
                ok = false,
                error = "Missing accountId or messageId",
            });

            return;
        }

        try
        {
            if (string.Equals(resolvedProvider, "outlook", StringComparison.OrdinalIgnoreCase))
            {
                OutlookMessageDetailResult outlook = await _outlookApi.GetMessageDetailAsync(
                    accountId,
                    account?.Email ?? string.Empty,
                    messageId,
                    CancellationToken.None);

                EmailMessageDetail outlookDetailModel = outlook.Detail;
                LogDebug($"[EmailOutlookMessageDetail] accountId={accountId} messageId={messageId} htmlLength={outlookDetailModel.HtmlBody?.Length ?? 0} plainLength={outlookDetailModel.PlainText?.Length ?? 0}");

                await PostAsync(new
                {
                    type = "gmail-api-message-detail-result",
                    accountId,
                    messageId,
                    ok = true,
                    message = new
                    {
                        id = outlookDetailModel.Id,
                        threadId = outlookDetailModel.ThreadId,
                        from = outlookDetailModel.From,
                        to = outlookDetailModel.To,
                        subject = outlookDetailModel.Subject,
                        date = outlookDetailModel.Date,
                        internalDateUtc = outlook.InternalDateUtc,
                        snippet = outlookDetailModel.Snippet,
                        bodyText = outlookDetailModel.BodyText,
                        plainText = outlookDetailModel.PlainText,
                        htmlBody = outlookDetailModel.HtmlBody,
                        inlineImagesCount = outlookDetailModel.InlineImagesCount,
                        attachments = outlookDetailModel.Attachments.Select(a => new
                        {
                            fileName = a.FileName,
                            mimeType = a.MimeType,
                            attachmentId = a.AttachmentId,
                            contentId = a.ContentId,
                            size = a.Size,
                            isInline = a.IsInline,
                        }),
                        labels = outlookDetailModel.Labels,
                        webLink = outlook.WebLink,
                        provider = "outlook",
                        isUnread = outlook.IsUnread,
                    },
                });

                _emailAgentSelectedCache[accountId] = new EmailAgentSelectedContext
                {
                    MessageId = outlookDetailModel.Id,
                    ThreadId = outlookDetailModel.ThreadId,
                    Provider = "outlook",
                    From = outlookDetailModel.From,
                    To = outlookDetailModel.To,
                    Subject = outlookDetailModel.Subject,
                    Date = outlookDetailModel.Date,
                    Snippet = outlookDetailModel.Snippet,
                    PlainText = CoalescePlainText(outlookDetailModel.PlainText, outlookDetailModel.BodyText, outlookDetailModel.Snippet),
                };

                return;
            }

            EmailMessageDetail detail = await _gmailApi.GetMessageDetailAsync(accountId, messageId, CancellationToken.None);
            LogDebug($"[EmailMessageDetail] accountId={accountId} messageId={messageId} htmlLength={detail.HtmlBody?.Length ?? 0} plainLength={detail.PlainText?.Length ?? 0} inlineImages={detail.InlineImagesCount} attachments={detail.Attachments?.Count ?? 0}");
            await PostAsync(new
            {
                type = "gmail-api-message-detail-result",
                accountId,
                messageId,
                ok = true,
                message = new
                {
                    id = detail.Id,
                    threadId = detail.ThreadId,
                    from = detail.From,
                    to = detail.To,
                    subject = detail.Subject,
                    date = detail.Date,
                    snippet = detail.Snippet,
                    bodyText = detail.BodyText,
                    plainText = detail.PlainText,
                    htmlBody = detail.HtmlBody,
                    inlineImagesCount = detail.InlineImagesCount,
                    attachments = detail.Attachments.Select(a => new
                    {
                        fileName = a.FileName,
                        mimeType = a.MimeType,
                        attachmentId = a.AttachmentId,
                        contentId = a.ContentId,
                        size = a.Size,
                        isInline = a.IsInline,
                    }),
                    labels = detail.Labels,
                },
            });

            _emailAgentSelectedCache[accountId] = new EmailAgentSelectedContext
            {
                MessageId = detail.Id,
                ThreadId = detail.ThreadId,
                Provider = "gmail",
                From = detail.From,
                To = detail.To,
                Subject = detail.Subject,
                Date = detail.Date,
                Snippet = detail.Snippet,
                PlainText = CoalescePlainText(detail.PlainText, detail.BodyText, detail.Snippet),
            };
        }
        catch (Exception ex)
        {
            LogDebug($"gmail-api-message-detail error accountId={accountId} messageId={messageId} error={ex.Message}");
            await PostAsync(new
            {
                type = "gmail-api-message-detail-result",
                accountId,
                ok = false,
                error = ex.Message,
            });
        }
    }

    private async Task HandleEmailAgentCommandAsync(JsonElement root)
    {
        string command = GetString(root, "command");
        string accountId = GetString(root, "accountId");
        string provider = GetString(root, "provider");
        string selectedMessageId = GetString(root, "selectedMessageId");
        string scope = GetString(root, "scope");

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(command))
        {
            await PostAsync(new
            {
                type = "email-agent-result",
                ok = false,
                command,
                source = "email-agent",
                summary = "",
                sections = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "Missing command or account context.",
            });
            return;
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        string resolvedProvider = !string.IsNullOrWhiteSpace(provider)
            ? provider.Trim().ToLowerInvariant()
            : account?.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        bool hasToken = account != null && HasProviderApiToken(account);

        try
        {
            string lower = command.Trim().ToLowerInvariant();
            if (string.Equals(scope, "selected-message", StringComparison.OrdinalIgnoreCase) ||
                lower.Contains("selected") ||
                lower.Contains("this email") ||
                lower.Contains("draft") ||
                lower.Contains("phishing") ||
                lower.Contains("explain") ||
                lower.Contains("action items"))
            {
                if (!_emailAgentSelectedCache.TryGetValue(accountId, out EmailAgentSelectedContext? selected) ||
                    string.IsNullOrWhiteSpace(selected.PlainText) ||
                    (!string.IsNullOrWhiteSpace(selectedMessageId) && !string.Equals(selected.MessageId, selectedMessageId, StringComparison.OrdinalIgnoreCase)))
                {
                    string selectedError = string.Equals(resolvedProvider, "outlook", StringComparison.OrdinalIgnoreCase)
                        ? "Outlook selected-message detail is not wired yet."
                        : "Select an email first.";

                    await PostAsync(new
                    {
                        type = "email-agent-result",
                        ok = false,
                        command,
                        source = "email-agent",
                        summary = "",
                        sections = Array.Empty<object>(),
                        warnings = Array.Empty<string>(),
                        error = selectedError,
                    });
                    return;
                }

                string plain = CoalescePlainText(selected.PlainText, selected.Snippet);
                if (plain.Length > 20000)
                    plain = plain[..20000];

                if (lower.Contains("phishing"))
                {
                    var riskSignals = new List<string>();
                    string body = $"{selected.Subject} {plain}";
                    if (ContainsAny(body, "password", "verify", "verification", "2fa", "urgent", "suspended", "click", "confirm account", "reset"))
                        riskSignals.Add("Contains account/password/verification language.");
                    if (ContainsAny(body, "gift card", "wire", "crypto", "bank", "payment immediately"))
                        riskSignals.Add("Contains money-transfer urgency wording.");
                    if (ContainsAny((selected.From ?? "") + " " + body, "unknown sender", "spoof", "no-reply") || string.IsNullOrWhiteSpace(selected.From))
                        riskSignals.Add("Sender context needs verification.");

                    string riskLevel = riskSignals.Count switch
                    {
                        0 => "low",
                        1 => "review",
                        _ => "high",
                    };

                    await PostAsync(new
                    {
                        type = "email-agent-result",
                        ok = true,
                        command,
                        source = "metadata+body",
                        summary = $"Risk level: {riskLevel}",
                        sections = new object[]
                        {
                            new
                            {
                                title = "Phishing signals",
                                items = riskSignals.Count == 0
                                    ? new object[] { new { message = "No strong phishing signal found in available metadata/body text." } }
                                    : riskSignals.Select(x => (object)new { message = x }).ToArray()
                            }
                        },
                        warnings = new[] { "This is a risk review, not certainty." },
                        error = "",
                    });
                    return;
                }

                if (lower.Contains("action item") || lower.Contains("extract"))
                {
                    var lines = plain.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToList();
                    var tasks = lines
                        .Where(l => ContainsAny(l, "please", "need", "can you", "by ", "before ", "todo", "action", "reply"))
                        .Take(8)
                        .ToList();

                    await PostAsync(new
                    {
                        type = "email-agent-result",
                        ok = true,
                        command,
                        source = "metadata+body",
                        summary = tasks.Count > 0 ? $"Found {tasks.Count} likely action item(s)." : "No explicit action item found.",
                        sections = new object[]
                        {
                            new
                            {
                                title = "Action items",
                                items = tasks.Count > 0
                                    ? tasks.Select(x => (object)new { message = x }).ToArray()
                                    : new object[] { new { message = "No explicit action item was detected in the loaded text." } }
                            }
                        },
                        warnings = Array.Empty<string>(),
                        error = "",
                    });
                    return;
                }

                string compact = plain.Length > 700 ? plain[..700] + "..." : plain;
                string summaryText = $"From: {selected.From}\nSubject: {selected.Subject}\nIntent: {(ContainsAny(compact, "invoice", "payment", "order") ? "Billing/order context" : ContainsAny(compact, "security", "verification", "password") ? "Security/account context" : "General correspondence")}\nSuggested response: Acknowledge key point and confirm next step.";
                if (lower.Contains("explain"))
                    summaryText = $"This message is asking for: {InferIntentLine(compact)}\nImportant facts: {InferFactLine(compact)}\nRecommended next response: {InferResponseLine(compact)}";

                await PostAsync(new
                {
                    type = "email-agent-result",
                    ok = true,
                    command,
                    source = "metadata+body",
                    summary = summaryText,
                    sections = new object[]
                    {
                        new
                        {
                            title = "Selected email",
                            items = new object[]
                            {
                                new
                                {
                                    messageId = selected.MessageId,
                                    subject = selected.Subject,
                                    sender = selected.From,
                                    date = selected.Date,
                                    snippet = selected.Snippet,
                                }
                            }
                        }
                    },
                    warnings = Array.Empty<string>(),
                    error = "",
                });
                return;
            }

            _emailAgentInboxCache.TryGetValue(accountId, out List<EmailAgentMessageMeta>? cached);
            var inbox = (cached ?? new List<EmailAgentMessageMeta>()).Take(100).ToList();

            if (inbox.Count == 0)
            {
                string connectError = hasToken
                    ? "No loaded inbox messages are available yet."
                    : "Sign-in/API required. Load mailbox messages first.";

                await PostAsync(new
                {
                    type = "email-agent-result",
                    ok = false,
                    command,
                    source = "local-rules",
                    summary = "",
                    sections = Array.Empty<object>(),
                    warnings = Array.Empty<string>(),
                    error = connectError,
                });
                return;
            }

            List<EmailAgentMessageMeta> urgent = inbox.Where(IsUrgentActionMessage).Take(20).ToList();
            List<EmailAgentMessageMeta> bills = inbox.Where(IsBillingOrderMessage).Take(20).ToList();
            List<EmailAgentMessageMeta> security = inbox.Where(IsSecurityAlertMessage).Take(20).ToList();
            List<EmailAgentMessageMeta> needsReply = inbox.Where(IsLikelyNeedsReplyMessage).Take(20).ToList();
            List<EmailAgentMessageMeta> noise = inbox.Where(IsNoiseMarketingMessage).Take(20).ToList();

            // Determine if this is a specific targeted command
            List<EmailAgentMessageMeta>? specificPick = null;
            string specificTitle = "Matches";

            if (lower.Contains("urgent")) { specificPick = urgent; specificTitle = "Urgent emails"; }
            else if (lower.Contains("bill") || lower.Contains("order")) { specificPick = bills; specificTitle = "Bills / orders"; }
            else if (lower.Contains("security")) { specificPick = security; specificTitle = "Security alerts"; }
            else if (lower.Contains("needing reply") || lower.Contains("needs reply") || lower.Contains("reply")) { specificPick = needsReply; specificTitle = "Emails needing reply"; }
            else if (ContainsAny(lower, "newsletter", "noise", "promo", "promotional", "marketing", "digest")) { specificPick = noise; specificTitle = "Newsletters / noise"; }

            List<object> sections = new();
            if (specificPick != null)
            {
                if (specificPick.Count > 0)
                {
                    sections.Add(new
                    {
                        title = specificTitle,
                        items = specificPick.Select(ToEmailAgentItem).ToArray(),
                    });
                }
                else
                {
                    sections.Add(new
                    {
                        title = specificTitle,
                        items = new object[] { new { message = $"No {specificTitle.ToLower()} found in your last {inbox.Count} loaded messages (keyword scan)." } },
                    });
                }
            }
            else
            {
                // General / overview command — show all categories
                sections.Add(new { title = "Urgent", items = urgent.Count > 0 ? urgent.Select(ToEmailAgentItem).ToArray() : new object[] { new { message = $"No urgent keyword hits in {inbox.Count} messages." } } });
                sections.Add(new { title = "Needs reply", items = needsReply.Count > 0 ? needsReply.Select(ToEmailAgentItem).ToArray() : new object[] { new { message = "No obvious unread reply-needed items found." } } });
                sections.Add(new { title = "Bills / orders", items = bills.Count > 0 ? bills.Select(ToEmailAgentItem).ToArray() : new object[] { new { message = "No billing/order emails detected." } } });
                sections.Add(new { title = "Security alerts", items = security.Count > 0 ? security.Select(ToEmailAgentItem).ToArray() : new object[] { new { message = "No security alert emails detected." } } });
                sections.Add(new { title = "Newsletters / noise", items = noise.Count > 0 ? noise.Select(ToEmailAgentItem).ToArray() : new object[] { new { message = "No newsletter/promo emails detected." } } });
                sections.Add(new
                {
                    title = "Suggested next actions",
                    items = new object[]
                    {
                        new { message = urgent.Count > 0 ? $"Review {urgent.Count} urgent item(s) first." : "No urgent keyword hits in loaded messages." },
                        new { message = needsReply.Count > 0 ? $"Reply to {needsReply.Count} unread direct ask(s)." : "No obvious unread reply-needed items found." },
                        new { message = security.Count > 0 ? $"Verify {security.Count} security/account alert(s) directly in provider." : "No obvious security alert hits found." },
                    }
                });
            }

            await PostAsync(new
            {
                type = "email-agent-result",
                ok = true,
                command,
                source = "local-rules",
                summary = $"Scanned {inbox.Count} loaded message(s) — {(specificPick != null ? (specificPick.Count > 0 ? $"{specificPick.Count} match(es) found" : "no matches") : $"urgent: {urgent.Count}, reply: {needsReply.Count}, bills: {bills.Count}, security: {security.Count}, noise: {noise.Count}")}.",
                sections,
                warnings = new[] { "Local keyword scan only — AI analysis unavailable." },
                error = "",
            });
        }
        catch (Exception ex)
        {
            await PostAsync(new
            {
                type = "email-agent-result",
                ok = false,
                command,
                source = "email-agent",
                summary = "",
                sections = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = ex.Message,
            });
        }
    }

    private async Task HandleEmailAgentDraftReplyAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        string provider = GetString(root, "provider");
        string selectedMessageId = GetString(root, "selectedMessageId");
        string tone = GetString(root, "tone");
        string instruction = GetString(root, "instruction");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            await PostAsync(new
            {
                type = "email-agent-draft-result",
                ok = false,
                draftText = "",
                subjectSuggestion = "",
                warnings = Array.Empty<string>(),
                error = "Missing account context.",
            });
            return;
        }

        string normalizedProvider = (provider ?? "").Trim().ToLowerInvariant();
        if (!_emailAgentSelectedCache.TryGetValue(accountId, out EmailAgentSelectedContext? selected) ||
            string.IsNullOrWhiteSpace(selected.PlainText) ||
            (!string.IsNullOrWhiteSpace(selectedMessageId) && !string.Equals(selected.MessageId, selectedMessageId, StringComparison.OrdinalIgnoreCase)))
        {
            string selectedError = string.Equals(normalizedProvider, "outlook", StringComparison.OrdinalIgnoreCase)
                ? "Outlook selected-message detail is not wired yet."
                : "Select an email first.";

            await PostAsync(new
            {
                type = "email-agent-draft-result",
                ok = false,
                draftText = "",
                subjectSuggestion = "",
                warnings = Array.Empty<string>(),
                error = selectedError,
            });
            return;
        }

        string requestedTone = string.IsNullOrWhiteSpace(tone) ? "professional" : tone.Trim().ToLowerInvariant();
        string greeting = requestedTone switch
        {
            "friendlier" => "Hi",
            "shorter" => "Hi",
            "professional" => "Hello",
            _ => "Hello",
        };

        string responseStyleLine = requestedTone switch
        {
            "friendlier" => "Thanks for the note. I appreciate the update.",
            "shorter" => "Thanks for your email.",
            _ => "Thank you for your email.",
        };

        string cleanBody = CoalescePlainText(selected.PlainText, selected.Snippet);
        if (cleanBody.Length > 20000)
            cleanBody = cleanBody[..20000];

        string contextLine = InferIntentLine(cleanBody);
        string instructionLine = string.IsNullOrWhiteSpace(instruction)
            ? ""
            : $"\n{instruction.Trim()}";

        string draft =
            $"{greeting} {(ExtractSenderName(selected.From) ?? "there")},\n\n" +
            $"{responseStyleLine} I understand that {contextLine}.\n" +
            "I can help with the next steps and confirm any missing details as needed." +
            instructionLine +
            "\n\nBest regards,\n";

        await PostAsync(new
        {
            type = "email-agent-draft-result",
            ok = true,
            draftText = draft,
            subjectSuggestion = BuildReplySubject(selected.Subject),
            warnings = string.Equals(normalizedProvider, "outlook", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Outlook draft insertion not wired yet - copy draft only." }
                : new[] { "Review before inserting. Insert as Draft never sends email." },
            error = "",
        });
    }

    private async Task HandleEmailAgentApplyLabelsAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        string provider = GetString(root, "provider");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            await PostAsync(new
            {
                type = "email-agent-apply-labels-result",
                ok = false,
                results = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "Missing account context.",
            });
            return;
        }

        string normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.Equals(normalizedProvider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            await PostAsync(new
            {
                type = "email-agent-apply-labels-result",
                ok = false,
                results = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "Label apply is Gmail only. Outlook label application is not wired yet.",
            });
            return;
        }

        // items: [{messageId, suggestedLabel}]
        if (!root.TryGetProperty("items", out JsonElement itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
        {
            await PostAsync(new
            {
                type = "email-agent-apply-labels-result",
                ok = false,
                results = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "No items provided.",
            });
            return;
        }

        var requestItems = new List<(string MessageId, string SuggestedLabel)>();
        foreach (JsonElement item in itemsEl.EnumerateArray())
        {
            string msgId = item.TryGetProperty("messageId", out JsonElement mid) && mid.ValueKind == JsonValueKind.String
                ? mid.GetString() ?? string.Empty : string.Empty;
            string lbl = item.TryGetProperty("suggestedLabel", out JsonElement lel) && lel.ValueKind == JsonValueKind.String
                ? lel.GetString() ?? string.Empty : string.Empty;

            if (!string.IsNullOrWhiteSpace(msgId) && !string.IsNullOrWhiteSpace(lbl))
                requestItems.Add((msgId, lbl));
        }

        if (requestItems.Count == 0)
        {
            await PostAsync(new
            {
                type = "email-agent-apply-labels-result",
                ok = false,
                results = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "No valid items to apply.",
            });
            return;
        }

        // Resolve label names → Gmail label ids (create if missing).
        var labelIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var itemResults = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        foreach ((string messageId, string suggestedLabel) in requestItems)
        {
            try
            {
                if (!labelIdCache.TryGetValue(suggestedLabel, out string? labelId))
                {
                    labelId = await _gmailApi.EnsureLabelAsync(accountId, suggestedLabel, CancellationToken.None).ConfigureAwait(false);
                    labelIdCache[suggestedLabel] = labelId;
                }

                GmailApplyLabelResult applied = await _gmailApi.ApplyLabelToMessageAsync(
                    accountId, messageId, labelId, CancellationToken.None).ConfigureAwait(false);

                successCount++;
                itemResults.Add(new
                {
                    messageId,
                    label = suggestedLabel,
                    ok = true,
                    error = string.Empty,
                });
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (LooksLikeGmailModifyScopeError(errMsg))
                    errMsg = "Gmail label apply needs modify permission. Reconnect Gmail or keep labels as suggestions.";

                failedCount++;
                itemResults.Add(new
                {
                    messageId,
                    label = suggestedLabel,
                    ok = false,
                    error = errMsg,
                });
            }
        }

        await PostAsync(new
        {
            type = "email-agent-apply-labels-result",
            ok = successCount > 0,
            appliedCount = successCount,
            failedCount,
            results = itemResults.ToArray(),
            warnings = new[] { "Labels applied. No messages were archived, deleted or sent." },
            error = successCount == 0 ? "All label applications failed." : "",
        });
    }

    private async Task HandleEmailAgentTriageAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            await PostAsync(new
            {
                type = "email-agent-triage-result",
                ok = false,
                accountId = "",
                totalScanned = 0,
                categories = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = "Missing account context.",
            });
            return;
        }

        try
        {
            _emailAgentInboxCache.TryGetValue(accountId, out List<EmailAgentMessageMeta>? cached);
            List<EmailAgentMessageMeta> inbox = (cached ?? new List<EmailAgentMessageMeta>()).Take(150).ToList();

            if (inbox.Count == 0)
            {
                await PostAsync(new
                {
                    type = "email-agent-triage-result",
                    ok = false,
                    accountId,
                    totalScanned = 0,
                    categories = Array.Empty<object>(),
                    warnings = Array.Empty<string>(),
                    error = "No loaded inbox messages. Connect Gmail API and load messages first.",
                });
                return;
            }

            var urgentItems = inbox
                .Where(IsUrgentActionMessage)
                .Take(20)
                .Select(i => ToTriageItem(i, "action-needed"))
                .ToArray();

            var needsReplyItems = inbox
                .Where(IsLikelyNeedsReplyMessage)
                .Take(20)
                .Select(i => ToTriageItem(i, "needs-reply"))
                .ToArray();

            var billsItems = inbox
                .Where(IsBillingOrderMessage)
                .Take(20)
                .Select(i => ToTriageItem(i, "billing"))
                .ToArray();

            var securityItems = inbox
                .Where(IsSecurityAlertMessage)
                .Take(20)
                .Select(i => ToTriageItem(i, "security-alert"))
                .ToArray();

            var noiseItems = inbox
                .Where(IsNoiseMarketingMessage)
                .Take(20)
                .Select(i => ToTriageItem(i, "newsletter"))
                .ToArray();

            await PostAsync(new
            {
                type = "email-agent-triage-result",
                ok = true,
                accountId,
                totalScanned = inbox.Count,
                categories = new object[]
                {
                    new { id = "urgent",     title = "Urgent / Action needed",  icon = "🔴", items = urgentItems },
                    new { id = "needsReply", title = "Needs reply",             icon = "💬", items = needsReplyItems },
                    new { id = "bills",      title = "Bills / Orders",          icon = "💳", items = billsItems },
                    new { id = "security",   title = "Security alerts",         icon = "🔒", items = securityItems },
                    new { id = "noise",      title = "Newsletters / Noise",     icon = "📨", items = noiseItems },
                },
                warnings = new[] { "Suggested labels only — no labels applied. No auto-archive or delete." },
                error = "",
            });
        }
        catch (Exception ex)
        {
            await PostAsync(new
            {
                type = "email-agent-triage-result",
                ok = false,
                accountId,
                totalScanned = 0,
                categories = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                error = ex.Message,
            });
        }
    }

    private static object ToTriageItem(EmailAgentMessageMeta item, string suggestedLabel) => new
    {
        messageId = item.Id,
        subject = item.Subject,
        sender = item.From,
        date = item.Date,
        snippet = item.Snippet,
        unread = item.IsUnread,
        labels = item.Labels,
        suggestedLabel,
    };

    private async Task HandleEmailAgentInsertDraftAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        string provider = GetString(root, "provider");
        string selectedMessageId = GetString(root, "selectedMessageId");
        string draftText = GetString(root, "draftText");
        string subjectSuggestion = GetString(root, "subjectSuggestion");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = "",
                status = "",
                warnings = Array.Empty<string>(),
                error = "Missing account context.",
            });
            return;
        }

        string normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (!_emailAgentSelectedCache.TryGetValue(accountId, out EmailAgentSelectedContext? selected) ||
            (!string.IsNullOrWhiteSpace(selectedMessageId) && !string.Equals(selected.MessageId, selectedMessageId, StringComparison.OrdinalIgnoreCase)))
        {
            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = "",
                status = "",
                warnings = Array.Empty<string>(),
                error = "Select an email first.",
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(draftText))
        {
            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = "",
                status = "",
                warnings = Array.Empty<string>(),
                error = "Generate a draft first.",
            });
            return;
        }

        if (string.Equals(normalizedProvider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = "",
                status = "fallback",
                warnings = new[] { "Outlook draft insertion not wired yet - copy draft only." },
                error = "Outlook draft insertion not wired yet - copy draft only.",
            });
            return;
        }

        StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
            string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        string replyTo = ExtractEmailAddress(selected.From);

        if (string.IsNullOrWhiteSpace(replyTo))
        {
            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = "",
                status = "",
                warnings = Array.Empty<string>(),
                error = "Unable to resolve reply recipient from the selected email.",
            });
            return;
        }

        try
        {
            GmailDraftCreateResult inserted = await _gmailApi.CreateDraftAsync(
                accountId,
                account?.Email ?? string.Empty,
                new GmailDraftCreateRequest
                {
                    To = replyTo,
                    Subject = string.IsNullOrWhiteSpace(subjectSuggestion) ? BuildReplySubject(selected.Subject) : subjectSuggestion,
                    Body = draftText,
                    ThreadId = selected.ThreadId,
                },
                CancellationToken.None);

            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = true,
                draftId = inserted.DraftId,
                messageId = inserted.MessageId,
                threadId = inserted.ThreadId,
                status = inserted.Status,
                warnings = new[] { "Draft inserted successfully. Sending remains manual in Gmail." },
                error = "",
            });
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            if (LooksLikeGmailComposeScopeError(message))
            {
                message = "Gmail draft insertion needs compose access. Reconnect Gmail API or use copy draft only.";
            }

            await PostAsync(new
            {
                type = "email-agent-insert-draft-result",
                ok = false,
                draftId = "",
                messageId = "",
                threadId = selected.ThreadId,
                status = "error",
                warnings = new[] { "Insert as Draft never sends email." },
                error = message,
            });
        }
    }

    private static bool ContainsAny(string source, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        foreach (string term in terms)
        {
            if (source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string BuildMessageCorpus(EmailAgentMessageMeta item)
    {
        return $"{item.Subject} {item.Snippet} {item.From}";
    }

    private static bool IsNoiseMarketingMessage(EmailAgentMessageMeta item)
    {
        string text = BuildMessageCorpus(item);
        return ContainsAny(text,
            "newsletter", "unsubscribe", "view online", "view in browser", "digest", "daily digest", "weekly digest",
            "promo", "promotion", "promotional", "deal", "offer", "% off", "sale", "flash sale", "limited time",
            "shop now", "order now", "buy now", "recommended for you", "stories", "notification", "trending",
            "restaurants near you", "uber eats", "doordash", "grubhub", "marketing")
            || ContainsAny(item.From ?? string.Empty, "no-reply", "noreply", "newsletter", "marketing", "updates@");
    }

    private static bool IsLikelyNeedsReplyMessage(EmailAgentMessageMeta item)
    {
        if (!item.IsUnread)
            return false;

        if (IsNoiseMarketingMessage(item))
            return false;

        string text = BuildMessageCorpus(item);
        bool directAsk = ContainsAny(text,
            "can you", "could you", "would you", "will you", "please confirm", "please review",
            "let me know", "get back to me", "need your input", "need your feedback",
            "what do you think", "are you able", "do you have", "when can", "reply needed", "respond by");

        bool hasQuestion = (item.Subject?.Contains('?') ?? false) || (item.Snippet?.Contains('?') ?? false);
        bool hasAskCue = ContainsAny(text, "can", "could", "would", "please", "when", "what", "who", "are you", "do you", "confirm");

        bool automatedSender = ContainsAny(item.From ?? string.Empty, "no-reply", "noreply", "mailer-daemon", "notification", "updates@");
        return !automatedSender && (directAsk || (hasQuestion && hasAskCue));
    }

    private static bool IsBillingOrderMessage(EmailAgentMessageMeta item)
    {
        string text = BuildMessageCorpus(item);
        if (IsNoiseMarketingMessage(item) && !ContainsAny(text, "invoice", "receipt", "statement", "payment due", "subscription renewal"))
            return false;

        return ContainsAny(text,
            "invoice", "payment", "receipt", "order", "subscription", "billing", "statement", "due", "renewal", "charged", "charge");
    }

    private static bool IsSecurityAlertMessage(EmailAgentMessageMeta item)
    {
        if (IsNoiseMarketingMessage(item))
            return false;

        string text = BuildMessageCorpus(item);
        return ContainsAny(text,
            "sign-in", "signin", "password", "2fa", "two-factor", "security", "verification",
            "account alert", "suspicious", "breach", "new device", "reset", "locked");
    }

    private static bool IsUrgentActionMessage(EmailAgentMessageMeta item)
    {
        if (IsNoiseMarketingMessage(item))
            return false;

        string text = BuildMessageCorpus(item);
        return ContainsAny(text,
            "urgent", "asap", "today", "final reminder", "deadline", "action required", "response needed", "by eod", "before end of day");
    }

    private static object ToEmailAgentItem(EmailAgentMessageMeta item)
    {
        return new
        {
            messageId = item.Id,
            subject = item.Subject,
            sender = item.From,
            date = item.Date,
            snippet = item.Snippet,
            unread = item.IsUnread,
            labels = item.Labels,
        };
    }

    private static string CoalescePlainText(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                string compact = candidate.Replace("\r", " ").Replace("\n", " ");
                return compact.Length > 20000 ? compact[..20000] : compact;
            }
        }

        return string.Empty;
    }

    private static string BuildReplySubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return "Re:";

        return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : $"Re: {subject}";
    }

    private static string ExtractEmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Match match = Regex.Match(value, @"<([^>]+)>");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        string trimmed = value.Trim().Trim('"');
        return trimmed.Contains('@', StringComparison.Ordinal) ? trimmed : string.Empty;
    }

    private static bool LooksLikeGmailComposeScopeError(string message)
    {
        return ContainsAny(message, "insufficient", "permission", "scope", "forbidden");
    }

    private static bool LooksLikeGmailModifyScopeError(string message)
    {
        return ContainsAny(message, "insufficient", "permission", "scope", "forbidden", "modify");
    }

    private static string? ExtractSenderName(string from)
    {
        if (string.IsNullOrWhiteSpace(from))
            return null;

        int lt = from.IndexOf('<');
        if (lt > 0)
            return from[..lt].Trim().Trim('"');

        if (from.Contains('@', StringComparison.Ordinal))
            return from.Split('@')[0].Trim();

        return from.Trim();
    }

    private static string InferIntentLine(string body)
    {
        if (ContainsAny(body, "invoice", "payment", "billing", "receipt", "order"))
            return "this is related to billing or order details";
        if (ContainsAny(body, "security", "password", "verification", "sign-in"))
            return "this is related to account security";
        if (ContainsAny(body, "meeting", "schedule", "calendar", "call"))
            return "this is related to scheduling";
        return "you are requesting follow-up on this topic";
    }

    private static string InferFactLine(string body)
    {
        string compact = body.Trim();
        if (compact.Length == 0)
            return "No additional facts were available from the loaded detail.";
        if (compact.Length <= 180)
            return compact;
        return compact[..180] + "...";
    }

    private static string InferResponseLine(string body)
    {
        if (ContainsAny(body, "deadline", "today", "asap", "urgent"))
            return "Acknowledge urgency and confirm a concrete timeline.";
        if (ContainsAny(body, "question", "?", "can you", "could you"))
            return "Answer the direct questions and ask for any missing specifics.";
        return "Acknowledge receipt and confirm your next step.";
    }

    private void ApplyNativeBounds(string accountId, HostBounds bounds)
    {
        try
        {
            LogNativeOverlayGate("apply-entry", bounds.Width, bounds.Height);
            StoredEmailAccount? activeAccount = _accounts.FirstOrDefault(a =>
                string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            bool isOutlook = string.Equals(activeAccount?.Provider, "outlook", StringComparison.OrdinalIgnoreCase);

            if (_apiInboxActiveAccounts.Contains(accountId))
            {
                if (_nativeGmailView != null)
                {
                    _nativeGmailView.Visibility = Visibility.Collapsed;
                    _nativeGmailView.IsHitTestVisible = false;
                }

                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                EmailWebView.IsHitTestVisible = true;
                SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: false, bounds: bounds);
                LogDebug($"[EmailApiInboxMode] accountId={accountId} nativeOverlaySuppressed=True reason=inbox-messages-posted");
                return;
            }

            if (_nativeGmailView == null || !string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"ApplyNativeBounds: native view not ready accountId={accountId} nativeAccId={_nativeViewAccountId} nativeViewNull={_nativeGmailView == null}");
                return;
            }

            if (!ShouldShowNativeOverlay())
            {
                if (ShouldKeepApiLockedCenterOverlayVisible(accountId, out bool keepOverlayTokenExists))
                {
                    ShowApiLockedNativeCard(accountId, keepOverlayTokenExists, bounds);
                    return;
                }

                SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: false, bounds: bounds);
                LogNativeOverlayGate("apply-collapse", bounds.Width, bounds.Height);
                _nativeGmailView.Visibility = Visibility.Collapsed;
                _nativeGmailView.IsHitTestVisible = false;
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                EmailWebView.IsHitTestVisible = true;
                LogDebug($"ApplyNativeBounds: host state hides native overlay accountId={accountId} mode={_activeViewMode} settingsOpen={_settingsPanelOpen}");
                return;
            }

            HostBounds effectiveBounds = bounds;
            if (!_aiPanelOpen)
            {
                double adjustedWidth = Math.Max(0, bounds.Width - AiReopenRightGutterWidth);
                effectiveBounds = new HostBounds(bounds.Left, bounds.Top, adjustedWidth, bounds.Height);
            }

            bool valid = effectiveBounds.Width > 4 && effectiveBounds.Height > 4;
            if (!valid)
            {
                if (ShouldKeepApiLockedCenterOverlayVisible(accountId, out bool keepOverlayTokenExists))
                {
                    ShowApiLockedNativeCard(accountId, keepOverlayTokenExists, null);
                    return;
                }

                SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: false, bounds: effectiveBounds);
                LogNativeOverlayGate("apply-collapse", effectiveBounds.Width, effectiveBounds.Height);
                _nativeGmailView.Visibility = Visibility.Collapsed;
                _nativeGmailView.IsHitTestVisible = false;
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                NativeOverlayLayer.IsHitTestVisible = false;
                EmailWebView.IsHitTestVisible = true;
                LogDebug($"ApplyNativeBounds: collapsing view – invalid bounds accountId={accountId} w={effectiveBounds.Width:F1} h={effectiveBounds.Height:F1}");
                return;
            }

            bool showApiLockedOverlay = ShouldShowApiLockedCenterOverlay(accountId, out bool tokenExists);
            if (showApiLockedOverlay)
            {
                ShowApiLockedNativeCard(accountId, tokenExists, effectiveBounds);
                return;
            }

            SetApiLockedCenterOverlay(accountId, visible: false, tokenExists: tokenExists, bounds: effectiveBounds);

            if (_nativeLockedCardActive)
            {
                _nativeLockedCardActive = false;
                _ = HideApiLockedCardViaJsAsync();
                StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
                    string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
                string targetUrl = GetNativeMailUrl(account?.Provider);
                _nativeGmailView!.CoreWebView2?.Navigate(targetUrl);
                LogDebug($"ApplyNativeBounds: exiting locked card mode, re-navigating to {targetUrl} accountId={accountId}");
            }

            LogNativeOverlayGate("apply-visible", effectiveBounds.Width, effectiveBounds.Height);
            Panel.SetZIndex(NativeOverlayLayer, 50);
            NativeOverlayLayer.Visibility = Visibility.Visible;
            NativeOverlayLayer.IsHitTestVisible = true;
            int nativeChildIndex = NativeOverlayLayer.Children.IndexOf(_nativeGmailView);
            if (nativeChildIndex != NativeOverlayLayer.Children.Count - 1)
            {
                if (nativeChildIndex >= 0)
                {
                    NativeOverlayLayer.Children.RemoveAt(nativeChildIndex);
                }

                NativeOverlayLayer.Children.Add(_nativeGmailView);
            }

            Panel.SetZIndex(_nativeGmailView, 100);
            _nativeGmailView.Visibility = Visibility.Visible;
            _nativeGmailView.IsHitTestVisible = true;
            // Keep React host interactive so right-side Email Agent actions remain clickable.
            EmailWebView.IsHitTestVisible = true;
            double left = double.IsNaN(effectiveBounds.Left) ? 0 : effectiveBounds.Left;
            double top = double.IsNaN(effectiveBounds.Top) ? 0 : effectiveBounds.Top;
            Canvas.SetLeft(_nativeGmailView, left);
            Canvas.SetTop(_nativeGmailView, top);
            _nativeGmailView.Width = effectiveBounds.Width;
            _nativeGmailView.Height = effectiveBounds.Height;
            LogNativeOverlayGate("apply-visible", effectiveBounds.Width, effectiveBounds.Height);

            LogDebug($"NativeOverlayLayer visibility={NativeOverlayLayer.Visibility} actualWidth={NativeOverlayLayer.ActualWidth:F1} actualHeight={NativeOverlayLayer.ActualHeight:F1}");
            LogDebug($"[EmailNativeBounds] accountId={accountId} left={left:F1} top={top:F1} width={_nativeGmailView.Width:F1} height={_nativeGmailView.Height:F1}");
            LogDebug($"[EmailNativeZOrder] accountId={accountId} overlayZ={Panel.GetZIndex(NativeOverlayLayer)} nativeZ={Panel.GetZIndex(_nativeGmailView)} emailHitTest={EmailWebView.IsHitTestVisible} nativeHitTest={_nativeGmailView.IsHitTestVisible} childIndex={NativeOverlayLayer.Children.IndexOf(_nativeGmailView)}");
            LogDebug($"WebView2 size/visibility accountId={accountId} width={_nativeGmailView.Width:F1} height={_nativeGmailView.Height:F1} visibility={_nativeGmailView.Visibility}");
            _ = EvaluateAndPostNativeMountedStateAsync(accountId, "ApplyNativeBounds");
        }
        catch (Exception ex)
        {
            if (!_apiInboxActiveAccounts.Contains(accountId))
            {
                _ = PostAsync(new { type = "native-gmail-mounted", accountId, mounted = false, error = ex.Message });
            }
            LogDebug($"native-gmail-mounted posted false accountId={accountId} error={ex.Message}");
        }
    }

    private async Task OpenGmailNativeAsync(StoredEmailAccount account, bool navigateIfNew)
    {
        bool isGmail = string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase);
        bool isOutlook = string.Equals(account.Provider, "outlook", StringComparison.OrdinalIgnoreCase);
        if (!isGmail && !isOutlook)
        {
            account.Status = "error";
            PersistAccounts();
            await PostAsync(new { type = "account-status", accountId = account.Id, status = "error" });
            return;
        }

        await _nativeGmailLock.WaitAsync();
        try
        {
            string targetUrl = GetNativeMailUrl(account.Provider);
            if (isOutlook)
            {
                LogDebug($"[EmailOutlookNative] step=open-start accountId={account.Id} target={targetUrl}");
            }

            LogDebug($"OpenGmailNativeAsync started accountId={account.Id}");
            bool reuse = _nativeGmailView != null && string.Equals(_nativeViewAccountId, account.Id, StringComparison.OrdinalIgnoreCase);
            LogDebug($"OpenGmailNativeAsync reuse={reuse} existingNativeAccId={_nativeViewAccountId}");
            if (!reuse)
            {
                DisposeNativeGmailView();
                string profileDir = GetAccountProfileDirectory(account.Id);
                Directory.CreateDirectory(profileDir);
                LogDebug($"userDataFolder path={profileDir}");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, profileDir);

                var native = new WebView2
                {
                    Visibility = Visibility.Collapsed,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                };

                _nativeCoreInitialized = false;
                _nativeNavigationCompletedSuccess = false;
                _nativeLastSource = string.Empty;

                LogDebug($"NativeOverlayLayer children count before clear={NativeOverlayLayer.Children.Count}");
                if (_nativeGmailView != null)
                {
                    NativeOverlayLayer.Children.Remove(_nativeGmailView);
                }
                NativeOverlayLayer.Children.Add(native);
                Panel.SetZIndex(native, 10);
                _nativeGmailView = native;
                _nativeViewAccountId = account.Id;

                LogDebug($"Calling EnsureCoreWebView2Async for accountId={account.Id}");
                await native.EnsureCoreWebView2Async(env);
                _nativeCoreInitialized = native.CoreWebView2 != null;
                LogDebug($"CoreWebView2 initialized accountId={account.Id} initialized={_nativeCoreInitialized}");

                native.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    LogDebug($"NavigationStarting accountId={account.Id} uri={args.Uri}");
                };

                native.CoreWebView2.SourceChanged += (_, _) =>
                {
                    try
                    {
                        _nativeLastSource = native.CoreWebView2?.Source ?? string.Empty;
                    }
                    catch
                    {
                        _nativeLastSource = string.Empty;
                    }

                    LogDebug($"SourceChanged accountId={account.Id} source={_nativeLastSource}");
                    _ = EvaluateAndPostNativeMountedStateAsync(account.Id, "SourceChanged");
                };

                native.CoreWebView2.NavigationCompleted += async (_, args) =>
                {
                    string source = "(unknown)";
                    try { source = native.CoreWebView2?.Source ?? "(null)"; } catch { }
                    _nativeLastSource = source;
                    _nativeNavigationCompletedSuccess = args.IsSuccess;
                    if (isOutlook)
                    {
                        LogDebug($"[EmailOutlookNative] step=navigation-completed accountId={account.Id} success={args.IsSuccess} source={source}");
                    }
                    LogDebug($"NavigationCompleted accountId={account.Id} success={args.IsSuccess} error={args.WebErrorStatus} source={source}");

                    await Dispatcher.InvokeAsync(async () =>
                    {
                        bool sourceReady = IsMailSource(source, account.Provider);
                        bool navIsGmail = string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase);
                        account.Status = args.IsSuccess
                            ? (sourceReady ? "connected" : "setup-pending")
                            : (navIsGmail && !sourceReady
                                ? (string.Equals(account.Status, "connected", StringComparison.OrdinalIgnoreCase)
                                    ? "connected"
                                    : "setup-pending")
                                : "error");
                        account.LastUsedUtc = DateTime.UtcNow;
                        PersistAccounts();
                        await PostAsync(new { type = "account-status", accountId = account.Id, status = account.Status });
                        await PostSavedAccountsAsync();

                        // Request React to re-fire fresh bounds so we always position with current layout.
                        await PostAsync(new { type = "request-bounds-refresh", accountId = account.Id });
                        LogDebug($"request-bounds-refresh posted after NavigationCompleted accountId={account.Id}");

                        if (_boundsByAccount.TryGetValue(account.Id, out HostBounds b))
                        {
                            LogDebug($"NavigationCompleted applying stored bounds accountId={account.Id} w={b.Width:F1} h={b.Height:F1}");
                            ApplyNativeBounds(account.Id, b);
                        }
                        else
                        {
                            LogDebug($"NavigationCompleted: no bounds stored yet for accountId={account.Id}, posted request-bounds-refresh");
                            if (!ShouldShowApiLockedCenterOverlay(account.Id, out bool _apiLockedNav) && !_apiInboxActiveAccounts.Contains(account.Id))
                            {
                                await PostAsync(new { type = "native-gmail-mounted", accountId = account.Id, mounted = false, error = "Waiting for host bounds from React." });
                                LogDebug($"native-gmail-mounted posted false accountId={account.Id} error=Waiting for host bounds from React.");
                            }
                        }

                        await EvaluateAndPostNativeMountedStateAsync(account.Id, "NavigationCompleted");
                    });
                };
            }

            if (navigateIfNew || !reuse)
            {
                LogDebug($"Setting Source to {targetUrl} for accountId={account.Id}");
                if (isOutlook)
                {
                    LogDebug($"[EmailOutlookNative] step=navigate-start accountId={account.Id} target={targetUrl}");
                }
                _nativeGmailView!.CoreWebView2.Navigate(targetUrl);
                LogDebug($"Navigate {targetUrl} accountId={account.Id}");
                // Ask React to re-send current bounds immediately after navigation begins.
                await PostAsync(new { type = "request-bounds-refresh", accountId = account.Id });
                LogDebug($"request-bounds-refresh posted after Source set for accountId={account.Id}");
            }

            if (_boundsByAccount.TryGetValue(account.Id, out HostBounds bounds))
            {
                LogDebug($"Applying initial stored bounds accountId={account.Id} w={bounds.Width:F1} h={bounds.Height:F1}");
                ApplyNativeBounds(account.Id, bounds);
            }
            else
            {
                LogDebug($"No bounds stored yet for accountId={account.Id} – will apply when React sends gmail-host-bounds");
                if (!ShouldShowApiLockedCenterOverlay(account.Id, out bool _lockedNoBounds) && !_apiInboxActiveAccounts.Contains(account.Id))
                {
                    await PostAsync(new { type = "native-gmail-mounted", accountId = account.Id, mounted = false, error = "Waiting for host bounds from React." });
                }
            }
        }
        catch (Exception ex)
        {
            account.Status = "error";
            PersistAccounts();
            await PostAsync(new { type = "account-status", accountId = account.Id, status = "error" });
            if (!_apiInboxActiveAccounts.Contains(account.Id))
            {
                await PostAsync(new { type = "native-gmail-mounted", accountId = account.Id, mounted = false, error = ex.Message });
            }
            LogDebug($"open native gmail failed: {ex}");
        }
        finally
        {
            _nativeGmailLock.Release();
        }
    }

    private async Task EvaluateAndPostNativeMountedStateAsync(string accountId, string reason)
    {
        try
        {
            if (_nativeGmailView == null || !string.Equals(_nativeViewAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Check if the account is actively being set up (user just clicked Sign In for first time).
            // For setup-pending accounts we must NOT suppress — the WebView2 needs to show the Google
            // sign-in page so the user can authenticate.
            StoredEmailAccount? acctForSetupCheck = _accounts.FirstOrDefault(a =>
                string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            bool isSetupPending = acctForSetupCheck != null &&
                string.Equals(acctForSetupCheck.Status, "setup-pending", StringComparison.OrdinalIgnoreCase);

            // When the API-locked card is showing, suppress mounted-state posts entirely.
            // Exception: setup-pending accounts must show the WebView so the user can sign in.
            if (ShouldShowApiLockedCenterOverlay(accountId, out _) && !isSetupPending)
            {
                LogDebug($"native-gmail-mounted suppressed (api locked) accountId={accountId} reason={reason}");
                return;
            }

            // When API inbox is active, suppress all false/error mount posts so React does not
            // overlay "Native Gmail host not mounted" / Retry Mount over the API inbox list.
            if (_apiInboxActiveAccounts.Contains(accountId))
            {
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                if (_nativeGmailView != null)
                {
                    _nativeGmailView.Visibility = Visibility.Collapsed;
                }
                LogDebug($"[EmailApiInboxMode] accountId={accountId} nativeMountErrorSuppressed=True reason={reason}");
                return;
            }

            // Suppress false/error mount posts when the Gmail API token is absent — the
            // api-locked overlay will render instead.  Exception: setup-pending accounts need
            // the WebView to show so the user can complete Google sign-in.
            StoredEmailAccount? acctForSuppress = acctForSetupCheck;
            if (acctForSuppress != null
                && string.Equals(acctForSuppress.Provider, "gmail", StringComparison.OrdinalIgnoreCase)
                && !HasGmailApiToken(acctForSuppress)
                && !isSetupPending)
            {
                NativeOverlayLayer.Visibility = Visibility.Collapsed;
                if (_nativeGmailView != null)
                {
                    _nativeGmailView.Visibility = Visibility.Collapsed;
                }
                LogDebug($"[EmailApiInboxMode] accountId={accountId} nativeMountErrorSuppressed=True reason=no-token-{reason}");
                return;
            }

            double width = _nativeGmailView.Width;
            double height = _nativeGmailView.Height;
            bool sizeReady = width > 100 && height > 100;
            string provider = acctForSetupCheck?.Provider ?? "gmail";
            bool sourceReady = IsMailSource(_nativeLastSource, provider);
            bool mounted = _nativeCoreInitialized && sizeReady && (_nativeNavigationCompletedSuccess || sourceReady);

            if (mounted)
            {
                await PostAsync(new { type = "native-gmail-mounted", accountId, mounted = true });
                LogDebug($"native-gmail-mounted posted true accountId={accountId} reason={reason} coreInit={_nativeCoreInitialized} sizeReady={sizeReady} navSuccess={_nativeNavigationCompletedSuccess} source={_nativeLastSource}");
                return;
            }

            string error = $"Not mounted yet: coreInit={_nativeCoreInitialized}, size={width:F1}x{height:F1}, navSuccess={_nativeNavigationCompletedSuccess}, source={_nativeLastSource}";
            await PostAsync(new { type = "native-gmail-mounted", accountId, mounted = false, error });
            LogDebug($"native-gmail-mounted posted false accountId={accountId} reason={reason} error={error}");
        }
        catch (Exception ex)
        {
            await PostAsync(new { type = "native-gmail-mounted", accountId, mounted = false, error = ex.Message });
            LogDebug($"native-gmail-mounted posted false accountId={accountId} reason={reason} exception={ex.Message}");
        }
    }

    private static string GetNativeMailUrl(string? provider)
    {
        if (string.Equals(provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            return "https://login.live.com/login.srf?wa=wsignin1.0&wreply=https%3A%2F%2Foutlook.live.com%2Fmail%2F";
        }

        return "https://mail.google.com/";
    }

    private async Task HandleOpenExternalWebmailAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        string provider = GetString(root, "provider");
        LogDebug($"[EmailOutlookNative] step=open-external accountId={accountId} target=outlook-login");
        const string outlookLoginUrl = "https://login.live.com/login.srf?wa=wsignin1.0&wreply=https%3A%2F%2Foutlook.live.com%2Fmail%2F";
        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outlookLoginUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogDebug($"[EmailOutlookNative] step=open-external-failed error={ex.Message}");
            }
        });
    }

    private async Task HandleReopenNativeWebmailAsync(JsonElement root)
    {
        string accountId = GetString(root, "accountId");
        LogDebug($"[EmailOutlookNative] step=open-atlas accountId={accountId}");
        const string loginUrl = "https://login.live.com/login.srf?wa=wsignin1.0&wreply=https%3A%2F%2Foutlook.live.com%2Fmail%2F";
        LogDebug($"[EmailOutlookNative] step=navigate-start accountId={accountId} target=outlook-login");
        try
        {
            if (_nativeGmailView?.CoreWebView2 != null)
            {
                await Dispatcher.InvokeAsync(() => _nativeGmailView.CoreWebView2.Navigate(loginUrl));
            }
            else
            {
                StoredEmailAccount? account = _accounts.FirstOrDefault(a =>
                    string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
                if (account != null)
                {
                    await OpenGmailNativeAsync(account, navigateIfNew: true);
                }
                else
                {
                    LogDebug($"[EmailOutlookNative] step=navigate-skipped reason=account-not-found accountId={accountId}");
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[EmailOutlookNative] step=navigate-failed error={ex.Message}");
        }
    }

    private static bool IsMailSource(string? source, string? provider)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (string.Equals(provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            return source.Contains("outlook.live.com/mail", StringComparison.OrdinalIgnoreCase)
                   || source.Contains("outlook.office.com/mail", StringComparison.OrdinalIgnoreCase)
                   || source.Contains("outlook.office365.com/mail", StringComparison.OrdinalIgnoreCase)
                   || source.Contains("outlook.office.com/owa", StringComparison.OrdinalIgnoreCase)
                   || source.Contains("mail.live.com", StringComparison.OrdinalIgnoreCase);
        }

        return source.Contains("mail.google.com/accounts", StringComparison.OrdinalIgnoreCase)
               || source.Contains("mail.google.com/mail", StringComparison.OrdinalIgnoreCase)
               || source.Contains("mail.google.com", StringComparison.OrdinalIgnoreCase);
    }

    private void LogNativeOverlayGate(string reason, double? width = null, double? height = null)
    {
        try
        {
            bool boundsProvided = width.HasValue && height.HasValue;
            bool boundsValid = boundsProvided && width.Value > 4 && height.Value > 4;
            string boundsText = boundsProvided ? $"{width.Value:F1}x{height.Value:F1}" : "n/a";

            LogDebug(
                $"[EmailNativeGate] reason={reason} mode={_activeViewMode} settingsOpen={_settingsPanelOpen} nativeHostAllowed={_nativeHostAllowed} connectionError={_reactConnectionError} selected={_reactSelectedAccountId} nativeAccount={_nativeViewAccountId} nativeViewExists={_nativeGmailView != null} shouldShow={ShouldShowNativeOverlay()} bounds={boundsText} boundsValid={boundsValid}");
        }
        catch
        {
        }
    }

    private void DisposeNativeGmailView()
    {
        try
        {
            SetApiLockedCenterOverlay(_lastApiLockedCenterAccountId ?? string.Empty, visible: false, tokenExists: false);
            if (_nativeGmailView != null)
            {
                NativeOverlayLayer.Children.Remove(_nativeGmailView);
                _nativeGmailView.Dispose();
                _nativeGmailView = null;
            }

            _nativeLockedCardActive = false;

            _nativeViewAccountId = null;
            _nativeCoreInitialized = false;
            _nativeNavigationCompletedSuccess = false;
            _nativeLastSource = string.Empty;
        }
        catch (Exception ex)
        {
            LogDebug($"dispose native view failed: {ex.Message}");
        }
    }

    private StoredEmailAccount UpsertAccount(string accountId, string provider, string email)
    {
        StoredEmailAccount? account = _accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (account == null)
        {
            account = new StoredEmailAccount { Id = accountId };
            _accounts.Add(account);
        }

        account.Provider = string.IsNullOrWhiteSpace(provider) ? "gmail" : provider.Trim().ToLowerInvariant();
        account.Email = (email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(account.DisplayName) && !string.IsNullOrWhiteSpace(account.Email))
        {
            account.DisplayName = account.Email.Split('@').FirstOrDefault() ?? account.Email;
        }

        if (account.LastUsedUtc == default)
        {
            account.LastUsedUtc = DateTime.UtcNow;
        }

        return account;
    }

    private async Task PostSavedAccountsAsync()
    {
        int count = _accounts.Count;
        StoredEmailAccount? a0 = count > 0 ? _accounts[0] : null;
        StoredEmailAccount? a1 = count > 1 ? _accounts[1] : null;
        string FormatAccount(StoredEmailAccount? a) => a == null
            ? "-"
            : $"{a.Id}/{a.Email}/{a.Provider}/{a.Status}/{a.UnreadCount}";

        LogDebug($"[EmailSavedAccountsPost] count={count} active={_activeAccountId ?? string.Empty} a0={FormatAccount(a0)} a1={FormatAccount(a1)}");

        var accountsPayload = _accounts.Select(a =>
        {
            bool apiTokenExists = HasProviderApiToken(a);
            bool isApiLocked = a.Provider == "gmail" && !apiTokenExists;
            int postedUnread = ShouldSuppressUnreadCount(a, apiTokenExists) ? 0 : a.UnreadCount;
            LogDebug($"[EmailApiTokenState] accountId={a.Id} provider={a.Provider} apiTokenExists={apiTokenExists} isApiLocked={isApiLocked} postedUnread={postedUnread}");

            // When API is locked: post status="connected" so React evaluates the H condition
            // (locked card), not the loading spinner. Post unreadCount=null so hasUnreadCount=false.
            // Post apiStatus="locked" so H's apiStatus!=="connected" check passes.
            return new
            {
                id = a.Id,
                provider = a.Provider,
                email = a.Email,
                displayName = a.DisplayName,
                unreadCount = isApiLocked ? (object?)null : (object?)postedUnread,
                status = isApiLocked ? "connected" : a.Status,
                apiStatus = isApiLocked ? (object?)"locked" : (object?)null,
                isPinned = a.IsPinned,
                lastUsed = a.LastUsedUtc == default ? DateTime.UtcNow : a.LastUsedUtc,
            };
        }).ToArray();

        await PostAsync(new
        {
            type = "saved-accounts",
            activeAccountId = _activeAccountId ?? string.Empty,
            accounts = accountsPayload,
        });
    }

    private Task PostAsync(object payload)
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (EmailWebView?.CoreWebView2 == null)
                        {
                            return;
                        }

                        string marshaledJson = JsonSerializer.Serialize(payload, CamelCaseJson);
                        if (TryGetPostedType(marshaledJson, out string? marshaledType) &&
                            !string.IsNullOrWhiteSpace(marshaledType) &&
                            marshaledType.StartsWith("email-agent-", StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug($"[EmailAgentBridge] post type={marshaledType}");
                        }

                        EmailWebView.CoreWebView2.PostWebMessageAsJson(marshaledJson);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"post message failed: {ex.Message}");
                    }
                });

                return Task.CompletedTask;
            }

            if (EmailWebView?.CoreWebView2 == null)
            {
                return Task.CompletedTask;
            }

            string json = JsonSerializer.Serialize(payload, CamelCaseJson);
            if (TryGetPostedType(json, out string? type) &&
                !string.IsNullOrWhiteSpace(type) &&
                type.StartsWith("email-agent-", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"[EmailAgentBridge] post type={type}");
            }

            EmailWebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            LogDebug($"post message failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task PostAgentMicTranscriptAsync(string transcript)
    {
        string text = (transcript ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await PostAsync(new
        {
            type = "email-agent-mic-transcript",
            text,
        });
    }

    private static bool TryGetPostedType(string json, out string? type)
    {
        type = null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("type", out JsonElement typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                type = typeElement.GetString();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void LoadPersistedAccounts()
    {
        _accounts.Clear();
        string path = GetAccountsFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            StoredEmailAccount[] loaded = JsonSerializer.Deserialize<StoredEmailAccount[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? Array.Empty<StoredEmailAccount>();

            foreach (StoredEmailAccount account in loaded)
            {
                if (string.IsNullOrWhiteSpace(account.Email) && string.IsNullOrWhiteSpace(account.Id))
                {
                    continue;
                }

                string stableId = ComputeStableAccountId(account.Provider, account.Email, account.Id);
                account.Id = stableId;
                account.Provider = string.IsNullOrWhiteSpace(account.Provider) ? "gmail" : account.Provider;

                bool hasProfile = Directory.Exists(GetAccountProfileDirectory(account.Id));
                bool hasToken = HasPersistedProviderToken(account.Provider, account.Id);

                if (IsVerifierIdentity(account.Provider, account.Email, account.Id) && !hasProfile && !hasToken)
                {
                    LogDebug($"[EmailPersistedAccounts] skipped verifier entry accountId={account.Id} email={account.Email}");
                    continue;
                }

                // Validate persisted "connected" status: only keep it if there is evidence
                // that the session/token is actually available (WebView2 profile dir or stored API token).
                // Otherwise reset to "signed-out" so the UI never shows a fake connected badge.
                if (string.Equals(account.Status, "connected", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasProfile && !hasToken)
                        account.Status = "signed-out";
                }
                else if (string.IsNullOrWhiteSpace(account.Status))
                {
                    account.Status = "signed-out";
                }

                // Restore the previously active account so it can be auto-opened after shell loads.
                if (account.IsActive && string.IsNullOrWhiteSpace(_activeAccountId))
                    _activeAccountId = account.Id;

                _accounts.Add(account);
            }

            PersistAccounts();
        }
        catch (Exception ex)
        {
            LogDebug($"load accounts failed: {ex.Message}");
        }
    }

    private void PersistAccounts()
    {
        try
        {
            // Stamp IsActive so the active account is restored correctly on next launch.
            foreach (StoredEmailAccount a in _accounts)
                a.IsActive = string.Equals(a.Id, _activeAccountId, StringComparison.OrdinalIgnoreCase);

            string path = GetAccountsFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_accounts, CamelCaseJson);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            LogDebug($"persist accounts failed: {ex.Message}");
        }
    }

    private bool HasProviderApiToken(StoredEmailAccount account)
    {
        if (string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            return GmailOAuthTokenStore.Load(account.Id) != null;
        }

        if (string.Equals(account.Provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            return OutlookOAuthTokenStore.Load(account.Id) != null;
        }

        return false;
    }

    private static bool IsVerifierIdentity(string provider, string email, string accountId)
    {
        string normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedId = (accountId ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedId) && normalizedId.Contains("-verify-", StringComparison.Ordinal))
        {
            return true;
        }

        int atIndex = normalizedEmail.IndexOf('@');
        if (atIndex <= 0 || atIndex >= normalizedEmail.Length - 1)
        {
            return false;
        }

        string localPart = normalizedEmail[..atIndex];
        string domain = normalizedEmail[(atIndex + 1)..];

        if (!string.Equals(domain, "example.test", StringComparison.Ordinal))
        {
            return false;
        }

        if (localPart.StartsWith("verify.", StringComparison.Ordinal))
        {
            return true;
        }

        return localPart.StartsWith($"verify-{normalizedProvider}", StringComparison.Ordinal);
    }

    private static bool HasPersistedProviderToken(string provider, string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        if (string.Equals(provider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            return GmailOAuthTokenStore.Load(accountId) != null;
        }

        if (string.Equals(provider, "outlook", StringComparison.OrdinalIgnoreCase))
        {
            return OutlookOAuthTokenStore.Load(accountId) != null;
        }

        return false;
    }

    private bool HasGmailApiToken(StoredEmailAccount account)
    {
        if (!string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GmailOAuthTokenStore.Load(account.Id) != null;
    }

    private static bool ShouldSuppressUnreadCount(StoredEmailAccount account, bool apiTokenExists)
    {
        return string.Equals(account.Provider, "gmail", StringComparison.OrdinalIgnoreCase) && !apiTokenExists;
    }

    private static bool IsValidEmailAddress(string value)
    {
        string email = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Contains(' '))
        {
            return false;
        }

        int atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex != email.LastIndexOf('@'))
        {
            return false;
        }

        string local = email[..atIndex];
        string domain = email[(atIndex + 1)..];
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        return domain.Contains('.', StringComparison.Ordinal);
    }

    private static string ComputeStableAccountId(string provider, string email, string? requestedId)
    {
        string safeProvider = SlugifySegment(string.IsNullOrWhiteSpace(provider) ? "gmail" : provider);
        string safeEmail = SlugifySegment(email);
        string stableId = string.IsNullOrWhiteSpace(safeEmail)
            ? safeProvider
            : $"{safeProvider}-{safeEmail}";

        if (string.IsNullOrWhiteSpace(requestedId))
        {
            return stableId;
        }

        return stableId;
    }

    private static string SlugifySegment(string value)
    {
        char[] chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        string collapsed = new string(chars);
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return collapsed.Trim('-');
    }

    private string GetAccountsFilePath() => Path.Combine(GetEmailDataDirectory(), "accounts.json");

    private string GetEmailDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasOS",
            "Email");
    }

    private string GetAccountProfileDirectory(string accountId)
    {
        return Path.Combine(GetEmailDataDirectory(), "Accounts", accountId);
    }

    private string? FindEmailDist()
    {
        var probes = new List<string>();

        try { probes.Add(Path.Combine(AppContext.BaseDirectory, "Figma", "Email", "dist")); } catch { }
        try { probes.Add(Path.Combine(AppContext.BaseDirectory, "Atlas_v2.exe.WebView2", "Email", "dist")); } catch { }

        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            probes.Add(Path.Combine(current, "Figma", "Email", "dist"));
            current = Directory.GetParent(current)?.FullName;
        }

        foreach (string probe in probes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(Path.Combine(probe, "index.html")))
                {
                    return probe;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : string.Empty;
    }

    private static double GetDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result) ? result : 0;
    }

    private static int GetInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : fallback;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed) && parsed);
    }

    private void LogDebug(string message)
    {
        try
        {
            string path = Path.Combine(GetEmailDataDirectory(), "email-debug.log");
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private readonly record struct HostBounds(double Left, double Top, double Width, double Height);

    private sealed class EmailAgentMessageMeta
    {
        public string Id { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public bool IsUnread { get; set; }
        public string Provider { get; set; } = string.Empty;
        public List<string> Labels { get; set; } = new();
    }

    private sealed class EmailAgentSelectedContext
    {
        public string MessageId { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string PlainText { get; set; } = string.Empty;
    }

    private sealed class StoredEmailAccount
    {
        public string Id { get; set; } = string.Empty;
        public string Provider { get; set; } = "gmail";
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public string Status { get; set; } = "signed-out";
        public bool IsPinned { get; set; }
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>True for the account that was selected when the app last closed. Used to restore selection on next launch.</summary>
        public bool IsActive { get; set; }
    }
}
