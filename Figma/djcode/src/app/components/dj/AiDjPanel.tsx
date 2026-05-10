import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import type { DeckLabel, DjActions, DjDeckState, DjLibraryTrack, DjAutoMixState } from '../../dj/types';

interface AiDjPanelProps {
  decks: Record<DeckLabel, DjDeckState>;
  library: DjLibraryTrack[];
  autoMix: DjAutoMixState;
  actions: Pick<DjActions, 'loadTrack' | 'play' | 'pause' | 'sync' | 'seek' | 'setCue' | 'startAutoMix' | 'stopAutoMix'>;
}

/* ── helpers ─────────────────────────────────────────────────────── */
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

function parseCamelot(key: string) {
  if (!key) return null;
  const cam = key.toUpperCase().match(/^(1[0-2]|[1-9])\s*([AB])$/);
  if (cam) return { n: Number(cam[1]), l: cam[2] };
  const mapped = keyToCamelot.get(key.toLowerCase().replace(/\s+/g, ''));
  if (!mapped) return null;
  return { n: Number(mapped.slice(0, -1)), l: mapped.slice(-1) };
}

function isHarmonic(a: string, b: string): boolean {
  const ca = parseCamelot(a);
  const cb = parseCamelot(b);
  if (!ca || !cb) return false;
  if (ca.n === cb.n) return true;
  const dist = Math.min((ca.n - cb.n + 12) % 12, (cb.n - ca.n + 12) % 12);
  return dist <= 1 && ca.l === cb.l;
}

function postMsg(type: string, payload?: Record<string, unknown>) {
  try {
    (window as any).chrome?.webview?.postMessage(JSON.stringify({ type, ...payload }));
  } catch { /* no webview */ }
}

function getBeatInterval(deck: DjDeckState) {
  return deck.analysis?.beatIntervalSeconds && deck.analysis.beatIntervalSeconds > 0
    ? deck.analysis.beatIntervalSeconds
    : (deck.effectiveBpm > 0 ? 60 / deck.effectiveBpm : 0.5);
}

function getRemainingBeats(deck: DjDeckState) {
  if (!deck.track.durationSeconds || deck.track.durationSeconds <= 0) return 999;
  const remainingSeconds = (1 - deck.currentTime / 100) * deck.track.durationSeconds;
  const beatInterval = getBeatInterval(deck);
  return beatInterval > 0 ? remainingSeconds / beatInterval : 999;
}

function getLaunchPercent(deck: DjDeckState) {
  if (!deck.track.durationSeconds || deck.track.durationSeconds <= 0) return 0;
  const phrase = deck.analysis?.phraseMarkers?.find((marker) => marker >= 0.25 && marker <= 16);
  const beat = deck.analysis?.beatMarkers?.find((marker) => marker >= 0.1);
  const launchSeconds = phrase ?? beat ?? 0;
  return Math.max(0, Math.min(100, launchSeconds / deck.track.durationSeconds * 100));
}

/* ── Suggestion type ─────────────────────────────────────────────── */
interface AiSuggestion {
  type: 'next-track' | 'transition' | 'energy' | 'warning';
  icon: string;
  title: string;
  detail: string;
  action?: () => void;
  actionLabel?: string;
}

/* ── Stem job state ──────────────────────────────────────────────── */
interface StemJob {
  deck: DeckLabel;
  status: 'idle' | 'processing' | 'done';
  stems: { vocals: boolean; drums: boolean; bass: boolean; other: boolean };
}

const emptyStemJob = (deck: DeckLabel): StemJob => ({
  deck,
  status: 'idle',
  stems: { vocals: true, drums: true, bass: true, other: true },
});

