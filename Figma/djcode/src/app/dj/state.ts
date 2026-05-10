import type {
  DeckLabel,
  DjBrowserFilterMode,
  DjBrowserSection,
  DjBrowserSortMode,
  DjBrowserState,
  DjAutoMixState,
  DjConsoleMode,
  DjControllerState,
  DjCrossfaderCurve,
  DjDeckState,
  DjLoopSize,
  DjMixerState,
  DjSample,
  DjState,
  DjTrack,
} from './types';

type DjLegacyServerState = Partial<
  Omit<DjState, 'browser' | 'mixer'> & {
    mixer: Partial<DjMixerState>;
    crossfader: number;
    crossfaderCurve: DjCrossfaderCurve;
    masterVolume: number;
    masterLevel: number;
    cueMix: number;
    headphoneVolume: number;
    micVolume: number;
    effectMode: string;
  }
>;

export type DjStateAction =
  | { type: 'server-state'; state: DjLegacyServerState | null | undefined }
  | { type: 'tick'; deltaMs: number }
  | { type: 'load-track'; deck: DeckLabel; track: DjTrack }
  | { type: 'play'; deck: DeckLabel }
  | { type: 'pause'; deck: DeckLabel }
  | { type: 'cue'; deck: DeckLabel }
  | { type: 'sync'; deck: DeckLabel }
  | { type: 'bend'; deck: DeckLabel; delta: number }
  | { type: 'seek'; deck: DeckLabel; value: number }
  | { type: 'set-cue'; deck: DeckLabel; value: number }
  | { type: 'patch-deck'; deck: DeckLabel; patch: Partial<DjDeckState> }
  | { type: 'patch-mixer'; patch: Partial<DjMixerState> }
  | { type: 'toggle-cue-monitor'; deck: DeckLabel }
  | { type: 'set-tempo'; deck: DeckLabel; value: number }
  | { type: 'set-pitch-range'; deck: DeckLabel; value: number }
  | { type: 'set-loop-size'; deck: DeckLabel; value: DjLoopSize }
  | { type: 'loop-in'; deck: DeckLabel }
  | { type: 'loop-out'; deck: DeckLabel }
  | { type: 'loop-clear'; deck: DeckLabel }
  | { type: 'hot-cue'; deck: DeckLabel; cueIndex: number }
  | { type: 'set-waveform-visibility'; deck: DeckLabel; visible: boolean }
  | { type: 'set-browser-search'; value: string }
  | { type: 'set-browser-section'; value: DjBrowserSection }
  | { type: 'set-browser-source'; value: string }
  | { type: 'cycle-browser-filter' }
  | { type: 'cycle-browser-sort' }
  | { type: 'toggle-browser-columns' }
  | { type: 'register-track-load'; path: string }
  | { type: 'set-console-mode'; mode: DjConsoleMode };

function buildPlaceholderWaveform(seed: string, bpm: number) {
  const bars: number[] = [];
  let hash = 0;

  for (let index = 0; index < seed.length; index += 1) {
    hash = (hash * 31 + seed.charCodeAt(index)) >>> 0;
  }

  const bpmInfluence = bpm > 0 ? Math.min(0.22, bpm / 1000) : 0.08;

  for (let index = 0; index < 72; index += 1) {
    const phase = ((hash + index * 37) % 360) * (Math.PI / 180);
    const accent = ((hash >> (index % 8)) & 15) / 15;
    const energy = 0.28 + bpmInfluence + Math.abs(Math.sin(phase)) * 0.4 + accent * 0.18;
    bars.push(Number(Math.min(1, energy).toFixed(3)));
  }

  return bars;
}

