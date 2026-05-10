import { useEffect, useMemo, useState } from 'react';
import { Layers3, Lightbulb, MicVocal, Save, Trash2, WandSparkles } from 'lucide-react';
import { deleteCustomCommand, runSmartHomeCommand, saveCustomCommand } from '../bridge';
import { inferRoomName } from '../smartHomeModel';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from './ui/select';
import type { SmartHomeCapability, SmartHomeCustomCommandDraft, SmartHomeSavedCommand, SmartHomeSnapshot } from '../types';

interface CommandStudioProps {
  snapshot: SmartHomeSnapshot | null;
}

interface FlatDevice {
  providerId: string;
  providerName: string;
  deviceId: string;
  sku: string;
  name: string;
  deviceType?: string;
  capabilities: SmartHomeCapability[];
}

interface CommandTargetOption {
  key: string;
  kind: 'device' | 'group' | 'scene';
  label: string;
  detail: string;
  providerId: string;
  deviceId: string;
  sku: string;
  targetScope?: string;
  targetLabel?: string;
  capabilities: SmartHomeCapability[];
}

function createSceneCapability(): SmartHomeCapability {
  return {
    type: 'atlas.scene',
    instance: 'runScene',
    dataType: 'boolean',
    unit: '',
    min: null,
    max: null,
    hasState: false,
    stateValue: null,
    options: [],
  };
}

function getCapabilityValue(capability: SmartHomeCapability): unknown {
  if (capability.options.length > 0) {
    return capability.options[0]?.value ?? null;
  }

  switch (capability.dataType.toLowerCase()) {
    case 'boolean':
      return true;
    case 'integer':
      return capability.min ?? 0;
    default:
      return capability.stateValue ?? '';
  }
}

function isLightDevice(device: FlatDevice) {
  const text = `${device.name} ${device.providerName} ${device.deviceType ?? ''}`.toLowerCase();
  const isMediaDevice = text.includes('tv') ||
    text.includes('speaker') ||
    text.includes('audio') ||
    text.includes('media') ||
    text.includes('webos');

  if (isMediaDevice) {
    return false;
  }

  return text.includes('light') ||
    text.includes('lamp') ||
    text.includes('bulb') ||
    text.includes('strip') ||
    text.includes('hue') ||
    device.capabilities.some((capability) => {
      const instance = capability.instance.toLowerCase();
      return instance.includes('brightness') ||
        instance.includes('colortemperature') ||
        instance.includes('colorhue') ||
        instance.includes('colorsaturation') ||
        instance.includes('lightscene') ||
        instance.includes('diyscene') ||
        instance.includes('musicmode');
    });
}

function buildSharedCapabilities(devices: FlatDevice[]) {
  if (devices.length === 0) {
    return [];
  }

  const first = devices[0].capabilities;
  return first.filter((candidate) =>
    devices.every((device) => device.capabilities.some((capability) =>
      capability.type === candidate.type && capability.instance === candidate.instance)),
  );
}

function getCapabilityPriority(capability: SmartHomeCapability) {
  const instance = capability.instance.toLowerCase();

  switch (instance) {
    case 'powerSwitch'.toLowerCase():
      return 10;
    case 'volume':
      return 20;
    case 'mute':
      return 21;
    case 'inputSource'.toLowerCase():
      return 22;
    case 'brightness':
      return 30;
    case 'colorTemperature'.toLowerCase():
      return 31;
    case 'colorRgb'.toLowerCase():
      return 32;
    case 'colorHue'.toLowerCase():
      return 33;
    case 'colorSaturation'.toLowerCase():
      return 34;
    case 'runScene'.toLowerCase():
      return 40;
    default:
      return 100;
  }
}

function formatCapabilityLabel(capability: SmartHomeCapability) {
  return capability.instance
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\b\w/g, (match) => match.toUpperCase());
}

function orderCapabilities(capabilities: SmartHomeCapability[]) {
  return [...capabilities].sort((left, right) => {
    const priority = getCapabilityPriority(left) - getCapabilityPriority(right);
    if (priority !== 0) {
      return priority;
    }

    return formatCapabilityLabel(left).localeCompare(formatCapabilityLabel(right));
  });
}

