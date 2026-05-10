import type {
  SmartHomeAlertState,
  SmartHomeAutomationState,
  SmartHomeCapability,
  SmartHomeCompanionPairingState,
  SmartHomeDevice,
  SmartHomeProviderState,
  SmartHomeSnapshot,
} from './types';

export interface LiveDevice extends SmartHomeDevice {
  providerId: string;
  providerName: string;
}

export interface RoomGroup {
  name: string;
  inferred: boolean;
  devices: LiveDevice[];
  offlineCount: number;
  cameraCount: number;
  controllableCount: number;
}

export interface SetupMethodState {
  id: string;
  label: string;
  status: 'available' | 'limited' | 'unavailable';
  detail: string;
  action?: string;
}

export interface IntegrationState {
  providerId: string;
  label: string;
  status: string;
  detail: string;
  deviceCount: number;
  configured: boolean;
  error: string;
  categories: string[];
  methods: string[];
}

const ROOM_KEYWORDS: Array<{ room: string; keywords: string[] }> = [
  { room: 'Living Room', keywords: ['living room', 'lounge', 'family room'] },
  { room: 'Kitchen', keywords: ['kitchen'] },
  { room: 'Bedroom', keywords: ['bedroom', 'master bedroom', 'guest room'] },
  { room: 'Office', keywords: ['office', 'studio', 'desk'] },
  { room: 'Garage', keywords: ['garage'] },
  { room: 'Front Door', keywords: ['front door', 'entry', 'porch'] },
  { room: 'Backyard', keywords: ['backyard', 'garden', 'patio', 'deck'] },
  { room: 'Hallway', keywords: ['hallway', 'hall'] },
  { room: 'Bathroom', keywords: ['bathroom', 'bath'] },
];

export function flattenDevices(snapshot: SmartHomeSnapshot | null): LiveDevice[] {
  if (!snapshot) {
    return [];
  }

  return snapshot.providers.flatMap((provider) =>
    provider.devices.map((device) => ({
      ...device,
      providerId: provider.providerId,
      providerName: provider.displayName,
    })),
  );
}

export function readCapabilityValue(capability: SmartHomeCapability): unknown {
  return capability.hasState ? capability.stateValue : null;
}

export function isTruthyCapability(capability: SmartHomeCapability): boolean {
  const value = readCapabilityValue(capability);
  return value === true || value === 'true' || value === 1 || value === '1';
}

export function looksLikeCamera(device: LiveDevice): boolean {
  if (looksLikeNonCameraLightDevice(device)) {
    return false;
  }

  if ((device.previewVideoUrl ?? '').trim() || (device.previewImageUrl ?? '').trim()) {
    return true;
  }

  if (matches(device, ['camera', 'doorbell', 'ring', 'doorcam', 'videodoor'])) {
    return true;
  }

  const externalUrl = (device.externalUrl ?? '').trim().toLowerCase();
  if (externalUrl && ['camera', 'doorbell', 'video', 'stream'].some((term) => externalUrl.includes(term))) {
    return true;
  }

  return device.capabilities.some((capability) => isCameraLikeCapability(capability));
}

function looksLikeNonCameraLightDevice(device: LiveDevice): boolean {
  const normalized = `${device.deviceType} ${device.sku}`.toLowerCase();
  const looksLikeLightingHardware = ['light', 'bulb', 'lamp', 'strip', 'panel', 'backlight'].some((term) => normalized.includes(term));

  if (!looksLikeLightingHardware) {
    return false;
  }

  if ((device.previewVideoUrl ?? '').trim() || (device.previewImageUrl ?? '').trim()) {
    return false;
  }

  return !device.capabilities.some((capability) => isCameraLikeCapability(capability));
}

function isCameraLikeCapability(capability: SmartHomeCapability): boolean {
  const normalizedType = (capability.type ?? '').toLowerCase();
  const normalizedInstance = (capability.instance ?? '').toLowerCase();
  const isDynamicSceneSnapshot = normalizedInstance.includes('snapshot') &&
    (normalizedType.includes('dynamic_scene') || normalizedType.includes('scene'));

  if (isDynamicSceneSnapshot) {
    return false;
  }

  return matchesCapability(capability, ['camera', 'doorbell', 'snapshot', 'stream']);
}

