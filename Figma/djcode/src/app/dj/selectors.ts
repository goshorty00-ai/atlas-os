import type { DeckLabel, DjDeckState, DjLibraryTrack, DjState, DjTrack } from './types';

type CompatibilityLevel = 'compatible' | 'adjacent' | 'clash' | 'unknown';
type EnergyLevel = 'lower' | 'similar' | 'higher' | 'unknown';

export interface DjBlendInsights {
  loadedDecks: number;
  harmonic: {
    level: CompatibilityLevel;
    label: string;
  };
  bpm: {
    delta: number | null;
    label: string;
  };
  readiness: {
    level: 'ready' | 'wait' | 'warn';
    label: string;
  };
}

export interface DjDeckRecommendation {
  deck: DeckLabel;
  keyLevel: CompatibilityLevel;
  bpmDelta: number | null;
  energy: EnergyLevel;
  label: string;
}

const keyToCamelot = new Map<string, string>([
  ['abm', '1A'], ['b', '1B'],
  ['ebm', '2A'], ['f#', '2B'], ['gb', '2B'],
  ['bbm', '3A'], ['db', '3B'], ['c#', '3B'],
  ['fm', '4A'], ['ab', '4B'],
  ['cm', '5A'], ['eb', '5B'], ['d#', '5B'],
  ['gm', '6A'], ['bb', '6B'], ['a#', '6B'],
  ['dm', '7A'], ['f', '7B'],
  ['am', '8A'], ['c', '8B'],
  ['em', '9A'], ['g', '9B'],
  ['bm', '10A'], ['d', '10B'],
  ['f#m', '11A'], ['gbm', '11A'], ['a', '11B'],
  ['c#m', '12A'], ['dbm', '12A'], ['e', '12B'],
]);

function normalizeKey(input: string) {
  const raw = input.trim();
  if (!raw) {
    return null;
  }

  const camelot = raw.toUpperCase().match(/^(1[0-2]|[1-9])\s*([AB])$/);
  if (camelot) {
    return { number: Number(camelot[1]), letter: camelot[2] as 'A' | 'B' };
  }

  const compact = raw.toLowerCase().replace(/\s+/g, '');
  const mapped = keyToCamelot.get(compact);
  if (!mapped) {
    return null;
  }

  return {
    number: Number(mapped.slice(0, -1)),
    letter: mapped.slice(-1) as 'A' | 'B',
  };
}

function compareKeys(left: string, right: string): CompatibilityLevel {
  const a = normalizeKey(left);
  const b = normalizeKey(right);
  if (!a || !b) {
    return 'unknown';
  }

  if (a.number === b.number && a.letter === b.letter) {
    return 'compatible';
  }

  if (a.number === b.number && a.letter !== b.letter) {
    return 'compatible';
  }

  const distance = Math.min((a.number - b.number + 12) % 12, (b.number - a.number + 12) % 12);
  if (distance === 1 && a.letter === b.letter) {
    return 'adjacent';
  }

  return 'clash';
}

function energyFromBpm(bpm: number): number | null {
  return bpm > 0 ? bpm : null;
}

function compareEnergy(referenceBpm: number, targetBpm: number): EnergyLevel {
  const reference = energyFromBpm(referenceBpm);
  const target = energyFromBpm(targetBpm);
  if (reference == null || target == null) {
    return 'unknown';
  }

  const delta = target - reference;
  if (delta <= -4) {
    return 'lower';
  }

  if (delta >= 4) {
    return 'higher';
  }

  return 'similar';
}

function formatCompatibility(level: CompatibilityLevel) {
  switch (level) {
    case 'compatible':
      return 'Compatible';
    case 'adjacent':
      return 'Adjacent';
    case 'clash':
      return 'Key clash';
    default:
      return 'Key unknown';
  }
}

function getEffectiveDeckBpm(deck: DjDeckState) {
  return deck.effectiveBpm > 0 ? deck.effectiveBpm : deck.track.bpm;
}

function scoreDeckForTrack(track: DjTrack, deck: DjDeckState) {
  if (!deck.track.path) {
    return {
      score: 1,
      keyLevel: 'unknown' as CompatibilityLevel,
      bpmDelta: null,
      energy: 'unknown' as EnergyLevel,
    };
  }

  const bpmDelta = track.bpm > 0 && getEffectiveDeckBpm(deck) > 0 ? Math.abs(track.bpm - getEffectiveDeckBpm(deck)) : null;
  const keyLevel = compareKeys(track.key, deck.track.key);
  const energy = compareEnergy(getEffectiveDeckBpm(deck), track.bpm);
  const keyPenalty = keyLevel === 'compatible' ? 0 : keyLevel === 'adjacent' ? 5 : keyLevel === 'clash' ? 14 : 7;
  const energyPenalty = energy === 'similar' ? 0 : energy === 'unknown' ? 4 : 3;
  const bpmPenalty = bpmDelta == null ? 6 : bpmDelta;

  return {
    score: bpmPenalty + keyPenalty + energyPenalty,
    keyLevel,
    bpmDelta,
    energy,
  };
}