function buildTargetOptions(devices: FlatDevice[], snapshot: SmartHomeSnapshot | null) {
  const sceneOptions: CommandTargetOption[] = [];
  const groupOptions: CommandTargetOption[] = [];
  const deviceOptions: CommandTargetOption[] = [];
  const lightDevices = devices.filter(isLightDevice);
  const sceneCapability = createSceneCapability();

  for (const scene of snapshot?.customScenes ?? []) {
    sceneOptions.push({
      key: `scene:${scene.id}`,
      kind: 'scene',
      label: scene.name,
      detail: scene.phrase || 'Saved scene',
      providerId: 'scene',
      deviceId: scene.id,
      sku: 'scene',
      targetScope: `scene:${scene.id}`,
      targetLabel: scene.name,
      capabilities: [sceneCapability],
    });
  }

  if (lightDevices.length > 1) {
    groupOptions.push({
      key: 'group:all-lights',
      kind: 'group',
      label: 'All lights',
      detail: `${lightDevices.length} devices`,
      providerId: '',
      deviceId: '',
      sku: '',
      targetScope: 'all-lights',
      targetLabel: 'All lights',
      capabilities: buildSharedCapabilities(lightDevices),
    });
  }

  const roomLightGroups = new Map<string, FlatDevice[]>();
  for (const device of lightDevices) {
    const room = inferRoomName(device.name);
    if (!room.inferred || room.name === 'Unassigned') {
      continue;
    }

    const existing = roomLightGroups.get(room.name) ?? [];
    existing.push(device);
    roomLightGroups.set(room.name, existing);
  }

  for (const [roomName, roomDevices] of Array.from(roomLightGroups.entries()).sort((left, right) => left[0].localeCompare(right[0]))) {
    if (roomDevices.length < 2) {
      continue;
    }

    groupOptions.push({
      key: `group:room-lights:${roomName.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`,
      kind: 'group',
      label: `${roomName} lights`,
      detail: `${roomDevices.length} devices`,
      providerId: '',
      deviceId: '',
      sku: '',
      targetScope: `room-lights:${roomName}`,
      targetLabel: `${roomName} lights`,
      capabilities: buildSharedCapabilities(roomDevices),
    });
  }

  const providerLightGroups = new Map<string, FlatDevice[]>();
  for (const device of lightDevices) {
    const existing = providerLightGroups.get(device.providerId) ?? [];
    existing.push(device);
    providerLightGroups.set(device.providerId, existing);
  }

  for (const [providerId, providerDevices] of providerLightGroups) {
    if (providerDevices.length < 2) {
      continue;
    }

    groupOptions.push({
      key: `group:provider-lights:${providerId}`,
      kind: 'group',
      label: `${providerDevices[0].providerName} lights`,
      detail: `${providerDevices.length} devices`,
      providerId: '',
      deviceId: '',
      sku: '',
      targetScope: `provider-lights:${providerId}`,
      targetLabel: `${providerDevices[0].providerName} lights`,
      capabilities: buildSharedCapabilities(providerDevices),
    });
  }

  for (const device of [...devices].sort((left, right) => left.name.localeCompare(right.name) || left.providerName.localeCompare(right.providerName))) {
    deviceOptions.push({
      key: `device:${device.providerId}:${device.deviceId}`,
      kind: 'device',
      label: device.name,
      detail: device.providerName,
      providerId: device.providerId,
      deviceId: device.deviceId,
      sku: device.sku,
      targetLabel: device.name,
      capabilities: device.capabilities,
    });
  }

  return [...groupOptions, ...deviceOptions, ...sceneOptions];
}

function getDefaultTarget(targetOptions: CommandTargetOption[]) {
  const scoreTarget = (target: CommandTargetOption) => {
    const kindScore = target.kind === 'device' ? 1000 : target.kind === 'scene' ? 100 : 0;
    const capabilityScore = orderCapabilities(target.capabilities).reduce((score, capability) => score + (120 - getCapabilityPriority(capability)), 0);
    const mediaBonus = target.capabilities.some((capability) => {
      const instance = capability.instance.toLowerCase();
      return instance === 'volume' || instance === 'mute' || instance === 'inputsource';
    }) ? 250 : 0;

    return kindScore + capabilityScore + mediaBonus;
  };

  return [...targetOptions]
    .filter((option) => option.capabilities.length > 0)
    .sort((left, right) => {
      const score = scoreTarget(right) - scoreTarget(left);
      if (score !== 0) {
        return score;
      }

      return left.label.localeCompare(right.label);
    })[0] ?? null;
}

