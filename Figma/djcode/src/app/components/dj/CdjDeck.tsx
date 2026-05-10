import { useRef, useState, useEffect } from 'react';
import type { DjActions, DjDeckState, DjLoopSize } from '../../dj/types';

interface CdjDeckProps {
  deck: DjDeckState;
  side: 'left' | 'right';
  actions: Pick<
    DjActions,
    | 'loadFile' | 'loadTrack' | 'play' | 'pause' | 'cue' | 'sync' | 'bend'
    | 'setCue' | 'setTempo' | 'setPitchRange' | 'setGain' | 'setEq' | 'setFilter'
    | 'setVolume' | 'loopIn' | 'loopOut' | 'loopClear' | 'hotCue' | 'setLoopSize'
  >;
}

const ac = (s: 'left' | 'right') => ({
  text: s === 'left' ? 'text-cyan-400' : 'text-amber-400',
  dim: s === 'left' ? 'text-cyan-300' : 'text-amber-300',
  bg: s === 'left' ? 'bg-cyan-400' : 'bg-amber-400',
  bgDim: s === 'left' ? 'bg-cyan-400/10' : 'bg-amber-400/10',
  border: s === 'left' ? 'border-cyan-400/30' : 'border-amber-400/30',
  fill: s === 'left' ? 'bg-cyan-300' : 'bg-amber-300',
  ring: s === 'left' ? '#22d3ee' : '#fbbf24',
});

function CdjKnob({ label, value, color, onChange }: { label: string; value: number; color: string; onChange: (v: number) => void }) {
  const angle = -135 + (value / 100) * 270;
  return (
    <div className="relative flex flex-col items-center">
      <span className="text-[7px] font-semibold uppercase tracking-widest text-zinc-500 leading-none mb-0.5">{label}</span>
      <div className="relative h-7 w-7 rounded-full border border-zinc-700 bg-zinc-900 cursor-pointer" onDoubleClick={() => onChange(50)}>
        <div className="absolute inset-0 flex items-center justify-center" style={{ transform: `rotate(${angle}deg)` }}>
          <div className={`mb-3 h-1.5 w-[2px] rounded-sm ${color}`} />
        </div>
      </div>
      <input type="range" min={0} max={100} value={value} onChange={(e) => onChange(Number(e.target.value))} className="h-0 w-7 opacity-0 cursor-pointer absolute bottom-0" />
    </div>
  );
}

