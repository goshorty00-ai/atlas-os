import { useEffect, useRef, useState } from 'react';
import type { DjActions, DjDeckState } from '../../dj/types';

interface DeckProps {
  deck: DjDeckState;
  side: 'left' | 'right';
  compact?: boolean;
  actions: Pick<
    DjActions,
    | 'loadFile'
    | 'loadTrack'
    | 'play'
    | 'pause'
    | 'cue'
    | 'sync'
    | 'bend'
    | 'seek'
    | 'setCue'
    | 'setTempo'
    | 'setPitchRange'
    | 'setGain'
    | 'setEq'
    | 'setFilter'
    | 'setVolume'
    | 'loopIn'
    | 'loopOut'
    | 'loopClear'
    | 'hotCue'
    | 'setLoopSize'
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
  glow: s === 'left' ? 'rgba(34,211,238,0.25)' : 'rgba(251,191,36,0.25)',
});

const clamp = (value: number, min: number, max: number) =>
  Math.min(max, Math.max(min, value));

/* ── EQ Rotary knob ──────────────────────────────────────────────── */
function EqKnob({ label, value, color, onChange }: { label: string; value: number; color: string; onChange: (v: number) => void }) {
  const angle = -135 + (value / 100) * 270;
  const dragRef = useRef<{ startY: number; startValue: number } | null>(null);

  useEffect(() => {
    if (!dragRef.current) return;
    const move = (e: PointerEvent) => {
      const drag = dragRef.current;
      if (!drag) return;
      const delta = (drag.startY - e.clientY) * 0.4;
      onChange(clamp(Math.round(drag.startValue + delta), 0, 100));
    };
    const up = () => {
      dragRef.current = null;
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, { once: true });
    return () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
  }, [onChange]);

  return (
    <div className="relative flex flex-col items-center">
      <span className="text-[7px] font-semibold uppercase tracking-widest text-zinc-500 leading-none mb-0.5">{label}</span>
      <div
        className="relative h-8 w-8 cursor-ns-resize rounded-full border border-zinc-700/80 bg-gradient-to-b from-zinc-800 to-zinc-900 shadow-inner"
        onDoubleClick={() => onChange(50)}
        onWheel={(e) => {
          e.preventDefault();
          onChange(clamp(value + (e.deltaY < 0 ? 2 : -2), 0, 100));
        }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.preventDefault();
          dragRef.current = { startY: e.clientY, startValue: value };
        }}
      >
        <div className="absolute inset-0 flex items-center justify-center" style={{ transform: `rotate(${angle}deg)` }}>
          <div className={`mb-3.5 h-2 w-[2px] rounded-sm ${color}`} />
        </div>
        {/* Subtle dot at center */}
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="h-1 w-1 rounded-full bg-zinc-700" />
        </div>
      </div>
      <input type="range" min={0} max={100} value={value} onChange={(e) => onChange(Number(e.target.value))} className="h-0 w-8 opacity-0 cursor-pointer absolute bottom-0" />
    </div>
  );
}

/* ── Volume fader (vertical) ─────────────────────────────────────── */
function VolumeFader({ value, accent, onChange }: { value: number; accent: string; onChange: (v: number) => void }) {
  const rail = useRef<HTMLDivElement | null>(null);
  const [drag, setDrag] = useState(false);

  useEffect(() => {
    if (!drag) return;
    const move = (e: PointerEvent) => {
      const r = rail.current; if (!r) return;
      const b = r.getBoundingClientRect();
      onChange(Math.max(0, Math.min(100, (1 - (e.clientY - b.top) / Math.max(1, b.height)) * 100)));
    };
    const up = () => setDrag(false);
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, { once: true });
    return () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
  }, [drag, onChange]);

  return (
    <div ref={rail} className="relative h-24 w-6 min-h-[96px] cursor-ns-resize rounded-sm bg-zinc-950/70" style={{ touchAction: 'none' }}
      onPointerDown={(e) => { if (e.button === 0) { e.preventDefault(); setDrag(true); } }}>
      <div className="absolute inset-y-1 left-1/2 w-px -translate-x-1/2 bg-zinc-800" />
      {/* Notches */}
      {[0, 25, 50, 75, 100].map(n => (
        <div key={n} className="absolute left-0 right-0 h-px bg-zinc-700/30" style={{ top: `${100 - n}%` }} />
      ))}
      <div className="absolute inset-x-0.5 bottom-0" style={{ height: `${value}%` }}>
        <div className={`mx-auto h-full w-[2px] ${accent}`} />
      </div>
      <div className="absolute left-1/2 h-3 w-6 -translate-x-1/2 -translate-y-1/2 rounded-[1px] border border-zinc-400 bg-gradient-to-b from-zinc-200 to-zinc-500 shadow-sm" style={{ top: `${100 - value}%` }}>
        <div className="absolute inset-0 flex items-center justify-center gap-[1px]">
          <div className="h-1.5 w-[1px] bg-zinc-700/60" />
          <div className="h-1.5 w-[1px] bg-zinc-700/60" />
        </div>
      </div>
    </div>
  );
}