export function selectBlendInsights(state: DjState): DjBlendInsights {
  const loadedDecks = selectLoadedDeckCount(state);
  if (loadedDecks < 2) {
    return {
      loadedDecks,
      harmonic: { level: 'unknown', label: 'Waiting for second deck' },
      bpm: { delta: null, label: 'Tempo unknown' },
      readiness: { level: 'wait', label: 'Wait' },
    };
  }

  const deckA = state.decks.A;
  const deckB = state.decks.B;
  const harmonicLevel = compareKeys(deckA.track.key, deckB.track.key);
  const bpmDelta = Math.abs(getEffectiveDeckBpm(deckA) - getEffectiveDeckBpm(deckB));

  let readiness: DjBlendInsights['readiness'];
  if (harmonicLevel === 'clash') {
    readiness = { level: 'warn', label: 'Key clash' };
  } else if (bpmDelta > 4) {
    readiness = { level: 'warn', label: 'Tempo mismatch' };
  } else if (bpmDelta > 1.5 || harmonicLevel === 'adjacent') {
    readiness = { level: 'wait', label: 'Almost ready' };
  } else {
    readiness = { level: 'ready', label: 'Ready to blend' };
  }

  return {
    loadedDecks,
    harmonic: {
      level: harmonicLevel,
      label: formatCompatibility(harmonicLevel),
    },
    bpm: {
      delta: Number(bpmDelta.toFixed(2)),
      label: `${bpmDelta.toFixed(1)} BPM delta`,
    },
    readiness,
  };
}

export function recommendDeckForTrack(track: DjTrack, decks: Record<DeckLabel, DjDeckState>): DjDeckRecommendation {
  const scoreA = scoreDeckForTrack(track, decks.A);
  const scoreB = scoreDeckForTrack(track, decks.B);
  const preferredDeck: DeckLabel = scoreA.score <= scoreB.score ? 'A' : 'B';
  const preferred = preferredDeck === 'A' ? scoreA : scoreB;

  const bpmLabel = preferred.bpmDelta == null ? 'tempo open' : `${preferred.bpmDelta.toFixed(1)} BPM delta`;
  const energyLabel = preferred.energy === 'unknown' ? 'energy open' : `${preferred.energy} energy`;

  return {
    deck: preferredDeck,
    keyLevel: preferred.keyLevel,
    bpmDelta: preferred.bpmDelta,
    energy: preferred.energy,
    label: `${formatCompatibility(preferred.keyLevel)} • ${bpmLabel} • ${energyLabel}`,
  };
}

export function selectLoadedTracks(state: DjState): Record<DeckLabel, DjTrack | null> {
  return {
    A: state.decks.A.track.path ? state.decks.A.track : null,
    B: state.decks.B.track.path ? state.decks.B.track : null,
    C: state.decks.C.track.path ? state.decks.C.track : null,
    D: state.decks.D.track.path ? state.decks.D.track : null,
  };
}

export function selectLoadedDeckCount(state: DjState): number {
  return Object.values(selectLoadedTracks(state)).filter(Boolean).length;
}

export function selectMasterReferenceDeck(state: DjState): DjDeckState {
  return state.decks.A.track.path ? state.decks.A : state.decks.B;
}

export function selectBrowserSources(state: DjState): string[] {
  const unique = Array.from(new Set(state.library.map((track) => track.source).filter(Boolean)));
  return ['All Sources', ...(unique.length > 0 ? unique : ['Local Files'])];
}

export function selectVisibleBrowserTracks(state: DjState): DjLibraryTrack[] {
  const query = state.browser.search.trim().toLowerCase();
  const loadedTracks = Object.values(selectLoadedTracks(state)).filter(Boolean) as DjTrack[];
  const loadedPaths = new Set([...state.browser.loadHistory, ...loadedTracks.map((track) => track.path)]);
  let nextTracks = state.library;

  if (state.browser.activeSection === 'favorites') {
    nextTracks = nextTracks.filter((track) => track.bpm > 0 || Boolean(track.key));
  } else if (state.browser.activeSection === 'recent') {
    nextTracks = state.browser.loadHistory
      .map((path) => nextTracks.find((track) => track.path === path))
      .filter(Boolean) as DjLibraryTrack[];
  } else if (state.browser.activeSection === 'local-files') {
    const streamingSources = new Set(['spotify', 'apple music', 'soundcloud', 'tidal', 'deezer', 'amazon music']);
    nextTracks = nextTracks.filter((track) => !streamingSources.has((track.source || '').toLowerCase()));
  } else if (state.browser.activeSection === 'playlists' || state.browser.activeSection === 'crates') {
    nextTracks = [];
  }

  if (state.browser.selectedSource !== 'All Sources') {
    nextTracks = nextTracks.filter((track) => track.source === state.browser.selectedSource);
  }

  if (state.browser.filterMode === 'tagged') {
    nextTracks = nextTracks.filter((track) => track.bpm > 0 || Boolean(track.key));
  } else if (state.browser.filterMode === 'loaded') {
    nextTracks = nextTracks.filter((track) => loadedPaths.has(track.path));
  }

  if (query) {
    nextTracks = nextTracks.filter((track) => [track.title, track.artist, track.key, track.source, track.path].join(' ').toLowerCase().includes(query));
  }

  return [...nextTracks].sort((left, right) => {
    const a = state.browser.sortMode === 'artist' ? left.artist || left.title : state.browser.sortMode === 'source' ? left.source : left.title;
    const b = state.browser.sortMode === 'artist' ? right.artist || right.title : state.browser.sortMode === 'source' ? right.source : right.title;
    return a.localeCompare(b);
  });
}