export function looksLikeSecurity(device: LiveDevice): boolean {
  return looksLikeCamera(device) || matches(device, ['alarm', 'siren', 'sensor', 'motion', 'security', 'entry']);
}

export function looksLikeClimate(device: LiveDevice): boolean {
  return matches(device, ['thermostat', 'climate', 'temperature', 'humidity', 'air quality', 'leak']) ||
    device.capabilities.some((capability) => matchesCapability(capability, ['temperature', 'humidity', 'airQuality', 'thermostat', 'leak', 'powerUsage', 'energy']));
}

export function looksLikeAccess(device: LiveDevice): boolean {
  return matches(device, ['lock', 'garage', 'gate', 'door opener', 'entry lock']);
}

export function looksLikeLight(device: LiveDevice): boolean {
  if (looksLikeMedia(device)) {
    return false;
  }

  return matches(device, ['light', 'lamp', 'bulb', 'strip', 'hue', 'govee']) ||
    device.capabilities.some((capability) =>
      matchesCapability(capability, ['brightness', 'colorTemperature', 'colorHue', 'colorSaturation', 'lightScene', 'diyScene', 'musicMode']));
}

export function looksLikeMedia(device: LiveDevice): boolean {
  return matches(device, ['tv', 'speaker', 'audio', 'media', 'webos']);
}

export function inferRoomName(deviceName: string): { name: string; inferred: boolean } {
  const normalized = deviceName.toLowerCase();
  for (const room of ROOM_KEYWORDS) {
    if (room.keywords.some((keyword) => normalized.includes(keyword))) {
      return { name: room.room, inferred: true };
    }
  }

  return { name: 'Unassigned', inferred: false };
}

export function buildRoomGroups(snapshot: SmartHomeSnapshot | null): RoomGroup[] {
  const groups = new Map<string, RoomGroup>();

  for (const device of flattenDevices(snapshot)) {
    const room = inferRoomName(device.name);
    const current = groups.get(room.name) ?? {
      name: room.name,
      inferred: room.inferred,
      devices: [],
      offlineCount: 0,
      cameraCount: 0,
      controllableCount: 0,
    };

    current.devices.push(device);
    if (device.isOnline === false) {
      current.offlineCount += 1;
    }
    if (looksLikeCamera(device)) {
      current.cameraCount += 1;
    }
    if (device.capabilities.length > 0) {
      current.controllableCount += 1;
    }

    groups.set(room.name, current);
  }

  return Array.from(groups.values()).sort((left, right) => right.devices.length - left.devices.length || left.name.localeCompare(right.name));
}

