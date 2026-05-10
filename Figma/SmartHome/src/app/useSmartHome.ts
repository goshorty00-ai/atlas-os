import { useState, useEffect, useCallback, useRef } from 'react';
import { postToHost, onHostMessage, requestState } from './bridge';

// ── Types matching C# SmartHomeSnapshot ──────────────────────────────────────

export interface SmartHomeCapabilityOption {
  name: string;
  value: unknown;
}

export interface SmartHomeCapability {
  type: string;
  instance: string;
  dataType: string;
  unit: string;
  min?: number;
  max?: number;
  hasState: boolean;
  stateValue: unknown;
  options: SmartHomeCapabilityOption[];
}

export interface SmartHomeDevice {
  deviceId: string;
  name: string;
  sku: string;
  deviceType: string;
  isOnline?: boolean;
  previewImageUrl: string;
  previewVideoUrl: string;
  externalUrl: string;
  capabilities: SmartHomeCapability[];
}

export interface SmartHomeProviderDescriptor {
  providerId: string;
  displayName: string;
  status: string;
  isConfigured: boolean;
  requiredFields: string[];
  configuredFields: string[];
  detail: string;
}

export interface SmartHomeProviderFormState {
  enabled: boolean;
  apiKey: string;
  bridgeIp: string;
  applicationKey: string;
  refreshToken: string;
  host: string;
  clientKey: string;
}

export interface SmartHomeProviderState {
  providerId: string;
  displayName: string;
  descriptor: SmartHomeProviderDescriptor;
  savedSettings: SmartHomeProviderFormState;
  devices: SmartHomeDevice[];
  error: string;
}

export interface SmartHomeAgentSettings {
  voiceCommandsEnabled: boolean;
  showDeviceShortcutsInSidebar: boolean;
  defaultVolumeStep: number;
}

export interface SmartHomeSavedCommand {
  id: string;
  enabled: boolean;
  phrase: string;
  providerId: string;
  deviceId: string;
  sku: string;
  capabilityType: string;
  capabilityInstance: string;
  value: unknown;
  responseText: string;
}

export interface SmartHomeSavedGreeting {
  id: string;
  enabled: boolean;
  phrase: string;
  responseText: string;
}

export interface SmartHomeSnapshot {
  generatedAtUtc: string;
  providers: SmartHomeProviderState[];
  totalDevices: number;
  onlineDevices: number;
  configuredProviders: number;
  agentSettings: SmartHomeAgentSettings;
  customGreetings: SmartHomeSavedGreeting[];
  customCommands: SmartHomeSavedCommand[];
}

// ── Hook ─────────────────────────────────────────────────────────────────────

export interface ActionResult {
  ok: boolean;
  message: string;
}

