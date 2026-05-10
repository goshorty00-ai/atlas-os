import { useState } from 'react';
import { motion } from 'motion/react';
import { Camera, Cpu, Lightbulb, Power, SlidersHorizontal, Sparkles, Tv, Wifi } from 'lucide-react';
import { executeDeviceAction, refreshState } from '../bridge';
import { getDeviceSectionId } from '../deviceSections';
import type { SmartHomeActionRequest, SmartHomeAgentSettings, SmartHomeProviderState } from '../types';

interface DeviceInventoryProps {
  providers: SmartHomeProviderState[];
  agentSettings: SmartHomeAgentSettings | null;
}

function formatValue(value: unknown) {
  if (value === null || value === undefined || value === '') {
    return 'No live value';
  }

  if (isRgbValue(value)) {
    return `RGB(${value.r}, ${value.g}, ${value.b})`;
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return String(value);
}

function valuesEqual(left: unknown, right: unknown) {
  if (typeof left === 'object' || typeof right === 'object') {
    return JSON.stringify(left) === JSON.stringify(right);
  }

  return String(left) === String(right);
}

function humanizeCapabilityName(instance: string) {
  return instance
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function isRgbValue(value: unknown): value is { r: number; g: number; b: number } {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return typeof candidate.r === 'number' && typeof candidate.g === 'number' && typeof candidate.b === 'number';
}

function isNumericCapability(dataType: string) {
  const normalized = dataType.toLowerCase();
  return normalized === 'integer' || normalized === 'number' || normalized === 'float' || normalized === 'decimal';
}

export function DeviceInventory({ providers, agentSettings }: DeviceInventoryProps) {
  const [pendingActionKey, setPendingActionKey] = useState<string | null>(null);
  const [rgbDrafts, setRgbDrafts] = useState<Record<string, { r: number; g: number; b: number }>>({});
  const [numberDrafts, setNumberDrafts] = useState<Record<string, number>>({});

  const deviceGroups = providers
    .map((provider) => ({
      providerId: provider.providerId,
      providerName: provider.displayName,
      devices: provider.devices.map((device) => ({
        ...device,
        providerId: provider.providerId,
        providerName: provider.displayName,
      })),
    }))
    .filter((group) => group.devices.length > 0);

  const totalDevices = deviceGroups.reduce((sum, group) => sum + group.devices.length, 0);

  const getBadgeAppearance = (providerId: string, isOnline?: boolean | null) => {
    if (isOnline === true) {
      return {
        label: 'Online',
        color: '#7CFFB2',
        border: 'rgba(124,255,178,0.35)',
        background: 'rgba(124,255,178,0.08)',
      };
    }

    if (isOnline === false) {
      if (providerId === 'philips_hue') {
        return {
          label: 'Hue Reachability Warning',
          color: '#FFB970',
          border: 'rgba(255,185,112,0.35)',
          background: 'rgba(255,185,112,0.08)',
        };
      }

      return {
        label: 'Offline / Cached',
        color: '#FFB970',
        border: 'rgba(255,185,112,0.35)',
        background: 'rgba(255,185,112,0.08)',
      };
    }

    return {
      label: 'Online State Unknown',
      color: '#8BD7FF',
      border: 'rgba(139,215,255,0.30)',
      background: 'rgba(139,215,255,0.08)',
    };
  };

  const executeAction = async (request: SmartHomeActionRequest) => {
    const actionKey = `${request.deviceId}:${request.capabilityType}:${request.capabilityInstance}`;
    setPendingActionKey(actionKey);
    executeDeviceAction(request);
    window.setTimeout(() => {
      refreshState();
      setPendingActionKey(null);
    }, 900);
  };

  const isControllable = (providerId: string) => providerId === 'govee' || providerId === 'philips_hue' || providerId === 'lg_webos' || providerId === 'ring';

  const getRangeBounds = (instance: string, min?: number | null, max?: number | null) => {
    if (typeof min === 'number' && typeof max === 'number') {
      return { min, max };
    }

    const normalized = instance.toLowerCase();
    if (normalized.includes('brightness')) {
      return { min: 0, max: 100 };
    }

    if (normalized.includes('colorsaturation')) {
      return { min: 0, max: 100 };
    }

    if (normalized.includes('colorhue')) {
      return { min: 0, max: 360 };
    }

    return null;
  };

  return (
    <section id="devices" className="mt-8 scroll-mt-24">
      <h3 className="text-sm text-cyan-400/80 mb-4 flex items-center gap-2">
        <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
        Live Device Inventory
      </h3>

      <div className="space-y-5">
        {totalDevices === 0 && (
          <div
            className="rounded-2xl p-6 text-sm text-cyan-100/72"
            style={{
              background: 'rgba(5, 10, 18, 0.52)',
              border: '1px solid rgba(0, 212, 255, 0.2)',
            }}
          >
            No live devices yet. Save a provider credential above, then refresh Smart Home.
          </div>
        )}

        {deviceGroups.map((group, groupIndex) => {
          const providerStyle = getProviderStyle(group.providerId);

          return (
            <motion.section
              key={group.providerId}
              initial={{ opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: groupIndex * 0.06 }}
              className="rounded-[30px] p-5 backdrop-blur-xl"
              style={{
                background: providerStyle.shellBackground,
                border: `1px solid ${providerStyle.border}`,
                boxShadow: providerStyle.shadow,
              }}
            >
              <div className="flex items-center justify-between gap-4 mb-5">
                <div className="flex items-center gap-4">
                  <div
                    className="w-12 h-12 rounded-2xl flex items-center justify-center"
                    style={{ background: providerStyle.iconBackground, border: `1px solid ${providerStyle.border}` }}
                  >
                    <providerStyle.icon className="w-5 h-5" style={{ color: providerStyle.accent }} />
                  </div>
                  <div>
                    <p className="text-lg font-semibold" style={{ color: providerStyle.titleColor }}>{group.providerName}</p>
                    <p className="text-xs mt-1" style={{ color: providerStyle.subtitleColor }}>{providerStyle.tagline}</p>
                  </div>
                </div>

                <div className="flex flex-wrap gap-2 items-center justify-end">
                  <div
                    className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.18em] flex items-center gap-2"
                    style={{ color: providerStyle.accent, border: `1px solid ${providerStyle.border}`, background: providerStyle.pillBackground }}
                  >
                    <Wifi className="w-3 h-3" />
                    {group.devices.length} device{group.devices.length === 1 ? '' : 's'}
                  </div>
                  {group.providerId === 'philips_hue' && (
                    <div
                      className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.18em]"
                      style={{ color: '#FFE79C', border: '1px solid rgba(255,231,156,0.22)', background: 'rgba(255,231,156,0.06)' }}
                    >
                      Grouped together
                    </div>
                  )}
                </div>
              </div>

              <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
                {group.devices.map((device) => {
                  const badge = getBadgeAppearance(device.providerId, device.isOnline);

                  return (
          <motion.div
            key={`${device.providerId}:${device.deviceId}`}
            id={getDeviceSectionId(device.providerId, device.deviceId)}
            className="rounded-2xl p-5 backdrop-blur-xl"
            style={{
              background: 'rgba(5, 10, 18, 0.62)',
              border: `1px solid ${providerStyle.border}`,
              boxShadow: providerStyle.cardShadow,
            }}
          >
            <div className="flex items-start justify-between gap-3 mb-4">
              <div>
                <p className="text-lg text-cyan-200 font-semibold">{device.name}</p>
                <p className="text-xs text-cyan-400/60 mt-1">{device.providerName} · {device.sku || 'Unknown SKU'}</p>
              </div>

              <div
                className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-[0.16em]"
                style={{
                  color: badge.color,
                  border: `1px solid ${badge.border}`,
                  background: badge.background,
                }}
              >
                {badge.label}
              </div>
            </div>

            <div className="flex flex-wrap gap-2 mb-4">
              <div className="px-2.5 py-1 rounded-full text-[11px] text-cyan-300 flex items-center gap-1.5"
                style={{ background: providerStyle.pillBackground, border: `1px solid ${providerStyle.border}` }}>
                <providerStyle.icon className="w-3 h-3" style={{ color: providerStyle.accent }} />
                {device.deviceType || 'Unknown type'}
              </div>
              <div className="px-2.5 py-1 rounded-full text-[11px] text-cyan-300 flex items-center gap-1.5"
                style={{ background: providerStyle.pillBackground, border: `1px solid ${providerStyle.border}` }}>
                <SlidersHorizontal className="w-3 h-3" />
                {device.capabilities.length} capabilities
              </div>
            </div>

            <div className="space-y-2.5 max-h-72 overflow-y-auto pr-1">
              {device.capabilities.map((capability) => (
                <div
                  key={`${device.deviceId}:${capability.type}:${capability.instance}`}
                  className="rounded-xl px-3 py-2.5"
                  style={{ background: 'rgba(0, 212, 255, 0.05)', border: '1px solid rgba(0, 212, 255, 0.14)' }}
                >
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-sm text-cyan-200 font-medium">{capability.instance}</p>
                      <p className="text-[11px] text-cyan-300/70 mt-0.5">{humanizeCapabilityName(capability.instance)}</p>
                      <p className="text-[11px] text-cyan-400/60 mt-0.5">{capability.type}</p>
                    </div>
                    <div className="text-right">
                      <p className="text-sm text-cyan-100">{formatValue(capability.stateValue)}</p>
                      <p className="text-[11px] text-cyan-400/60 mt-0.5">{capability.dataType || 'state'}</p>
                    </div>
                  </div>

                  {isControllable(device.providerId) && capability.dataType.toLowerCase() === 'boolean' && capability.options.length === 0 && (
                    <div className="flex flex-wrap gap-1.5 mt-2">
                      {[true, false].map((optionValue) => {
                        const isActiveOption = capability.hasState && valuesEqual(capability.stateValue, optionValue);

                        return (
                          <button
                            key={`${capability.instance}:${String(optionValue)}`}
                            type="button"
                            onClick={() => {
                              void executeAction({
                                providerId: device.providerId,
                                deviceId: device.deviceId,
                                sku: device.sku,
                                capabilityType: capability.type,
                                capabilityInstance: capability.instance,
                                value: optionValue,
                              });
                            }}
                            disabled={pendingActionKey === `${device.deviceId}:${capability.type}:${capability.instance}`}
                            className="px-2 py-1 rounded-full text-[10px] text-cyan-300/86 cursor-pointer disabled:cursor-not-allowed disabled:opacity-55 select-none"
                            style={{
                              background: isActiveOption ? 'rgba(0, 212, 255, 0.18)' : 'rgba(0, 102, 255, 0.12)',
                              border: isActiveOption ? '1px solid rgba(0, 212, 255, 0.38)' : '1px solid rgba(0, 102, 255, 0.18)',
                              boxShadow: isActiveOption ? '0 0 16px rgba(0, 212, 255, 0.22)' : 'none',
                            }}
                          >
                            {optionValue ? 'on' : 'off'}
                          </button>
                        );
                      })}
                    </div>
                  )}

                  {capability.options.length > 0 && (
                    <div className="flex flex-wrap gap-1.5 mt-2">
                      {capability.options.map((option, optionIndex) => (
                        (() => {
                          const isActiveOption = capability.hasState && valuesEqual(capability.stateValue, option.value);

                          return (
                        <button
                          key={`${capability.instance}:${option.name}:${optionIndex}`}
                          type="button"
                          onClick={() => {
                            if (!isControllable(device.providerId)) {
                              return;
                            }

                            void executeAction({
                              providerId: device.providerId,
                              deviceId: device.deviceId,
                              sku: device.sku,
                              capabilityType: capability.type,
                              capabilityInstance: capability.instance,
                              value: option.value,
                            });
                          }}
                          disabled={!isControllable(device.providerId) || pendingActionKey === `${device.deviceId}:${capability.type}:${capability.instance}`}
                          className="px-2 py-1 rounded-full text-[10px] text-cyan-300/86 cursor-pointer disabled:cursor-not-allowed disabled:opacity-55 select-none"
                          style={{
                            background: isActiveOption ? 'rgba(0, 212, 255, 0.18)' : 'rgba(0, 102, 255, 0.12)',
                            border: isActiveOption ? '1px solid rgba(0, 212, 255, 0.38)' : '1px solid rgba(0, 102, 255, 0.18)',
                            boxShadow: isActiveOption ? '0 0 16px rgba(0, 212, 255, 0.22)' : 'none',
                          }}
                        >
                          {option.name || formatValue(option.value)}
                        </button>
                          );
                        })()
                      ))}
                    </div>
                  )}

                  {isControllable(device.providerId) && isRgbValue(capability.stateValue) && (
                    (() => {
                      const draftKey = `${device.deviceId}:${capability.type}:${capability.instance}`;
                      const draft = rgbDrafts[draftKey] ?? capability.stateValue;

                      return (
                        <div className="mt-3 rounded-xl p-3" style={{ background: 'rgba(0, 212, 255, 0.04)', border: '1px solid rgba(0, 212, 255, 0.1)' }}>
                          <div className="flex items-center gap-3 mb-3">
                            <div
                              className="w-10 h-10 rounded-xl"
                              style={{
                                background: `rgb(${draft.r}, ${draft.g}, ${draft.b})`,
                                boxShadow: '0 0 18px rgba(0, 212, 255, 0.16)',
                              }}
                            />
                            <div>
                              <p className="text-xs text-cyan-200 font-medium">Color Editor</p>
                              <p className="text-[11px] text-cyan-400/60">Adjust RGB then apply to the device.</p>
                            </div>
                          </div>

                          {(['r', 'g', 'b'] as const).map((channel) => (
                            <label key={channel} className="flex items-center gap-3 mb-2 last:mb-0">
                              <span className="w-4 text-[11px] uppercase tracking-[0.18em] text-cyan-300/70">{channel}</span>
                              <input
                                type="range"
                                min={0}
                                max={255}
                                value={draft[channel]}
                                onChange={(event) => {
                                  const nextValue = Number(event.currentTarget.value);
                                  setRgbDrafts((current) => ({
                                    ...current,
                                    [draftKey]: {
                                      ...draft,
                                      [channel]: nextValue,
                                    },
                                  }));
                                }}
                                className="flex-1 accent-cyan-400 cursor-pointer"
                              />
                              <span className="w-8 text-right text-[11px] text-cyan-200">{draft[channel]}</span>
                            </label>
                          ))}

                          <button
                            type="button"
                            onClick={() => {
                              void executeAction({
                                providerId: device.providerId,
                                deviceId: device.deviceId,
                                sku: device.sku,
                                capabilityType: capability.type,
                                capabilityInstance: capability.instance,
                                value: draft,
                              });
                            }}
                            className="mt-3 px-3 py-2 rounded-lg text-[11px] text-cyan-200"
                            style={{ background: 'rgba(0, 212, 255, 0.12)', border: '1px solid rgba(0, 212, 255, 0.22)' }}
                          >
                            Apply Color
                          </button>
                        </div>
                      );
                    })()
                  )}

                  {isControllable(device.providerId) && isNumericCapability(capability.dataType) && getRangeBounds(capability.instance, capability.min, capability.max) && (
                    (() => {
                      const bounds = getRangeBounds(capability.instance, capability.min, capability.max);
                      if (!bounds) {
                        return null;
                      }

                      const draftKey = `${device.deviceId}:${capability.type}:${capability.instance}`;
                      const currentValue = typeof capability.stateValue === 'number'
                        ? capability.stateValue
                        : Number(capability.stateValue ?? bounds.min);
                      const draftValue = numberDrafts[draftKey] ?? (Number.isFinite(currentValue) ? currentValue : bounds.min);
                      const step = capability.instance === 'volume' ? Math.max(1, agentSettings?.defaultVolumeStep ?? 5) : 1;

                      return (
                        <div className="mt-3 space-y-3">
                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              onClick={() => setNumberDrafts((current) => ({
                                ...current,
                                [draftKey]: Math.max(bounds.min, draftValue - step),
                              }))}
                              className="px-2.5 py-1.5 rounded-xl text-xs text-cyan-200"
                              style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.18)' }}
                            >
                              -{step}
                            </button>
                            <input
                              type="range"
                              min={bounds.min}
                              max={bounds.max}
                              value={draftValue}
                              onChange={(event) => {
                                const value = Number(event.currentTarget.value);
                                setNumberDrafts((current) => ({
                                  ...current,
                                  [draftKey]: value,
                                }));
                              }}
                              className="flex-1 accent-cyan-400 cursor-pointer"
                            />
                            <button
                              type="button"
                              onClick={() => setNumberDrafts((current) => ({
                                ...current,
                                [draftKey]: Math.min(bounds.max, draftValue + step),
                              }))}
                              className="px-2.5 py-1.5 rounded-xl text-xs text-cyan-200"
                              style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.18)' }}
                            >
                              +{step}
                            </button>
                          </div>
                          <input
                            type="number"
                            min={bounds.min}
                            max={bounds.max}
                            value={draftValue}
                            onChange={(event) => {
                              const value = Number(event.currentTarget.value);
                              setNumberDrafts((current) => ({
                                ...current,
                                [draftKey]: Number.isFinite(value) ? value : bounds.min,
                              }));
                            }}
                            className="w-full px-3 py-2 rounded-xl bg-transparent outline-none text-sm"
                            style={{ border: '1px solid rgba(0,212,255,0.16)', color: '#D8F9FF' }}
                          />
                          <div className="flex items-center justify-between gap-3">
                            <span className="text-[11px] text-cyan-300 min-w-16 text-right">Live {formatValue(capability.stateValue)}{capability.unit ? ` ${capability.unit}` : ''}</span>
                            <button
                              type="button"
                              onClick={() => {
                                void executeAction({
                                  providerId: device.providerId,
                                  deviceId: device.deviceId,
                                  sku: device.sku,
                                  capabilityType: capability.type,
                                  capabilityInstance: capability.instance,
                                  value: draftValue,
                                });
                              }}
                              className="px-3 py-2 rounded-xl text-xs text-cyan-100"
                              style={{ background: 'rgba(0,212,255,0.12)', border: '1px solid rgba(0,212,255,0.22)' }}
                            >
                              Apply {capability.instance}
                            </button>
                          </div>
                        </div>
                      );
                    })()
                  )}
                </div>
              ))}
            </div>

            <div className="mt-4 text-[11px] text-cyan-400/58 flex items-center gap-2 break-all">
              <Power className="w-3 h-3" />
              {device.deviceId}
            </div>

            {pendingActionKey?.startsWith(`${device.deviceId}:`) && (
              <div
                className="mt-3 rounded-xl px-3 py-2 text-xs"
                style={{
                  color: '#B8F7FF',
                  background: 'rgba(0, 212, 255, 0.07)',
                  border: '1px solid rgba(0, 212, 255, 0.16)',
                }}
              >
                Sending command to {device.providerName}...
              </div>
            )}

            {device.providerId === 'philips_hue' && device.isOnline === false && (
              <div
                className="mt-3 rounded-xl px-3 py-2 text-xs"
                style={{
                  color: '#FFD7B0',
                  background: 'rgba(255, 185, 112, 0.08)',
                  border: '1px solid rgba(255, 185, 112, 0.16)',
                }}
              >
                The Hue bridge is online, but its last device report says this light is not currently reachable on the Hue mesh. The on/off and brightness values shown above may be the last known state rather than live state. If the bulb is visibly on, Hue likely has stale reachability data or the light is intermittently dropping from the Zigbee mesh.
              </div>
            )}
          </motion.div>
                  );
                })}
              </div>
            </motion.section>
          );
        })}
      </div>
    </section>
  );
}