/* ══════════════════════════════════════════════════════════════════ */
export function AiDjPanel({ decks, library, autoMix, actions }: AiDjPanelProps) {
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<'suggestions' | 'stems' | 'detect'>('suggestions');
  const [stemA, setStemA] = useState<StemJob>(emptyStemJob('A'));
  const [stemB, setStemB] = useState<StemJob>(emptyStemJob('B'));
  const [detectStatus, setDetectStatus] = useState<Record<DeckLabel, 'idle' | 'running' | 'done'>>({ A: 'idle', B: 'idle', C: 'idle', D: 'idle' });

  const deckA = decks.A;
  const deckB = decks.B;
  const hasA = Boolean(deckA.track.path);
  const hasB = Boolean(deckB.track.path);

  /* ── generate suggestions ── */
  const suggestions = useMemo<AiSuggestion[]>(() => {
    const out: AiSuggestion[] = [];

    if (hasA && hasB) {
      const bpmDelta = Math.abs(deckA.effectiveBpm - deckB.effectiveBpm);
      if (bpmDelta > 2) {
        out.push({
          type: 'warning', icon: '⚠',
          title: `BPM mismatch: ${bpmDelta.toFixed(1)} BPM`,
          detail: `A: ${deckA.effectiveBpm.toFixed(1)} / B: ${deckB.effectiveBpm.toFixed(1)}.`,
          action: () => actions.sync('B'), actionLabel: 'Sync B→A',
        });
      }

      if (deckA.track.key && deckB.track.key) {
        const compat = isHarmonic(deckA.track.key, deckB.track.key);
        out.push(compat
          ? { type: 'transition', icon: '✓', title: 'Harmonic match', detail: `${deckA.track.key} ↔ ${deckB.track.key} are compatible.` }
          : { type: 'warning', icon: '🎵', title: 'Key clash', detail: `${deckA.track.key} ↔ ${deckB.track.key}. Use EQ to mask.` });
      }

      const remaining = (1 - deckA.currentTime / 100) * deckA.track.durationSeconds;
      if (deckA.isPlaying && remaining < 30 && remaining > 0) {
        out.push({
          type: 'transition', icon: '⏱',
          title: `${Math.round(remaining)}s left on A`,
          detail: 'Start transition now.',
          action: () => actions.play('B'), actionLabel: 'Play B',
        });
      }

      if (deckA.track.bpm > 0 && deckB.track.bpm > 0) {
        const d = deckB.track.bpm - deckA.track.bpm;
        if (d > 10) out.push({ type: 'energy', icon: '📈', title: 'Energy boost', detail: `B is +${d.toFixed(0)} BPM.` });
        else if (d < -10) out.push({ type: 'energy', icon: '📉', title: 'Energy drop', detail: `B is ${d.toFixed(0)} BPM.` });
      }
    }

    if ((hasA && !hasB) || (!hasA && hasB)) {
      const loaded = hasA ? deckA : deckB;
      const target: DeckLabel = hasA ? 'B' : 'A';
      library
        .filter((t) => t.path !== loaded.track.path)
        .map((t) => ({
          track: t,
          score: (t.bpm > 0 && loaded.track.bpm > 0 ? Math.abs(t.bpm - loaded.track.bpm) : 50) + (isHarmonic(loaded.track.key, t.key) ? 0 : 20),
        }))
        .sort((a, b) => a.score - b.score)
        .slice(0, 3)
        .forEach((c) => {
          out.push({
            type: 'next-track', icon: '💿',
            title: c.track.title || 'Unknown',
            detail: `${c.track.artist || '?'} · ${c.track.bpm > 0 ? c.track.bpm + ' BPM' : ''} ${c.track.key || ''}`,
            action: () => actions.loadTrack(target, c.track.path), actionLabel: `Load ${target}`,
          });
        });
    }

    if (!hasA && !hasB) {
      out.push({ type: 'energy', icon: '🎧', title: 'Load a track', detail: 'Drag a track from the browser onto a deck to get started.' });
      out.push({ type: 'next-track', icon: '💡', title: 'Quick start', detail: 'Click LOAD A or B in the track list, or use the + button to add music folders.' });
    }

    if (out.length === 0) {
      out.push({ type: 'energy', icon: '✨', title: 'Ready to mix', detail: 'Both decks loaded — start playing to get transition suggestions.' });
    }

    return out;
  }, [hasA, hasB, deckA, deckB, library, actions]);

  /* ── stem separation handlers ── */
  const requestStemSplit = useCallback((label: DeckLabel) => {
    const setter = label === 'A' ? setStemA : setStemB;
    setter((prev) => ({ ...prev, status: 'processing' }));
    postMsg('dj.ai.stemSplit', { deck: label });
    // Simulate completion since backend may not support it yet
    setTimeout(() => setter((prev) => ({ ...prev, status: 'done' })), 3000);
  }, []);

  const toggleStem = useCallback((label: DeckLabel, stem: keyof StemJob['stems']) => {
    const setter = label === 'A' ? setStemA : setStemB;
    setter((prev) => ({ ...prev, stems: { ...prev.stems, [stem]: !prev.stems[stem] } }));
    postMsg('dj.ai.stemToggle', { deck: label, stem });
  }, []);

  /* ── beat/key detect ── */
  const runDetect = useCallback((label: DeckLabel) => {
    setDetectStatus((prev) => ({ ...prev, [label]: 'running' }));
    postMsg('dj.ai.detectBpmKey', { deck: label });
    setTimeout(() => setDetectStatus((prev) => ({ ...prev, [label]: 'done' })), 2500);
  }, []);

  /* ── Backend-driven auto-mix orchestration ─────────────────── */
  const usedTracksRef = useRef<Set<string>>(new Set());
  const pendingAutoMixRef = useRef<{ sourceDeck: DeckLabel; targetDeck: DeckLabel; launchPercent: number } | null>(null);
  const [preparingAutoMix, setPreparingAutoMix] = useState(false);

  // Pick best next track: BPM within ±15, key compatible, not recently played
  const pickNextTrack = useCallback((currentDeck: DjDeckState): DjLibraryTrack | null => {
    const currentPath = currentDeck.track.path;
    const currentBpm = currentDeck.effectiveBpm || currentDeck.track.bpm;
    const currentKey = currentDeck.track.key;
    const used = usedTracksRef.current;

    // Also exclude whatever is on the other deck
    const otherPath = currentDeck.label === 'A' ? deckB.track.path : deckA.track.path;

    const candidates = library
      .filter((t) => t.path !== currentPath && t.path !== otherPath && !used.has(t.path))
      .map((t) => {
        let score = 100;
        // BPM: prefer within ±10 BPM (accounting for octave)
        if (t.bpm > 0 && currentBpm > 0) {
          const directDiff = Math.abs(t.bpm - currentBpm);
          const doubleDiff = Math.abs(t.bpm * 2 - currentBpm);
          const halfDiff = Math.abs(t.bpm / 2 - currentBpm);
          const bpmDiff = Math.min(directDiff, doubleDiff, halfDiff);
          score = bpmDiff * 3; // Heavily weight BPM match
          if (bpmDiff <= 5) score -= 20; // bonus for very close match
        }
        // Key compatibility
        if (currentKey && t.key) {
          if (isHarmonic(currentKey, t.key)) score -= 15;
          else score += 10;
        }
        return { track: t, score };
      })
      .sort((a, b) => a.score - b.score);

    if (candidates.length === 0) {
      usedTracksRef.current = new Set();
      return library.find((t) => t.path !== currentPath && t.path !== otherPath) ?? null;
    }
    return candidates[0].track;
  }, [library, deckA.track.path, deckB.track.path]);

  const startPreparedAutoMix = useCallback((sourceDeck: DeckLabel, targetDeck: DeckLabel) => {
    const targetState = decks[targetDeck];
    if (!targetState.track.path) {
      return;
    }

    const launchPercent = getLaunchPercent(targetState);
    actions.seek(targetDeck, launchPercent);
    actions.setCue(targetDeck, launchPercent);
    actions.startAutoMix(sourceDeck, targetDeck, 16);
    pendingAutoMixRef.current = null;
    setPreparingAutoMix(false);
  }, [actions, decks]);

  // Instant mix trigger — loads the target deck if needed, then hands off to backend automix
  const triggerMixNow = useCallback(() => {
    const aPlaying = deckA.isPlaying;
    const bPlaying = deckB.isPlaying;

    if (!aPlaying && !bPlaying) {
      if (hasA) actions.play('A');
      else if (hasB) actions.play('B');
      return;
    }

    const sourceDeck: DeckLabel = aPlaying ? 'A' : 'B';
    const targetDeck: DeckLabel = sourceDeck === 'A' ? 'B' : 'A';
    const sourceState = decks[sourceDeck];
    const targetState = decks[targetDeck];

    if (!targetState.track.path) {
      const next = pickNextTrack(sourceState);
      if (!next) {
        return;
      }

      usedTracksRef.current.add(next.path);
      pendingAutoMixRef.current = {
        sourceDeck,
        targetDeck,
        launchPercent: getLaunchPercent({ ...targetState, track: next } as DjDeckState),
      };
      setPreparingAutoMix(true);
      actions.loadTrack(targetDeck, next.path);
      return;
    }

    startPreparedAutoMix(sourceDeck, targetDeck);
  }, [actions, deckA.isPlaying, deckB.isPlaying, decks, hasA, hasB, pickNextTrack, startPreparedAutoMix]);

  useEffect(() => {
    const pending = pendingAutoMixRef.current;
    if (!pending) {
      return;
    }

    const targetState = decks[pending.targetDeck];
    if (!targetState.track.path) {
      return;
    }

    actions.seek(pending.targetDeck, pending.launchPercent);
    actions.setCue(pending.targetDeck, pending.launchPercent);
    actions.startAutoMix(pending.sourceDeck, pending.targetDeck, 16);
    pendingAutoMixRef.current = null;
    setPreparingAutoMix(false);
  }, [actions, decks]);

  useEffect(() => {
    if (!autoMix.enabled) {
      return;
    }

    if (autoMix.progress >= 1 && autoMix.targetDeck) {
      const targetTrack = decks[autoMix.targetDeck].track.path;
      if (targetTrack) {
        usedTracksRef.current.add(targetTrack);
      }
    }
  }, [autoMix.enabled, autoMix.progress, autoMix.targetDeck, decks]);

  const autoMixEnabled = autoMix.enabled || preparingAutoMix;
  const autoMixStatus = preparingAutoMix
    ? `Preparing ${pendingAutoMixRef.current?.targetDeck ?? 'next deck'}`
    : autoMix.status || (autoMix.enabled ? `Crossfading ${Math.round(autoMix.progress * 100)}%` : '');

  /* ── collapsed button ── */
  if (!open) {
    return (
      <div className="fixed bottom-3 right-3 z-50 flex items-center gap-2">
        {/* Mix Now instant button */}
        {(hasA || hasB) && (
          <button
            onClick={triggerMixNow}
            className="flex h-9 items-center gap-1.5 rounded-full border border-emerald-400/40 bg-emerald-500/20 px-4 text-[10px] font-bold uppercase tracking-wider text-emerald-200 shadow-lg backdrop-blur-sm hover:bg-emerald-500/35 transition-colors active:scale-95"
          >
            ⚡ Mix Now
          </button>
        )}
        <button
          onClick={() => setOpen(true)}
          className={`flex h-9 items-center gap-1.5 rounded-full px-4 text-[10px] font-bold uppercase tracking-wider shadow-lg backdrop-blur-sm transition-colors ${
            autoMixEnabled
              ? 'border border-purple-400/50 bg-purple-500/30 text-purple-100'
              : 'border border-purple-400/30 bg-purple-500/20 text-purple-200 hover:bg-purple-500/30'
          }`}
        >
          <span className="text-[14px]">🤖</span> AI DJ
          {autoMixEnabled && autoMixStatus && (
            <span className="text-[8px] font-normal text-purple-300 ml-1 max-w-[120px] truncate">{autoMixStatus}</span>
          )}
          {!autoMixEnabled && suggestions.length > 0 && (
            <span className="flex h-4 w-4 items-center justify-center rounded-full bg-purple-500 text-[8px] font-bold text-white">{suggestions.length}</span>
          )}
        </button>
      </div>
    );
  }

  /* ── stem section ── */
  const renderStemControls = (label: DeckLabel, job: StemJob, track: DjDeckState) => {
    const hasTrack = Boolean(track.track.path);
    return (
      <div className="px-3 py-2 border-b border-white/[0.04]">
        <div className="flex items-center justify-between mb-1.5">
          <span className="text-[10px] font-bold text-zinc-200">Deck {label}: <span className="font-normal text-zinc-400">{track.track.title || 'Empty'}</span></span>
          {hasTrack && job.status !== 'processing' && (
            <button onClick={() => requestStemSplit(label)}
              className="h-5 rounded-sm border border-purple-400/30 bg-purple-500/15 px-2 text-[7px] font-bold uppercase tracking-wider text-purple-200 hover:bg-purple-500/25">
              {job.status === 'done' ? 'Re-split' : 'Split Stems'}
            </button>
          )}
          {job.status === 'processing' && (
            <span className="text-[8px] text-purple-300 animate-pulse">Processing…</span>
          )}
        </div>
        {job.status === 'done' && (
          <div className="grid grid-cols-4 gap-1">
            {(['vocals', 'drums', 'bass', 'other'] as const).map((stem) => (
              <button key={stem} onClick={() => toggleStem(label, stem)}
                className={`h-6 rounded-sm text-[8px] font-bold uppercase ${
                  job.stems[stem]
                    ? 'border border-purple-400/40 bg-purple-500/20 text-purple-200'
                    : 'border border-white/10 bg-white/[0.03] text-zinc-500 line-through'
                }`}>
                {stem}
              </button>
            ))}
          </div>
        )}
        {!hasTrack && <span className="text-[8px] text-zinc-600">Load a track first</span>}
      </div>
    );
  };

  return (
    <div className="fixed bottom-3 right-3 z-50 flex flex-col w-[340px] max-h-[440px] rounded-lg border border-purple-400/20 bg-[#0c0e14]/95 shadow-2xl backdrop-blur-md overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-white/8">
        <div className="flex items-center gap-2">
          <span className="text-[14px]">🤖</span>
          <span className="text-[11px] font-bold uppercase tracking-widest text-purple-300">AI DJ</span>
        </div>
        <div className="flex items-center gap-1.5">
          <button onClick={() => {
            if (autoMixEnabled) {
              pendingAutoMixRef.current = null;
              setPreparingAutoMix(false);
              actions.stopAutoMix();
            } else {
              triggerMixNow();
            }
          }}
            className={`flex h-6 items-center gap-1.5 rounded-full px-3 text-[8px] font-bold uppercase tracking-wider transition-colors ${
              autoMixEnabled ? 'bg-purple-500/40 text-purple-100 border border-purple-400/50 shadow-sm shadow-purple-500/30' : 'bg-white/5 text-zinc-500 border border-white/10 hover:bg-white/10'
            }`}>
            <div className={`h-2 w-2 rounded-full ${autoMixEnabled ? 'bg-purple-400 animate-pulse' : 'bg-zinc-600'}`} />
            {autoMixEnabled ? 'Auto Mix ON' : 'Auto Mix'}
          </button>
          <button onClick={() => setOpen(false)} className="text-zinc-500 hover:text-white text-[14px] leading-none">✕</button>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-white/8">
        {([['suggestions', 'Mix'] as const, ['stems', 'Stems'] as const, ['detect', 'Detect'] as const]).map(([key, label]) => (
          <button key={key} onClick={() => setTab(key)}
            className={`flex-1 py-1.5 text-[8px] font-bold uppercase tracking-widest ${tab === key ? 'text-purple-300 border-b-2 border-purple-400' : 'text-zinc-500 hover:text-zinc-300'}`}>
            {label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        {/* Auto-mix status banner */}
        {autoMixEnabled && (
          <div className="flex items-center gap-2 px-3 py-2 bg-purple-500/10 border-b border-purple-400/20">
            <div className="h-2 w-2 rounded-full bg-purple-400 animate-pulse" />
            <div className="min-w-0 flex-1">
              <div className="truncate text-[9px] font-semibold text-purple-200">
                {autoMixStatus || 'AI Auto-Mix active'}
              </div>
              {autoMix.enabled && (
                <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-white/10">
                  <div className="h-full rounded-full bg-gradient-to-r from-purple-400 to-fuchsia-400 transition-[width] duration-300" style={{ width: `${Math.max(0, Math.min(100, autoMix.progress * 100))}%` }} />
                </div>
              )}
            </div>
          </div>
        )}
        {tab === 'suggestions' && (
          <>
            {suggestions.length === 0 ? (
              <div className="flex items-center justify-center py-8 text-[10px] text-zinc-500 uppercase tracking-wider">Analyzing…</div>
            ) : (
              suggestions.map((s, i) => (
                <div key={i} className="flex items-start gap-2 px-3 py-2 border-b border-white/[0.04] hover:bg-white/[0.02]">
                  <span className="text-[14px] shrink-0 mt-0.5">{s.icon}</span>
                  <div className="min-w-0 flex-1">
                    <div className={`text-[10px] font-semibold ${
                      s.type === 'warning' ? 'text-amber-300' : s.type === 'next-track' ? 'text-cyan-200' : s.type === 'transition' ? 'text-emerald-300' : 'text-zinc-200'
                    }`}>{s.title}</div>
                    <div className="text-[8px] text-zinc-400 mt-0.5 leading-snug">{s.detail}</div>
                  </div>
                  {s.action && (
                    <button onClick={s.action}
                      className="shrink-0 mt-0.5 flex h-5 items-center rounded-sm border border-purple-400/30 bg-purple-500/15 px-2 text-[7px] font-bold uppercase tracking-wider text-purple-200 hover:bg-purple-500/25">
                      {s.actionLabel}
                    </button>
                  )}
                </div>
              ))
            )}
          </>
        )}

        {tab === 'stems' && (
          <div>
            <div className="px-3 py-1.5 text-[8px] text-zinc-500 leading-snug border-b border-white/[0.04]">
              Extract vocals, drums, bass & instrumentals. Toggle stems on/off for live remixing.
            </div>
            {renderStemControls('A', stemA, deckA)}
            {renderStemControls('B', stemB, deckB)}
          </div>
        )}

        {tab === 'detect' && (
          <div>
            <div className="px-3 py-1.5 text-[8px] text-zinc-500 leading-snug border-b border-white/[0.04]">
              Analyse audio to auto-detect BPM and musical key.
            </div>
            {(['A', 'B'] as DeckLabel[]).map((label) => {
              const dk = decks[label];
              const st = detectStatus[label];
              const hasTrk = Boolean(dk.track.path);
              return (
                <div key={label} className="flex items-center gap-2 px-3 py-2 border-b border-white/[0.04]">
                  <span className={`text-[10px] font-bold ${label === 'A' ? 'text-cyan-400' : 'text-amber-400'}`}>{label}</span>
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-[9px] text-zinc-300">{dk.track.title || 'Empty'}</div>
                    <div className="text-[8px] text-zinc-500">
                      BPM: {dk.track.bpm > 0 ? dk.track.bpm : '—'} · Key: {dk.track.key || '—'}
                    </div>
                  </div>
                  {hasTrk && st !== 'running' && (
                    <button onClick={() => runDetect(label)}
                      className="h-5 rounded-sm border border-purple-400/30 bg-purple-500/15 px-2 text-[7px] font-bold uppercase tracking-wider text-purple-200 hover:bg-purple-500/25">
                      {st === 'done' ? 'Re-detect' : 'Detect'}
                    </button>
                  )}
                  {st === 'running' && <span className="text-[8px] text-purple-300 animate-pulse">Analysing…</span>}
                  {st === 'done' && <span className="text-[8px] text-emerald-400">✓</span>}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Quick actions */}
      <div className="flex items-center gap-1 px-3 py-1.5 border-t border-white/8">
        <span className="text-[7px] uppercase tracking-wider text-zinc-600 mr-auto">Quick</span>
        {(hasA || hasB) && (
          <button onClick={triggerMixNow} className="h-6 rounded border border-emerald-400/30 bg-emerald-500/15 px-3 text-[8px] font-bold uppercase text-emerald-200 hover:bg-emerald-500/25 transition-colors">⚡ Mix Now</button>
        )}
        {hasA && hasB && (
          <>
            <button onClick={() => actions.sync('B')} className="h-5 rounded-sm border border-white/10 px-2 text-[7px] font-bold uppercase text-zinc-300 hover:bg-white/5">Sync B</button>
            <button onClick={() => actions.setCrossfader(50)} className="h-5 rounded-sm border border-white/10 px-2 text-[7px] font-bold uppercase text-zinc-300 hover:bg-white/5">Center X</button>
          </>
        )}
      </div>
    </div>
  );
}
