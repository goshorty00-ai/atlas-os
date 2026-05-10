import type { SmartHomeSnapshot } from './types';

export type SidebarIconKey = 'agent' | 'home' | 'settings' | 'light' | 'tv' | 'speaker' | 'camera' | 'shield' | 'plug' | 'device';

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'device';
}

export function getDeviceSectionId(providerId: string, deviceId: string) {
  return `device-${slugify(providerId)}-${slugify(deviceId)}`;
}

export function getDeviceShortcutLabel(name: string) {
  const trimmed = name.trim();
  if (!trimmed) {
    return 'DV';
  }

  const parts = trimmed.split(/\s+/).filter(Boolean);
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }

  return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase();
}

export function getSidebarDeviceItems(snapshot: SmartHomeSnapshot | null) {
  if (!snapshot || !snapshot.agentSettings.showDeviceShortcutsInSidebar) {
    return [];
  }

  return snapshot.providers.flatMap((provider) =>
    provider.devices.map((device) => ({
      id: getDeviceSectionId(provider.providerId, device.deviceId),
      label: device.name,
      shortLabel: getDeviceShortcutLabel(device.name),
      providerLabel: provider.displayName,
      isOnline: device.isOnline,
      iconKey: inferSidebarIcon(provider.providerId, device.deviceType, device.name),
    })),
  );
}

function inferSidebarIcon(providerId: string, deviceType: string, name: string): SidebarIconKey {
  const normalized = `${providerId} ${deviceType} ${name}`.toLowerCase();

  if (normalized.includes('tv') || normalized.includes('webos')) {
    return 'tv';
  }

  if (normalized.includes('light') || normalized.includes('lamp') || normalized.includes('bulb') || normalized.includes('hue') || normalized.includes('govee')) {
    return 'light';
  }

  if (normalized.includes('speaker') || normalized.includes('sound') || normalized.includes('audio')) {
    return 'speaker';
  }

  if (normalized.includes('camera') || normalized.includes('doorbell') || normalized.includes('ring')) {
    return 'camera';
  }

  if (normalized.includes('plug') || normalized.includes('outlet')) {
    return 'plug';
  }

  return 'device';
}