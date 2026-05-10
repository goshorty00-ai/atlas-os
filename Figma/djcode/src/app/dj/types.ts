export type DeckLabel = 'A' | 'B' | 'C' | 'D';

export type DjCrossfaderCurve = 'smooth' | 'linear' | 'sharp';

export type DjLoopSize = '1/4' | '1/2' | '1' | '2';

export type DjConsoleMode = 'two-deck' | 'four-deck' | 'cdj' | 'sampler';

export type DjBrowserSection = 'all' | 'favorites' | 'recent' | 'playlists' | 'crates' | 'local-files';

export type DjBrowserFilterMode = 'all' | 'tagged' | 'loaded';

export type DjBrowserSortMode = 'title' | 'artist' | 'source';

export interface DjCuePoint {
  time: number;
  label: string;
  color: string;
}

export interface DjTrackAnalysis {
  bpm: number;
  confidence: number;
  beatIntervalSeconds: number;
  gridOffsetSeconds: number;
  beatMarkers: number[];
  phraseMarkers: number[];
  waveformMin: number[];
  waveformMax: number[];
  waveformRms: number[];
}

export interface DjControllerDevice {
  id: string;
  name: string;
  inputName: string;
  outputName: string;
  connected: boolean;
  supportsInput: boolean;
  supportsOutput: boolean;
  protocol: string;
}

export interface DjControllerProfile {
  id: string;
  name: string;
  protocol: string;
  bindingCount: number;
}

export interface DjControllerInput {
  deviceId: string;
  deviceName: string;
  controlType: string;
  channel: number;
  controlNumber: number;
  rawValue: number;
  normalizedValue: number;
  timestampUtc: string;
}

export interface DjControllerState {
  devices: DjControllerDevice[];
  profiles: DjControllerProfile[];
  lastInput: DjControllerInput | null;
}

export interface DjAutoMixState {
  enabled: boolean;
  status: string;
  sourceDeck: DeckLabel | '';
  targetDeck: DeckLabel | '';
  progress: number;
  transitionBeats: number;
  remainingBeats: number;
}

export interface DjTrack {
  path: string;
  title: string;
  artist: string;
  key: string;
  duration: string;
  durationSeconds: number;
  bpm: number;
  source: string;
}

export interface DjLibraryTrack extends DjTrack {
  id: string;
}

export interface DjDeckState {
  label: DeckLabel;
  isPlaying: boolean;
  isCued: boolean;
  loopActive: boolean;
  syncEnabled: boolean;
  isSyncMaster: boolean;
  syncSourceDeck: DeckLabel | '';
  syncTargetBpm: number;
  syncTempoDelta: number;
  syncBeatPhaseOffsetSeconds: number;
  syncAligned: boolean;
  cueMonitoring: boolean;
  waveformVisible: boolean;
  tempo: number;
  pitchRange: number;
  volume: number;
  gain: number;
  eqHigh: number;
  eqMid: number;
  eqLow: number;
  filter: number;
  currentTime: number;
  cuePosition: number;
  currentTimeSeconds: number;
  remainingTimeSeconds: number;
  loopStart: number | null;
  loopEnd: number | null;
  loopSize: DjLoopSize;
  cuePoints: DjCuePoint[];
  beatIntervalSeconds: number;
  beatPhaseFraction: number;
  beatPhaseSeconds: number;
  beatPosition: number;
  barIndex: number;
  beatInBar: number;
  phraseIndex: number;
  waveformBars: number[];
  analysis: DjTrackAnalysis | null;
  mixAmount: number;
  level: number;
  elapsed: string;
  remaining: string;
  effectiveBpm: number;
  track: DjTrack;
}

export interface DjMixerState {
  crossfader: number;
  crossfaderCurve: DjCrossfaderCurve;
  masterVolume: number;
  masterLevel: number;
  cueMix: number;
  headphoneVolume: number;
  micVolume: number;
  effectMode: string;
}

export interface DjBrowserState {
  search: string;
  activeSection: DjBrowserSection;
  selectedSource: string;
  filterMode: DjBrowserFilterMode;
  sortMode: DjBrowserSortMode;
  columnsCompact: boolean;
  loadHistory: string[];
}

export interface DjSample {
  path: string;
  name: string;
  category: string;
}

export interface DjState {
  decks: Record<DeckLabel, DjDeckState>;
  mixer: DjMixerState;
  browser: DjBrowserState;
  library: DjLibraryTrack[];
  samples: DjSample[];
  controllers: DjControllerState;
  isRecording: boolean;
  recordingPath: string;
  autoMix: DjAutoMixState;
  lastUpdatedUtc: string;
  online: boolean;
  consoleMode: DjConsoleMode;
}

export interface DjActions {
  loadFile: (deck: DeckLabel) => void;
  loadTrack: (deck: DeckLabel, path: string) => void;
  play: (deck: DeckLabel) => void;
  pause: (deck: DeckLabel) => void;
  cue: (deck: DeckLabel) => void;
  sync: (deck: DeckLabel) => void;
  bend: (deck: DeckLabel, delta: number) => void;
  seek: (deck: DeckLabel, value: number) => void;
  setCue: (deck: DeckLabel, value: number) => void;
  setTempo: (deck: DeckLabel, value: number) => void;
  setPitchRange: (deck: DeckLabel, value: number) => void;
  setVolume: (deck: DeckLabel, value: number) => void;
  setGain: (deck: DeckLabel, value: number) => void;
  setEq: (deck: DeckLabel, band: 'high' | 'mid' | 'low', value: number) => void;
  setFilter: (deck: DeckLabel, value: number) => void;
  toggleCueMonitor: (deck: DeckLabel) => void;
  loopIn: (deck: DeckLabel) => void;
  loopOut: (deck: DeckLabel) => void;
  loopClear: (deck: DeckLabel) => void;
  hotCue: (deck: DeckLabel, cueIndex: number) => void;
  setLoopSize: (deck: DeckLabel, value: DjLoopSize) => void;
  setWaveformVisibility: (deck: DeckLabel, visible: boolean) => void;
  setCrossfader: (value: number) => void;
  setCrossfaderCurve: (value: DjCrossfaderCurve) => void;
  setMasterVolume: (value: number) => void;
  setCueMix: (value: number) => void;
  setHeadphoneVolume: (value: number) => void;
  setMicVolume: (value: number) => void;
  setEffectMode: (value: string) => void;
  setBrowserSearch: (value: string) => void;
  setBrowserSection: (value: DjBrowserSection) => void;
  setBrowserSource: (value: string) => void;
  cycleBrowserFilter: () => void;
  cycleBrowserSort: () => void;
  toggleBrowserColumns: () => void;
  addFolder: () => void;
  addFiles: () => void;
  setConsoleMode: (mode: DjConsoleMode) => void;
  startRecording: () => void;
  stopRecording: () => void;
  startAutoMix: (sourceDeck: DeckLabel, targetDeck: DeckLabel, transitionBeats?: number) => void;
  stopAutoMix: () => void;
  playSample: (path: string) => void;
  addSamplesFolder: () => void;
}