function getTargetKindLabel(kind: CommandTargetOption['kind']) {
  switch (kind) {
    case 'group':
      return 'Group';
    case 'scene':
      return 'Scene';
    default:
      return 'Device';
  }
}

function describeCommand(command: SmartHomeSavedCommand, devices: FlatDevice[]) {
  if (command.targetKind === 'atlas-intent' && command.targetScope === 'door-answer') {
    return 'Doorbell auto-answer intent';
  }

  if (command.targetKind === 'scene') {
    return `${command.targetLabel || command.deviceId} · scene`;
  }

  if (command.targetKind === 'group' && command.targetLabel) {
    return `${command.targetLabel} · ${command.capabilityInstance}`;
  }

  const device = devices.find((item) => item.deviceId === command.deviceId && item.providerId === command.providerId);
  return device ? `${device.name} · ${command.capabilityInstance}` : `${command.providerId} · ${command.capabilityInstance}`;
}

function groupCommands(commands: SmartHomeSavedCommand[], devices: FlatDevice[]) {
  const groups = new Map<string, { title: string; accent: string; commands: SmartHomeSavedCommand[] }>();

  for (const command of commands) {
    if (command.targetKind === 'atlas-intent') {
      const key = `intent:${command.targetScope || command.capabilityInstance || command.id}`;
      const group = groups.get(key) ?? { title: 'Atlas Intents', accent: '#7CF5C8', commands: [] };
      group.commands.push(command);
      groups.set(key, group);
      continue;
    }

    if (command.targetKind === 'scene') {
      const group = groups.get('scene') ?? { title: 'Scenes', accent: '#D7A6FF', commands: [] };
      group.commands.push(command);
      groups.set('scene', group);
      continue;
    }

    if (command.targetKind === 'group' && command.targetScope) {
      const title = command.targetLabel || 'Grouped commands';
      const key = command.targetScope;
      const accent = command.targetScope === 'all-lights' ? '#FFE79C' : '#8BE9FF';
      const group = groups.get(key) ?? { title, accent, commands: [] };
      group.commands.push(command);
      groups.set(key, group);
      continue;
    }

    const device = devices.find((item) => item.deviceId === command.deviceId && item.providerId === command.providerId);
    const key = command.providerId || 'unassigned';
    const title = device?.providerName ?? command.providerId ?? 'Unassigned';
    const accent = command.providerId === 'philips_hue' ? '#FFE79C' : '#8BE9FF';
    const group = groups.get(key) ?? { title, accent, commands: [] };
    group.commands.push(command);
    groups.set(key, group);
  }

  return Array.from(groups.values()).sort((left, right) => left.title.localeCompare(right.title));
}