export function useSmartHome() {
  const [state, setState] = useState<SmartHomeSnapshot | null>(null);
  const [lastResult, setLastResult] = useState<ActionResult | null>(null);
  const [lastError, setLastError] = useState<string | null>(null);
  const pendingRef = useRef<Map<string, (result: ActionResult) => void>>(new Map());

  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      const p = payload as any;
      switch (type) {
        case 'smart-home.state':
          setState(p as SmartHomeSnapshot);
          break;
        case 'smart-home.actionResult':
          setLastResult({ ok: p?.ok ?? true, message: p?.message ?? '' });
          break;
        case 'smart-home.error':
          setLastError(p?.message ?? 'Unknown error');
          break;
        case 'smart-home.settingsSaved':
          setLastResult({ ok: true, message: 'Settings saved.' });
          break;
      }
    });

    // Request initial state
    requestState();

    return unsub;
  }, []);

  const executeAction = useCallback((
    providerId: string,
    deviceId: string,
    sku: string,
    capabilityType: string,
    capabilityInstance: string,
    value: unknown
  ) => {
    // Optimistic update - immediately reflect the change in local state
    setState(prev => {
      if (!prev) return prev;
      return {
        ...prev,
        providers: prev.providers.map(p => {
          if (p.providerId !== providerId) return p;
          return {
            ...p,
            devices: p.devices.map(d => {
              if (d.deviceId !== deviceId) return d;
              return {
                ...d,
                capabilities: d.capabilities.map(c => {
                  if (c.type === capabilityType && c.instance === capabilityInstance) {
                    return { ...c, stateValue: value, hasState: true };
                  }
                  return c;
                }),
              };
            }),
          };
        }),
      };
    });
    postToHost('smart-home.executeAction', { providerId, deviceId, sku, capabilityType, capabilityInstance, value });
  }, []);

  const runCommand = useCallback((text: string) => {
    postToHost('smart-home.runCommand', { text });
  }, []);

  const refresh = useCallback(() => {
    postToHost('smart-home.refresh');
  }, []);

  const saveSettings = useCallback((providerId: string, settings: Record<string, unknown>) => {
    postToHost('smart-home.saveSettings', { providerId, settings });
  }, []);

  const linkHueBridge = useCallback((bridgeIp: string) => {
    postToHost('smart-home.linkHueBridge', { bridgeIp });
  }, []);

  const linkLgTv = useCallback((host: string) => {
    postToHost('smart-home.linkLgTv', { host });
  }, []);

  const discoverLgTv = useCallback(() => {
    postToHost('smart-home.discoverLgTv');
  }, []);

  const loginRing = useCallback((email: string, password: string, code?: string) => {
    postToHost('smart-home.loginRing', { email, password, code: code ?? '' });
  }, []);

  const openExternalUrl = useCallback((url: string) => {
    postToHost('smart-home.openExternalUrl', { url });
  }, []);

  const startRingManagedLiveView = useCallback((requestId: string, deviceId: string) => {
    postToHost('smart-home.startRingManagedLiveView', { requestId, deviceId });
  }, []);

  const stopRingManagedLiveView = useCallback((requestId: string, deviceId: string) => {
    postToHost('smart-home.stopRingManagedLiveView', { requestId, deviceId });
  }, []);

  const saveCustomCommand = useCallback((cmd: Partial<SmartHomeSavedCommand>) => {
    postToHost('smart-home.saveCustomCommand', cmd);
  }, []);

  const deleteCustomCommand = useCallback((id: string) => {
    postToHost('smart-home.deleteCustomCommand', { id });
  }, []);

  const saveCustomGreeting = useCallback((greeting: Partial<SmartHomeSavedGreeting>) => {
    postToHost('smart-home.saveCustomGreeting', greeting);
  }, []);

  const deleteCustomGreeting = useCallback((id: string) => {
    postToHost('smart-home.deleteCustomGreeting', { id });
  }, []);

  const saveAgentSettings = useCallback((settings: Partial<SmartHomeAgentSettings>) => {
    postToHost('smart-home.saveAgentSettings', settings);
  }, []);

  // ── Helpers ────────────────────────────────────────────────────────────────

  const getProvider = useCallback((id: string) =>
    state?.providers.find(p => p.providerId === id) ?? null,
  [state]);

  const getAllDevices = useCallback(() =>
    state?.providers.flatMap(p => p.devices) ?? [],
  [state]);

  const getCapabilityState = useCallback((device: SmartHomeDevice, capType: string, instance: string) => {
    return device.capabilities.find(c => c.type === capType && c.instance === instance);
  }, []);

  const isDeviceOn = useCallback((device: SmartHomeDevice): boolean => {
    const cap = device.capabilities.find(c =>
      c.type === 'devices.capabilities.on_off' && c.instance === 'powerSwitch'
    );
    if (!cap || !cap.hasState) return false;
    const v = cap.stateValue as any;
    return v === true || v === 1 || v?.value === true || v?.value === 1;
  }, []);

  const getDeviceBrightness = useCallback((device: SmartHomeDevice): number => {
    const cap = device.capabilities.find(c =>
      c.type === 'devices.capabilities.range' && c.instance === 'brightness'
    );
    if (!cap || !cap.hasState) return 100;
    const v = cap.stateValue as any;
    const raw = typeof v === 'number' ? v : v?.value ?? 100;
    return Math.round(raw);
  }, []);

  const getDeviceVolume = useCallback((device: SmartHomeDevice): number => {
    const cap = device.capabilities.find(c =>
      c.type === 'devices.capabilities.range' && c.instance === 'volume'
    );
    if (!cap || !cap.hasState) return 0;
    const v = cap.stateValue as any;
    return typeof v === 'number' ? v : v?.value ?? 0;
  }, []);

  return {
    state,
    lastResult,
    lastError,
    clearError: () => setLastError(null),
    clearResult: () => setLastResult(null),
    // actions
    executeAction,
    runCommand,
    refresh,
    saveSettings,
    linkHueBridge,
    linkLgTv,
    discoverLgTv,
    loginRing,
    openExternalUrl,
    startRingManagedLiveView,
    stopRingManagedLiveView,
    saveCustomCommand,
    deleteCustomCommand,
    saveCustomGreeting,
    deleteCustomGreeting,
    saveAgentSettings,
    // helpers
    getProvider,
    getAllDevices,
    getCapabilityState,
    isDeviceOn,
    getDeviceBrightness,
    getDeviceVolume,
  };
}
