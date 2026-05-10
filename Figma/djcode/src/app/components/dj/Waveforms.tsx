import { useEffect, useRef, useMemo, useCallback } from 'react';
import type { MouseEvent } from 'react';
import type { DeckLabel, DjDeckState } from '../../dj/types';

const clamp = (value: number, min: number, max: number) =>
  Math.min(max, Math.max(min, value));

const RENDER_BARS = 900;
const VISIBLE_BARS = 200;

/* ── Resample server bars to the desired resolution ──────────────── */
function resampleBars(source: number[], targetCount: number): number[] {
  if (source.length === 0) return [];
  if (targetCount <= 0) return [];
  if (source.length === targetCount) return source.slice();
  if (source.length === 1) return new Array(targetCount).fill(source[0]);
  const out: number[] = new Array(targetCount);
  for (let i = 0; i < targetCount; i++) {
    const srcPos = (i / Math.max(1, targetCount - 1)) * Math.max(1, source.length - 1);
    const lo = Math.floor(srcPos);
    const hi = Math.min(lo + 1, source.length - 1);
    const frac = srcPos - lo;
    out[i] = source[lo] * (1 - frac) + source[hi] * frac;
  }
  return out;
}

function getWaveformSeries(source: number[], fallback: number[], targetCount: number): number[] {
  if (source.length > 0) return resampleBars(source, targetCount);
  if (fallback.length > 0) return resampleBars(fallback, targetCount);
  return [];
}

/* ── Generate synthetic waveform when server bars aren't available ── */
function generateSyntheticBars(seed: string, bpm: number, count: number): number[] {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  const influence = bpm > 0 ? Math.min(0.22, bpm / 1000) : 0.08;
  const bars: number[] = new Array(count);
  for (let i = 0; i < count; i++) {
    const phase = ((hash + i * 37) % 360) * (Math.PI / 180);
    const accent = ((hash >> (i % 16)) & 31) / 31;
    bars[i] = Math.min(1, 0.28 + influence + Math.abs(Math.sin(phase)) * 0.4 + accent * 0.18);
  }
  return bars;
}

function waveformColor(v: number, played: boolean, deckColor: 'cyan' | 'amber'): string {
  const intensity = Math.max(0.18, Math.min(1, v * 0.85 + 0.2));
  if (deckColor === 'cyan') {
    return played
      ? `rgba(102, 178, 255, ${0.24 + intensity * 0.18})`
      : `rgba(156, 233, 255, ${0.62 + intensity * 0.28})`;
  }
  return played
    ? `rgba(255, 164, 92, ${0.24 + intensity * 0.18})`
    : `rgba(255, 214, 122, ${0.62 + intensity * 0.28})`;
}