export function CommandStudio({ snapshot }: CommandStudioProps) {
  const devices = useMemo<FlatDevice[]>(() => {
    if (!snapshot) {
      return [];
    }

    return snapshot.providers.flatMap((provider) =>
      provider.devices.map((device) => ({
        providerId: provider.providerId,
        providerName: provider.displayName,
        deviceId: device.deviceId,
        sku: device.sku,
        name: device.name,
        deviceType: device.deviceType,
        capabilities: device.capabilities,
      })),
    );
  }, [snapshot]);

  const targetOptions = useMemo(() => buildTargetOptions(devices, snapshot), [devices, snapshot]);

  const [draft, setDraft] = useState<SmartHomeCustomCommandDraft | null>(null);
  const commandGroups = groupCommands(snapshot?.customCommands ?? [], devices);

  useEffect(() => {
    if (targetOptions.length === 0) {
      setDraft(null);
      return;
    }

    setDraft((current) => {
      if (current && targetOptions.some((option) => option.key === getDraftTargetKey(current))) {
        return current;
      }

      const target = getDefaultTarget(targetOptions);
      if (!target) {
        return null;
      }

      const capability = orderCapabilities(target.capabilities)[0];

      return {
        phrase: '',
        targetKind: target.kind,
        targetScope: target.targetScope ?? '',
        targetLabel: target.targetLabel ?? target.label,
        providerId: target.providerId,
        deviceId: target.deviceId,
        sku: target.sku,
        capabilityType: capability?.type ?? '',
        capabilityInstance: capability?.instance ?? '',
        value: capability ? getCapabilityValue(capability) : null,
        enabled: true,
        responseText: '',
        doorbellResponseText: '',
      };
    });
  }, [targetOptions]);

  const selectedTarget = targetOptions.find((option) => option.key === getDraftTargetKey(draft)) ?? null;
  const isDoorAnswerIntent = draft?.targetKind === 'atlas-intent' && draft?.targetScope === 'door-answer';
  const orderedCapabilities = useMemo(
    () => (selectedTarget ? orderCapabilities(selectedTarget.capabilities) : []),
    [selectedTarget],
  );
  const selectedCapability = orderedCapabilities.find((capability) => capability.instance === draft?.capabilityInstance && capability.type === draft?.capabilityType) ?? null;

  const setTarget = (targetKey: string) => {
    const target = targetOptions.find((entry) => entry.key === targetKey);
    if (!target) {
      return;
    }

    const capability = orderCapabilities(target.capabilities)[0];
    setDraft((current) => current ? {
      ...current,
      targetKind: target.kind,
      targetScope: target.targetScope ?? '',
      targetLabel: target.targetLabel ?? target.label,
      providerId: target.providerId,
      deviceId: target.deviceId,
      sku: target.sku,
      capabilityType: capability?.type ?? '',
      capabilityInstance: capability?.instance ?? '',
      value: capability ? getCapabilityValue(capability) : null,
    } : current);
  };

  const setCapability = (capabilityKey: string) => {
    const capability = selectedTarget?.capabilities.find((entry) => `${entry.type}:${entry.instance}` === capabilityKey);
    if (!capability) {
      return;
    }

    setDraft((current) => current ? {
      ...current,
      capabilityType: capability.type,
      capabilityInstance: capability.instance,
      value: getCapabilityValue(capability),
    } : current);
  };

  const save = () => {
    if (!draft || !draft.phrase.trim() || !draft.capabilityInstance) {
      return;
    }

    saveCustomCommand({
      ...draft,
      phrase: draft.phrase.trim(),
      responseText: draft.responseText?.trim() ?? '',
      doorbellResponseText: draft.doorbellResponseText?.trim() ?? '',
    });
    setDraft((current) => current ? { ...current, id: undefined, phrase: '', responseText: '', doorbellResponseText: current.targetKind === 'atlas-intent' ? current.doorbellResponseText : '' } : current);
  };

  const editExisting = (command: SmartHomeSavedCommand) => {
    setDraft({
      id: command.id,
      enabled: command.enabled,
      phrase: command.phrase,
      targetKind: command.targetKind || 'device',
      targetScope: command.targetScope || '',
      targetLabel: command.targetLabel || '',
      providerId: command.providerId,
      deviceId: command.deviceId,
      sku: command.sku,
      capabilityType: command.capabilityType,
      capabilityInstance: command.capabilityInstance,
      value: command.value,
      responseText: command.responseText,
      doorbellResponseText: command.doorbellResponseText,
    });
  };

  return (
    <div className="mt-8 grid grid-cols-1 xl:grid-cols-[1.1fr_1fr] gap-5">
      <section className="rounded-[28px] p-6"
               style={{ background: 'rgba(5, 10, 18, 0.82)', border: '1px solid rgba(0,212,255,0.16)', boxShadow: '0 20px 48px rgba(0,0,0,0.24)' }}>
        <div className="flex items-center gap-3 mb-5">
          <div className="w-11 h-11 rounded-2xl flex items-center justify-center"
               style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)' }}>
            <MicVocal className="w-5 h-5 text-cyan-200" />
          </div>
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Voice Builder</p>
            <h3 className="text-2xl text-cyan-100 font-semibold">Create Smart Home Commands</h3>
          </div>
        </div>

        {draft ? (
          <div className="space-y-4">
            <Field label="Phrase you want to say">
              <input
                value={draft.phrase}
                onChange={(event) => setDraft({ ...draft, phrase: event.target.value })}
                placeholder="movie time"
                className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
                style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
              />
            </Field>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Field label="Target">
                {isDoorAnswerIntent ? (
                  <div
                    className="w-full rounded-2xl px-4 py-3 text-sm"
                    style={{ border: '1px solid rgba(124,245,200,0.22)', background: 'rgba(10, 24, 20, 0.92)', color: '#D8FFF3' }}
                  >
                    Atlas doorbell auto-answer
                  </div>
                ) : (
                  <Select
                    value={getDraftTargetKey(draft)}
                    onValueChange={setTarget}
                  >
                    <SelectTrigger
                      className="w-full rounded-2xl px-4 py-3 text-sm"
                      style={{ border: '1px solid rgba(0,212,255,0.22)', background: 'rgba(4, 16, 28, 0.92)', color: '#D8F9FF' }}
                    >
                      <SelectValue placeholder="Select a target" />
                    </SelectTrigger>
                    <SelectContent
                      className="rounded-2xl border-0"
                      style={{ background: 'rgba(4, 16, 28, 0.98)', color: '#D8F9FF', boxShadow: '0 24px 48px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,212,255,0.16)' }}
                    >
                      {targetOptions.map((target) => (
                        <SelectItem
                          key={target.key}
                          value={target.key}
                          className="rounded-xl text-sm text-cyan-100 focus:text-cyan-50"
                          style={{ background: 'transparent' }}
                        >
                          {getTargetKindLabel(target.kind)} · {target.label} · {target.detail}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </Field>

              <Field label="Action">
                {isDoorAnswerIntent ? (
                  <div
                    className="w-full rounded-2xl px-4 py-3 text-sm"
                    style={{ border: '1px solid rgba(124,245,200,0.22)', background: 'rgba(10, 24, 20, 0.92)', color: '#D8FFF3' }}
                  >
                    Open live view and send a saved doorbell reply
                  </div>
                ) : selectedTarget?.kind === 'scene' ? (
                  <div
                    className="w-full rounded-2xl px-4 py-3 text-sm"
                    style={{ border: '1px solid rgba(215,166,255,0.22)', background: 'rgba(22, 10, 28, 0.92)', color: '#E8D8FF' }}
                  >
                    Run saved scene
                  </div>
                ) : (
                  <Select
                    value={`${draft.capabilityType}:${draft.capabilityInstance}`}
                    onValueChange={setCapability}
                  >
                    <SelectTrigger
                      className="w-full rounded-2xl px-4 py-3 text-sm"
                      style={{ border: '1px solid rgba(0,212,255,0.22)', background: 'rgba(4, 16, 28, 0.92)', color: '#D8F9FF' }}
                    >
                      <SelectValue placeholder="Select an action" />
                    </SelectTrigger>
                    <SelectContent
                      className="rounded-2xl border-0"
                      style={{ background: 'rgba(4, 16, 28, 0.98)', color: '#D8F9FF', boxShadow: '0 24px 48px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,212,255,0.16)' }}
                    >
                      {orderedCapabilities.map((capability) => (
                        <SelectItem
                          key={`${capability.type}:${capability.instance}`}
                          value={`${capability.type}:${capability.instance}`}
                          className="rounded-xl text-sm text-cyan-100 focus:text-cyan-50"
                          style={{ background: 'transparent' }}
                        >
                          {formatCapabilityLabel(capability)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </Field>
            </div>

            {!isDoorAnswerIntent && selectedTarget?.kind !== 'scene' && (
              <Field label="Command value">
                <CapabilityValueEditor
                  capability={selectedCapability}
                  value={draft.value}
                  onChange={(value) => setDraft({ ...draft, value })}
                />
              </Field>
            )}

            <Field label="Optional confirmation">
              <input
                value={draft.responseText ?? ''}
                onChange={(event) => setDraft({ ...draft, responseText: event.target.value })}
                placeholder={isDoorAnswerIntent ? 'Doorbell feed is open.' : 'Movie scene ready.'}
                className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
                style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
              />
            </Field>

            {isDoorAnswerIntent && (
              <Field label="Doorbell reply spoken to visitors">
                <input
                  value={draft.doorbellResponseText ?? ''}
                  onChange={(event) => setDraft({ ...draft, doorbellResponseText: event.target.value })}
                  placeholder="Hello, Atlas here. One moment please."
                  className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
                  style={{ border: '1px solid rgba(124,245,200,0.24)', color: '#D8FFF3' }}
                />
              </Field>
            )}

            {selectedTarget?.kind === 'group' && (
              <div className="rounded-3xl px-4 py-4 flex items-start gap-3"
                   style={{ background: 'rgba(255,231,156,0.06)', border: '1px solid rgba(255,231,156,0.18)' }}>
                <div className="w-9 h-9 rounded-2xl flex items-center justify-center" style={{ background: 'rgba(255,231,156,0.08)' }}>
                  <Layers3 className="w-4 h-4 text-[#FFE79C]" />
                </div>
                <div className="text-sm text-cyan-100/80">
                  This command will run across <span className="text-cyan-100 font-medium">{selectedTarget.label}</span> instead of a single device.
                </div>
              </div>
            )}

            {selectedTarget?.kind === 'scene' && (
              <div className="rounded-3xl px-4 py-4 flex items-start gap-3"
                   style={{ background: 'rgba(215,166,255,0.06)', border: '1px solid rgba(215,166,255,0.18)' }}>
                <div className="w-9 h-9 rounded-2xl flex items-center justify-center" style={{ background: 'rgba(215,166,255,0.08)' }}>
                  <Lightbulb className="w-4 h-4 text-[#D7A6FF]" />
                </div>
                <div className="text-sm text-cyan-100/80">
                  This phrase will run the saved scene <span className="text-cyan-100 font-medium">{selectedTarget.label}</span> instead of a single capability action.
                </div>
              </div>
            )}

            <div className="flex flex-wrap gap-3 pt-2">
              <button
                type="button"
                onClick={save}
                className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
                style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.18), rgba(0,102,255,0.18))', border: '1px solid rgba(0,212,255,0.28)', color: '#D8F9FF' }}
              >
                <Save className="w-4 h-4" />
                Save Command
              </button>

              <button
                type="button"
                onClick={() => draft.phrase.trim() && runSmartHomeCommand(draft.phrase.trim())}
                className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
                style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.18)', color: '#D8F9FF' }}
              >
                <WandSparkles className="w-4 h-4" />
                Test Phrase
              </button>
            </div>
          </div>
        ) : (
          <p className="text-sm text-cyan-100/70">Link a device first, then you can create voice phrases for it here.</p>
        )}
      </section>

      <section className="rounded-[28px] p-6"
               style={{ background: 'rgba(5, 10, 18, 0.72)', border: '1px solid rgba(0,212,255,0.14)', boxShadow: '0 18px 40px rgba(0,0,0,0.2)' }}>
        <div className="flex items-center justify-between gap-3 mb-5">
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Saved Commands</p>
            <h3 className="text-2xl text-cyan-100 font-semibold">Command Library</h3>
          </div>
          <div className="px-3 py-2 rounded-full text-xs text-cyan-200"
               style={{ background: 'rgba(0,212,255,0.06)', border: '1px solid rgba(0,212,255,0.14)' }}>
            {snapshot?.customCommands.length ?? 0} saved
          </div>
        </div>

        <div className="space-y-3 max-h-[560px] overflow-y-auto pr-1">
          {commandGroups.map((group) => (
            <div key={group.title}>
              <div className="flex items-center justify-between gap-3 mb-3 px-1">
                <p className="text-xs uppercase tracking-[0.22em]" style={{ color: group.accent }}>{group.title}</p>
                {group.title.toLowerCase().includes('light') && (
                  <div className="px-3 py-1.5 rounded-full text-[10px] uppercase tracking-[0.18em]"
                       style={{ color: '#FFE79C', border: '1px solid rgba(255,231,156,0.22)', background: 'rgba(255,231,156,0.06)' }}>
                    grouped target
                  </div>
                )}
              </div>

              <div className="space-y-3 mb-4">
                {group.commands.map((command) => (
                  <div key={command.id} className="rounded-3xl px-4 py-4"
                       style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
                    <div className="flex items-start justify-between gap-3 mb-2">
                      <div>
                        <p className="text-base text-cyan-100 font-medium">"{command.phrase}"</p>
                        <p className="text-xs text-cyan-100/58 mt-1">{describeCommand(command, devices)}</p>
                      </div>
                      <div className="px-2 py-1 rounded-full text-[10px] uppercase tracking-[0.18em]"
                           style={{ background: command.enabled ? 'rgba(124,255,178,0.08)' : 'rgba(255,185,112,0.08)', border: command.enabled ? '1px solid rgba(124,255,178,0.22)' : '1px solid rgba(255,185,112,0.18)', color: command.enabled ? '#BFFFD5' : '#FFCAA0' }}>
                        {command.enabled ? 'Live' : 'Paused'}
                      </div>
                    </div>

                    {command.responseText && (
                      <p className="text-sm text-cyan-100/72 mb-3">Response: {command.responseText}</p>
                    )}

                    {command.doorbellResponseText && command.targetKind === 'atlas-intent' && command.targetScope === 'door-answer' && (
                      <p className="text-sm text-cyan-100/72 mb-3">Doorbell reply: {command.doorbellResponseText}</p>
                    )}

                    <div className="flex flex-wrap gap-2">
                      <button
                        type="button"
                        onClick={() => runSmartHomeCommand(command.phrase)}
                        className="px-3 py-2 rounded-2xl text-xs"
                        style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)', color: '#D8F9FF' }}
                      >
                        Run Now
                      </button>
                      <button
                        type="button"
                        onClick={() => editExisting(command)}
                        className="px-3 py-2 rounded-2xl text-xs"
                        style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)', color: '#D8F9FF' }}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        onClick={() => deleteCustomCommand(command.id)}
                        className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                        style={{ background: 'rgba(255,92,92,0.08)', border: '1px solid rgba(255,92,92,0.18)', color: '#FFD3D3' }}>
                        <Trash2 className="w-3.5 h-3.5" />
                        Delete
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}

          {(snapshot?.customCommands.length ?? 0) === 0 && (
            <div className="rounded-3xl px-4 py-5 text-sm text-cyan-100/68"
                 style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.1)' }}>
              No saved Smart Home phrases yet. Build the first one on the left and Atlas will route it through the native Smart Home command engine.
            </div>
          )}
        </div>
      </section>
    </div>
  );
}

function getDraftTargetKey(draft: SmartHomeCustomCommandDraft | null) {
  if (!draft) {
    return '';
  }

  if ((draft.targetKind || 'device') === 'group' && draft.targetScope) {
    return `group:${draft.targetScope}`;
  }

  if ((draft.targetKind || 'device') === 'scene' && draft.deviceId) {
    return `scene:${draft.deviceId}`;
  }

  return `device:${draft.providerId}:${draft.deviceId}`;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <p className="text-xs uppercase tracking-[0.16em] text-cyan-400/58 mb-2">{label}</p>
      {children}
    </label>
  );
}

function CapabilityValueEditor({ capability, value, onChange }: { capability: SmartHomeCapability | null; value: unknown; onChange: (value: unknown) => void }) {
  if (!capability) {
    return null;
  }

  if (isRgbValue(value)) {
    const hexValue = rgbToHex(value.r, value.g, value.b);
    return (
      <div className="space-y-3">
        <div className="flex items-center gap-3">
          <input
            type="color"
            value={hexValue}
            onChange={(event) => onChange(hexToRgb(event.target.value))}
            className="w-14 h-12 rounded-xl bg-transparent"
          />
          <input
            value={hexValue}
            onChange={(event) => onChange(hexToRgb(normalizeHex(event.target.value)))}
            className="flex-1 px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
            style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
          />
        </div>
        {(['r', 'g', 'b'] as const).map((channel) => (
          <label key={channel} className="flex items-center gap-3">
            <span className="w-4 text-[11px] uppercase tracking-[0.18em] text-cyan-300/70">{channel}</span>
            <input
              type="range"
              min={0}
              max={255}
              value={value[channel]}
              onChange={(event) => onChange({ ...value, [channel]: Number(event.target.value) })}
              className="flex-1 accent-cyan-400"
            />
            <span className="w-8 text-right text-[11px] text-cyan-200">{value[channel]}</span>
          </label>
        ))}
      </div>
    );
  }

  if (capability.options.length > 0) {
    return (
      <Select
        value={JSON.stringify(value)}
        onValueChange={(nextValue) => onChange(JSON.parse(nextValue))}
      >
        <SelectTrigger
          className="w-full rounded-2xl px-4 py-3 text-sm"
          style={{ border: '1px solid rgba(0,212,255,0.22)', background: 'rgba(4, 16, 28, 0.92)', color: '#D8F9FF' }}
        >
          <SelectValue placeholder="Select a value" />
        </SelectTrigger>
        <SelectContent
          className="rounded-2xl border-0"
          style={{ background: 'rgba(4, 16, 28, 0.98)', color: '#D8F9FF', boxShadow: '0 24px 48px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,212,255,0.16)' }}
        >
          {capability.options.map((option, index) => (
            <SelectItem
              key={`${option.name}:${index}`}
              value={JSON.stringify(option.value)}
              className="rounded-xl text-sm text-cyan-100 focus:text-cyan-50"
              style={{ background: 'transparent' }}
            >
              {option.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }

  if (capability.dataType.toLowerCase() === 'boolean') {
    return (
      <div className="flex gap-2">
        {[true, false].map((optionValue) => (
          <button
            key={String(optionValue)}
            type="button"
            onClick={() => onChange(optionValue)}
            className="px-4 py-3 rounded-2xl text-sm"
            style={{ background: value === optionValue ? 'rgba(0,212,255,0.16)' : 'rgba(255,255,255,0.03)', border: value === optionValue ? '1px solid rgba(0,212,255,0.28)' : '1px solid rgba(255,255,255,0.08)', color: '#D8F9FF' }}
          >
            {optionValue ? 'On' : 'Off'}
          </button>
        ))}
      </div>
    );
  }

  if (capability.dataType.toLowerCase() === 'integer') {
    return (
      <div className="grid grid-cols-[1fr_auto] gap-3">
        <input
          type="range"
          min={capability.min ?? 0}
          max={capability.max ?? 100}
          value={typeof value === 'number' ? value : Number(value ?? capability.min ?? 0)}
          onChange={(event) => onChange(Number(event.target.value))}
          className="w-full accent-cyan-400"
        />
        <input
          type="number"
          min={capability.min ?? 0}
          max={capability.max ?? 100}
          value={typeof value === 'number' ? value : Number(value ?? capability.min ?? 0)}
          onChange={(event) => onChange(Number(event.target.value))}
          className="w-24 px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
          style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
        />
      </div>
    );
  }

  return (
    <input
      value={typeof value === 'string' ? value : JSON.stringify(value ?? '')}
      onChange={(event) => onChange(event.target.value)}
      className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
      style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
    />
  );
}

function isRgbValue(value: unknown): value is { r: number; g: number; b: number } {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return typeof candidate.r === 'number' && typeof candidate.g === 'number' && typeof candidate.b === 'number';
}

function rgbToHex(red: number, green: number, blue: number) {
  return `#${[red, green, blue]
    .map((channel) => Math.max(0, Math.min(255, channel)).toString(16).padStart(2, '0'))
    .join('')}`;
}

function hexToRgb(hex: string) {
  const normalized = normalizeHex(hex).replace('#', '');
  return {
    r: parseInt(normalized.slice(0, 2), 16),
    g: parseInt(normalized.slice(2, 4), 16),
    b: parseInt(normalized.slice(4, 6), 16),
  };
}

function normalizeHex(value: string) {
  const trimmed = value.trim();
  if (/^#[0-9a-f]{6}$/i.test(trimmed)) {
    return trimmed;
  }

  return '#00d4ff';
}