function PitchFader({
  value,
  range,
  accent,
  onChange,
  onRangeChange,
}: {
  value: number;
  range: number;
  accent: string;
  onChange: (v: number) => void;
  onRangeChange: (v: number) => void;
}) {
  const rangeOptions = [8, 12, 16, 50];
  const rail = useRef<HTMLDivElement | null>(null);
  const [drag, setDrag] = useState(false);

  const updateFromClientY = (clientY: number) => {
    const r = rail.current;
    if (!r) return;
    const b = r.getBoundingClientRect();
    const normalized = 1 - (clientY - b.top) / Math.max(1, b.height);
    const next = (clamp(normalized, 0, 1) * 2 - 1) * range;
    onChange(Number(next.toFixed(1)));
  };

  useEffect(() => {
    if (!drag) return;
    const move = (e: PointerEvent) => updateFromClientY(e.clientY);
    const up = () => setDrag(false);
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, { once: true });
    return () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
  }, [drag, range]);

  const position = range <= 0 ? 50 : ((value + range) / (range * 2)) * 100;

  return (
    <div className="flex w-full flex-col items-center gap-1.5 rounded-xl border border-white/8 bg-[#111319] px-1.5 py-1.5 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
      <div className="text-[6px] font-bold uppercase tracking-[0.2em] text-zinc-500">Pitch</div>
      <span className={`text-[10px] font-mono font-black ${accent}`}>
        {value >= 0 ? '+' : ''}{value.toFixed(1)}%
      </span>
      <div className="text-[6px] font-black tracking-tight text-zinc-100">
        {range <= 0 ? '0.0' : ((120 * (1 + value / 100))).toFixed(1)}
      </div>
      <div
        ref={rail}
        className="relative h-20 w-8 cursor-ns-resize rounded-lg border border-white/10 bg-zinc-950/90"
        style={{ touchAction: 'none' }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.preventDefault();
          updateFromClientY(e.clientY);
          setDrag(true);
        }}
      >
        <div className="absolute inset-y-1 left-1/2 w-px -translate-x-1/2 bg-zinc-700" />
        {[0, 20, 40, 60, 80, 100].map((marker) => (
          <div key={marker} className="absolute left-1 right-1 h-px bg-zinc-700/40" style={{ top: `${marker}%` }} />
        ))}
        <div className="absolute left-1 right-1 top-1/2 h-px -translate-y-1/2 bg-white/30" />
        <div className="absolute left-1/2 h-3 w-7 -translate-x-1/2 -translate-y-1/2 rounded-md border border-zinc-300 bg-gradient-to-b from-zinc-200 to-zinc-500 shadow-sm" style={{ top: `${position}%` }} />
      </div>
      <div className="flex w-full items-center justify-between gap-1">
        <button onClick={() => onChange(Math.max(-range, Number((value - 0.5).toFixed(1))))} className="flex h-6 w-6 items-center justify-center rounded-md border border-white/12 text-[14px] leading-none text-zinc-300 hover:bg-white/5">-</button>
        <button onClick={() => onChange(Math.min(range, Number((value + 0.5).toFixed(1))))} className="flex h-6 w-6 items-center justify-center rounded-md border border-white/12 text-[14px] leading-none text-zinc-300 hover:bg-white/5">+</button>
      </div>
      <button onClick={() => onChange(0)} className="flex h-5 w-full items-center justify-center rounded-md border border-white/12 px-1 text-[6px] font-bold uppercase text-zinc-300 hover:bg-white/5">Center</button>
      <div className="grid w-full grid-cols-2 gap-1">
        {rangeOptions.slice(0, 2).map((option) => (
          <button
            key={option}
            onClick={() => onRangeChange(option)}
            className={`h-5 rounded-md border px-1 text-[6px] font-bold ${Math.round(range) === option ? `border-current ${accent} bg-white/10` : 'border-white/12 text-zinc-500 hover:bg-white/5'}`}
          >
            ±{option}
          </button>
        ))}
      </div>
      <div className="text-[6px] font-semibold uppercase tracking-[0.14em] text-zinc-500">±{range.toFixed(0)}</div>
    </div>
  );
}