function getProviderStyle(providerId: string) {
  switch (providerId) {
    case 'philips_hue':
      return {
        icon: Lightbulb,
        accent: '#FFE07A',
        border: 'rgba(255, 224, 122, 0.24)',
        pillBackground: 'rgba(255, 224, 122, 0.07)',
        iconBackground: 'linear-gradient(145deg, rgba(255,224,122,0.12), rgba(255,196,82,0.06))',
        shellBackground: 'linear-gradient(145deg, rgba(30, 24, 8, 0.62), rgba(8, 12, 18, 0.86))',
        shadow: '0 18px 40px rgba(0,0,0,0.22), inset 0 1px 0 rgba(255,224,122,0.04)',
        cardShadow: '0 0 24px rgba(255,224,122,0.08)',
        titleColor: '#FFF2BF',
        subtitleColor: 'rgba(255, 239, 188, 0.68)',
        tagline: 'Hue lights kept together as one lighting deck.',
      };
    case 'govee':
      return {
        icon: Sparkles,
        accent: '#8AF4FF',
        border: 'rgba(138, 244, 255, 0.22)',
        pillBackground: 'rgba(138, 244, 255, 0.07)',
        iconBackground: 'linear-gradient(145deg, rgba(138,244,255,0.12), rgba(30,123,255,0.06))',
        shellBackground: 'linear-gradient(145deg, rgba(5, 14, 22, 0.9), rgba(4, 18, 30, 0.74))',
        shadow: '0 18px 40px rgba(0,0,0,0.22), inset 0 1px 0 rgba(138,244,255,0.04)',
        cardShadow: '0 0 24px rgba(138,244,255,0.08)',
        titleColor: '#DDFDFF',
        subtitleColor: 'rgba(179, 246, 255, 0.66)',
        tagline: 'Effects, strips, and ambient lighting controls.',
      };
    case 'lg_webos':
      return {
        icon: Tv,
        accent: '#9BC7FF',
        border: 'rgba(155, 199, 255, 0.22)',
        pillBackground: 'rgba(155, 199, 255, 0.07)',
        iconBackground: 'linear-gradient(145deg, rgba(155,199,255,0.12), rgba(42,84,190,0.06))',
        shellBackground: 'linear-gradient(145deg, rgba(6, 12, 24, 0.92), rgba(8, 14, 22, 0.76))',
        shadow: '0 18px 40px rgba(0,0,0,0.22), inset 0 1px 0 rgba(155,199,255,0.04)',
        cardShadow: '0 0 24px rgba(155,199,255,0.08)',
        titleColor: '#E3F0FF',
        subtitleColor: 'rgba(201, 223, 255, 0.66)',
        tagline: 'TV power, input, mute, and volume controls.',
      };
    case 'ring':
      return {
        icon: Camera,
        accent: '#9AF0E2',
        border: 'rgba(154, 240, 226, 0.22)',
        pillBackground: 'rgba(154, 240, 226, 0.07)',
        iconBackground: 'linear-gradient(145deg, rgba(154,240,226,0.12), rgba(33,121,101,0.06))',
        shellBackground: 'linear-gradient(145deg, rgba(5, 14, 18, 0.92), rgba(5, 16, 24, 0.76))',
        shadow: '0 18px 40px rgba(0,0,0,0.22), inset 0 1px 0 rgba(154,240,226,0.04)',
        cardShadow: '0 0 24px rgba(154,240,226,0.08)',
        titleColor: '#DDFBF6',
        subtitleColor: 'rgba(183, 248, 236, 0.66)',
        tagline: 'Cameras, chimes, and alarm hardware grouped together.',
      };
    default:
      return {
        icon: Cpu,
        accent: '#7CDFFF',
        border: 'rgba(124, 223, 255, 0.22)',
        pillBackground: 'rgba(124, 223, 255, 0.07)',
        iconBackground: 'linear-gradient(145deg, rgba(124,223,255,0.12), rgba(24,74,125,0.06))',
        shellBackground: 'linear-gradient(145deg, rgba(5, 10, 18, 0.9), rgba(5, 12, 22, 0.76))',
        shadow: '0 18px 40px rgba(0,0,0,0.22)',
        cardShadow: '0 0 24px rgba(124,223,255,0.08)',
        titleColor: '#DDF6FF',
        subtitleColor: 'rgba(184, 240, 255, 0.66)',
        tagline: 'Connected devices grouped by provider.',
      };
  }
}