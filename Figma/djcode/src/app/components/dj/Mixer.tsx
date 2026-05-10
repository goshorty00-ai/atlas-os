import { useEffect, useRef, useState } from 'react';
import type { DeckLabel, DjActions, DjDeckState, DjMixerState } from '../../dj/types';

/* ── Crossfader ──────────────────────────────────────────────────── */
function Crossfader({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  const rail = useRef<HTMLDivElement | null>(null);
  const [dm, setDm] = useState<{ mode: 'permanent' | 'temporary'; prev: number } | null>(null);

  useEffect(() => {
    if (!dm) return;
    const move = (e: PointerEvent) => {
      const r = rail.current; if (!r) return;
      const b = r.getBoundingClientRect();
      onChange(Math.max(0, Math.min(100, ((e.clientX - b.left) / Math.max(1, b.width)) * 100)));
    };
    const up = () => { if (dm.mode === 'temporary') onChange(dm.prev); setDm(null); };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, { once: true });
    return () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
  }, [dm, onChange]);

  // Clamp thumb so it stays within the rail (thumb is 20px wide)
  const thumbPct = Math.max(0, Math.min(100, value));

  return (
    <div onContextMenu={(e) => e.preventDefault()}>
      <div className="mb-1 flex items-center justify-between px-0.5 text-[6px] uppercase tracking-wider text-zinc-500">
        <span className="text-cyan-400 font-bold">A</span><span>Crossfader</span><span className="text-amber-400 font-bold">B</span>
      </div>
      <div ref={rail} className="relative h-8 w-full cursor-ew-resize select-none rounded-md bg-zinc-950/70" style={{ touchAction: 'none' }}
        onPointerDown={(e) => {
          if (e.button !== 0 && e.button !== 2) return;
          e.preventDefault(); const r = rail.current; if (!r) return;
          const b = r.getBoundingClientRect();
          setDm({ mode: e.button === 2 ? 'temporary' : 'permanent', prev: value });
          onChange(Math.max(0, Math.min(100, ((e.clientX - b.left) / Math.max(1, b.width)) * 100)));
        }}>
        <div className="absolute inset-x-3 top-1/2 h-px -translate-y-1/2 bg-zinc-700" />
        <div className="absolute inset-y-1 left-1/2 w-px -translate-x-1/2 bg-zinc-700" />
        <div className="absolute top-1/2 h-6 w-6 -translate-x-1/2 -translate-y-1/2 rounded border border-zinc-400 bg-gradient-to-b from-zinc-200 to-zinc-500 shadow-sm" style={{ left: `${thumbPct}%` }}>
          <div className="absolute inset-0 flex items-center justify-center gap-[1px]">
            <div className="h-3 w-[1px] bg-zinc-700/80" />
            <div className="h-3 w-[1px] bg-zinc-700/80" />
          </div>
        </div>
      </div>
    </div>
  );
}

/* ════════════════════════════════════════════════════════════════════ */
/*  MIXER                                                              */
/* ════════════════════════════════════════════════════════════════════ */

export function Mixer({ decks, mixer, actions, isRecording }: {
  decks: Record<DeckLabel, DjDeckState>;
  mixer: DjMixerState;
  actions: Pick<DjActions,
    'setGain' | 'setEq' | 'setFilter' | 'setVolume' | 'toggleCueMonitor' |
    'setCrossfader' | 'setCrossfaderCurve' | 'setMasterVolume' | 'setCueMix' |
    'setHeadphoneVolume' | 'setMicVolume' | 'setEffectMode' | 'startRecording' | 'stopRecording'>;
  isRecording?: boolean;
}) {
  return (
    <section className="relative flex w-full min-h-0 flex-col items-center justify-center overflow-hidden border-l border-r border-white/8 bg-[#161a20]/42 px-2 py-1 backdrop-blur-[1px]">
      <div className="relative z-10 flex w-full flex-col items-center gap-3">
        <div className="flex items-center justify-between w-full text-[6px] font-bold uppercase tracking-[0.2em] text-zinc-600">
          <span>A</span>
          <span>Blend</span>
          <span>B</span>
        </div>
        <div className="flex w-full items-center justify-center gap-2 text-[8px] font-black uppercase tracking-[0.14em]">
          <button
            onClick={() => actions.setCrossfader(0)}
            className="flex h-8 w-8 items-center justify-center rounded-full border border-white/10 text-cyan-300 hover:bg-white/5"
          >A</button>
          <div className="min-w-0 flex-1">
            <Crossfader value={mixer.crossfader} onChange={actions.setCrossfader} />
          </div>
          <button
            onClick={() => actions.setCrossfader(100)}
            className="flex h-8 w-8 items-center justify-center rounded-full border border-white/10 text-amber-300 hover:bg-white/5"
          >B</button>
        </div>
        <button
          onClick={() => actions.setCrossfader(50)}
          className="rounded-full border border-white/12 px-4 py-1 text-[6px] font-bold uppercase tracking-[0.18em] text-zinc-300 hover:bg-white/5"
        >Center</button>
        <div className="flex w-full items-center justify-center gap-2 text-[6px] font-bold uppercase tracking-[0.18em] text-zinc-500">
          <div className="h-px flex-1 bg-white/10" />
          <span>Crossfader</span>
          <div className="h-px flex-1 bg-white/10" />
        </div>
        <button
          onClick={() => isRecording ? actions.stopRecording() : actions.startRecording()}
          className={`flex h-6 items-center gap-1 rounded-full px-3 text-[6px] font-bold uppercase tracking-wider transition-colors ${
            isRecording
              ? 'border border-red-500/50 bg-red-500/20 text-red-300 animate-pulse'
              : 'border border-white/15 bg-white/5 text-zinc-400 hover:bg-white/10 hover:text-zinc-200'
          }`}
        >
          <div className={`h-2 w-2 rounded-full ${
            isRecording ? 'bg-red-500' : 'bg-red-500/40'
          }`} />
          {isRecording ? 'Stop Rec' : 'Record'}
        </button>
      </div>
    </section>
  );
}