/* ── Spinning jog wheel ──────────────────────────────────────────── */
function JogWheel({ deck, a, size, onTogglePlay, onScratch }: {
  deck: DjDeckState;
  a: ReturnType<typeof ac>;
  size?: string;
  onTogglePlay: () => void;
  onScratch: (deltaPct: number) => void;
}) {
  const loaded = Boolean(deck.track.path);
  const elapsed = deck.elapsed && !deck.elapsed.includes('NaN') ? deck.elapsed : '00:00';
  const [rotation, setRotation] = useState(0);
  const rafRef = useRef(0);
  const lastTime = useRef(0);
  const dragRef = useRef<{ startY: number; lastY: number; startAngle: number; didMove: boolean } | null>(null);
  const wheelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!deck.isPlaying) {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
      lastTime.current = 0;
      return;
    }
    const spin = (ts: number) => {
      if (lastTime.current) {
        const dt = ts - lastTime.current;
        setRotation((prev) => (prev + dt * 0.12) % 360);
      }
      lastTime.current = ts;
      rafRef.current = requestAnimationFrame(spin);
    };
    rafRef.current = requestAnimationFrame(spin);
    return () => { cancelAnimationFrame(rafRef.current); lastTime.current = 0; };
  }, [deck.isPlaying]);

  const currentRot = deck.isPlaying ? rotation : (deck.currentTime * 3.6) % 360;

  /* ── Drag-to-scratch ── */
  useEffect(() => {
    if (!dragRef.current) return;

    const onMove = (e: PointerEvent) => {
      const d = dragRef.current;
      if (!d) return;
      const dy = e.clientY - d.startY;
      if (Math.abs(dy) > 3) d.didMove = true;
      if (!d.didMove) return;
      // Incremental drag: each movement nudges position
      const incrementalDy = e.clientY - d.lastY;
      d.lastY = e.clientY;
      // 400px drag = 10% of track — gentle, controllable scratching
      const deltaPct = -(incrementalDy / 400) * 10;
      onScratch(deltaPct);
    };
    const onUp = () => {
      const d = dragRef.current;
      dragRef.current = null;
      if (d && !d.didMove && loaded) {
        onTogglePlay();
      }
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp, { once: true });
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  });

  const handlePointerDown = (e: React.PointerEvent) => {
    if (e.button !== 0) return;
    e.preventDefault();
    dragRef.current = { startY: e.clientY, lastY: e.clientY, startAngle: currentRot, didMove: false };
  };

  return (
    <div className="flex items-center justify-center p-1">
      <div
        ref={wheelRef}
        onPointerDown={handlePointerDown}
        className={`relative aspect-square rounded-full cursor-pointer select-none ${size || 'w-[220px]'}`}
        style={{
          border: `3px solid ${a.ring}50`,
          background: 'radial-gradient(circle at 45% 40%, #12161f 20%, #0a0d14 60%, #080a10 100%)',
          boxShadow: deck.isPlaying
            ? `0 0 30px ${a.glow}, 0 0 12px ${a.glow}, inset 0 0 20px ${a.glow}`
            : `inset 0 0 8px rgba(0,0,0,0.5)`,
          transition: 'box-shadow 0.3s ease',
          touchAction: 'none',
        }}
      >
        {/* Outer ticks */}
        {Array.from({ length: 32 }).map((_, i) => (
          <div key={i} className="absolute left-1/2 top-0 -translate-x-1/2" style={{ height: '50%', transformOrigin: '50% 100%', transform: `rotate(${i * 11.25}deg)` }}>
            <div className={`h-1.5 w-px ${i % 4 === 0 ? 'bg-zinc-500' : 'bg-zinc-800'}`} />
          </div>
        ))}

        {/* Inner rings */}
        <div className="absolute inset-3 rounded-full border border-white/[0.05]" />
        <div className="absolute inset-7 rounded-full border border-white/[0.03]" />

        {/* Spinning platter */}
        <div className="absolute inset-1 rounded-full" style={{ transform: `rotate(${currentRot}deg)`, transition: deck.isPlaying ? 'none' : 'transform 0.1s' }}>
          <div className="absolute left-1/2 top-0 h-5 w-[2px] -translate-x-1/2 rounded-full" style={{ backgroundColor: loaded ? a.ring : '#52525b' }} />
          {[90, 180, 270].map((deg) => (
            <div key={deg} className="absolute left-1/2 top-1 -translate-x-1/2" style={{ height: '48%', transformOrigin: '50% 100%', transform: `rotate(${deg}deg)` }}>
              <div className="h-2 w-px bg-zinc-700/40" />
            </div>
          ))}
          <div className="absolute inset-[18%] flex items-center justify-center">
            <img
              src="/atlas-ai-chat-logo.png"
              alt="Atlas AI Chat"
              className="max-h-full max-w-full object-contain opacity-45 saturate-[1.05] drop-shadow-[0_0_14px_rgba(0,0,0,0.55)]"
              style={{ filter: loaded ? 'drop-shadow(0 0 14px rgba(0,0,0,0.55))' : 'grayscale(0.25) brightness(0.7)' }}
              draggable={false}
            />
          </div>
        </div>

        {/* Center info */}
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <div className="rounded-full border border-white/10 bg-black/30 px-3 py-1">
            <span className="text-[10px] font-mono font-semibold tabular-nums text-zinc-200">{elapsed}</span>
          </div>
          {!loaded && <span className="mt-1 text-[7px] uppercase tracking-[0.18em] text-zinc-600">Empty</span>}
        </div>
      </div>
    </div>
  );
}

