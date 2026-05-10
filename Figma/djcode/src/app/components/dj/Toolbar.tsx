import { useState } from 'react';
import type { DeckLabel, DjConsoleMode, DjDeckState, DjMixerState } from '../../dj/types';
import type { DjBlendInsights } from '../../dj/selectors';

/* ── Artwork placeholder ─────────────────────────────────────────── */
function ArtPlaceholder({ deck, size }: { deck: DjDeckState; size: number }) {
  const loaded = Boolean(deck.track.path);
  const artUrl = (deck.track as Record<string, unknown>).artworkUrl as string | undefined;
  return (
    <div className="shrink-0 rounded-sm overflow-hidden" style={{ width: size, height: size }}>
      {artUrl ? (
        <img src={artUrl} alt="" className="w-full h-full object-cover" />
      ) : (
        <div className={`w-full h-full flex items-center justify-center ${loaded ? 'bg-zinc-800' : 'bg-zinc-900'}`}>
          <span className="text-[10px] text-zinc-600">♪</span>
        </div>
      )}
    </div>
  );
}

/* ── Deck track info (left-aligned for A, right-aligned for B) ───── */
function TrackInfo({ deck, side, onLoad }: { deck: DjDeckState; side: 'left' | 'right'; onLoad: () => void }) {
  const isA = side === 'left';
  const ac = isA ? 'text-cyan-400' : 'text-amber-400';
  const acBorder = isA ? 'border-cyan-400/40' : 'border-amber-400/40';
  const acBg = isA ? 'bg-cyan-400/10' : 'bg-amber-400/10';
  const loaded = Boolean(deck.track.path);

  const trackTitle = loaded ? deck.track.title || 'Loaded' : 'No track';
  const trackArtist = loaded ? deck.track.artist || 'Unknown' : '—';

  const info = (
    <div className={`min-w-0 flex-1 ${side === 'right' ? 'text-right' : ''}`}>
      <div className="truncate text-[11px] font-semibold text-zinc-100">{trackTitle}</div>
      <div className="truncate text-[9px] text-zinc-400">{trackArtist} · {loaded && deck.effectiveBpm > 0 ? deck.effectiveBpm.toFixed(1) : '—'}</div>
    </div>
  );

  const timeDisplay = (
    <span className="text-[13px] font-mono font-bold text-zinc-200 tabular-nums shrink-0">
      {deck.remaining || '-00:00'}
    </span>
  );

  const keyBadge = loaded && deck.track.key ? (
    <span className="shrink-0 rounded-sm border border-white/10 bg-white/5 px-1.5 py-0.5 text-[9px] font-semibold text-zinc-300">
      {deck.track.key}
    </span>
  ) : null;

  const artwork = <ArtPlaceholder deck={deck} size={34} />;

  if (side === 'left') {
    return (
      <div className="flex items-center gap-2 min-w-0 flex-1">
        {artwork}
        {info}
        {timeDisplay}
        {keyBadge}
      </div>
    );
  }
  return (
    <div className="flex items-center gap-2 min-w-0 flex-1 justify-end">
      {keyBadge}
      {timeDisplay}
      {info}
      {artwork}
    </div>
  );
}

/* ── Console selector ────────────────────────────────────────────── */
const consoleOptions: Array<{ mode: DjConsoleMode; label: string }> = [
  { mode: 'two-deck', label: 'Two Decks' },
  { mode: 'four-deck', label: 'Four Decks' },
  { mode: 'cdj', label: 'CDJ' },
  { mode: 'sampler', label: 'Sampler' },
];

/* ================================================================== */
/*  Toolbar                                                            */
/* ================================================================== */

export function Toolbar({
  decks,
  mixer,
  online,
  insights,
  consoleMode,
  onLoadFile,
  onSetConsoleMode,
}: {
  decks: Record<DeckLabel, DjDeckState>;
  mixer: DjMixerState;
  online: boolean;
  insights: DjBlendInsights;
  consoleMode: DjConsoleMode;
  onLoadFile: (deck: DeckLabel) => void;
  onSetConsoleMode: (mode: DjConsoleMode) => void;
}) {
  const [showConsoleMenu, setShowConsoleMenu] = useState(false);
  const currentLabel = consoleOptions.find((o) => o.mode === consoleMode)?.label ?? 'Two Decks';

  return (
    <header className="flex h-[46px] items-center gap-3 border-b border-white/8 bg-[#0a0c10] px-3">
      {/* Deck A track info (left side) */}
      <TrackInfo deck={decks.A} side="left" onLoad={() => onLoadFile('A')} />

      {/* Center controls */}
      <div className="flex items-center gap-2 shrink-0">
        {/* Console selector */}
        <div className="relative">
          <button onClick={() => setShowConsoleMenu((v) => !v)}
            className="flex items-center gap-1 rounded-sm border border-white/10 bg-white/[0.03] px-2.5 h-7 text-[9px] font-semibold text-zinc-200 hover:bg-white/[0.06]">
            ▪▪ {currentLabel} ▾
          </button>
          {showConsoleMenu && (
            <div className="absolute left-1/2 -translate-x-1/2 top-full z-50 mt-1 w-28 rounded-sm border border-white/10 bg-[#0c0e13] shadow-xl">
              {consoleOptions.map((opt) => (
                <button key={opt.mode} onClick={() => { onSetConsoleMode(opt.mode); setShowConsoleMenu(false); }}
                  className={`flex w-full items-center px-2.5 py-1.5 text-left text-[9px] ${consoleMode === opt.mode ? 'bg-cyan-400/10 text-cyan-100' : 'text-zinc-300 hover:bg-white/[0.05]'}`}>
                  {opt.label}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Online indicator */}
        <div className={`flex items-center gap-1 text-[8px] ${online ? 'text-emerald-300' : 'text-zinc-500'}`}>
          <div className={`h-1.5 w-1.5 rounded-full ${online ? 'bg-emerald-400' : 'bg-zinc-600'}`} />
        </div>

        {/* Header toggle */}
        <button
          onClick={() => {
            try { (window as any).chrome?.webview?.postMessage(JSON.stringify({ type: 'dj.shell.toggleHeader' })); } catch {}
          }}
          className="flex h-6 w-6 items-center justify-center rounded-sm border border-white/10 text-[10px] text-zinc-400 hover:bg-white/[0.06] hover:text-white"
          title="Toggle header"
        >˄</button>
      </div>

      {/* Deck B track info (right side) */}
      <TrackInfo deck={decks.B} side="right" onLoad={() => onLoadFile('B')} />
    </header>
  );
}