function formatClock(seconds: number) {
  const totalSeconds = Math.max(0, Math.round(seconds));
  const minutes = Math.floor(totalSeconds / 60);
  const remainder = totalSeconds % 60;
  return `${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
}

function clampPercent(value: number) {
  return Math.min(100, Math.max(0, value));
}

function getCrossfadeAmount(position: number, curve: DjCrossfaderCurve, isDeckA: boolean) {
  const normalized = position / 100;

  switch (curve) {
    case 'smooth':
      return isDeckA ? Math.cos(normalized * Math.PI * 0.5) : Math.sin(normalized * Math.PI * 0.5);
    case 'sharp':
      return isDeckA
        ? normalized <= 0.5 ? 1 : Math.max(0, 2 - normalized * 2)
        : normalized >= 0.5 ? 1 : Math.max(0, normalized * 2);
    default:
      return isDeckA ? 1 - normalized : normalized;
  }
}

function getEffectiveBpmValue(deck: DjDeckState) {
  if (deck.analysis?.bpm && deck.analysis.bpm > 0) {
    return Number((deck.analysis.bpm * (1 + deck.tempo / 100)).toFixed(2));
  }

  if (deck.track.bpm <= 0) {
    return 0;
  }

  return Number((deck.track.bpm * (1 + deck.tempo / 100)).toFixed(2));
}

function getDeckBaseBpm(deck: DjDeckState) {
  if (deck.analysis?.bpm && deck.analysis.bpm > 0) {
    return deck.analysis.bpm;
  }

  return deck.track.bpm;
}

function getBeatIntervalSeconds(deck: DjDeckState) {
  if (deck.analysis?.beatIntervalSeconds && deck.analysis.beatIntervalSeconds > 0) {
    return deck.analysis.beatIntervalSeconds;
  }

  const bpm = getDeckBaseBpm(deck);
  return bpm > 0 ? 60 / bpm : 0;
}

function getBeatIntervalSecondsForBaseBpm(deck: DjDeckState, baseBpmOverride = 0) {
  const baseBpm = baseBpmOverride > 0 ? baseBpmOverride : getDeckBaseBpm(deck);
  if (deck.analysis?.beatIntervalSeconds && deck.analysis.beatIntervalSeconds > 0) {
    const analysisBpm = deck.analysis.bpm > 0 ? deck.analysis.bpm : getDeckBaseBpm(deck);
    if (analysisBpm > 0 && baseBpm > 0) {
      return deck.analysis.beatIntervalSeconds * (analysisBpm / baseBpm);
    }

    return deck.analysis.beatIntervalSeconds;
  }

  return baseBpm > 0 ? 60 / baseBpm : 0;
}

function getCurrentSeconds(deck: DjDeckState) {
  const dur = deck.track.durationSeconds;
  if (!dur || dur <= 0 || !isFinite(dur)) {
    return 0;
  }

  const ct = deck.currentTime;
  if (!isFinite(ct)) return 0;
  return ct / 100 * dur;
}

function normalizePhaseOffset(offset: number, beatInterval: number) {
  if (beatInterval <= 0) {
    return 0;
  }

  let normalized = offset % beatInterval;
  if (normalized > beatInterval / 2) {
    normalized -= beatInterval;
  } else if (normalized < -beatInterval / 2) {
    normalized += beatInterval;
  }

  return normalized;
}

function normalizePhaseBeatOffset(offset: number) {
  let normalized = offset % 1;
  if (normalized > 0.5) {
    normalized -= 1;
  } else if (normalized < -0.5) {
    normalized += 1;
  }

  return normalized;
}

function getBeatPhaseFraction(deck: DjDeckState, beatInterval: number) {
  if (beatInterval <= 0) {
    return 0;
  }

  const seconds = getCurrentSeconds(deck);
  let phase = seconds % beatInterval;
  if (phase < 0) {
    phase += beatInterval;
  }

  return phase / beatInterval;
}

function getBeatPhaseOffsetSeconds(deck: DjDeckState, deckBeatInterval: number, reference: DjDeckState, referenceBeatInterval: number) {
  if (deckBeatInterval <= 0 || referenceBeatInterval <= 0) {
    return 0;
  }

  const deckPhaseFraction = getBeatPhaseFraction(deck, deckBeatInterval);
  const referencePhaseFraction = getBeatPhaseFraction(reference, referenceBeatInterval);
  return normalizePhaseBeatOffset(deckPhaseFraction - referencePhaseFraction) * deckBeatInterval;
}

function clearDeckSyncState(deck: DjDeckState): DjDeckState {
  return {
    ...deck,
    syncEnabled: false,
    isSyncMaster: false,
    syncSourceDeck: '',
    syncTargetBpm: 0,
    syncTempoDelta: 0,
    syncBeatPhaseOffsetSeconds: 0,
    syncAligned: false,
  };
}

function buildMasterSyncState(deck: DjDeckState): DjDeckState {
  return {
    ...deck,
    syncEnabled: true,
    isSyncMaster: true,
    syncSourceDeck: '',
    syncTargetBpm: getEffectiveBpmValue(deck),
    syncTempoDelta: 0,
    syncBeatPhaseOffsetSeconds: 0,
    syncAligned: true,
  };
}

function synchronizeFollowerDeck(deck: DjDeckState, master: DjDeckState, snapPhase: boolean): DjDeckState {
  if (!deck.track.path || !master.track.path) {
    return clearDeckSyncState(deck);
  }

  const masterEffectiveBpm = getEffectiveBpmValue(master);
  const baseBpm = getDeckBaseBpm(deck);
  if (masterEffectiveBpm <= 0 || baseBpm <= 0) {
    return {
      ...deck,
      syncEnabled: true,
      isSyncMaster: false,
      syncSourceDeck: master.label,
      syncTargetBpm: masterEffectiveBpm,
      syncTempoDelta: 0,
      syncBeatPhaseOffsetSeconds: 0,
      syncAligned: false,
    };
  }

  let adjustedBaseBpm = baseBpm;
  const ratio = masterEffectiveBpm / adjustedBaseBpm;
  if (ratio > 1.5) {
    adjustedBaseBpm *= 2;
  } else if (ratio < 0.667) {
    adjustedBaseBpm /= 2;
  }

  const targetTempo = Math.max(-deck.pitchRange, Math.min(deck.pitchRange, ((masterEffectiveBpm / adjustedBaseBpm) - 1) * 100));
  const deckBeatInterval = getBeatIntervalSecondsForBaseBpm(deck, adjustedBaseBpm);
  const masterBeatInterval = getBeatIntervalSeconds(master);
  let nextDeck: DjDeckState = {
    ...deck,
    syncEnabled: true,
    isSyncMaster: false,
    syncSourceDeck: master.label,
    tempo: targetTempo,
    syncTargetBpm: masterEffectiveBpm,
  };

  let phaseOffset = getBeatPhaseOffsetSeconds(nextDeck, deckBeatInterval, master, masterBeatInterval);
  if (snapPhase && Math.abs(phaseOffset) >= 0.09 && nextDeck.track.durationSeconds > 0) {
    const targetSeconds = Math.max(0, Math.min(nextDeck.track.durationSeconds, getCurrentSeconds(nextDeck) - phaseOffset));
    nextDeck = {
      ...nextDeck,
      currentTime: clampPercent(targetSeconds / nextDeck.track.durationSeconds * 100),
    };
    phaseOffset = getBeatPhaseOffsetSeconds(nextDeck, deckBeatInterval, master, masterBeatInterval);
  }

  return {
    ...nextDeck,
    syncTempoDelta: Number((masterEffectiveBpm - getEffectiveBpmValue(nextDeck)).toFixed(3)),
    syncBeatPhaseOffsetSeconds: Number(phaseOffset.toFixed(4)),
    syncAligned: Math.abs(phaseOffset) <= 0.035,
  };
}

function applyLocalSyncPair(state: DjState): DjState {
  const masterLabel = state.decks.A.syncEnabled && state.decks.A.isSyncMaster
    ? 'A'
    : state.decks.B.syncEnabled && state.decks.B.isSyncMaster
      ? 'B'
      : null;

  if (!masterLabel) {
    return {
      ...state,
      decks: {
        ...state.decks,
        A: state.decks.A.syncEnabled ? state.decks.A : clearDeckSyncState(state.decks.A),
        B: state.decks.B.syncEnabled ? state.decks.B : clearDeckSyncState(state.decks.B),
      },
    };
  }

  const followerLabel: DeckLabel = masterLabel === 'A' ? 'B' : 'A';
  const masterDeck = buildMasterSyncState(state.decks[masterLabel]);
  const followerDeck = state.decks[followerLabel].syncEnabled && !state.decks[followerLabel].isSyncMaster
    ? synchronizeFollowerDeck(state.decks[followerLabel], masterDeck, state.decks[followerLabel].isPlaying || masterDeck.isPlaying)
    : state.decks[followerLabel].syncEnabled
      ? clearDeckSyncState(state.decks[followerLabel])
      : state.decks[followerLabel];

  return {
    ...state,
    decks: {
      ...state.decks,
      [masterLabel]: masterDeck,
      [followerLabel]: followerDeck,
    },
  };
}

function getLoopBeatLength(loopSize: DjLoopSize) {
  switch (loopSize) {
    case '1/4':
      return 1;
    case '1/2':
      return 2;
    case '2':
      return 8;
    default:
      return 4;
  }
}

function getLoopPercent(deck: DjDeckState) {
  const bpm = getEffectiveBpmValue(deck) || deck.track.bpm;
  if (deck.track.durationSeconds <= 0 || bpm <= 0) {
    return 8;
  }

  const loopSeconds = (60 / bpm) * getLoopBeatLength(deck.loopSize);
  return clampPercent((loopSeconds / deck.track.durationSeconds) * 100);
}

function createCuePoint(deck: DeckLabel, cueIndex: number, time: number): DjDeckState['cuePoints'][number] {
  return {
    time,
    label: `C${cueIndex + 1}`,
    color: deck === 'A' ? 'cyan' : 'amber',
  };
}

function formatDeckTiming(deck: DjDeckState): Pick<DjDeckState, 'elapsed' | 'remaining' | 'effectiveBpm'> {
  const durationSeconds = deck.track.durationSeconds;
  const currentSeconds = getCurrentSeconds(deck);
  const remainingSeconds = Math.max(0, durationSeconds - currentSeconds);
  return {
    elapsed: formatClock(currentSeconds),
    remaining: `-${formatClock(remainingSeconds)}`,
    effectiveBpm: getEffectiveBpmValue(deck),
  };
}

function getDeckSourceLevel(deck: DjDeckState) {
  if (!deck.track.path) {
    return 0;
  }

  if (deck.waveformBars.length > 0) {
    const index = Math.round((deck.currentTime / 100) * (deck.waveformBars.length - 1));
    return deck.waveformBars[Math.max(0, Math.min(index, deck.waveformBars.length - 1))] ?? 0;
  }

  if (deck.analysis?.waveformRms?.length) {
    const index = Math.round((deck.currentTime / 100) * (deck.analysis.waveformRms.length - 1));
    return deck.analysis.waveformRms[Math.max(0, Math.min(index, deck.analysis.waveformRms.length - 1))] ?? 0;
  }

  return deck.isPlaying ? 0.45 : 0.15;
}

function recalculateState(state: DjState): DjState {
  const nextDecks = { ...state.decks } as DjState['decks'];

  for (const label of ['A', 'B', 'C', 'D'] as const) {
    const deck = nextDecks[label];
    const timing = formatDeckTiming(deck);
    nextDecks[label] = {
      ...deck,
      ...timing,
      waveformVisible: Boolean(deck.track.path) && deck.waveformVisible,
    };
  }

  const deckA = nextDecks.A;
  const deckB = nextDecks.B;
  const mixA = getCrossfadeAmount(state.mixer.crossfader, state.mixer.crossfaderCurve, true);
  const mixB = getCrossfadeAmount(state.mixer.crossfader, state.mixer.crossfaderCurve, false);
  const masterScalar = state.mixer.masterVolume / 100;

  const computeOutput = (deck: DjDeckState, mixAmount: number) => {
    const source = getDeckSourceLevel(deck);
    const gain = 0.55 + deck.gain / 200;
    const eq = (deck.eqHigh + deck.eqMid + deck.eqLow) / 300;
    const filter = 1 - Math.abs(deck.filter - 50) / 120;
    const transport = deck.isPlaying ? 1 : deck.isCued ? 0.22 : 0.1;
    return Math.min(1, source * mixAmount * (deck.volume / 100) * gain * eq * filter * transport * masterScalar * 1.35);
  };

  nextDecks.A = {
    ...deckA,
    mixAmount: Number(mixA.toFixed(3)),
    level: Number(computeOutput(deckA, mixA).toFixed(3)),
  };
  nextDecks.B = {
    ...deckB,
    mixAmount: Number(mixB.toFixed(3)),
    level: Number(computeOutput(deckB, mixB).toFixed(3)),
  };
  // C/D decks use the same crossfader sides as A/B
  nextDecks.C = {
    ...nextDecks.C,
    mixAmount: Number(mixA.toFixed(3)),
    level: Number(computeOutput(nextDecks.C, mixA).toFixed(3)),
  };
  nextDecks.D = {
    ...nextDecks.D,
    mixAmount: Number(mixB.toFixed(3)),
    level: Number(computeOutput(nextDecks.D, mixB).toFixed(3)),
  };

  const masterLevel = Math.min(1, nextDecks.A.level + nextDecks.B.level + nextDecks.C.level + nextDecks.D.level);

  const recalculated = {
    ...state,
    decks: nextDecks,
    mixer: {
      ...state.mixer,
      masterLevel: Number(masterLevel.toFixed(3)),
    },
  };

  return state.online ? recalculated : applyLocalSyncPair(recalculated);
}

function advanceDeckPlayback(deck: DjDeckState, deltaMs: number): DjDeckState {
  if (!deck.isPlaying || deck.track.durationSeconds <= 0) {
    return deck;
  }

  const speedMultiplier = deck.track.bpm > 0 ? getEffectiveBpmValue(deck) / deck.track.bpm : 1;
  const deltaSeconds = (deltaMs / 1000) * speedMultiplier;
  const deltaPercent = (deltaSeconds / deck.track.durationSeconds) * 100;
  let nextTime = clampPercent(deck.currentTime + deltaPercent);

  if (deck.loopActive && deck.loopStart != null && deck.loopEnd != null && deck.loopEnd > deck.loopStart) {
    const loopLength = deck.loopEnd - deck.loopStart;
    while (nextTime > deck.loopEnd) {
      nextTime = deck.loopStart + (nextTime - deck.loopEnd) % loopLength;
    }
  }

  if (nextTime >= 100) {
    return {
      ...deck,
      currentTime: 100,
      isPlaying: false,
    };
  }

  return {
    ...deck,
    currentTime: nextTime,
  };
}

function loadTrackIntoDeck(state: DjState, deckLabel: DeckLabel, track: DjTrack): DjState {
  const nextDecks = { ...state.decks };
  nextDecks[deckLabel] = {
    ...nextDecks[deckLabel],
    ...createDeckLoadPatch(deckLabel, track),
  } as DjDeckState;

  // Update waveform visibility for all other decks
  for (const label of ['A', 'B', 'C', 'D'] as const) {
    if (label !== deckLabel) {
      nextDecks[label] = {
        ...nextDecks[label],
        waveformVisible: Boolean(nextDecks[label].track.path),
      };
    }
  }

  return recalculateState({ ...state, decks: nextDecks });
}

export function createDeckLoadPatch(deck: DeckLabel, track: DjTrack): Partial<DjDeckState> {
  const durationLabel = track.duration || formatClock(track.durationSeconds);

  return {
    label: deck,
    isPlaying: false,
    isCued: false,
    loopActive: false,
    syncEnabled: false,
    isSyncMaster: false,
    syncSourceDeck: '',
    syncTargetBpm: 0,
    syncTempoDelta: 0,
    syncBeatPhaseOffsetSeconds: 0,
    syncAligned: false,
    currentTime: 0,
    cuePosition: 0,
    loopStart: null,
    loopEnd: null,
    cuePoints: [],
    waveformVisible: true,
    waveformBars: buildPlaceholderWaveform(track.path || `${track.artist}-${track.title}`, track.bpm),
    level: 0,
    elapsed: '00:00',
    remaining: durationLabel ? `-${durationLabel}` : '00:00',
    effectiveBpm: track.bpm,
    track: {
      ...emptyTrack,
      ...track,
      duration: durationLabel,
    },
  };
}

export const emptyTrack: DjTrack = {
  path: '',
  title: '',
  artist: '',
  key: '',
  duration: '',
  durationSeconds: 0,
  bpm: 0,
  source: 'Local Files',
};

export const emptyDeck = (label: DeckLabel): DjDeckState => ({
  label,
  isPlaying: false,
  isCued: false,
  loopActive: false,
  syncEnabled: false,
  isSyncMaster: false,
  syncSourceDeck: '',
  syncTargetBpm: 0,
  syncTempoDelta: 0,
  syncBeatPhaseOffsetSeconds: 0,
  syncAligned: false,
  cueMonitoring: false,
  waveformVisible: false,
  tempo: 0,
  pitchRange: 16,
  volume: 80,
  gain: 50,
  eqHigh: 50,
  eqMid: 50,
  eqLow: 50,
  filter: 50,
  currentTime: 0,
  cuePosition: 0,
  currentTimeSeconds: 0,
  remainingTimeSeconds: 0,
  loopStart: null,
  loopEnd: null,
  loopSize: '1',
  cuePoints: [],
  beatIntervalSeconds: 0,
  beatPhaseFraction: 0,
  beatPhaseSeconds: 0,
  beatPosition: 0,
  barIndex: 0,
  beatInBar: 0,
  phraseIndex: 0,
  waveformBars: [],
  analysis: null,
  mixAmount: label === 'A' ? 0.5 : 0.5,
  level: 0,
  elapsed: '00:00',
  remaining: '00:00',
  effectiveBpm: 0,
  track: emptyTrack,
});

export const initialMixerState: DjMixerState = {
  crossfader: 50,
  crossfaderCurve: 'sharp',
  masterVolume: 80,
  masterLevel: 0,
  cueMix: 50,
  headphoneVolume: 74,
  micVolume: 35,
  effectMode: 'Punch',
};

export const initialBrowserState: DjBrowserState = {
  search: '',
  activeSection: 'all',
  selectedSource: 'All Sources',
  filterMode: 'all',
  sortMode: 'title',
  columnsCompact: false,
  loadHistory: [],
};

export const initialAutoMixState: DjAutoMixState = {
  enabled: false,
  status: '',
  sourceDeck: '',
  targetDeck: '',
  progress: 0,
  transitionBeats: 16,
  remainingBeats: 0,
};

export const initialState: DjState = {
  decks: {
    A: emptyDeck('A'),
    B: emptyDeck('B'),
    C: emptyDeck('C'),
    D: emptyDeck('D'),
  },
  mixer: initialMixerState,
  browser: initialBrowserState,
  library: [],
  samples: [],
  controllers: {
    devices: [],
    profiles: [],
    lastInput: null,
  },
  isRecording: false,
  recordingPath: '',
  autoMix: initialAutoMixState,
  lastUpdatedUtc: '',
  online: false,
  consoleMode: 'two-deck',
};

function nextBrowserFilterMode(mode: DjBrowserFilterMode): DjBrowserFilterMode {
  return mode === 'all' ? 'tagged' : mode === 'tagged' ? 'loaded' : 'all';
}

function nextBrowserSortMode(mode: DjBrowserSortMode): DjBrowserSortMode {
  return mode === 'title' ? 'artist' : mode === 'artist' ? 'source' : 'title';
}

function mergeTrackState(base: DjTrack, incoming?: Partial<DjTrack> | null): DjTrack {
  if (!incoming) {
    return base;
  }

  return {
    ...emptyTrack,
    ...base,
    ...incoming,
  };
}

function mergeDeckState(base: DjDeckState, incoming?: Partial<DjDeckState> | null): DjDeckState {
  if (!incoming) {
    return base;
  }

  const incomingTrackPath = incoming.track?.path?.trim();
  if (base.track.path && !incomingTrackPath) {
    return base;
  }

  const nextTrack = mergeTrackState(base.track, incoming.track);
  const waveformVisible = incoming.waveformVisible ?? Boolean(nextTrack.path);

  // Keep waveformBars reference stable for the same track to avoid RAF restarts
  const nextWaveformBars = incoming.waveformBars && incoming.waveformBars.length > 0
    ? (nextTrack.path === base.track.path && base.waveformBars.length > 0
      ? base.waveformBars
      : incoming.waveformBars)
    : base.waveformBars;

  const nextAnalysis = incoming.analysis
    ? incoming.track?.path === base.track.path && base.analysis
      ? base.analysis
      : incoming.analysis
    : base.analysis;

  return {
    ...base,
    ...incoming,
    waveformVisible,
    cuePoints: incoming.cuePoints ?? base.cuePoints,
    waveformBars: nextWaveformBars,
    analysis: nextAnalysis,
    track: nextTrack,
  };
}

function mergeMixerState(base: DjMixerState, incoming?: DjLegacyServerState | null): DjMixerState {
  if (!incoming) {
    return base;
  }

  const mixer = incoming.mixer ?? {};

  return {
    crossfader: mixer.crossfader ?? incoming.crossfader ?? base.crossfader,
    crossfaderCurve: mixer.crossfaderCurve ?? incoming.crossfaderCurve ?? base.crossfaderCurve,
    masterVolume: mixer.masterVolume ?? incoming.masterVolume ?? base.masterVolume,
    masterLevel: mixer.masterLevel ?? incoming.masterLevel ?? base.masterLevel,
    cueMix: mixer.cueMix ?? incoming.cueMix ?? base.cueMix,
    headphoneVolume: mixer.headphoneVolume ?? incoming.headphoneVolume ?? base.headphoneVolume,
    micVolume: mixer.micVolume ?? incoming.micVolume ?? base.micVolume,
    effectMode: mixer.effectMode ?? incoming.effectMode ?? base.effectMode,
  };
}

function mergeAutoMixState(base: DjAutoMixState, incoming?: Record<string, unknown> | null): DjAutoMixState {
  if (!incoming) {
    return base;
  }

  return {
    enabled: typeof incoming.enabled === 'boolean' ? incoming.enabled : base.enabled,
    status: typeof incoming.status === 'string' ? incoming.status : base.status,
    sourceDeck: (typeof incoming.sourceDeck === 'string' ? incoming.sourceDeck : base.sourceDeck) as DjAutoMixState['sourceDeck'],
    targetDeck: (typeof incoming.targetDeck === 'string' ? incoming.targetDeck : base.targetDeck) as DjAutoMixState['targetDeck'],
    progress: typeof incoming.progress === 'number' ? incoming.progress : base.progress,
    transitionBeats: typeof incoming.transitionBeats === 'number' ? incoming.transitionBeats : base.transitionBeats,
    remainingBeats: typeof incoming.remainingBeats === 'number' ? incoming.remainingBeats : base.remainingBeats,
  };
}

export function normalizeServerState(current: DjState, incoming: DjLegacyServerState | null | undefined): DjState {
  if (!incoming) {
    return recalculateState({
      ...current,
      online: false,
    });
  }

  return recalculateState({
    ...current,
    decks: {
      A: mergeDeckState(current.decks.A, incoming.decks?.A),
      B: mergeDeckState(current.decks.B, incoming.decks?.B),
      C: mergeDeckState(current.decks.C, (incoming.decks as Record<string, Partial<DjDeckState>> | undefined)?.C),
      D: mergeDeckState(current.decks.D, (incoming.decks as Record<string, Partial<DjDeckState>> | undefined)?.D),
    },
    mixer: mergeMixerState(current.mixer, incoming),
    library: incoming.library ?? current.library,
    samples: (incoming as any).samples ?? current.samples,
    controllers: (incoming as any).controllers ?? current.controllers,
    isRecording: (incoming as any).isRecording ?? current.isRecording,
    recordingPath: (incoming as any).recordingPath ?? current.recordingPath,
    autoMix: mergeAutoMixState(current.autoMix, (incoming as any).autoMix ?? null),
    lastUpdatedUtc: incoming.lastUpdatedUtc ?? current.lastUpdatedUtc,
    online: true,
  });
}

export function djReducer(state: DjState, action: DjStateAction): DjState {
  switch (action.type) {
    case 'server-state':
      return normalizeServerState(state, action.state);
    case 'tick':
      return recalculateState({
        ...state,
        decks: {
          A: advanceDeckPlayback(state.decks.A, action.deltaMs),
          B: advanceDeckPlayback(state.decks.B, action.deltaMs),
          C: advanceDeckPlayback(state.decks.C, action.deltaMs),
          D: advanceDeckPlayback(state.decks.D, action.deltaMs),
        },
      });
    case 'load-track':
      return loadTrackIntoDeck(state, action.deck, action.track);
    case 'play': {
      const deck = state.decks[action.deck];
      if (!deck.track.path) {
        return state;
      }

      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            isPlaying: true,
            isCued: false,
            currentTime: deck.currentTime >= 100 ? 0 : deck.currentTime,
          },
        },
      });
    }
    case 'pause':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            isPlaying: false,
          },
        },
      });
    case 'cue': {
      const deck = state.decks[action.deck];
      if (!deck.track.path) {
        return state;
      }

      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            currentTime: clampPercent(deck.cuePosition),
            isPlaying: false,
            isCued: true,
          },
        },
      });
    }
    case 'sync': {
      const target = state.decks[action.deck];
      const sourceLabel: DeckLabel = action.deck === 'A' ? 'B' : 'A';
      const source = state.decks[sourceLabel];
      if (!target.track.path) {
        return state;
      }

      if (target.isSyncMaster) {
        return recalculateState({
          ...state,
          decks: {
            ...state.decks,
            [action.deck]: clearDeckSyncState(target),
            [sourceLabel]: source.syncEnabled && !source.isSyncMaster ? clearDeckSyncState(source) : source,
          },
        });
      }

      if (target.syncEnabled) {
        return recalculateState({
          ...state,
          decks: {
            ...state.decks,
            [action.deck]: clearDeckSyncState(target),
          },
        });
      }

      if (source.syncEnabled && source.isSyncMaster && source.track.path) {
        return recalculateState({
          ...state,
          decks: {
            ...state.decks,
            [sourceLabel]: buildMasterSyncState(source),
            [action.deck]: {
              ...target,
              syncEnabled: true,
              isSyncMaster: false,
              syncSourceDeck: source.label,
            },
          },
        });
      }

      if (source.track.path && (source.isPlaying || !target.isPlaying)) {
        return recalculateState({
          ...state,
          decks: {
            ...state.decks,
            [sourceLabel]: buildMasterSyncState(source),
            [action.deck]: {
              ...target,
              syncEnabled: true,
              isSyncMaster: false,
              syncSourceDeck: source.label,
            },
          },
        });
      }

      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: buildMasterSyncState(target),
          [sourceLabel]: source.isSyncMaster ? clearDeckSyncState(source) : source,
        },
      });
    }
    case 'bend': {
      const deck = state.decks[action.deck];
      if (!deck.track.path || deck.track.durationSeconds <= 0) {
        return state;
      }

      const nextTime = clampPercent(deck.currentTime + (action.delta / deck.track.durationSeconds) * 100);
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            currentTime: nextTime,
          },
        },
      });
    }
    case 'seek':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            currentTime: clampPercent(action.value),
            isCued: false,
          },
        },
      });
    case 'set-cue':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            cuePosition: clampPercent(action.value),
            isCued: true,
          },
        },
      });
    case 'patch-deck':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            ...action.patch,
            waveformVisible: action.patch.waveformVisible ?? Boolean(action.patch.track?.path ?? state.decks[action.deck].track.path),
          },
        },
      });
    case 'patch-mixer':
      return recalculateState({
        ...state,
        mixer: {
          ...state.mixer,
          ...action.patch,
        },
      });
    case 'toggle-cue-monitor':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            cueMonitoring: !state.decks[action.deck].cueMonitoring,
          },
        },
      });
    case 'set-tempo':
      const nextTempo = Math.max(-state.decks[action.deck].pitchRange, Math.min(state.decks[action.deck].pitchRange, action.value));
      const nextDeck = state.decks[action.deck].isSyncMaster
        ? {
            ...state.decks[action.deck],
            tempo: nextTempo,
            syncEnabled: true,
            isSyncMaster: true,
            syncSourceDeck: '',
            syncTargetBpm: getEffectiveBpmValue({ ...state.decks[action.deck], tempo: nextTempo }),
            syncTempoDelta: 0,
            syncBeatPhaseOffsetSeconds: 0,
            syncAligned: true,
          }
        : {
            ...clearDeckSyncState(state.decks[action.deck]),
            tempo: nextTempo,
          };
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: nextDeck,
        },
      });
    case 'set-pitch-range':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            pitchRange: Math.max(4, Math.min(50, action.value)),
            tempo: Math.max(-Math.max(4, Math.min(50, action.value)), Math.min(Math.max(4, Math.min(50, action.value)), state.decks[action.deck].tempo)),
          },
        },
      });
    case 'set-loop-size':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            loopSize: action.value,
          },
        },
      });
    case 'loop-in': {
      const deck = state.decks[action.deck];
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            loopStart: deck.currentTime,
            loopEnd: null,
            loopActive: false,
          },
        },
      });
    }
    case 'loop-out': {
      const deck = state.decks[action.deck];
      const loopStart = deck.loopStart ?? clampPercent(deck.currentTime - getLoopPercent(deck));
      const loopEnd = Math.max(loopStart + 0.1, clampPercent(deck.currentTime));

      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            loopStart,
            loopEnd,
            loopActive: true,
          },
        },
      });
    }
    case 'loop-clear':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            loopStart: null,
            loopEnd: null,
            loopActive: false,
          },
        },
      });
    case 'hot-cue': {
      const deck = state.decks[action.deck];
      const cues = [...deck.cuePoints];
      const existingCue = cues[action.cueIndex];

      if (existingCue) {
        return recalculateState({
          ...state,
          decks: {
            ...state.decks,
            [action.deck]: {
              ...deck,
              currentTime: existingCue.time,
              cuePosition: existingCue.time,
              isCued: true,
            },
          },
        });
      }

      cues[action.cueIndex] = createCuePoint(action.deck, action.cueIndex, deck.currentTime);
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...deck,
            cuePoints: cues,
            cuePosition: deck.currentTime,
            isCued: true,
          },
        },
      });
    }
    case 'set-waveform-visibility':
      return recalculateState({
        ...state,
        decks: {
          ...state.decks,
          [action.deck]: {
            ...state.decks[action.deck],
            waveformVisible: action.visible,
          },
        },
      });
    case 'set-browser-search':
      return {
        ...state,
        browser: {
          ...state.browser,
          search: action.value,
        },
      };
    case 'set-browser-section':
      return {
        ...state,
        browser: {
          ...state.browser,
          activeSection: action.value,
        },
      };
    case 'set-browser-source':
      return {
        ...state,
        browser: {
          ...state.browser,
          selectedSource: action.value,
        },
      };
    case 'cycle-browser-filter':
      return {
        ...state,
        browser: {
          ...state.browser,
          filterMode: nextBrowserFilterMode(state.browser.filterMode),
        },
      };
    case 'cycle-browser-sort':
      return {
        ...state,
        browser: {
          ...state.browser,
          sortMode: nextBrowserSortMode(state.browser.sortMode),
        },
      };
    case 'toggle-browser-columns':
      return {
        ...state,
        browser: {
          ...state.browser,
          columnsCompact: !state.browser.columnsCompact,
        },
      };
    case 'register-track-load':
      return {
        ...state,
        browser: {
          ...state.browser,
          loadHistory: [action.path, ...state.browser.loadHistory.filter((item) => item !== action.path)].slice(0, 20),
        },
      };
    case 'set-console-mode':
      return { ...state, consoleMode: action.mode };
    default:
      return state;
  }
}