/* ════════════════════════════════════════════════════════════════════ */
/*  DECK                                                               */
/* ════════════════════════════════════════════════════════════════════ */

export function Deck({ deck, side, compact, actions }: DeckProps) {
  const a = ac(side);
  const loaded = Boolean(deck.track.path);
  const lbl = deck.label;
  const displayBpm = deck.effectiveBpm > 0 ? deck.effectiveBpm : deck.track.bpm;
  const [dropOver, setDropOver] = useState(false);
  const platterSize = compact ? 'w-[124px]' : 'w-[168px]';
  const togglePreset = (current: number, active: number, reset = 50) => (Math.abs(current - active) < 1 ? reset : active);

  /* ── Sync block ─────────────────────────────────────────────── */
  const syncBlock = (
    <div className="flex h-full shrink-0 flex-col items-center gap-1 rounded-none border-r border-white/8 bg-[#1a1d24]/68 px-1 py-1.5 backdrop-blur-[1px]">
      <button onClick={() => actions.sync(lbl)}
        className={`h-8 w-full text-[8px] font-black uppercase rounded-lg tracking-[0.08em] transition-colors ${
          deck.syncEnabled
            ? `border-2 ${a.border} ${a.bgDim} ${a.text} shadow-sm`
            : 'border border-white/15 text-zinc-400 hover:text-zinc-200 hover:bg-white/5'
        }`}>Sync</button>
      <div className="flex min-h-[18px] flex-col items-center justify-center text-center">
        {deck.syncEnabled ? (
          <>
            <span className={`text-[7px] font-black uppercase tracking-[0.14em] ${deck.isSyncMaster ? a.text : 'text-zinc-300'}`}>
              {deck.isSyncMaster ? 'Master' : `Follow ${deck.syncSourceDeck || '—'}`}
            </span>
            <span className={`text-[6px] font-semibold uppercase tracking-[0.12em] ${deck.syncAligned ? 'text-emerald-300' : 'text-amber-300'}`}>
              {deck.syncAligned ? 'Locked' : 'Align'}
            </span>
          </>
        ) : (
          <span className="text-[6px] font-semibold uppercase tracking-[0.14em] text-zinc-500">Sync Off</span>
        )}
      </div>
      <div className="flex flex-col items-center gap-0.5 text-center">
        <span className={`text-[10px] font-mono font-black ${a.text}`}>
          {displayBpm > 0 ? `${displayBpm.toFixed(1)} BPM` : '---.- BPM'}
        </span>
        <span className="text-[6px] font-semibold uppercase tracking-[0.14em] text-zinc-500">
          Tempo
        </span>
      </div>
      <div className="flex flex-col items-center gap-0.5 text-center">
        <span className={`text-[10px] font-mono font-black ${a.text}`}>
          {deck.tempo >= 0 ? '+' : ''}{deck.tempo.toFixed(1)}%
        </span>
        <span className="text-[6px] font-semibold uppercase tracking-[0.14em] text-zinc-500">
          Pitch
        </span>
      </div>
      <div className="w-full pt-0.5">
        <PitchFader
        value={deck.tempo}
        range={deck.pitchRange}
        accent={a.text}
        onChange={(v) => actions.setTempo(lbl, v)}
        onRangeChange={(v) => actions.setPitchRange(lbl, v)}
        />
      </div>
    </div>
  );

  /* ── EQ + Volume strip ──────────────────────────────────────── */
  const eqVolStrip = (
    <div className="flex h-full shrink-0 items-start gap-1 border-l border-white/8 bg-[#1a1d24]/68 px-1 py-1.5 backdrop-blur-[1px]">
      <div className="flex min-w-0 flex-1 flex-col items-center gap-1.5">
        <div className="rounded-md bg-white/[0.06] px-1.5 py-1 text-[7px] font-bold uppercase tracking-[0.12em] text-zinc-300">EQ</div>
        <EqKnob label="High" value={deck.eqHigh} color={a.fill} onChange={(v) => actions.setEq(lbl, 'high', v)} />
        <EqKnob label="Mid" value={deck.eqMid} color={a.fill} onChange={(v) => actions.setEq(lbl, 'mid', v)} />
        <EqKnob label="Low" value={deck.eqLow} color={a.fill} onChange={(v) => actions.setEq(lbl, 'low', v)} />
      </div>
      <div className="flex w-[36px] flex-col items-center gap-1.5 pt-8">
        <EqKnob label="Filter" value={deck.filter} color={a.fill} onChange={(v) => actions.setFilter(lbl, v)} />
        <div className="flex min-h-0 w-full flex-col items-center gap-1 pt-2">
          <span className="text-[6px] font-semibold uppercase tracking-widest text-zinc-500">Vol</span>
          <VolumeFader value={deck.volume} accent={a.fill} onChange={(v) => actions.setVolume(lbl, v)} />
        </div>
      </div>
    </div>
  );

  const padFxLane = (
    <div className="flex h-full min-w-0 flex-col gap-3 border-r border-white/6 bg-[#181b22]/60 px-3 py-2 text-zinc-500 backdrop-blur-[1px]">
      <div>
        <div className="mb-1 flex items-center gap-2 text-[6px] font-bold uppercase tracking-[0.18em] text-zinc-600">
          <div className="h-px flex-1 bg-white/10" />
          <span>Pads</span>
          <div className="h-px flex-1 bg-white/10" />
        </div>
        <div className="grid grid-cols-2 gap-1.5">
          {[
            { label: 'Beat', onClick: () => actions.hotCue(lbl, 0), active: Boolean(deck.cuePoints[0]) },
            { label: 'Roll', onClick: () => actions.hotCue(lbl, 1), active: Boolean(deck.cuePoints[1]) },
            { label: 'Scratch', onClick: () => actions.hotCue(lbl, 2), active: Boolean(deck.cuePoints[2]) },
            { label: 'Sampler', onClick: () => actions.hotCue(lbl, 3), active: Boolean(deck.cuePoints[3]) },
          ].map((item) => (
            <button key={item.label} onClick={item.onClick} className={`flex h-6 items-center justify-center rounded-md border text-[6px] font-bold uppercase tracking-[0.08em] transition-colors ${
              item.active ? `${a.border} ${a.bgDim} ${a.dim}` : 'border-white/8 bg-black/10 text-zinc-400 hover:bg-white/5'
            }`}>
              {item.label}
            </button>
          ))}
        </div>
      </div>
      <div>
        <div className="mb-1 flex items-center gap-2 text-[6px] font-bold uppercase tracking-[0.18em] text-zinc-600">
          <div className="h-px flex-1 bg-white/10" />
          <span>FX</span>
          <div className="h-px flex-1 bg-white/10" />
        </div>
        <div className="grid grid-cols-3 gap-2">
          {[
            { label: 'Filter', onClick: () => actions.setFilter(lbl, togglePreset(deck.filter, 78, 50)), active: Math.abs(deck.filter - 50) > 1 },
            { label: 'Flange', onClick: () => actions.setEq(lbl, 'mid', togglePreset(deck.eqMid, 72, 50)), active: Math.abs(deck.eqMid - 50) > 1 },
            { label: 'Cut', onClick: () => actions.setEq(lbl, 'low', togglePreset(deck.eqLow, 22, 50)), active: Math.abs(deck.eqLow - 50) > 1 },
          ].map((item) => (
            <button key={item.label} onClick={item.onClick} className="flex flex-col items-center gap-1">
              <div className={`relative h-7 w-7 rounded-full border transition-colors ${item.active ? 'border-cyan-400/30 bg-cyan-400/10' : 'border-white/10 bg-black/20'}`}>
                <div className="absolute left-1/2 top-1 h-2 w-px -translate-x-1/2 rounded-full bg-zinc-300" />
              </div>
              <span className="text-[6px] uppercase tracking-[0.08em] text-zinc-500">{item.label}</span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );

  /* ── Transport row ──────────────────────────────────────────── */
  const transport = (
    <div className="flex shrink-0 items-center justify-center gap-1.5 border-t border-white/8 bg-[#12151c]/70 px-2 py-2 backdrop-blur-[1px]">
      <button onClick={() => actions.loopIn(lbl)}
        className={`flex h-7 min-w-8 items-center justify-center rounded-md border px-2 text-[6px] font-bold uppercase tracking-[0.08em] transition-colors ${
          deck.loopActive && deck.loopStart != null ? `${a.border} ${a.bgDim} ${a.dim}` : 'border-white/12 text-zinc-400 hover:bg-white/5'
        }`}>In</button>
      <button onClick={() => actions.loopOut(lbl)}
        className={`flex h-7 min-w-8 items-center justify-center rounded-md border px-2 text-[6px] font-bold uppercase tracking-[0.08em] transition-colors ${
          deck.loopActive && deck.loopEnd != null ? `${a.border} ${a.bgDim} ${a.dim}` : 'border-white/12 text-zinc-400 hover:bg-white/5'
        }`}>Out</button>
      <div className="flex h-7 min-w-9 items-center justify-center rounded-md border border-white/10 bg-black/10 px-2 text-[6px] font-bold uppercase tracking-[0.08em] text-zinc-400">
        {deck.loopSize}
      </div>
      <button onClick={() => actions.cue(lbl)}
        className="flex h-7 min-w-10 items-center justify-center rounded-md border border-white/12 bg-white/[0.04] px-3 text-[7px] font-bold uppercase tracking-[0.08em] text-zinc-200 hover:bg-white/[0.08] transition-colors">Cue</button>
      {deck.isPlaying ? (
        <button onClick={() => actions.pause(lbl)}
          className="flex h-7 min-w-10 items-center justify-center rounded-md border border-red-500/40 bg-red-500/20 px-3 text-[7px] font-bold uppercase tracking-[0.08em] text-red-300 hover:bg-red-500/30 transition-colors">Stop</button>
      ) : (
        <button onClick={() => actions.play(lbl)}
          className={`flex h-7 min-w-10 items-center justify-center rounded-md px-3 text-[7px] font-bold uppercase tracking-[0.08em] transition-colors ${
            loaded ? `${a.bgDim} border ${a.border} ${a.text} hover:brightness-125` : 'bg-white/[0.03] border border-white/10 text-zinc-400'
          }`}>Play</button>
      )}
      <button onClick={() => actions.sync(lbl)}
        className={`flex h-7 min-w-10 items-center justify-center rounded-md px-3 text-[7px] font-bold uppercase tracking-[0.08em] transition-colors ${
          deck.syncEnabled ? `${a.bgDim} border ${a.border} ${a.text}` : 'border border-white/12 text-zinc-400 hover:bg-white/5'
        }`}>Sync</button>
      <button onClick={() => actions.loopClear(lbl)}
        className="flex h-7 min-w-8 items-center justify-center rounded-md border border-white/12 px-2 text-[6px] font-bold uppercase tracking-[0.08em] text-zinc-400 hover:bg-white/5 transition-colors">Clear</button>
      <button onClick={() => actions.setCue(lbl, deck.currentTime)}
        className="flex h-7 min-w-8 items-center justify-center rounded-md border border-white/12 px-2 text-[6px] font-bold uppercase tracking-[0.08em] text-zinc-300 hover:bg-white/5 transition-colors">Set</button>
    </div>
  );

  const compactTransport = (
    <div className="flex shrink-0 items-center justify-center gap-1 border-t border-white/8 bg-[#12151c] px-1 py-1">
      <button onClick={() => actions.cue(lbl)} className="flex h-6 min-w-8 items-center justify-center rounded-md border border-white/12 px-2 text-[6px] font-bold uppercase text-zinc-200 hover:bg-white/5">Cue</button>
      {deck.isPlaying ? (
        <button onClick={() => actions.pause(lbl)} className="flex h-6 min-w-8 items-center justify-center rounded-md border border-red-500/40 bg-red-500/20 px-2 text-[6px] font-bold uppercase text-red-300 hover:bg-red-500/30">Stop</button>
      ) : (
        <button onClick={() => actions.play(lbl)} className={`flex h-6 min-w-8 items-center justify-center rounded-md px-2 text-[6px] font-bold uppercase ${loaded ? `${a.bgDim} border ${a.border} ${a.text}` : 'border border-white/10 text-zinc-400'}`}>Play</button>
      )}
      <button onClick={() => actions.sync(lbl)} className={`flex h-6 min-w-8 items-center justify-center rounded-md px-2 text-[6px] font-bold uppercase ${deck.syncEnabled ? `${a.bgDim} border ${a.border} ${a.text}` : 'border border-white/12 text-zinc-400 hover:bg-white/5'}`}>Sync</button>
      <button onClick={() => actions.setCue(lbl, deck.currentTime)} className="flex h-6 min-w-8 items-center justify-center rounded-md border border-white/12 px-2 text-[6px] font-bold uppercase text-zinc-300 hover:bg-white/5">Set</button>
    </div>
  );

  /* ── Jog wheel callbacks ─────────────────────────────────────── */
  const handleJogToggle = () => {
    if (deck.isPlaying) actions.pause(lbl);
    else actions.play(lbl);
  };
  const handleJogScratch = (deltaPct: number) => {
    const newPct = Math.min(100, Math.max(0, deck.currentTime + deltaPct));
    actions.seek(lbl, newPct);
  };

  /* ── Body: compact lane | platter | side controls ───────────── */
  const body = side === 'left' ? (
    <div className="relative grid min-h-0 min-w-0 flex-1 grid-cols-[80px_minmax(112px,0.9fr)_168px_minmax(90px,0.72fr)] overflow-hidden bg-[#12161d]/38">
      {syncBlock}
      {padFxLane}
      <div className="flex min-w-0 items-start justify-center border-l border-r border-white/6 px-0 pt-3 pb-1">
        <JogWheel deck={deck} a={a} size={platterSize} onTogglePlay={handleJogToggle} onScratch={handleJogScratch} />
      </div>
      <div className="h-full min-w-0">
        {eqVolStrip}
      </div>
    </div>
  ) : (
    <div className="relative grid min-h-0 min-w-0 flex-1 grid-cols-[minmax(90px,0.72fr)_168px_minmax(112px,0.9fr)_80px] overflow-hidden bg-[#12161d]/38">
      <div className="h-full min-w-0">
        {eqVolStrip}
      </div>
      <div className="flex min-w-0 items-start justify-center border-l border-r border-white/6 px-0 pt-3 pb-1">
        <JogWheel deck={deck} a={a} size={platterSize} onTogglePlay={handleJogToggle} onScratch={handleJogScratch} />
      </div>
      {padFxLane}
      {syncBlock}
    </div>
  );

  const compactOuterPitch = (
    <div className="flex h-full flex-col items-center gap-1 border-r border-white/8 bg-[#1a1d24] px-1 py-1">
      <button onClick={() => actions.sync(lbl)} className={`h-6 w-full rounded-md text-[6px] font-black uppercase ${deck.syncEnabled ? `${a.bgDim} border ${a.border} ${a.text}` : 'border border-white/12 text-zinc-400'}`}>Sync</button>
      <span className={`text-[7px] font-mono font-black ${a.text}`}>{displayBpm > 0 ? `${displayBpm.toFixed(1)}` : '---.-'}</span>
      <span className="text-[6px] font-semibold uppercase tracking-[0.14em] text-zinc-500">BPM</span>
      <span className={`text-[8px] font-mono font-black ${a.text}`}>{deck.tempo >= 0 ? '+' : ''}{deck.tempo.toFixed(1)}%</span>
      <PitchFader
        value={deck.tempo}
        range={deck.pitchRange}
        accent={a.text}
        onChange={(v) => actions.setTempo(lbl, v)}
        onRangeChange={(v) => actions.setPitchRange(lbl, v)}
      />
    </div>
  );

  const compactSideControls = (
    <div className="flex h-full flex-col items-center gap-1 border-l border-white/8 bg-[#1a1d24] px-1 py-1">
      <EqKnob label="Hi" value={deck.eqHigh} color={a.fill} onChange={(v) => actions.setEq(lbl, 'high', v)} />
      <EqKnob label="Mid" value={deck.eqMid} color={a.fill} onChange={(v) => actions.setEq(lbl, 'mid', v)} />
      <EqKnob label="Lo" value={deck.eqLow} color={a.fill} onChange={(v) => actions.setEq(lbl, 'low', v)} />
      <EqKnob label="Flt" value={deck.filter} color={a.fill} onChange={(v) => actions.setFilter(lbl, v)} />
      <div className="flex min-h-0 w-full flex-col items-center gap-1 pt-1">
        <span className="text-[6px] font-semibold uppercase tracking-widest text-zinc-500">Vol</span>
        <VolumeFader value={deck.volume} accent={a.fill} onChange={(v) => actions.setVolume(lbl, v)} />
      </div>
    </div>
  );

  const compactBody = side === 'left' ? (
    <div className="relative grid min-h-0 min-w-0 flex-1 grid-cols-[52px_minmax(0,1fr)_56px] overflow-hidden bg-[#12161d]/38">
      {compactOuterPitch}
      <div className="flex min-w-0 items-start justify-center border-l border-r border-white/6 px-0 pt-2 pb-1">
        <JogWheel deck={deck} a={a} size="w-[98px]" onTogglePlay={handleJogToggle} onScratch={handleJogScratch} />
      </div>
      {compactSideControls}
    </div>
  ) : (
    <div className="relative grid min-h-0 min-w-0 flex-1 grid-cols-[56px_minmax(0,1fr)_52px] overflow-hidden bg-[#12161d]/38">
      {compactSideControls}
      <div className="flex min-w-0 items-start justify-center border-l border-r border-white/6 px-0 pt-2 pb-1">
        <JogWheel deck={deck} a={a} size="w-[98px]" onTogglePlay={handleJogToggle} onScratch={handleJogScratch} />
      </div>
      {compactOuterPitch}
    </div>
  );

  return (
    <section
      className={`flex min-h-0 min-w-0 flex-col overflow-hidden border-t border-white/6 bg-[#11141b]/52 ${dropOver ? 'ring-2 ring-inset ' + (side === 'left' ? 'ring-cyan-400/50' : 'ring-amber-400/50') : ''}`}
      onDragOver={(e) => { if (e.dataTransfer.types.includes('application/x-dj-track')) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; setDropOver(true); } }}
      onDragLeave={() => setDropOver(false)}
      onDrop={(e) => { e.preventDefault(); setDropOver(false); const path = e.dataTransfer.getData('application/x-dj-track'); if (path) actions.loadTrack(lbl, path); }}
    >
      {compact ? compactBody : body}
      {compact ? compactTransport : transport}
    </section>
  );
}
