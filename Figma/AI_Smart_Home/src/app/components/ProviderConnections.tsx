import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { CircleAlert, KeyRound, Link2, RefreshCcw, Save, ShieldCheck, Wifi } from 'lucide-react';
import { discoverLgTv, linkHueBridge, linkLgTv, loginRing, refreshState, saveProviderSettings } from '../bridge';
import type { ProviderFormValues, SmartHomeProviderState } from '../types';

interface ProviderConnectionsProps {
  providers: SmartHomeProviderState[];
  activeProviderId?: string | null;
}

function fieldLabel(field: string) {
  switch (field) {
    case 'api_key':
      return 'API Key';
    case 'bridge_ip':
      return 'Bridge IP';
    case 'application_key':
      return 'Application Key';
    case 'refresh_token':
      return 'Refresh Token';
    case 'host':
      return 'Host';
    case 'client_key':
      return 'Client Key';
    case 'mac_address':
      return 'MAC Address';
    case 'access_token':
      return 'Access Token';
    case 'base_url':
      return 'Base URL';
    case 'username':
      return 'Username';
    case 'password':
      return 'Password';
    case 'rtsp_url':
      return 'RTSP URL';
    case 'location_id':
      return 'Location ID';
    default:
      return field;
  }
}

function normalizeLgHostInput(value: string) {
  return value
    .trim()
    .replace(/^wss?:\/\//i, '')
    .replace(/^https?:\/\//i, '')
    .replace(/\/$/, '');
}

function normalizeHueBridgeInput(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return '';
  }

  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (Array.isArray(parsed)) {
      const first = parsed.find((item) => typeof item === 'object' && item !== null && 'internalipaddress' in item) as { internalipaddress?: unknown } | undefined;
      if (typeof first?.internalipaddress === 'string') {
        return first.internalipaddress;
      }
    }

    if (parsed && typeof parsed === 'object' && 'internalipaddress' in parsed) {
      const ip = (parsed as { internalipaddress?: unknown }).internalipaddress;
      if (typeof ip === 'string') {
        return ip;
      }
    }
  } catch {
  }

  return trimmed
    .replace(/^https?:\/\//i, '')
    .replace(/\/$/, '');
}

export function ProviderConnections({ providers, activeProviderId }: ProviderConnectionsProps) {
  const [forms, setForms] = useState<Record<string, ProviderFormValues>>({});
  const [savingProviderId, setSavingProviderId] = useState<string | null>(null);
  const [linkingProviderId, setLinkingProviderId] = useState<string | null>(null);
  const [discoveringProviderId, setDiscoveringProviderId] = useState<string | null>(null);
  const [authenticatingProviderId, setAuthenticatingProviderId] = useState<string | null>(null);

  useEffect(() => {
    setForms((current) => {
      const next = { ...current };

      for (const provider of providers) {
        const existing = current[provider.providerId] ?? {};
        next[provider.providerId] = {
          apiKey: existing.apiKey?.trim() ? existing.apiKey : provider.savedSettings.apiKey,
          bridgeIp: existing.bridgeIp?.trim() ? existing.bridgeIp : provider.savedSettings.bridgeIp,
          applicationKey: existing.applicationKey?.trim() ? existing.applicationKey : provider.savedSettings.applicationKey,
          refreshToken: existing.refreshToken?.trim() ? existing.refreshToken : provider.savedSettings.refreshToken,
          host: existing.host?.trim() ? existing.host : provider.savedSettings.host,
          clientKey: existing.clientKey?.trim() ? existing.clientKey : provider.savedSettings.clientKey,
          accessToken: existing.accessToken?.trim() ? existing.accessToken : provider.savedSettings.accessToken,
          baseUrl: existing.baseUrl?.trim() ? existing.baseUrl : provider.savedSettings.baseUrl,
          username: existing.username?.trim() ? existing.username : provider.savedSettings.username,
          password: existing.password?.trim() ? existing.password : provider.savedSettings.password,
          rtspUrl: existing.rtspUrl?.trim() ? existing.rtspUrl : provider.savedSettings.rtspUrl,
          locationId: existing.locationId?.trim() ? existing.locationId : provider.savedSettings.locationId,
          enabled: existing.enabled ?? provider.savedSettings.enabled ?? provider.descriptor.isConfigured,
          email: existing.email ?? '',
          twoFactorCode: existing.twoFactorCode ?? '',
        };
      }

      return next;
    });
  }, [providers]);

  useEffect(() => {
    if (!activeProviderId) {
      return;
    }

    let attempts = 0;
    const focusProvider = () => {
      const container = document.getElementById(`provider-${activeProviderId}`);
      if (!container) {
        if (attempts < 8) {
          attempts += 1;
          window.setTimeout(focusProvider, 120);
        }
        return;
      }

      container.scrollIntoView({ behavior: 'smooth', block: 'start' });
      const target = container.querySelector('input, button, textarea') as HTMLElement | null;
      target?.focus();
    };

    window.setTimeout(focusProvider, 60);
  }, [activeProviderId]);

  const setField = (providerId: string, patch: ProviderFormValues) => {
    setForms((current) => ({
      ...current,
      [providerId]: {
        ...current[providerId],
        ...patch,
      },
    }));
  };

  const handleSave = async (providerId: string) => {
    setSavingProviderId(providerId);
    saveProviderSettings(providerId, forms[providerId] ?? {});
    window.setTimeout(() => {
      refreshState();
      setSavingProviderId(null);
    }, 500);
  };

  const handleHueLink = async () => {
    const providerId = 'philips_hue';
    const bridgeIp = normalizeHueBridgeInput(forms[providerId]?.bridgeIp ?? '');
    setField(providerId, { bridgeIp });
    setLinkingProviderId(providerId);
    linkHueBridge(bridgeIp);
    window.setTimeout(() => {
      refreshState();
      setLinkingProviderId(null);
    }, 1500);
  };

  const handleLgLink = async () => {
    const providerId = 'lg_webos';
    const host = normalizeLgHostInput(forms[providerId]?.host ?? '');
    setField(providerId, { host });
    setLinkingProviderId(providerId);
    linkLgTv(host);
    window.setTimeout(() => {
      refreshState();
      setLinkingProviderId(null);
    }, 2000);
  };

  const handleLgDiscovery = async () => {
    const providerId = 'lg_webos';
    setDiscoveringProviderId(providerId);
    discoverLgTv();
    window.setTimeout(() => {
      refreshState();
      setDiscoveringProviderId(null);
    }, 3000);
  };

  const handleRingLogin = async () => {
    const providerId = 'ring';
    const email = (forms[providerId]?.email ?? '').trim();
    const password = forms[providerId]?.password ?? '';
    const code = (forms[providerId]?.twoFactorCode ?? '').trim();

    setAuthenticatingProviderId(providerId);
    loginRing(email, password, code);
    window.setTimeout(() => {
      refreshState();
      setAuthenticatingProviderId(null);
    }, 2500);
  };

  return (
    <div className="mt-8">
      <div className="flex items-center justify-between gap-4 mb-4">
        <h3 className="text-sm text-cyan-400/80 flex items-center gap-2">
          <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          Provider Connections
        </h3>

        <button
          onClick={() => refreshState()}
          className="px-4 py-2 rounded-lg text-xs text-cyan-300 flex items-center gap-2"
          style={{
            background: 'rgba(0, 212, 255, 0.08)',
            border: '1px solid rgba(0, 212, 255, 0.25)',
          }}
        >
          <RefreshCcw className="w-3.5 h-3.5" />
          Refresh Providers
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {providers.map((provider, index) => {
          const form = forms[provider.providerId] ?? {};
          const isSaving = savingProviderId === provider.providerId;
          const isLinking = linkingProviderId === provider.providerId;
          const isDiscovering = discoveringProviderId === provider.providerId;
          const isAuthenticating = authenticatingProviderId === provider.providerId;
          const hasError = Boolean(provider.error);
          const isLive = !hasError && provider.devices.length > 0;
          const statusTone = hasError ? '#FFB5B5' : isLive ? '#7CFFB2' : provider.descriptor.isConfigured ? '#FFDC8A' : '#FFB970';
          const statusBorder = hasError
            ? 'rgba(255,102,102,0.35)'
            : isLive
              ? 'rgba(124,255,178,0.4)'
              : provider.descriptor.isConfigured
                ? 'rgba(255,220,138,0.35)'
                : 'rgba(255,185,112,0.35)';
          const statusBackground = hasError
            ? 'rgba(255,102,102,0.08)'
            : isLive
              ? 'rgba(124,255,178,0.08)'
              : provider.descriptor.isConfigured
                ? 'rgba(255,220,138,0.08)'
                : 'rgba(255,185,112,0.08)';
          const statusLabel = hasError ? 'Needs Attention' : isLive ? 'Live' : provider.descriptor.isConfigured ? 'Saved' : 'Needs Setup';

          return (
            <motion.div
              key={provider.providerId}
              id={`provider-${provider.providerId}`}
              initial={{ opacity: 0, y: 16 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: index * 0.08 }}
              className="rounded-2xl p-5 backdrop-blur-xl"
              style={{
                background: provider.providerId === activeProviderId ? 'rgba(8, 24, 38, 0.84)' : 'rgba(5, 10, 18, 0.64)',
                border: provider.providerId === activeProviderId ? '1px solid rgba(34, 211, 238, 0.55)' : '1px solid rgba(0, 212, 255, 0.24)',
                boxShadow: provider.providerId === activeProviderId
                  ? '0 0 0 1px rgba(34, 211, 238, 0.22), 0 0 36px rgba(34, 211, 238, 0.18), inset 0 0 28px rgba(34, 211, 238, 0.06)'
                  : '0 0 28px rgba(0, 212, 255, 0.12), inset 0 0 28px rgba(0, 212, 255, 0.04)',
              }}
            >
              <div className="flex items-start justify-between gap-3 mb-4">
                <div>
                  <p className="text-lg font-semibold text-cyan-300">{provider.displayName}</p>
                  <p className="text-xs text-cyan-400/60 mt-1">{provider.descriptor.status}</p>
                </div>

                <div
                  className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-[0.18em]"
                  style={{
                    color: statusTone,
                    border: `1px solid ${statusBorder}`,
                    background: statusBackground,
                  }}
                >
                  {statusLabel}
                </div>
              </div>

              <p className="text-sm leading-6 text-cyan-100/72 mb-4">{provider.descriptor.detail}</p>

              <div className="flex flex-wrap gap-2 mb-4">
                {provider.descriptor.requiredFields.map((field) => {
                  const stored = provider.descriptor.configuredFields.includes(field);
                  return (
                    <div
                      key={field}
                      className="px-2.5 py-1 rounded-full text-[11px] flex items-center gap-1.5"
                      style={{
                        background: stored ? 'rgba(124,255,178,0.08)' : 'rgba(0, 212, 255, 0.06)',
                        border: `1px solid ${stored ? 'rgba(124,255,178,0.28)' : 'rgba(0, 212, 255, 0.18)'}`,
                        color: stored ? '#7CFFB2' : '#7CDFFF',
                      }}
                    >
                      {stored ? <ShieldCheck className="w-3 h-3" /> : <KeyRound className="w-3 h-3" />}
                      {fieldLabel(field)}
                    </div>
                  );
                })}
              </div>

              <div className="space-y-3">
                {provider.providerId === 'govee' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">Govee uses the cloud developer API.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            Paste your Govee API key, save it, then refresh providers. Atlas will pull the live device list, state, and any scene options published by your Govee devices.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.apiKey ?? ''}
                      onChange={(event) => setField(provider.providerId, { apiKey: event.target.value, enabled: true })}
                      placeholder="Paste your Govee developer API key"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                  </>
                )}

                {provider.providerId === 'philips_hue' && (
                  <>
                    <input
                      value={form.bridgeIp ?? ''}
                      onChange={(event) => setField(provider.providerId, { bridgeIp: event.target.value })}
                      onBlur={(event) => setField(provider.providerId, { bridgeIp: normalizeHueBridgeInput(event.target.value) })}
                      placeholder="Hue bridge IP, hostname, or discovery JSON"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <input
                      value={form.applicationKey ?? ''}
                      onChange={(event) => setField(provider.providerId, { applicationKey: event.target.value, enabled: true })}
                      placeholder="Hue application key"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <button
                      type="button"
                      onClick={() => void handleHueLink()}
                      disabled={isLinking}
                      className="w-full px-4 py-2.5 rounded-xl text-xs text-cyan-200 flex items-center justify-center gap-2 disabled:opacity-60"
                      style={{
                        background: 'rgba(0, 212, 255, 0.08)',
                        border: '1px solid rgba(0, 212, 255, 0.28)',
                      }}
                    >
                      <Link2 className="w-3.5 h-3.5" />
                      {isLinking ? 'Linking Bridge...' : 'Press Hue Button, Then Link Bridge'}
                    </button>
                  </>
                )}

                {provider.providerId === 'ring' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">Ring setup stays inside Atlas.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            This button does not open Ring.com. Enter your Ring email and password here, click the button, and if Ring requires 2FA Atlas will ask you to enter the verification code and click again.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.refreshToken ?? ''}
                      onChange={(event) => setField(provider.providerId, { refreshToken: event.target.value, enabled: true })}
                      placeholder="Ring refresh token"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      <input
                        value={form.email ?? ''}
                        onChange={(event) => setField(provider.providerId, { email: event.target.value })}
                        placeholder="Ring email"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                      <input
                        type="password"
                        value={form.password ?? ''}
                        onChange={(event) => setField(provider.providerId, { password: event.target.value })}
                        placeholder="Ring password"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                    </div>
                    <input
                      value={form.twoFactorCode ?? ''}
                      onChange={(event) => setField(provider.providerId, { twoFactorCode: event.target.value })}
                      placeholder="Ring verification code if prompted"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <button
                      type="button"
                      onClick={() => void handleRingLogin()}
                      disabled={isAuthenticating || !String(form.email ?? '').trim() || !String(form.password ?? '').trim()}
                      className="w-full px-4 py-2.5 rounded-xl text-xs text-cyan-200 flex items-center justify-center gap-2 disabled:opacity-60"
                      style={{
                        background: 'rgba(0, 212, 255, 0.08)',
                        border: '1px solid rgba(0, 212, 255, 0.28)',
                      }}
                    >
                      <Link2 className="w-3.5 h-3.5" />
                      {isAuthenticating ? 'Signing In To Ring...' : 'Sign In Here And Discover Ring Devices'}
                    </button>
                  </>
                )}

                {provider.providerId === 'lg_webos' && (
                  <>
                    <button
                      type="button"
                      onClick={() => void handleLgDiscovery()}
                      disabled={isDiscovering}
                      className="w-full px-4 py-2.5 rounded-xl text-xs text-cyan-200 flex items-center justify-center gap-2 disabled:opacity-60"
                      style={{
                        background: 'rgba(0, 212, 255, 0.08)',
                        border: '1px solid rgba(0, 212, 255, 0.28)',
                      }}
                    >
                      <Wifi className="w-3.5 h-3.5" />
                      {isDiscovering ? 'Scanning Network For LG TV...' : 'Find LG TV On Network'}
                    </button>
                    <input
                      value={form.host ?? ''}
                      onChange={(event) => setField(provider.providerId, { host: event.target.value })}
                      onBlur={(event) => setField(provider.providerId, { host: normalizeLgHostInput(event.target.value) })}
                      placeholder="LG TV IP or hostname"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <input
                      value={form.clientKey ?? ''}
                      onChange={(event) => setField(provider.providerId, { clientKey: event.target.value, enabled: true })}
                      placeholder="webOS client key"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <button
                      type="button"
                      onClick={() => void handleLgLink()}
                      disabled={isLinking}
                      className="w-full px-4 py-2.5 rounded-xl text-xs text-cyan-200 flex items-center justify-center gap-2 disabled:opacity-60"
                      style={{
                        background: 'rgba(0, 212, 255, 0.08)',
                        border: '1px solid rgba(0, 212, 255, 0.28)',
                      }}
                    >
                      <Link2 className="w-3.5 h-3.5" />
                      {isLinking ? 'Waiting For TV Pairing...' : 'Show LG Pairing Prompt'}
                    </button>
                  </>
                )}

                {provider.providerId === 'smartthings' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">SmartThings uses a personal access token.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            Paste a SmartThings personal access token. Location ID is optional if you want Atlas to limit the view to one location.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.accessToken ?? ''}
                      onChange={(event) => setField(provider.providerId, { accessToken: event.target.value, enabled: true })}
                      placeholder="SmartThings personal access token"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <input
                      value={form.locationId ?? ''}
                      onChange={(event) => setField(provider.providerId, { locationId: event.target.value })}
                      placeholder="Optional SmartThings location id"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                  </>
                )}

                {provider.providerId === 'home_assistant' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">Home Assistant uses a base URL and long-lived token.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            Point Atlas at your Home Assistant instance and paste a long-lived access token to load lights, switches, locks, media players, and other supported entities.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.baseUrl ?? ''}
                      onChange={(event) => setField(provider.providerId, { baseUrl: event.target.value })}
                      placeholder="Home Assistant base URL, e.g. http://homeassistant.local:8123"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <input
                      value={form.accessToken ?? ''}
                      onChange={(event) => setField(provider.providerId, { accessToken: event.target.value, enabled: true })}
                      placeholder="Home Assistant long-lived access token"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                  </>
                )}

                {provider.providerId === 'tapo_kasa' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">TP-Link Kasa / Tapo currently uses a direct local device host.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            Enter the device IP or hostname. Compatible local Kasa devices work best today; some newer Tapo hardware may still need a different auth path.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.host ?? ''}
                      onChange={(event) => setField(provider.providerId, { host: event.target.value, enabled: true })}
                      placeholder="TP-Link device IP or hostname"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      <input
                        value={form.username ?? ''}
                        onChange={(event) => setField(provider.providerId, { username: event.target.value })}
                        placeholder="Optional username"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                      <input
                        type="password"
                        value={form.password ?? ''}
                        onChange={(event) => setField(provider.providerId, { password: event.target.value })}
                        placeholder="Optional password"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                    </div>
                  </>
                )}

                {provider.providerId === 'onvif_rtsp' && (
                  <>
                    <div
                      className="rounded-2xl px-4 py-3 text-sm text-cyan-100/72"
                      style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                    >
                      <div className="flex items-start gap-3">
                        <CircleAlert className="w-4 h-4 mt-0.5 text-cyan-300" />
                        <div>
                          <p className="text-cyan-100 font-medium">ONVIF / RTSP is the manual camera endpoint path.</p>
                          <p className="text-xs text-cyan-100/62 mt-1 leading-5">
                            Enter a camera host and optionally a direct RTSP URL. Atlas will surface the camera endpoint inside Smart Home and use the host as the fallback page target.
                          </p>
                        </div>
                      </div>
                    </div>
                    <input
                      value={form.host ?? ''}
                      onChange={(event) => setField(provider.providerId, { host: event.target.value })}
                      placeholder="Camera host or NVR host"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      <input
                        value={form.username ?? ''}
                        onChange={(event) => setField(provider.providerId, { username: event.target.value })}
                        placeholder="Camera username"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                      <input
                        type="password"
                        value={form.password ?? ''}
                        onChange={(event) => setField(provider.providerId, { password: event.target.value })}
                        placeholder="Camera password"
                        className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                        style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                      />
                    </div>
                    <input
                      value={form.rtspUrl ?? ''}
                      onChange={(event) => setField(provider.providerId, { rtspUrl: event.target.value, enabled: true })}
                      placeholder="Optional direct RTSP URL"
                      className="w-full px-3 py-2.5 rounded-xl text-sm bg-transparent outline-none"
                      style={{ border: '1px solid rgba(0, 212, 255, 0.26)', color: '#B8F7FF' }}
                    />
                  </>
                )}

                <label className="flex items-center gap-2 text-xs text-cyan-200/72">
                  <input
                    type="checkbox"
                    checked={Boolean(form.enabled)}
                    onChange={(event) => setField(provider.providerId, { enabled: event.target.checked })}
                  />
                  Enable {provider.displayName} in Atlas
                </label>

                <div className="flex items-center justify-between gap-3 pt-2">
                  <div className="text-xs text-cyan-400/60 flex items-center gap-2">
                    <Wifi className="w-3.5 h-3.5" />
                    {provider.devices.length} discovered device{provider.devices.length === 1 ? '' : 's'}
                  </div>

                  <button
                    onClick={() => void handleSave(provider.providerId)}
                    disabled={isSaving}
                    className="px-4 py-2 rounded-lg text-xs text-cyan-200 flex items-center gap-2 disabled:opacity-60"
                    style={{
                      background: 'linear-gradient(135deg, rgba(0, 212, 255, 0.18), rgba(0, 102, 255, 0.18))',
                      border: '1px solid rgba(0, 212, 255, 0.35)',
                    }}
                  >
                    <Save className="w-3.5 h-3.5" />
                    {isSaving ? 'Saving...' : 'Save'}
                  </button>
                </div>
              </div>

              {provider.error && (
                <div
                  className="mt-4 rounded-xl px-3 py-2 text-xs"
                  style={{
                    color: '#FFB5B5',
                    background: 'rgba(255, 102, 102, 0.08)',
                    border: '1px solid rgba(255, 102, 102, 0.22)',
                  }}
                >
                  {provider.error}
                </div>
              )}
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}