export function buildSetupMethods(snapshot: SmartHomeSnapshot | null): SetupMethodState[] {
  const providers = snapshot?.providers ?? [];
  const hasProvider = (providerId: string) => providers.some((provider) => provider.providerId === providerId);
  const pairing = snapshot?.companionPairing ?? emptyPairingState();
  const configuredProviders = providers.filter((provider) => provider.descriptor.isConfigured).length;

  return [
    {
      id: 'qr-pairing',
      label: 'QR pairing',
      status: pairing.qrCodeDataUrl ? 'available' : 'limited',
      detail: pairing.qrCodeDataUrl
        ? 'Companion transport is publishing a live pairing QR and encoded payload.'
        : pairing.availabilityMessage || 'Pairing payload exists only when the companion transport is reachable.',
    },
    {
      id: 'ecosystem-sign-in',
      label: 'Ecosystem sign-in',
      status: hasProvider('ring') ? 'available' : 'unavailable',
      detail: hasProvider('ring')
        ? 'Ring sign-in is wired through the existing Atlas provider login flow.'
        : 'No cloud sign-in provider is registered in the current Smart Home runtime.',
    },
    {
      id: 'local-discovery',
      label: 'Local discovery',
      status: hasProvider('lg_webos') ? 'available' : 'unavailable',
      detail: hasProvider('lg_webos')
        ? 'LG webOS discovery runs on the local network through the existing native bridge.'
        : 'No provider with native discovery support is active in the current runtime.',
    },
    {
      id: 'bluetooth-pairing',
      label: 'Bluetooth pairing',
      status: 'available',
      detail: 'Open Windows Bluetooth settings to pair smart home devices like speakers, sensors, or controllers.',
      action: 'ms-settings:bluetooth',
    },
    {
      id: 'wifi-onboarding',
      label: 'Wi-Fi onboarding',
      status: 'available',
      detail: 'Open Windows Wi-Fi settings to connect smart home devices that pair over your local network.',
      action: 'ms-settings:network-wifi',
    },
    {
      id: 'matter-onboarding',
      label: 'Matter onboarding',
      status: 'unavailable',
      detail: 'No Matter commissioning path is implemented in the current Smart Home runtime.',
    },
    {
      id: 'zigbee-onboarding',
      label: 'Zigbee onboarding',
      status: hasProvider('philips_hue') ? 'limited' : 'unavailable',
      detail: hasProvider('philips_hue')
        ? 'Hue devices can surface through the Hue bridge, but direct Zigbee onboarding is not exposed here.'
        : 'No direct Zigbee onboarding backend is available.',
    },
    {
      id: 'zwave-onboarding',
      label: 'Z-Wave onboarding',
      status: 'unavailable',
      detail: 'No Z-Wave controller or inclusion service is present in the current backend.',
    },
    {
      id: 'manual-ip',
      label: 'Manual or local IP setup',
      status: hasProvider('philips_hue') || hasProvider('lg_webos') ? 'available' : 'unavailable',
      detail: hasProvider('philips_hue') || hasProvider('lg_webos')
        ? 'Hue bridge IP and LG host pairing both use the current native provider forms.'
        : 'No provider currently accepts manual local addressing through this Smart Home UI.',
    },
    {
      id: 'bridge-hub',
      label: 'Bridge and hub setup',
      status: hasProvider('philips_hue') ? 'available' : 'unavailable',
      detail: hasProvider('philips_hue')
        ? 'Hue bridge linking is already integrated and uses the saved bridge/application key flow.'
        : 'No bridge or hub integration is currently registered in this runtime.',
    },
    {
      id: 'cloud-integrations',
      label: 'Cloud integrations',
      status: hasProvider('ring') || hasProvider('govee') ? 'available' : 'unavailable',
      detail: hasProvider('ring') || hasProvider('govee')
        ? 'Ring and Govee both rely on live cloud-backed integration credentials.'
        : 'No cloud-backed device integrations are active in the current Smart Home runtime.',
    },
    {
      id: 'ai-guided-setup',
      label: 'AI-guided integration',
      status: configuredProviders > 0 || pairing.isAvailable ? 'available' : 'limited',
      detail: configuredProviders > 0 || pairing.isAvailable
        ? 'Atlas can now guide setup using your live providers, pairing payload, and current device snapshot.'
        : 'AI guidance is available, but Atlas needs at least one provider or a live pairing transport to give concrete setup steps.',
    },
  ];
}

export function buildIntegrationStates(snapshot: SmartHomeSnapshot | null): IntegrationState[] {
  if (!snapshot) {
    return [];
  }

  return snapshot.providers.map((provider) => {
    const devices = provider.devices.map((device) => ({ ...device, providerId: provider.providerId, providerName: provider.displayName }));
    const categories = collectCategories(devices);
    const methods = buildProviderMethods(provider.providerId);

    return {
      providerId: provider.providerId,
      label: provider.displayName,
      status: provider.descriptor.status,
      detail: provider.error || provider.descriptor.detail,
      deviceCount: provider.devices.length,
      configured: provider.descriptor.isConfigured,
      error: provider.error,
      categories,
      methods,
    };
  });
}

export function buildRecommendations(snapshot: SmartHomeSnapshot | null): Array<{ title: string; detail: string }> {
  if (!snapshot) {
    return [];
  }

  const devices = flattenDevices(snapshot);
  const recommendations: Array<{ title: string; detail: string }> = [];

  for (const provider of snapshot.providers) {
    if (provider.error) {
      recommendations.push({
        title: `${provider.displayName} needs attention`,
        detail: provider.error,
      });
    }
  }

  const offline = devices.filter((device) => device.isOnline === false);
  if (offline.length > 0) {
    recommendations.push({
      title: `${offline.length} device${offline.length === 1 ? '' : 's'} offline`,
      detail: 'Review provider connectivity and recent offline telemetry in the Alerts and Devices pages.',
    });
  }

  if (snapshot.automations.length === 0 && devices.length > 0) {
    recommendations.push({
      title: 'Automations are available but unused',
      detail: 'Atlas already has a persisted automation engine. Add a trigger and a few native actions from the Automations page.',
    });
  }

  if (!snapshot.agentSettings.voiceCommandsEnabled) {
    recommendations.push({
      title: 'Voice command routing is disabled',
      detail: 'Enable Smart Home voice routing if you want Atlas chat and voice to execute device phrases directly.',
    });
  }

  if (!snapshot.companionPairing.isAvailable) {
    recommendations.push({
      title: 'Companion pairing is not reachable',
      detail: snapshot.companionPairing.availabilityMessage,
    });
  }

  return recommendations.slice(0, 4);
}