export function CdjDeck({ deck, side, actions }: CdjDeckProps) {
  const a = ac(side);
  const loaded = Boolean(deck.track.path);
  const lbl = deck.label;
  const loopSizes: DjLoopSize[] = ['1/4', '1/2', '1', '2'];
  const [dropOver, setDropOver] = useState(false);

  /* ── Tempo slider (horizontal) ─── */
  const tempoSlider = (
    <div className="flex items-center gap-1.5 px-3 py-1">
      <span className="text-[8px] font-semibold uppercase tracking-wider text-zinc-500 w-12">Tempo</span>
      <input
        type="range" min={-deck.pitchRange} max={deck.pitchRange} step={0.1}
        value={deck.tempo}
        onChange={(e) => actions.setTempo(lbl, Number(e.target.value))}
        className="flex-1 h-1 accent-zinc-400"
      />
      <span className={`text-[10px] font-mono w-12 text-right ${a.text}`}>
        {deck.tempo >= 0 ? '+' : ''}{deck.tempo.toFixed(1)}%
      </span>
      <div className="flex gap-1">
        {[8, 16, 50].map((range) => (
          <button
            key={range}
            onClick={() => actions.setPitchRange(lbl, range)}
            className={`h-5 rounded-sm px-1.5 text-[7px] font-bold ${Math.round(deck.pitchRange) === range ? `${a.border} ${a.bgDim} ${a.dim}` : 'border border-white/10 text-zinc-400'}`}
          >
            ±{range}
          </button>
        ))}
      </div>
    </div>
  );

  /* ── Track info banner ── */
  const trackBanner = (
    <div className="flex items-center gap-3 px-3 py-1.5 border-b border-white/6">
      <div className="min-w-0 flex-1">
        <div className="truncate text-[12px] font-semibold text-zinc-100">{loaded ? deck.track.title || 'Loaded' : 'No track'}</div>
        <div className="truncate text-[9px] text-zinc-400">{loaded ? deck.track.artist || 'Unknown' : '—'}</div>
      </div>
      <div className="flex flex-col items-end shrink-0">
        <span className={`text-[18px] font-black leading-none ${a.text}`}>
          {deck.effectiveBpm > 0 ? deck.effectiveBpm.toFixed(1) : '—'}
        </span>
        <span className="text-[8px] text-zinc-500">BPM</span>
      </div>
      {deck.track.key && (
        <span className="shrink-0 rounded-sm border border-white/10 bg-white/5 px-1.5 py-0.5 text-[10px] font-semibold text-zinc-300">
          {deck.track.key}
        </span>
      )}
    </div>
  );

  /* ── Big time display ── */
  const timeDisplay = (
    <div className="flex items-center justify-center gap-4 py-2 border-b border-white/6">
      <span className="text-[28px] font-mono font-bold text-zinc-100 tabular-nums">
        {deck.elapsed || '00:00'}
      </span>
      <span className="text-[18px] font-mono text-zinc-500 tabular-nums">
        {deck.remaining || '-00:00'}
      </span>
    </div>
  );

  /* ── EQ strip (horizontal) ── */
  const eqStrip = (
    <div className="flex items-center justify-center gap-3 px-3 py-1.5 border-b border-white/6">
      <CdjKnob label="Hi" value={deck.eqHigh} color={a.fill} onChange={(v) => actions.setEq(lbl, 'high', v)} />
      <CdjKnob label="Mid" value={deck.eqMid} color={a.fill} onChange={(v) => actions.setEq(lbl, 'mid', v)} />
      <CdjKnob label="Lo" value={deck.eqLow} color={a.fill} onChange={(v) => actions.setEq(lbl, 'low', v)} />
      <CdjKnob label="Flt" value={deck.filter} color={a.fill} onChange={(v) => actions.setFilter(lbl, v)} />
      <div className="w-px h-6 bg-white/10 mx-1" />
      <CdjKnob label="Gain" value={deck.gain} color={a.fill} onChange={(v) => actions.setGain(lbl, v)} />
      <CdjKnob label="Vol" value={deck.volume} color={a.fill} onChange={(v) => actions.setVolume(lbl, v)} />
    </div>
  );

  /* ── Transport ── */
  const transport = (
    <div className="flex flex-col gap-1 px-3 py-1.5">
      <div className="flex gap-1">
        {loaded && deck.isPlaying ? (
          <button onClick={() => actions.pause(lbl)}
            className="flex h-8 flex-1 items-center justify-center rounded-sm bg-red-500/20 border border-red-500/40 text-[11px] font-bold uppercase text-red-300 gap-1">
            ◼ Stop
          </button>
        ) : (
          <button onClick={() => actions.play(lbl)}
            className={`flex h-8 flex-1 items-center justify-center rounded-sm text-[11px] font-bold uppercase gap-1 ${
              loaded ? `${a.bgDim} border ${a.border} ${a.text}` : 'bg-white/[0.03] border border-white/10 text-zinc-400'
            }`}>
            ▶ Play
          </button>
        )}
        <button onClick={() => actions.cue(lbl)}
          className="flex h-8 flex-1 items-center justify-center rounded-sm border border-white/10 bg-white/[0.03] text-[11px] font-bold uppercase text-zinc-200">Cue</button>
        <button onClick={() => actions.sync(lbl)}
          className={`flex h-8 px-3 items-center justify-center rounded-sm text-[10px] font-bold uppercase ${
            deck.syncEnabled ? `${a.border} ${a.bgDim} ${a.text}` : 'border border-white/10 text-zinc-400'
          }`}>Sync</button>
      </div>
      <div className="flex gap-0.5 items-center">
        <button onClick={() => actions.setCue(lbl, deck.currentTime)}
          className="flex h-5 items-center rounded-sm border border-white/10 px-1.5 text-[8px] font-semibold uppercase text-zinc-300">Set</button>
        {[0, 1, 2, 3].map((i) => (
          <button key={i} onClick={() => actions.hotCue(lbl, i)}
            className={`flex h-5 w-5 items-center justify-center rounded-sm text-[8px] font-bold ${
              deck.cuePoints[i] ? `${a.border} ${a.bgDim} ${a.dim}` : 'border border-white/10 text-zinc-400'
            }`}>{i + 1}</button>
        ))}
        <div className="w-2" />
        <button onClick={() => actions.loopIn(lbl)}
          className={`flex h-5 items-center rounded-sm border px-1.5 text-[8px] font-semibold uppercase ${
            deck.loopActive && deck.loopStart != null ? `${a.border} ${a.bgDim} ${a.dim}` : 'border-white/10 text-zinc-400'
          }`}>In</button>
        <button onClick={() => actions.loopOut(lbl)}
          className={`flex h-5 items-center rounded-sm border px-1.5 text-[8px] font-semibold uppercase ${
            deck.loopActive && deck.loopEnd != null ? `${a.border} ${a.bgDim} ${a.dim}` : 'border-white/10 text-zinc-400'
          }`}>Out</button>
        <button onClick={() => actions.loopClear(lbl)}
          className="flex h-5 items-center rounded-sm border border-white/10 px-1.5 text-[8px] font-semibold uppercase text-zinc-400">X</button>
        {loopSizes.map((s) => (
          <button key={s} onClick={() => actions.setLoopSize(lbl, s)}
            className={`flex h-5 w-5 items-center justify-center rounded-sm text-[8px] font-bold ${
              deck.loopSize === s ? `${a.border} ${a.bgDim} ${a.dim}` : 'border border-white/10 text-zinc-400'
            }`}>{s}</button>
        ))}
      </div>
    </div>
  );

  return (
    <section
      className={`relative flex min-h-0 min-w-0 flex-col overflow-hidden bg-[#0c0e13]/46 backdrop-blur-[1px] ${dropOver ? 'ring-2 ring-inset ' + (side === 'left' ? 'ring-cyan-400/50' : 'ring-amber-400/50') : ''}`}
      onDragOver={(e) => { if (e.dataTransfer.types.includes('application/x-dj-track')) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; setDropOver(true); } }}
      onDragLeave={() => setDropOver(false)}
      onDrop={(e) => { e.preventDefault(); setDropOver(false); const path = e.dataTransfer.getData('application/x-dj-track'); if (path) actions.loadTrack(lbl, path); }}
    >
      <div className="relative z-10 flex min-h-0 min-w-0 flex-1 flex-col">
      {trackBanner}
      {timeDisplay}
      {eqStrip}
      {tempoSlider}
      <div className="flex-1 min-h-0" />
      {transport}
      </div>
    </section>
  );
}
