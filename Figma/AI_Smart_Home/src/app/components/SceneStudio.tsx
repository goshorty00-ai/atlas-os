import { useEffect, useMemo, useState } from 'react';
import { Bot, Palette, Play, Save, Sparkles, Trash2, Wand2 } from 'lucide-react';
import { deleteScene, executeScenePreview, refreshState, runScene, saveScene } from '../bridge';
import type {
  SmartHomeCapability,
  SmartHomeDevice,
  SmartHomeProviderState,
  SmartHomeSceneAction,
  SmartHomeSceneDraft,
  SmartHomeSavedScene,
  SmartHomeSnapshot,
} from '../types';

interface SceneStudioProps {
  snapshot: SmartHomeSnapshot | null;
}

interface LightDevice {
  providerId: string;
  providerName: string;
  deviceId: string;
  deviceName: string;
  sku: string;
  supportsPower: boolean;
  supportsBrightness: boolean;
  supportsHue: boolean;
  supportsSaturation: boolean;
  supportsRgb: boolean;
  rgbCapabilityType?: string;
  rgbCapabilityInstance?: string;
  sceneCapabilityType?: string;
  sceneCapabilityInstance?: string;
  sceneOptions: Array<{ name: string; value: unknown }>;
}

interface LightSceneAssignment {
  deviceId: string;
  enabled: boolean;
  hexColor: string;
  brightness: number;
}

const paletteLibrary = [
  { id: 'tokyo', name: 'Tokyo', description: 'Neon purple, blue, and pink city glow.', colors: ['#7a3cff', '#4d6bff', '#ff4fa3', '#c44dff', '#38d9ff'] },
  { id: 'soho', name: 'Soho', description: 'Warm pink corridor with violet edges.', colors: ['#ff5fa2', '#ff7ac8', '#c553ff', '#7b5cff', '#ff9ab5'] },
  { id: 'vapor-wave', name: 'Vapor wave', description: 'Retro synth glow with cyan, magenta, and violet.', colors: ['#9b5dff', '#ff71ce', '#01cdfe', '#b967ff', '#05ffa1'] },
  { id: 'ibiza', name: 'Ibiza', description: 'Warm nightlife shimmer.', colors: ['#ffb000', '#ff8a00', '#ff5e7e', '#ffd166', '#ff9f1c'] },
  { id: 'spring-blossom', name: 'Spring blossom', description: 'Soft bloom pinks with fresh highlights.', colors: ['#f7b7d2', '#fbd4e2', '#ff8cb3', '#ffd8a8', '#fff1c1'] },
  { id: 'nebula', name: 'Nebula', description: 'Deep dusk purple with cool blue haze.', colors: ['#42206f', '#6c3bf5', '#2e6cff', '#7b5cff', '#1b0f38'] },
  { id: 'tropical-twilight', name: 'Tropical twilight', description: 'Palm-sunset peach and violet blend.', colors: ['#f39c6b', '#ffb36b', '#7a4dff', '#d86dff', '#ffd37a'] },
  { id: 'concentrate', name: 'Concentrate', description: 'Cool clarity with bright cyan focus.', colors: ['#dff5ff', '#8ce6ff', '#67c5ff', '#d5f7ff', '#b8efff'] },
];

const goveeFallbackScenes: Array<{ keywords: string[] }> = [
  { keywords: ['tokyo', 'soho', 'pink', 'purple', 'neon'] },
  { keywords: ['vapor', 'retro', 'wave', 'movie'] },
  { keywords: ['ibiza', 'party', 'nightlife'] },
  { keywords: ['spring', 'blossom', 'romantic'] },
  { keywords: ['nebula', 'focus', 'concentrate', 'blue'] },
  { keywords: ['sunrise', 'gold', 'amber'] },
  { keywords: ['sunset', 'warm', 'orange'] },
];