/* ── Canvas-based scrolling waveform (playhead centred like djay) ── */
function WaveRow({
  deck,
  deckColor,
  onSeek,
}: {
  deck: DjDeckState;
  deckColor: 'cyan' | 'amber';
  onSeek: (deck: DeckLabel, value: number) => void;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const rafRef = useRef<number>(0);
  const currentTimeRef = useRef(deck.currentTime);
  const renderProgressRef = useRef(deck.currentTime);
  const isPlayingRef = useRef(deck.isPlaying);
  const tempoRef = useRef(deck.tempo);
  const durationRef = useRef(deck.track.durationSeconds);
  const lastServerTimeRef = useRef(deck.currentTime);
  const lastServerPerfRef = useRef(typeof performance !== 'undefined' ? performance.now() : 0);
  const lastRenderPerfRef = useRef(typeof performance !== 'undefined' ? performance.now() : 0);
  const loaded = Boolean(deck.track.path);
  const labelColor = deckColor === 'cyan' ? 'text-cyan-400' : 'text-amber-400';

  const getRenderProgress = useCallback(() => {
    const baseProgress = renderProgressRef.current;
    const durationSeconds = durationRef.current;
    const now = performance.now();

    if (!isPlayingRef.current || durationSeconds <= 0) {
      lastRenderPerfRef.current = now;
      return clamp(baseProgress, 0, 100);
    }

    const elapsedMs = Math.max(0, now - lastRenderPerfRef.current);
    const speed = clamp(1 + tempoRef.current / 100, 0.5, 1.5);
    const deltaPct = elapsedMs / (durationSeconds * 1000) * 100 * speed;
    const next = clamp(baseProgress + deltaPct, 0, 100);
    renderProgressRef.current = next;
    lastRenderPerfRef.current = now;
    return next;
  }, []);

  const waveformMin = useMemo(() => {
    if (!loaded) return [];
    return getWaveformSeries(deck.analysis?.waveformMin ?? [], [], RENDER_BARS);
  }, [loaded, deck.analysis?.waveformMin]);
  const waveformMax = useMemo(() => {
    if (!loaded) return [];
    return getWaveformSeries(deck.analysis?.waveformMax ?? [], deck.waveformBars, RENDER_BARS);
  }, [loaded, deck.analysis?.waveformMax, deck.waveformBars]);
  const waveformRms = useMemo(() => {
    if (!loaded) return [];
    if (deck.analysis?.waveformRms?.length) return resampleBars(deck.analysis.waveformRms, RENDER_BARS);
    if (deck.waveformBars.length > 0) return resampleBars(deck.waveformBars, RENDER_BARS);
    return generateSyntheticBars(deck.track.path || deck.track.title, deck.track.bpm, RENDER_BARS);
  }, [loaded, deck.analysis?.waveformRms, deck.waveformBars, deck.track.path, deck.track.title, deck.track.bpm]);

  // Stable draw function using refs — never recreated
  const drawWaveform = useCallback(() => {
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (!canvas || !container || waveformRms.length === 0) return;

    const rect = container.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const w = rect.width;
    const h = rect.height;
    if (w < 1 || h < 1) return;

    // Only resize canvas when dimensions actually change
    const targetW = Math.round(w * dpr);
    const targetH = Math.round(h * dpr);
    if (canvas.width !== targetW || canvas.height !== targetH) {
      canvas.width = targetW;
      canvas.height = targetH;
      canvas.style.width = `${w}px`;
      canvas.style.height = `${h}px`;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    const barCount = waveformRms.length;
    const barWidth = w / VISIBLE_BARS;
    const totalWaveW = barCount * barWidth;
    const midY = h / 2;
    const renderProgress = getRenderProgress();
    currentTimeRef.current = renderProgress;
    const playPos = renderProgress / 100;
    const centreX = w / 2;
    const playBarIndex = playPos * barCount;
    const offsetX = centreX - playBarIndex * barWidth;
    const gap = Math.max(0.5, barWidth * 0.12);

    // Draw centered waveform bars using min/max envelope and RMS thickness.
    const startBar = Math.max(0, Math.floor(-offsetX / barWidth));
    const endBar = Math.min(barCount, Math.ceil((w - offsetX) / barWidth));

    for (let i = startBar; i < endBar; i++) {
      const x = offsetX + i * barWidth;
      const v = waveformRms[i] ?? 0;
      const played = i < playBarIndex;
      const col = waveformColor(v, played, deckColor);
      const bw = Math.max(1, barWidth - gap);
      const maxSample = waveformMax[i] ?? v;
      const minSample = waveformMin[i] ?? -v;
      const upperY = midY - Math.max(0, maxSample) * midY * 0.95;
      const lowerY = midY - Math.min(0, minSample) * midY * 0.95;

      ctx.fillStyle = col;
      if (lowerY - upperY > 0.6) {
        ctx.fillRect(x, upperY, bw, lowerY - upperY);
      } else {
        ctx.fillRect(x, midY - 0.5, bw, 1);
      }

      const rmsHeight = Math.max(1, v * midY * 0.9);
      ctx.fillStyle = deckColor === 'cyan'
        ? (played ? 'rgba(210,245,255,0.12)' : 'rgba(235,250,255,0.22)')
        : (played ? 'rgba(255,240,215,0.12)' : 'rgba(255,244,204,0.22)');
      ctx.fillRect(x, midY - rmsHeight * 0.5, bw, rmsHeight);
    }

    // Centre line
    ctx.fillStyle = 'rgba(255,255,255,0.03)';
    ctx.fillRect(0, midY - 0.5, w, 1);

    // Beat grid
    if (deck.analysis?.beatMarkers?.length && deck.track.durationSeconds > 0) {
      const phraseSet = new Set((deck.analysis.phraseMarkers ?? []).map((marker) => marker.toFixed(3)));
      for (const marker of deck.analysis.beatMarkers) {
        const progress = marker / deck.track.durationSeconds;
        const bx = offsetX + progress * barCount * barWidth;
        if (bx < 0 || bx > w) continue;
        const isPhrase = phraseSet.has(marker.toFixed(3));
        ctx.fillStyle = isPhrase ? 'rgba(255,255,255,0.16)' : 'rgba(255,255,255,0.07)';
        ctx.fillRect(bx, 0, isPhrase ? 2 : 1, h);
      }
    }

    // Playhead — prominent glowing centre line
    const glowW = 24;
    const grad = ctx.createLinearGradient(centreX - glowW, 0, centreX + glowW, 0);
    grad.addColorStop(0, 'rgba(255,255,255,0)');
    grad.addColorStop(0.3, 'rgba(255,255,255,0.05)');
    grad.addColorStop(0.48, 'rgba(255,255,255,0.5)');
    grad.addColorStop(0.5, 'rgba(255,255,255,1)');
    grad.addColorStop(0.52, 'rgba(255,255,255,0.5)');
    grad.addColorStop(0.7, 'rgba(255,255,255,0.05)');
    grad.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = grad;
    ctx.fillRect(centreX - glowW, 0, glowW * 2, h);

    // Dim regions beyond track
    ctx.fillStyle = 'rgba(0,0,0,0.55)';
    const trackStartX = offsetX;
    const trackEndX = offsetX + totalWaveW;
    if (trackStartX > 0) ctx.fillRect(0, 0, trackStartX, h);
    if (trackEndX < w) ctx.fillRect(trackEndX, 0, w - trackEndX, h);
  }, [waveformRms, waveformMin, waveformMax, deck.analysis?.beatMarkers, deck.analysis?.phraseMarkers, deck.track.durationSeconds, deckColor, getRenderProgress]);

  useEffect(() => {
    const now = performance.now();
    const predicted = renderProgressRef.current;
    const incoming = deck.currentTime;
    const delta = incoming - predicted;

    currentTimeRef.current = deck.currentTime;
    isPlayingRef.current = deck.isPlaying;
    tempoRef.current = deck.tempo;
    durationRef.current = deck.track.durationSeconds;

    if (!deck.isPlaying || Math.abs(delta) > 1.4) {
      renderProgressRef.current = incoming;
    } else {
      renderProgressRef.current = clamp(predicted + delta * 0.22, 0, 100);
    }

    lastServerTimeRef.current = incoming;
    lastServerPerfRef.current = now;
    lastRenderPerfRef.current = now;
  }, [deck.currentTime, deck.isPlaying, deck.tempo, deck.track.durationSeconds]);

  useEffect(() => {
    if (!loaded || waveformRms.length === 0) return;

    drawWaveform();

    if (!deck.isPlaying) {
      return;
    }

    let running = true;
    const loop = () => {
      if (!running) return;
      drawWaveform();
      rafRef.current = requestAnimationFrame(loop);
    };
    rafRef.current = requestAnimationFrame(loop);
    return () => { running = false; cancelAnimationFrame(rafRef.current); };
  }, [loaded, waveformRms.length, drawWaveform, deck.isPlaying]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    const onResize = () => {
      drawWaveform();
    };

    if (typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(() => {
        onResize();
      });
      observer.observe(container);
      return () => observer.disconnect();
    }

    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [drawWaveform]);

  const handleSeek = (e: MouseEvent<HTMLDivElement>) => {
    if (!loaded) return;
    const container = containerRef.current;
    if (!container) return;
    const b = container.getBoundingClientRect();
    const clickX = e.clientX - b.left;
    const w = b.width;
    const centreX = w / 2;
    const barWidth = w / VISIBLE_BARS;
    const playPos = currentTimeRef.current / 100;
    const playBarIndex = playPos * waveformRms.length;
    const offsetX = centreX - playBarIndex * barWidth;
    const clickedBar = (clickX - offsetX) / barWidth;
    const pct = (clickedBar / waveformRms.length) * 100;
    onSeek(deck.label, Math.min(100, Math.max(0, pct)));
  };

  return (
    <div className="flex min-h-0 flex-1 items-center gap-1 px-1">
      {/* Deck label + key */}
      <div className="w-[26px] shrink-0 text-center">
        <div className={`text-[9px] font-bold ${labelColor}`}>{deck.label}</div>
        <div className="text-[7px] text-zinc-500">{deck.track.key || '—'}</div>
      </div>

      {/* Waveform canvas */}
      <div
        ref={containerRef}
        onClick={handleSeek}
        className={`relative h-full min-w-0 flex-1 overflow-hidden rounded-sm ${loaded ? 'bg-black/50 cursor-pointer' : 'bg-black/15 cursor-default'}`}
      >
        {loaded && waveformRms.length > 0 ? (
          <canvas ref={canvasRef} className="absolute inset-0" />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center text-[9px] uppercase tracking-wider text-zinc-600">
            {loaded ? 'Loading waveform…' : 'No track loaded'}
          </div>
        )}
      </div>

      {/* Time + BPM */}
      <div className="flex w-[70px] shrink-0 flex-col items-end text-[9px] font-mono tabular-nums">
        <span className="text-zinc-200">{loaded ? deck.elapsed || '00:00' : '00:00'}</span>
        <span className="text-zinc-500">{loaded ? deck.remaining || '-00:00' : '-00:00'}</span>
        {loaded && (deck.analysis?.bpm || deck.track.bpm) > 0 && <span className={`text-[8px] ${deckColor === 'cyan' ? 'text-cyan-400' : 'text-amber-400'}`}>{deck.effectiveBpm > 0 ? deck.effectiveBpm.toFixed(1) : (deck.analysis?.bpm ?? deck.track.bpm)} BPM</span>}
      </div>
    </div>
  );
}

/* ── Exported Waveforms strip ────────────────────────────────────── */

export function Waveforms({
  decks,
  onSeek,
  mini,
  deckLabels,
}: {
  decks: { A: DjDeckState; B: DjDeckState };
  onSeek: (deck: DeckLabel, value: number) => void;
  mini?: boolean;
  deckLabels?: [DeckLabel, DeckLabel];
}) {
  const [labelA, labelB] = deckLabels ?? ['A', 'B'];
  const deckA = deckLabels ? { ...decks.A, label: labelA } : decks.A;
  const deckB = deckLabels ? { ...decks.B, label: labelB } : decks.B;

  return (
    <section className={`flex flex-col gap-0 overflow-hidden border-b border-white/8 bg-[#060810] ${mini ? 'text-[8px]' : ''}`}>
      <WaveRow deck={deckA} deckColor="cyan" onSeek={onSeek} />
      <div className="h-px bg-white/6" />
      <WaveRow deck={deckB} deckColor="amber" onSeek={onSeek} />
    </section>
  );
}