export function formatRelativeTime(value?: string | null): string {
  if (!value) {
    return 'No recent activity';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const diffMs = Date.now() - date.getTime();
  const diffMinutes = Math.round(diffMs / 60000);
  if (diffMinutes < 1) {
    return 'Just now';
  }
  if (diffMinutes < 60) {
    return `${diffMinutes}m ago`;
  }
  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours}h ago`;
  }
  const diffDays = Math.round(diffHours / 24);
  return `${diffDays}d ago`;
}

export function groupAlerts(alerts: SmartHomeAlertState[]): SmartHomeAlertState[] {
  return [...alerts].sort((left, right) => {
    if (left.isResolved !== right.isResolved) {
      return left.isResolved ? 1 : -1;
    }

    return new Date(right.timestampUtc).getTime() - new Date(left.timestampUtc).getTime();
  });
}

export function summarizeAutomations(automations: SmartHomeAutomationState[]) {
  return {
    total: automations.length,
    enabled: automations.filter((automation) => automation.isEnabled).length,
    scheduled: automations.filter((automation) => Boolean(automation.schedule)).length,
    recentlyTriggered: automations.filter((automation) => Boolean(automation.lastTriggeredUtc)).length,
  };
}

export function getClimateTelemetry(devices: LiveDevice[]) {
  return devices.filter(looksLikeClimate);
}

export function getAccessDevices(devices: LiveDevice[]) {
  return devices.filter(looksLikeAccess);
}

export function getSecurityDevices(devices: LiveDevice[]) {
  return devices.filter(looksLikeSecurity);
}

function emptyPairingState(): SmartHomeCompanionPairingState {
  return {
    isAvailable: false,
    availabilityMessage: 'Companion pairing is unavailable.',
    baseUrl: '',
    protocol: '',
    host: '',
    port: 0,
    displayName: '',
    apiVersion: '',
    payloadFormat: '',
    payload: '',
    qrCodeDataUrl: '',
  };
}

function collectCategories(devices: LiveDevice[]) {
  const categories = new Set<string>();
  for (const device of devices) {
    if (looksLikeCamera(device)) categories.add('Cameras');
    if (looksLikeLight(device)) categories.add('Lighting');
    if (looksLikeMedia(device)) categories.add('Media');
    if (looksLikeSecurity(device)) categories.add('Security');
    if (looksLikeClimate(device)) categories.add('Climate');
    if (looksLikeAccess(device)) categories.add('Access');
  }

  if (categories.size === 0) {
    categories.add('General device control');
  }

  return Array.from(categories);
}

function buildProviderMethods(providerId: string) {
  switch (providerId) {
    case 'philips_hue':
      return ['Bridge setup', 'Manual IP', 'Local bridge authentication'];
    case 'lg_webos':
      return ['Local discovery', 'Manual host', 'On-device pairing'];
    case 'ring':
      return ['Ecosystem sign-in', 'Cloud integration', 'Managed live view'];
    case 'govee':
      return ['Cloud integration', 'API key onboarding'];
    default:
      return ['Provider-specific setup'];
  }
}

function matches(device: LiveDevice, terms: string[]) {
  const normalized = `${device.providerId} ${device.providerName} ${device.deviceType} ${device.sku} ${device.name}`.toLowerCase();
  return terms.some((term) => normalized.includes(term.toLowerCase()));
}

function matchesCapability(capability: SmartHomeCapability, terms: string[]) {
  const normalized = `${capability.type} ${capability.instance} ${capability.unit}`.toLowerCase();
  return terms.some((term) => normalized.includes(term.toLowerCase()));
}