export function SceneStudio({ snapshot }: SceneStudioProps) {
  const lights = useMemo(() => buildLightDevices(snapshot?.providers ?? []), [snapshot?.providers]);
  const [prompt, setPrompt] = useState('');
  const [draft, setDraft] = useState<SmartHomeSceneDraft | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);

  useEffect(() => {
    if (lights.length === 0) {
      setDraft(null);
      return;
    }

    setDraft((current) => current ?? createEmptyDraft(lights));
  }, [lights]);

  const savedScenes = snapshot?.customScenes ?? [];
  const assignments = useMemo(() => buildAssignmentsMap(draft), [draft]);

  const applyActionsOnce = (actions: SmartHomeSceneAction[]) => {
    if (actions.length === 0) {
      return;
    }

    executeScenePreview(draft?.name || 'Preview Scene', actions);
    window.setTimeout(() => refreshState(), 1200);
  };

  const playSceneTransition = async (scene: Pick<SmartHomeSceneDraft, 'name' | 'previewColors' | 'actions'>) => {
    if (isPlaying) {
      return;
    }

    const effectiveDraft: SmartHomeSceneDraft = {
      name: scene.name,
      phrase: '',
      previewColors: scene.previewColors,
      actions: scene.actions,
    };

    const sceneAssignments = buildAssignmentsMap(effectiveDraft);
    const frames = buildAnimatedFrames(lights, sceneAssignments, effectiveDraft);
    if (frames.length === 0) {
      applyActionsOnce(effectiveDraft.actions);
      return;
    }

    setIsPlaying(true);

    try {
      for (let index = 0; index < frames.length; index += 1) {
        const frame = frames[index];
        executeScenePreview(`${effectiveDraft.name || 'Scene'} • frame ${index + 1}`, frame.actions);
        await wait(frame.delayMs);
      }
    } finally {
      setIsPlaying(false);
      window.setTimeout(() => refreshState(), 1200);
    }
  };

  const playDraftSequence = async () => {
    if (!draft) {
      return;
    }

    await playSceneTransition(draft);
  };

  const applyPalette = (paletteName: string, colors: string[]) => {
    if (!draft) {
      return;
    }

    const nextActions = buildActionsFromPalette(lights, paletteName, colors, assignments, draft.actions);
    setDraft({
      ...draft,
      name: draft.name || paletteName,
      phrase: draft.phrase || `run ${paletteName.toLowerCase()}`,
      previewColors: colors.slice(0, 5),
      actions: nextActions,
    });

    applyActionsOnce(nextActions);
  };

  const generateFromPrompt = () => {
    if (!draft) {
      return;
    }

    const generated = generateSceneFromPrompt(prompt, lights, assignments, draft.actions);
    setDraft({
      ...draft,
      name: generated.name,
      phrase: generated.phrase,
      previewColors: generated.previewColors,
      actions: generated.actions,
    });
    applyActionsOnce(generated.actions);
  };

  const updateAssignment = (deviceId: string, patch: Partial<LightSceneAssignment>) => {
    setDraft((current) => {
      if (!current) {
        return current;
      }

      const nextAssignments = lights.map((light) => {
        const currentAssignment = assignments.get(light.deviceId) ?? createDefaultAssignment(light.deviceId, '#c41fff');
        if (light.deviceId !== deviceId) {
          return currentAssignment;
        }

        return {
          ...currentAssignment,
          ...patch,
        };
      });

      return {
        ...current,
        previewColors: nextAssignments.filter((assignment) => assignment.enabled).map((assignment) => assignment.hexColor).slice(0, 5),
        actions: buildActionsFromAssignments(lights, nextAssignments, current.actions, current.name),
      };
    });
  };

  const saveDraft = () => {
    if (!draft || !draft.name.trim()) {
      return;
    }

    const actions = draft.actions.filter((action) => !!action.deviceId);
    if (actions.length === 0) {
      return;
    }

    saveScene({
      ...draft,
      name: draft.name.trim(),
      phrase: draft.phrase?.trim() || '',
      previewColors: draft.previewColors,
      actions,
    });
  };

  const applyDraftOnce = () => {
    if (!draft) {
      return;
    }

    applyActionsOnce(draft.actions);
  };

  const editScene = (scene: SmartHomeSavedScene) => {
    setDraft({
      id: scene.id,
      enabled: scene.enabled,
      name: scene.name,
      phrase: scene.phrase,
      previewColors: scene.previewColors,
      actions: scene.actions,
    });
  };

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 xl:grid-cols-[1.1fr_0.9fr] gap-6">
        <section className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
          <div className="flex items-center gap-3 mb-5">
            <div className="w-11 h-11 rounded-2xl flex items-center justify-center bg-purple-500/10 border border-purple-400/20">
              <Bot className="w-5 h-5 text-purple-300" />
            </div>
            <div>
              <p className="text-xs uppercase tracking-[0.22em] text-purple-300/60">AI Scene Generator</p>
              <h3 className="text-2xl font-semibold text-gray-100">Build Scenes From Mood Or Palette</h3>
            </div>
          </div>

          <div className="space-y-4">
            <label className="block">
              <div className="text-sm text-gray-400 mb-2">Describe the vibe or palette</div>
              <input
                value={prompt}
                onChange={(event) => setPrompt(event.target.value)}
                placeholder="Tokyo purple blue pink neon"
                className="w-full rounded-2xl bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
              />
            </label>

            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                onClick={generateFromPrompt}
                className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2 bg-purple-600 hover:bg-purple-700 text-white"
              >
                <Sparkles className="w-4 h-4" />
                Generate Scene
              </button>
              {paletteLibrary.slice(0, 5).map((palette) => (
                <button
                  key={palette.id}
                  type="button"
                  onClick={() => applyPalette(palette.name, palette.colors)}
                  className="px-3 py-2 rounded-full text-xs border border-gray-700 hover:border-purple-400/40 text-gray-200"
                >
                  {palette.name}
                </button>
              ))}
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-2">
              {paletteLibrary.map((palette) => (
                <button
                  key={palette.id}
                  type="button"
                  onClick={() => applyPalette(palette.name, palette.colors)}
                  className="rounded-3xl p-5 text-left border border-gray-800/60 hover:border-purple-400/40 transition-all"
                  style={{ background: buildGradient(palette.colors) }}
                >
                  <div className="text-xl font-semibold text-white mb-2">{palette.name}</div>
                  <div className="text-sm text-white/80 mb-4">{palette.description}</div>
                  <div className="flex gap-2">
                    {palette.colors.slice(0, 5).map((color) => (
                      <div key={color} className="w-7 h-7 rounded-full border border-white/20" style={{ backgroundColor: color }} />
                    ))}
                  </div>
                </button>
              ))}
            </div>
          </div>
        </section>

        <section className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
          <div className="flex items-center gap-3 mb-5">
            <div className="w-11 h-11 rounded-2xl flex items-center justify-center bg-cyan-500/10 border border-cyan-400/20">
              <Palette className="w-5 h-5 text-cyan-300" />
            </div>
            <div>
              <p className="text-xs uppercase tracking-[0.22em] text-cyan-300/60">Scene Details</p>
              <h3 className="text-2xl font-semibold text-gray-100">Save Or Apply Once</h3>
            </div>
          </div>

          {draft ? (
            <div className="space-y-4">
              <label className="block">
                <div className="text-sm text-gray-400 mb-2">Scene name</div>
                <input
                  value={draft.name}
                  onChange={(event) => setDraft({ ...draft, name: event.target.value })}
                  placeholder="Tokyo"
                  className="w-full rounded-2xl bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
                />
              </label>
              <label className="block">
                <div className="text-sm text-gray-400 mb-2">Optional phrase</div>
                <input
                  value={draft.phrase ?? ''}
                  onChange={(event) => setDraft({ ...draft, phrase: event.target.value })}
                  placeholder="run tokyo"
                  className="w-full rounded-2xl bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
                />
              </label>

              <div className="rounded-3xl p-5 border border-gray-800/60 bg-gray-950/80">
                <div className="text-sm text-gray-400 mb-3">Scene preview</div>
                <div className="rounded-3xl h-32 mb-4 border border-gray-800/60" style={{ background: buildGradient(draft.previewColors) }} />
                <div className="flex gap-3 flex-wrap">
                  {draft.previewColors.map((color, index) => (
                    <div key={`${color}:${index}`} className="w-10 h-10 rounded-full border border-white/10" style={{ backgroundColor: color }} />
                  ))}
                </div>
              </div>

              <div className="flex flex-wrap gap-3">
                <button type="button" onClick={saveDraft} className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2 bg-cyan-600 hover:bg-cyan-700 text-white">
                  <Save className="w-4 h-4" />
                  Save Scene
                </button>
                <button type="button" onClick={applyDraftOnce} className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2 border border-gray-700 hover:border-cyan-500/40 text-gray-100">
                  <Play className="w-4 h-4" />
                  Set Once
                </button>
                <button
                  type="button"
                  onClick={playDraftSequence}
                  disabled={isPlaying}
                  className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2 border border-purple-500/40 hover:border-purple-400 text-purple-100 disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  <Play className="w-4 h-4" />
                  {isPlaying ? 'Playing Scene…' : 'Play Transition'}
                </button>
              </div>
            </div>
          ) : (
            <div className="text-sm text-gray-400">No color-capable lights are available for scene creation yet.</div>
          )}
        </section>
      </div>

      {draft ? (
        <section className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
          <div className="flex items-center gap-3 mb-5">
            <div className="w-11 h-11 rounded-2xl flex items-center justify-center bg-yellow-500/10 border border-yellow-400/20">
              <Wand2 className="w-5 h-5 text-yellow-200" />
            </div>
            <div>
              <p className="text-xs uppercase tracking-[0.22em] text-yellow-200/60">Per-Light Builder</p>
              <h3 className="text-2xl font-semibold text-gray-100">Assign Individual Colors</h3>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {lights.map((light) => {
              const assignment = assignments.get(light.deviceId) ?? createDefaultAssignment(light.deviceId, '#c41fff');
              return (
                <div key={`${light.providerId}:${light.deviceId}`} className="rounded-3xl p-5 border border-gray-800/60 bg-gray-950/70">
                  <div className="flex items-center justify-between gap-3 mb-4">
                    <div>
                      <div className="text-lg font-semibold text-gray-100">{light.deviceName}</div>
                      <div className="text-sm text-gray-500">{light.providerName}</div>
                    </div>
                    <label className="inline-flex items-center gap-2 text-sm text-gray-300">
                      <input type="checkbox" checked={assignment.enabled} onChange={(event) => updateAssignment(light.deviceId, { enabled: event.target.checked })} />
                      Active
                    </label>
                  </div>

                  <div className="space-y-4">
                    <label className="block">
                      <div className="text-sm text-gray-400 mb-2">Color</div>
                      <div className="flex items-center gap-3">
                        <input type="color" value={assignment.hexColor} onChange={(event) => updateAssignment(light.deviceId, { hexColor: event.target.value })} className="w-14 h-12 rounded-xl bg-transparent" />
                        <input value={assignment.hexColor} onChange={(event) => updateAssignment(light.deviceId, { hexColor: normalizeHex(event.target.value) })} className="flex-1 rounded-2xl bg-gray-900 border border-gray-800 px-4 py-3 text-sm text-gray-100" />
                      </div>
                    </label>

                    <label className="block">
                      <div className="text-sm text-gray-400 mb-2">Brightness</div>
                      <input type="range" min={5} max={100} value={assignment.brightness} onChange={(event) => updateAssignment(light.deviceId, { brightness: Number(event.target.value) })} className="w-full accent-cyan-400" />
                      <div className="text-xs text-gray-500 mt-1">{assignment.brightness}%</div>
                    </label>
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      ) : null}

      <section className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
        <div className="flex items-center justify-between gap-3 mb-5">
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-300/60">My Scenes</p>
            <h3 className="text-2xl font-semibold text-gray-100">Saved Scene Library</h3>
          </div>
          <div className="text-sm text-gray-400">{savedScenes.length} saved</div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {savedScenes.map((scene) => (
            <div key={scene.id} className="rounded-3xl overflow-hidden border border-gray-800/60 bg-gray-950/70">
              <div className="h-40 p-5 flex items-end" style={{ background: buildGradient(scene.previewColors) }}>
                <div>
                  <div className="text-3xl font-semibold text-white">{scene.name}</div>
                  <div className="text-sm text-white/80 mt-1">{scene.phrase || 'No phrase bound yet'}</div>
                </div>
              </div>
              <div className="p-5">
                <div className="flex gap-2 flex-wrap mb-4">
                  {scene.previewColors.map((color, index) => (
                    <div key={`${scene.id}:${color}:${index}`} className="w-8 h-8 rounded-full border border-white/10" style={{ backgroundColor: color }} />
                  ))}
                </div>
                <div className="flex gap-2 flex-wrap">
                  <button type="button" onClick={() => runScene(scene.id)} className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2 bg-cyan-600 hover:bg-cyan-700 text-white">
                    <Play className="w-3.5 h-3.5" />
                    Run Scene
                  </button>
                  <button type="button" onClick={() => void playSceneTransition(scene)} className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2 border border-purple-500/40 text-purple-100 hover:border-purple-300/60">
                    <Play className="w-3.5 h-3.5" />
                    Play Transition
                  </button>
                  <button type="button" onClick={() => editScene(scene)} className="px-3 py-2 rounded-2xl text-xs border border-gray-700 hover:border-cyan-500/40 text-gray-100">
                    Edit
                  </button>
                  <button type="button" onClick={() => deleteScene(scene.id)} className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2 border border-red-500/40 text-red-300 hover:bg-red-500/10">
                    <Trash2 className="w-3.5 h-3.5" />
                    Delete
                  </button>
                </div>
              </div>
            </div>
          ))}
          {savedScenes.length === 0 ? <div className="text-sm text-gray-400 rounded-3xl border border-dashed border-gray-800 p-5">No saved scenes yet. Generate one from a palette or prompt above.</div> : null}
        </div>
      </section>
    </div>
  );
}

function buildLightDevices(providers: SmartHomeProviderState[]) {
  const result: LightDevice[] = [];
  for (const provider of providers) {
    for (const device of provider.devices) {
      const sceneCapability = findSceneCapability(device.capabilities);
      const rgbCapability = findRgbCapability(device.capabilities);
      if (!isLightDevice(device) && !sceneCapability) {
        continue;
      }

      result.push({
        providerId: provider.providerId,
        providerName: provider.displayName,
        deviceId: device.deviceId,
        deviceName: device.name,
        sku: device.sku,
        supportsPower: hasCapability(device.capabilities, 'powerSwitch'),
        supportsBrightness: hasCapability(device.capabilities, 'brightness'),
        supportsHue: hasCapability(device.capabilities, 'colorHue'),
        supportsSaturation: hasCapability(device.capabilities, 'colorSaturation'),
        supportsRgb: Boolean(rgbCapability),
        rgbCapabilityType: rgbCapability?.type,
        rgbCapabilityInstance: rgbCapability?.instance,
        sceneCapabilityType: sceneCapability?.type,
        sceneCapabilityInstance: sceneCapability?.instance,
        sceneOptions: sceneCapability?.options.map((option) => ({ name: option.name, value: option.value })) ?? [],
      });
    }
  }

  return result;
}

function isLightDevice(device: SmartHomeDevice) {
  const text = `${device.name} ${device.deviceType} ${device.sku}`.toLowerCase();
  return text.includes('light') ||
    text.includes('lamp') ||
    text.includes('bulb') ||
    text.includes('strip') ||
    text.includes('hue') ||
    device.capabilities.some((capability) => {
      const instance = capability.instance.toLowerCase();
      return instance === 'powerswitch' ||
        instance === 'brightness' ||
        instance === 'colorhue' ||
        instance === 'colorsaturation' ||
        instance.includes('scene');
    });
}

function hasCapability(capabilities: SmartHomeCapability[], instance: string) {
  return capabilities.some((capability) => capability.instance.toLowerCase() === instance.toLowerCase());
}

function findSceneCapability(capabilities: SmartHomeCapability[]) {
  return capabilities.find((capability) => {
    const type = capability.type.toLowerCase();
    const instance = capability.instance.toLowerCase();
    return type.includes('dynamic_scene') ||
      type.includes('music_setting') ||
      instance === 'lightscene' ||
      instance === 'diyscene' ||
      instance === 'musicmode' ||
      instance.includes('scene');
  });
}

function findRgbCapability(capabilities: SmartHomeCapability[]) {
  return capabilities.find((capability) => {
    const type = capability.type.toLowerCase();
    const instance = capability.instance.toLowerCase();
    return type.includes('color_setting') && instance === 'colorrgb';
  });
}

function createEmptyDraft(lights: LightDevice[]): SmartHomeSceneDraft {
  const defaultAssignments = lights.map((light, index) => createDefaultAssignment(light.deviceId, paletteLibrary[0].colors[index % paletteLibrary[0].colors.length]));
  return {
    name: '',
    phrase: '',
    previewColors: defaultAssignments.map((assignment) => assignment.hexColor).slice(0, 5),
    actions: buildActionsFromAssignments(lights, defaultAssignments, [], paletteLibrary[0].name),
  };
}

function createDefaultAssignment(deviceId: string, hexColor: string): LightSceneAssignment {
  return {
    deviceId,
    enabled: true,
    hexColor,
    brightness: 72,
  };
}

function buildAssignmentsMap(draft: SmartHomeSceneDraft | null) {
  const map = new Map<string, LightSceneAssignment>();
  for (const action of draft?.actions ?? []) {
    const current = map.get(action.deviceId) ?? createDefaultAssignment(action.deviceId, action.hexColor || '#c41fff');
    current.enabled = true;
    if (action.hexColor) {
      current.hexColor = action.hexColor;
    }
    if (action.capabilityInstance === 'brightness' && typeof action.value === 'number') {
      current.brightness = action.value;
    }
    map.set(action.deviceId, current);
  }

  return map;
}

function buildActionsFromPalette(
  lights: LightDevice[],
  sceneName: string,
  colors: string[],
  assignments: Map<string, LightSceneAssignment>,
  existingActions: SmartHomeSceneAction[],
) {
  const nextAssignments = lights.map((light, index) => ({
    ...(assignments.get(light.deviceId) ?? createDefaultAssignment(light.deviceId, colors[index % colors.length])),
    enabled: true,
    hexColor: colors[index % colors.length],
  }));
  return buildActionsFromAssignments(lights, nextAssignments, existingActions, sceneName);
}

function buildActionsFromAssignments(
  lights: LightDevice[],
  assignments: LightSceneAssignment[],
  existingActions: SmartHomeSceneAction[] = [],
  sceneName = '',
) {
  const actions: SmartHomeSceneAction[] = [];

  for (const light of lights) {
    const assignment = assignments.find((entry) => entry.deviceId === light.deviceId);
    if (!assignment || !assignment.enabled) {
      continue;
    }

    const hsv = hexToHsv(assignment.hexColor);
    if (light.supportsPower) {
      actions.push(createSceneAction(light, 'devices.capabilities.on_off', 'powerSwitch', true, assignment.hexColor));
    }
    if (light.supportsBrightness) {
      actions.push(createSceneAction(light, 'devices.capabilities.range', 'brightness', assignment.brightness, assignment.hexColor));
    }
    if (light.supportsRgb && light.rgbCapabilityType && light.rgbCapabilityInstance) {
      actions.push(createSceneAction(light, light.rgbCapabilityType, light.rgbCapabilityInstance, hexToRgb(assignment.hexColor), assignment.hexColor));
    } else if (light.supportsHue) {
      actions.push(createSceneAction(light, 'devices.capabilities.range', 'colorHue', hsv.hue, assignment.hexColor));
    }
    if (light.supportsSaturation) {
      actions.push(createSceneAction(light, 'devices.capabilities.range', 'colorSaturation', hsv.saturation, assignment.hexColor));
    }
    if (!light.supportsHue && !light.supportsSaturation && !light.supportsRgb && light.sceneCapabilityType && light.sceneCapabilityInstance) {
      const matchedOption = resolveSceneOption(light, sceneName, assignment.hexColor);
      if (matchedOption) {
        actions.push(createSceneAction(light, light.sceneCapabilityType, light.sceneCapabilityInstance, matchedOption.value, assignment.hexColor));
      }
    }
  }

  for (const existingAction of existingActions) {
    if (!actions.some((candidate) => candidate.deviceId === existingAction.deviceId && candidate.capabilityInstance === existingAction.capabilityInstance)) {
      const light = lights.find((candidate) => candidate.deviceId === existingAction.deviceId);
      if (!light || !assignments.find((assignment) => assignment.deviceId === existingAction.deviceId)?.enabled) {
        continue;
      }
      actions.push(existingAction);
    }
  }

  return actions;
}

function resolveSceneOption(light: LightDevice, sceneName: string, hexColor: string) {
  const normalized = `${sceneName} ${hexColor}`.toLowerCase();
  const exactOption = light.sceneOptions.find((option) => option.name && normalized.includes(option.name.toLowerCase()));
  if (exactOption) {
    return exactOption;
  }

  const fallback = goveeFallbackScenes.find((entry) => entry.keywords.some((keyword) => normalized.includes(keyword)));
  if (fallback) {
    const fallbackOption = light.sceneOptions.find((option) => {
      const optionName = (option.name ?? '').toLowerCase();
      return fallback.keywords.some((keyword) => optionName.includes(keyword));
    });

    if (fallbackOption) {
      return fallbackOption;
    }
  }

  return light.sceneOptions.length === 1 ? light.sceneOptions[0] : undefined;
}

function createSceneAction(light: LightDevice, capabilityType: string, capabilityInstance: string, value: unknown, hexColor: string): SmartHomeSceneAction {
  return {
    providerId: light.providerId,
    deviceId: light.deviceId,
    deviceName: light.deviceName,
    sku: light.sku,
    capabilityType,
    capabilityInstance,
    value,
    hexColor,
  };
}

function generateSceneFromPrompt(
  prompt: string,
  lights: LightDevice[],
  assignments: Map<string, LightSceneAssignment>,
  existingActions: SmartHomeSceneAction[],
) {
  const normalized = prompt.trim().toLowerCase();
  const matchedPalette = paletteLibrary.find((palette) => normalized.includes(palette.id.replace(/-/g, ' ')) || normalized.includes(palette.name.toLowerCase()));
  const extractedColors = extractNamedColors(normalized);
  const colors = extractedColors.length > 0 ? extractedColors : matchedPalette?.colors ?? paletteLibrary[0].colors;
  const name = toTitleCase(prompt.trim()) || matchedPalette?.name || 'New Scene';
  const phrase = `run ${name.toLowerCase()}`;
  return {
    name,
    phrase,
    previewColors: colors.slice(0, 5),
    actions: buildActionsFromPalette(lights, name, colors, assignments, existingActions),
  };
}

function extractNamedColors(prompt: string) {
  const lookup: Record<string, string> = {
    purple: '#b41fff',
    blue: '#4e7bff',
    pink: '#ff4f9a',
    cyan: '#59f0d0',
    teal: '#2dd4bf',
    orange: '#ff995e',
    amber: '#ffc857',
    red: '#ff4d6d',
    white: '#f5f7ff',
    gold: '#ffd166',
  };

  return Object.entries(lookup)
    .filter(([name]) => prompt.includes(name))
    .map(([, color]) => color);
}

function normalizeHex(value: string) {
  const trimmed = value.trim();
  if (/^#[0-9a-f]{6}$/i.test(trimmed)) {
    return trimmed;
  }
  return '#c41fff';
}

function hexToHsv(hex: string) {
  const normalized = hex.replace('#', '');
  const red = parseInt(normalized.slice(0, 2), 16) / 255;
  const green = parseInt(normalized.slice(2, 4), 16) / 255;
  const blue = parseInt(normalized.slice(4, 6), 16) / 255;

  const max = Math.max(red, green, blue);
  const min = Math.min(red, green, blue);
  const delta = max - min;

  let hue = 0;
  if (delta !== 0) {
    if (max === red) {
      hue = 60 * (((green - blue) / delta) % 6);
    } else if (max === green) {
      hue = 60 * (((blue - red) / delta) + 2);
    } else {
      hue = 60 * (((red - green) / delta) + 4);
    }
  }

  if (hue < 0) {
    hue += 360;
  }

  const saturation = max === 0 ? 0 : Math.round((delta / max) * 100);
  const brightness = Math.round(max * 100);
  return { hue: Math.round(hue), saturation, brightness };
}

function hexToRgb(hex: string) {
  const normalized = hex.replace('#', '');
  return {
    r: parseInt(normalized.slice(0, 2), 16),
    g: parseInt(normalized.slice(2, 4), 16),
    b: parseInt(normalized.slice(4, 6), 16),
  };
}

function buildGradient(colors: string[]) {
  const safe = colors.length > 0 ? colors : ['#312e81', '#0f172a', '#1f2937'];
  return `linear-gradient(135deg, ${safe.join(', ')})`;
}

function buildAnimatedFrames(
  lights: LightDevice[],
  assignments: Map<string, LightSceneAssignment>,
  draft: SmartHomeSceneDraft,
) {
  const baseColors = draft.previewColors.length > 0 ? draft.previewColors : ['#c41fff'];
  const enabledLights = lights.filter((light) => (assignments.get(light.deviceId) ?? createDefaultAssignment(light.deviceId, baseColors[0])).enabled);
  if (enabledLights.length === 0) {
    return [];
  }

  const steps = Math.max(baseColors.length, 4);
  const frames: Array<{ actions: SmartHomeSceneAction[]; delayMs: number }> = [];

  for (let step = 0; step < steps; step += 1) {
    const nextAssignments = enabledLights.map((light, index) => {
      const current = assignments.get(light.deviceId) ?? createDefaultAssignment(light.deviceId, baseColors[index % baseColors.length]);
      return {
        ...current,
        enabled: true,
        hexColor: baseColors[(index + step) % baseColors.length],
      };
    });

    frames.push({
      actions: buildActionsFromAssignments(lights, nextAssignments, draft.actions, draft.name),
      delayMs: 900,
    });
  }

  return frames;
}

function wait(delayMs: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, delayMs);
  });
}

function toTitleCase(value: string) {
  return value.replace(/\b\w/g, (letter) => letter.toUpperCase());
}