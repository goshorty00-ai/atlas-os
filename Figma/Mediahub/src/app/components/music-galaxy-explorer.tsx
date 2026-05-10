import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Sparkles, Maximize2, Minimize2, X, ChevronDown, Brain, Crosshair, Settings2, Check,
  Play, Pause, SkipBack, SkipForward, Volume2, ListMusic, Mic2, Activity, Layers,
} from 'lucide-react';
import { ImageWithFallback } from './figma/ImageWithFallback';

// ============================================================
// Types
// ============================================================
type ExplorerState = 'closed' | 'open' | 'fullscreen';
type Mode =
  | 'Holographic Reactor' | 'Spectrum Cathedral' | 'Waveform Engine' | 'Bass Core'
  | 'Neural Audio Map' | 'Lyrics Projection' | 'Album Coverflow Pulse' | 'Stage Light Grid';

const MODES: Array<{ name: Mode; desc: string }> = [
  { name: 'Holographic Reactor',    desc: 'Glass core with circular waveform rings' },
  { name: 'Spectrum Cathedral',     desc: '3D hall of translucent spectrum columns' },
  { name: 'Waveform Engine',        desc: 'Stereo turbines powered by waveform ribbons' },
  { name: 'Bass Core',              desc: 'Deep bass orb with subwoofer shockwaves' },
  { name: 'Neural Audio Map',       desc: 'AI signal pulses across artist & genre nodes' },
  { name: 'Lyrics Projection',      desc: 'Crisp HUD with vocal-frequency glow' },
  { name: 'Album Coverflow Pulse',  desc: 'Beat-pulsing related-album coverflow' },
  { name: 'Stage Light Grid',       desc: 'Concert lighting with laser sweeps' },
];

type AIView =
  | 'None' | 'Similar Vibe' | 'Mood Path' | 'Artist Network' | 'Genre Blend'
  | 'Listening History' | 'Recommended Next' | 'Karaoke Match';
const AI_VIEWS: AIView[] = ['None','Similar Vibe','Mood Path','Artist Network','Genre Blend','Listening History','Recommended Next','Karaoke Match'];

type Focus =
  | 'None' | 'Current Track' | 'Artist' | 'Album'
  | 'Genre' | 'Mood' | 'BPM' | 'Decade' | 'Playlist';
const FOCUSES: Focus[] = ['None','Current Track','Artist','Album','Genre','Mood','BPM','Decade','Playlist'];

const CONTEXT_OPTIONS = [
  'Play Now', 'Add to Queue', 'Add to Playlist', 'Show Similar Vibe',
  'AI Optimize Audio', 'Get Album Metadata', 'Fix Track Metadata',
  'AI Generate Cover', 'Edit Custom Cover', 'Replace Cover Image', 'Restore Original Cover',
  'Search Lyrics', 'Sync Lyrics', 'Convert to Karaoke', 'Analyse BPM / Key', 'Show File Location',
];

const LYRICS = [
  'I lost my mind in the neon lights',
  'Dancing through the static glow',
  'You were the signal in the noise',
  'A glitch inside the dream',
  'Wake me when the sunrise breaks',
  'Echoes of a synthwave night',
  'Carry me home through the wires',
];

const QUEUE = [
  { title: 'Blinding Lights', artist: 'The Weeknd', dur: '3:20' },
  { title: 'One More Time',   artist: 'Daft Punk',  dur: '5:20' },
  { title: 'Solar',           artist: 'Tame Impala', dur: '4:42' },
  { title: 'Midnight City',   artist: 'M83',        dur: '4:03' },
  { title: 'Strobe',          artist: 'Deadmau5',   dur: '7:00' },
];

const ARTIST_NODES = [
  { id: 'a1', name: 'The Weeknd',  x: 160, y: 120, c: '#22d3ee' },
  { id: 'a2', name: 'Daft Punk',   x: 360, y: 90,  c: '#a78bfa' },
  { id: 'a3', name: 'Tame Impala', x: 540, y: 160, c: '#f472b6' },
  { id: 'a4', name: 'M83',         x: 240, y: 250, c: '#fbbf24' },
  { id: 'a5', name: 'Deadmau5',    x: 460, y: 280, c: '#34d399' },
  { id: 'a6', name: 'Boards of Canada', x: 620, y: 230, c: '#60a5fa' },
];

const COVERS = [
  'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=400&q=80',
  'https://images.unsplash.com/photo-1470225620780-dba8ba36b745?w=400&q=80',
  'https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?w=400&q=80',
  'https://images.unsplash.com/photo-1459749411175-04bf5292ceea?w=400&q=80',
  'https://images.unsplash.com/photo-1514525253161-7a46d19cd819?w=400&q=80',
  'https://images.unsplash.com/photo-1487537708572-3c850b5e856e?w=400&q=80',
  'https://images.unsplash.com/photo-1485579149621-3123dd979885?w=400&q=80',
];

interface AudioFrame {
  bass: number; mid: number; treble: number; beat: number; kick: number;
  snare: number; vocal: number; energy: number; stereoL: number; stereoR: number;
  bpm: number; peak: number;
}

function frame(tick: number): AudioFrame {
  const bass = (Math.sin(tick * 0.09) * 0.5 + 0.5) * 0.6 + 0.3;
  const mid  = (Math.sin(tick * 0.21 + 1) * 0.5 + 0.5) * 0.7 + 0.15;
  const treble = Math.abs(Math.sin(tick * 0.42 + 2)) * 0.6 + Math.random() * 0.15;
  const kickPhase = (tick % 30) / 30;
  const kick = Math.max(0, 1 - kickPhase * 5);
  const snarePhase = ((tick + 15) % 30) / 30;
  const snare = Math.max(0, 1 - snarePhase * 6) * 0.85;
  const beat = Math.max(kick, snare * 0.7);
  const vocal = (Math.sin(tick * 0.18 + 3) * 0.5 + 0.5) * 0.7;
  const energy = (bass + mid + treble) / 3;
  const stereoL = bass * 0.6 + mid * 0.3 + Math.sin(tick * 0.3) * 0.1;
  const stereoR = bass * 0.55 + treble * 0.4 + Math.cos(tick * 0.3) * 0.1;
  return { bass, mid, treble, beat, kick, snare, vocal, energy, stereoL, stereoR, bpm: 124, peak: Math.min(1, energy + beat * 0.4) };
}

interface ContextState { x: number; y: number; label: string; }

// ============================================================
// Main component
// ============================================================
export function MusicGalaxyExplorer() {
  const [state, setState] = useState<ExplorerState>('closed');
  const [mode, setMode] = useState<Mode>('Holographic Reactor');
  const [aiView, setAiView] = useState<AIView>('None');
  const [focus, setFocus] = useState<Focus>('None');
  const [rotate, setRotate] = useState(true);
  const [zoom, setZoom] = useState(100);
  const [showLyrics, setShowLyrics] = useState(false);
  const [showQueue, setShowQueue] = useState(false);
  const [showMetrics, setShowMetrics] = useState(true);
  const [showHelp, setShowHelp] = useState(false);
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const [ctx, setCtx] = useState<ContextState | null>(null);
  const [tick, setTick] = useState(0);

  const a = useMemo(() => frame(tick), [tick]);

  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 40);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      if (ctx) return setCtx(null);
      if (openMenu) return setOpenMenu(null);
      if (state === 'fullscreen') return setState('open');
      if (state === 'open') return setState('closed');
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [state, openMenu, ctx]);

  const onContextMenu = (e: React.MouseEvent, label: string) => {
    e.preventDefault();
    setCtx({ x: e.clientX, y: e.clientY, label });
  };

  // -------- closed --------
  if (state === 'closed') {
    return (
      <div className="rounded-2xl bg-gradient-to-br from-slate-900/80 via-indigo-950/50 to-slate-900/80 border border-cyan-400/20 backdrop-blur-md p-4 flex items-center gap-4 ring-1 ring-cyan-500/10">
        <div className="relative w-32 h-20 rounded-xl overflow-hidden border border-cyan-400/30 bg-slate-950 flex-shrink-0">
          <MiniPreview a={a} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 text-cyan-300 text-xs">
            <Sparkles size={12} /> Holographic Audio Reactor
          </div>
          <div className="text-slate-100 mt-0.5 truncate">Premium AI music visualizer</div>
          <div className="text-slate-400 text-xs truncate">Sound-reactive holographic core, spectrum cathedral, neural audio map and more.</div>
        </div>
        <button
          onClick={() => setState('open')}
          className="px-3 py-1.5 rounded-lg bg-cyan-500/90 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30"
        >
          <Play size={12} fill="currentColor" /> Open Reactor
        </button>
        <button
          onClick={() => setState('fullscreen')}
          className="px-3 py-1.5 rounded-lg bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-200 text-xs flex items-center gap-1.5"
        >
          <Maximize2 size={12} /> Full Screen
        </button>
      </div>
    );
  }

  const isFs = state === 'fullscreen';
  const containerClass = isFs
    ? 'fixed inset-0 z-50 bg-[#04060d]'
    : 'relative rounded-2xl border border-cyan-400/20 bg-[#05070f] overflow-hidden';

  return (
    <div className={containerClass}>
      {/* premium ambient background */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          background: `
            radial-gradient(circle at 50% 50%, rgba(34,211,238,${0.08 + a.beat * 0.12}) 0%, transparent 55%),
            radial-gradient(circle at 20% 80%, rgba(167,139,250,0.08) 0%, transparent 50%),
            radial-gradient(circle at 80% 20%, rgba(244,114,182,0.06) 0%, transparent 50%)
          `,
        }}
      />
      {/* edge glow on kick */}
      <div className="absolute inset-0 pointer-events-none rounded-2xl" style={{ boxShadow: `inset 0 0 ${60 + a.kick * 80}px rgba(34,211,238,${0.1 + a.kick * 0.25})` }} />
      {/* scanline grid */}
      <div className="absolute inset-0 opacity-[0.06] pointer-events-none" style={{ backgroundImage: 'linear-gradient(rgba(34,211,238,0.6) 1px, transparent 1px), linear-gradient(90deg, rgba(34,211,238,0.6) 1px, transparent 1px)', backgroundSize: '40px 40px' }} />

      {/* top control bar */}
      <div className="relative z-30 flex items-center gap-2 px-4 py-3 border-b border-cyan-400/10 bg-slate-950/40 backdrop-blur-md">
        <div className="flex items-center gap-1.5 text-cyan-300 text-xs mr-2">
          <Sparkles size={12} /> Holographic Audio Reactor
        </div>
        <PillDropdown
          icon={<Layers size={11} />} label={mode} active={true}
          open={openMenu === 'mode'} onToggle={() => setOpenMenu(openMenu === 'mode' ? null : 'mode')}
        >
          {MODES.map((m) => (
            <MenuItem key={m.name} active={mode === m.name} onClick={() => { setMode(m.name); setOpenMenu(null); }}>
              <div>
                <div className="text-slate-100">{m.name}</div>
                <div className="text-slate-500 text-[10px]">{m.desc}</div>
              </div>
            </MenuItem>
          ))}
        </PillDropdown>

        <PillDropdown
          icon={<Brain size={11} />} label={aiView === 'None' ? 'AI Layer' : `AI: ${aiView}`} active={aiView !== 'None'}
          open={openMenu === 'ai'} onToggle={() => setOpenMenu(openMenu === 'ai' ? null : 'ai')}
        >
          {AI_VIEWS.map((v) => (
            <MenuItem key={v} active={aiView === v} onClick={() => { setAiView(v); setOpenMenu(null); }}>{v}</MenuItem>
          ))}
        </PillDropdown>

        <PillDropdown
          icon={<Crosshair size={11} />} label={focus === 'None' ? 'Focus' : `Focus: ${focus}`} active={focus !== 'None'}
          open={openMenu === 'focus'} onToggle={() => setOpenMenu(openMenu === 'focus' ? null : 'focus')}
        >
          {FOCUSES.map((f) => (
            <MenuItem key={f} active={focus === f} onClick={() => { setFocus(f); setOpenMenu(null); }}>{f}</MenuItem>
          ))}
          {focus !== 'None' && (
            <MenuItem onClick={() => { setFocus('None'); setOpenMenu(null); }}>
              <span className="text-rose-300">Clear Focus</span>
            </MenuItem>
          )}
        </PillDropdown>

        <PillDropdown
          icon={<Settings2 size={11} />} label="Options"
          open={openMenu === 'opts'} onToggle={() => setOpenMenu(openMenu === 'opts' ? null : 'opts')}
        >
          <MenuToggle on={rotate} onClick={() => setRotate(!rotate)}>Rotate Visuals</MenuToggle>
          <MenuItem onClick={() => setZoom(Math.min(160, zoom + 10))}>Zoom In  <span className="ml-auto text-cyan-300">{zoom}%</span></MenuItem>
          <MenuItem onClick={() => setZoom(Math.max(60, zoom - 10))}>Zoom Out</MenuItem>
          <MenuItem onClick={() => setZoom(100)}>Reset View</MenuItem>
          <Divider />
          <MenuToggle on={showMetrics} onClick={() => setShowMetrics(!showMetrics)}>Show Metrics</MenuToggle>
          <MenuToggle on={showLyrics} onClick={() => setShowLyrics(!showLyrics)}>Show Lyrics</MenuToggle>
          <MenuToggle on={showQueue} onClick={() => setShowQueue(!showQueue)}>Show Queue</MenuToggle>
          <MenuToggle on={showHelp} onClick={() => setShowHelp(!showHelp)}>Show Right-Click Help</MenuToggle>
          <Divider />
          {state === 'fullscreen'
            ? <MenuItem onClick={() => { setState('open'); setOpenMenu(null); }}>Exit Full Screen</MenuItem>
            : <MenuItem onClick={() => { setState('fullscreen'); setOpenMenu(null); }}>Full Screen</MenuItem>}
          <MenuItem onClick={() => { setState('closed'); setOpenMenu(null); }}>
            <span className="text-rose-300">Close Explorer</span>
          </MenuItem>
        </PillDropdown>

        <div className="flex-1" />

        {/* live chips */}
        {aiView !== 'None' && <Chip color="violet">{aiView}</Chip>}
        {focus !== 'None' && <Chip color="cyan">Focus · {focus}</Chip>}
        {zoom !== 100 && <Chip color="slate">{zoom}%</Chip>}
        {!rotate && <Chip color="slate">Rotate Off</Chip>}

        <button onClick={() => setState(isFs ? 'open' : 'fullscreen')}
          className="px-2.5 py-1 rounded-md bg-slate-800/80 hover:bg-slate-700 border border-slate-600/60 text-slate-200 text-[11px] flex items-center gap-1">
          {isFs ? <><Minimize2 size={11} /> Exit</> : <><Maximize2 size={11} /> Full Screen</>}
        </button>
        <button onClick={() => setState('closed')}
          className="px-2.5 py-1 rounded-md bg-rose-500/15 hover:bg-rose-500/25 border border-rose-400/30 text-rose-200 text-[11px] flex items-center gap-1">
          <X size={11} /> Close
        </button>
      </div>

      {/* visualizer stage */}
      <div
        className="relative"
        style={{ height: isFs ? 'calc(100vh - 56px - 72px)' : 520 }}
        onContextMenu={(e) => onContextMenu(e, 'Visualizer')}
      >
        <div className="absolute inset-0" style={{ transform: `scale(${zoom / 100})`, transformOrigin: 'center', transition: 'transform 200ms' }}>
          <ModeStage mode={mode} a={a} rotate={rotate} tick={tick} focus={focus} />
        </div>

        <AIOverlay aiView={aiView} a={a} />

        {showMetrics && <MetricsHUD a={a} />}
        {showLyrics  && <LyricsPanel a={a} tick={tick} />}
        {showQueue   && <QueuePanel onContextMenu={onContextMenu} />}
        {showHelp    && <HelpPanel />}
      </div>

      {/* compact now-playing strip (always visible at bottom) */}
      <NowPlaying a={a} />

      {/* context menu */}
      {ctx && (
        <>
          <div className="fixed inset-0 z-50" onClick={() => setCtx(null)} />
          <div
            className="fixed z-50 w-56 rounded-xl border border-cyan-400/20 bg-slate-950/95 backdrop-blur-xl shadow-2xl py-1 ring-1 ring-cyan-500/10"
            style={{ left: Math.min(ctx.x, window.innerWidth - 240), top: Math.min(ctx.y, window.innerHeight - 480) }}
          >
            <div className="px-3 py-1.5 text-[10px] text-cyan-300 border-b border-slate-700/50 truncate">{ctx.label}</div>
            {CONTEXT_OPTIONS.map((o) => (
              <button key={o} onClick={() => setCtx(null)}
                className="w-full text-left px-3 py-1.5 text-[11px] text-slate-200 hover:bg-cyan-500/10 hover:text-cyan-200">
                {o}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
}

export default MusicGalaxyExplorer;

// ============================================================
// Stage router
// ============================================================
function ModeStage({ mode, a, rotate, tick, focus }: { mode: Mode; a: AudioFrame; rotate: boolean; tick: number; focus: Focus }) {
  switch (mode) {
    case 'Holographic Reactor':   return <HolographicReactor a={a} rotate={rotate} tick={tick} />;
    case 'Spectrum Cathedral':    return <SpectrumCathedral a={a} tick={tick} />;
    case 'Waveform Engine':       return <WaveformEngine a={a} tick={tick} />;
    case 'Bass Core':             return <BassCore a={a} tick={tick} />;
    case 'Neural Audio Map':      return <NeuralAudioMap a={a} tick={tick} focus={focus} />;
    case 'Lyrics Projection':     return <LyricsProjection a={a} tick={tick} />;
    case 'Album Coverflow Pulse': return <AlbumCoverflowPulse a={a} tick={tick} />;
    case 'Stage Light Grid':      return <StageLightGrid a={a} tick={tick} />;
  }
}

// ============================================================
// 1. Holographic Reactor — glass core + circular waveform rings
// ============================================================
function HolographicReactor({ a, rotate, tick }: { a: AudioFrame; rotate: boolean; tick: number }) {
  const cx = 400, cy = 260;
  const ringCount = 5;
  const wavePoints = 96;

  const wavePath = (radius: number, amp: number, phase: number) => {
    const pts: string[] = [];
    for (let i = 0; i <= wavePoints; i++) {
      const t = (i / wavePoints) * Math.PI * 2;
      const r = radius + Math.sin(t * 6 + phase) * amp * 8 + Math.sin(t * 13 + phase * 1.7) * amp * 3;
      const x = cx + Math.cos(t) * r;
      const y = cy + Math.sin(t) * r;
      pts.push(`${i === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`);
    }
    return pts.join(' ') + ' Z';
  };

  const baseRot = rotate ? tick * 0.6 : 0;

  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <radialGradient id="reactor-core" cx="50%" cy="50%">
          <stop offset="0%" stopColor="#22d3ee" stopOpacity="0.9" />
          <stop offset="50%" stopColor="#0891b2" stopOpacity="0.4" />
          <stop offset="100%" stopColor="#0c4a6e" stopOpacity="0" />
        </radialGradient>
        <linearGradient id="ring-grad" x1="0" x2="1">
          <stop offset="0%" stopColor="#22d3ee" />
          <stop offset="50%" stopColor="#a78bfa" />
          <stop offset="100%" stopColor="#f472b6" />
        </linearGradient>
        <filter id="reactor-glow">
          <feGaussianBlur stdDeviation="3" />
        </filter>
      </defs>

      {/* outer shockwave on kick */}
      {a.kick > 0.1 && (
        <circle cx={cx} cy={cy} r={140 + (1 - a.kick) * 200} fill="none" stroke="#22d3ee" strokeWidth={2} opacity={a.kick * 0.6} />
      )}

      {/* rotating glass frequency panels */}
      {Array.from({ length: 8 }).map((_, i) => {
        const ang = (i / 8) * 360 + baseRot;
        const r1 = 200, r2 = 230 + a.mid * 25;
        const w = 18;
        const rad = (ang * Math.PI) / 180;
        const x1 = cx + Math.cos(rad - w / 200) * r1;
        const y1 = cy + Math.sin(rad - w / 200) * r1;
        const x2 = cx + Math.cos(rad + w / 200) * r1;
        const y2 = cy + Math.sin(rad + w / 200) * r1;
        const x3 = cx + Math.cos(rad + w / 200) * r2;
        const y3 = cy + Math.sin(rad + w / 200) * r2;
        const x4 = cx + Math.cos(rad - w / 200) * r2;
        const y4 = cy + Math.sin(rad - w / 200) * r2;
        return (
          <polygon key={i}
            points={`${x1},${y1} ${x2},${y2} ${x3},${y3} ${x4},${y4}`}
            fill="rgba(34,211,238,0.12)" stroke="rgba(34,211,238,0.6)" strokeWidth="0.7" />
        );
      })}

      {/* concentric waveform rings */}
      {Array.from({ length: ringCount }).map((_, i) => {
        const radius = 70 + i * 24 + a.bass * 14;
        const amp = 0.4 + i * 0.2 + a.treble * 1.2;
        const opacity = 0.85 - i * 0.13;
        return (
          <path key={i} d={wavePath(radius, amp, tick * 0.05 + i * 0.7)}
            fill="none" stroke="url(#ring-grad)" strokeWidth={1.4 - i * 0.15} opacity={opacity} filter="url(#reactor-glow)" />
        );
      })}

      {/* spectrum arcs */}
      {Array.from({ length: 64 }).map((_, i) => {
        const ang = (i / 64) * Math.PI * 2;
        const band = i / 64;
        const v = band < 0.33 ? a.bass : band < 0.66 ? a.mid : a.treble;
        const r1 = 175;
        const r2 = r1 + 18 + v * 50;
        const x1 = cx + Math.cos(ang) * r1;
        const y1 = cy + Math.sin(ang) * r1;
        const x2 = cx + Math.cos(ang) * r2;
        const y2 = cy + Math.sin(ang) * r2;
        const c = band < 0.33 ? '#f472b6' : band < 0.66 ? '#a78bfa' : '#22d3ee';
        return <line key={i} x1={x1} y1={y1} x2={x2} y2={y2} stroke={c} strokeWidth="1.6" opacity="0.85" />;
      })}

      {/* bass shockwave concentric */}
      {[0, 1, 2].map((i) => (
        <circle key={i} cx={cx} cy={cy} r={50 + i * 12 + a.bass * 14} fill="none"
          stroke="rgba(34,211,238,0.35)" strokeWidth="0.8" />
      ))}

      {/* glass core */}
      <circle cx={cx} cy={cy} r={50 + a.beat * 6} fill="url(#reactor-core)" />
      <circle cx={cx} cy={cy} r={50 + a.beat * 6} fill="none" stroke="rgba(34,211,238,0.85)" strokeWidth="1.2" />
      {/* hologram album disc */}
      <foreignObject x={cx - 42} y={cy - 42} width={84} height={84}>
        <div className="w-full h-full rounded-full overflow-hidden ring-2 ring-cyan-400/60"
          style={{ boxShadow: `0 0 ${20 + a.beat * 30}px rgba(34,211,238,0.6)`, transform: `rotate(${baseRot}deg)` }}>
          <ImageWithFallback src={COVERS[0]} alt="cover" className="w-full h-full object-cover" />
        </div>
      </foreignObject>
      {/* core highlight */}
      <ellipse cx={cx - 12} cy={cy - 16} rx="14" ry="6" fill="rgba(255,255,255,0.5)" opacity="0.5" />
    </svg>
  );
}

// ============================================================
// 2. Spectrum Cathedral — translucent glass columns w/ depth
// ============================================================
function SpectrumCathedral({ a, tick }: { a: AudioFrame; tick: number }) {
  const cols = 24;
  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <linearGradient id="cath-floor" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="rgba(34,211,238,0.15)" />
          <stop offset="100%" stopColor="rgba(15,23,42,0.95)" />
        </linearGradient>
        <linearGradient id="cath-col" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="rgba(244,114,182,0.85)" />
          <stop offset="50%" stopColor="rgba(167,139,250,0.65)" />
          <stop offset="100%" stopColor="rgba(34,211,238,0.5)" />
        </linearGradient>
      </defs>

      {/* horizon */}
      <rect x="0" y="0" width="800" height="300" fill="rgba(34,211,238,0.04)" />
      <line x1="0" y1="300" x2="800" y2="300" stroke="rgba(34,211,238,0.4)" strokeWidth="0.6" />
      {/* reflective floor grid */}
      {Array.from({ length: 14 }).map((_, i) => {
        const y = 300 + i * i * 1.2;
        return <line key={`h${i}`} x1="0" y1={y} x2="800" y2={y} stroke="rgba(34,211,238,0.15)" strokeWidth="0.4" />;
      })}
      {Array.from({ length: 16 }).map((_, i) => {
        const x = (i / 15) * 800;
        return <line key={`v${i}`} x1={x} y1="300" x2={400 + (x - 400) * 6} y2="520" stroke="rgba(34,211,238,0.18)" strokeWidth="0.4" />;
      })}
      <rect x="0" y="300" width="800" height="220" fill="url(#cath-floor)" opacity="0.4" />

      {/* light beams from ceiling */}
      {[0.2, 0.45, 0.55, 0.8].map((p, i) => (
        <polygon key={i} points={`${p * 800 - 8},0 ${p * 800 + 8},0 ${p * 800 + 60},300 ${p * 800 - 60},300`}
          fill="rgba(34,211,238,0.05)" />
      ))}

      {/* columns - back to front */}
      {Array.from({ length: cols }).map((_, i) => {
        const t = i / cols;
        const band = Math.abs(t - 0.5) * 2; // 0 centre, 1 edges
        const v = band < 0.3 ? a.bass : band < 0.7 ? a.mid : a.treble;
        const h = 60 + v * 220 + a.beat * 30;
        const baseY = 300;
        const xCenter = (t - 0.5) * 800;
        // rows of depth: alternate
        const depth = i % 2 === 0 ? 1 : 0.7;
        const w = 16 * depth;
        const x = 400 + xCenter * depth;
        const y = baseY - h * depth;
        const colColor = band < 0.3 ? '#f472b6' : band < 0.7 ? '#a78bfa' : '#22d3ee';
        return (
          <g key={i} opacity={0.5 + depth * 0.5}>
            {/* reflection */}
            <rect x={x - w / 2} y={baseY} width={w} height={h * depth * 0.5} fill={colColor} opacity="0.15" />
            <rect x={x - w / 2} y={y} width={w} height={h * depth} fill="url(#cath-col)" opacity="0.85"
              style={{ filter: a.beat > 0.5 ? `drop-shadow(0 0 8px ${colColor})` : 'none' }} />
            <rect x={x - w / 2} y={y} width={w} height={4} fill="white" opacity={0.4 + a.beat * 0.4} />
            {/* treble spark at top */}
            {a.treble > 0.7 && band > 0.6 && (
              <circle cx={x} cy={y - 4} r="2" fill="#22d3ee" />
            )}
          </g>
        );
      })}

      {/* foreground depth blur via gradient */}
      <rect x="0" y="450" width="800" height="70" fill="rgba(5,7,15,0.6)" />
    </svg>
  );
}

// ============================================================
// 3. Waveform Engine — turbines + ribbons + pistons
// ============================================================
function WaveformEngine({ a, tick }: { a: AudioFrame; tick: number }) {
  const ribbon = (yMid: number, amp: number, phase: number, color: string) => {
    const pts: string[] = [];
    for (let i = 0; i <= 80; i++) {
      const x = (i / 80) * 800;
      const y = yMid + Math.sin(i * 0.18 + phase) * amp * 30 + Math.sin(i * 0.06 + phase * 2) * amp * 12;
      pts.push(`${i === 0 ? 'M' : 'L'} ${x} ${y}`);
    }
    return <path d={pts.join(' ')} fill="none" stroke={color} strokeWidth="2" opacity="0.85" filter="url(#engine-glow)" />;
  };

  const turbine = (cx: number, cy: number, blades = 8, base = 60) => {
    const rot = tick * (1 + a.bpm / 120) * 1.5;
    return (
      <g transform={`translate(${cx} ${cy}) rotate(${rot})`}>
        <circle r={base + a.bass * 10} fill="rgba(15,23,42,0.7)" stroke="rgba(34,211,238,0.5)" strokeWidth="1" />
        {Array.from({ length: blades }).map((_, i) => {
          const ang = (i / blades) * 360;
          return (
            <g key={i} transform={`rotate(${ang})`}>
              <path d={`M 0 0 L ${base * 0.9} -6 L ${base * 0.95} 0 L ${base * 0.9} 6 Z`}
                fill="rgba(34,211,238,0.5)" stroke="rgba(34,211,238,0.9)" strokeWidth="0.5" />
            </g>
          );
        })}
        <circle r="14" fill="#22d3ee" opacity={0.6 + a.beat * 0.4} />
        <circle r="6" fill="white" opacity="0.8" />
      </g>
    );
  };

  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <filter id="engine-glow"><feGaussianBlur stdDeviation="2" /></filter>
        <linearGradient id="lane-l" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="rgba(34,211,238,0.15)" />
          <stop offset="100%" stopColor="rgba(34,211,238,0)" />
        </linearGradient>
      </defs>

      {/* stereo lanes */}
      <rect x="0" y="120" width="800" height="120" fill="url(#lane-l)" />
      <rect x="0" y="280" width="800" height="120" fill="url(#lane-l)" />
      <text x="20" y="115" fill="#22d3ee" fontSize="9" opacity="0.7">L · {(a.stereoL * 100).toFixed(0)}%</text>
      <text x="20" y="275" fill="#a78bfa" fontSize="9" opacity="0.7">R · {(a.stereoR * 100).toFixed(0)}%</text>

      {/* waveform ribbons through the engine */}
      {ribbon(180, 1 + a.mid * 1.5, tick * 0.1, '#22d3ee')}
      {ribbon(200, 0.8 + a.treble, tick * 0.13 + 1, '#a78bfa')}
      {ribbon(340, 1 + a.mid * 1.5, tick * 0.1 + 2, '#f472b6')}
      {ribbon(360, 0.8 + a.treble, tick * 0.13 + 3, '#22d3ee')}

      {/* turbines */}
      {turbine(180, 200, 8, 60)}
      {turbine(620, 200, 8, 60)}
      {turbine(180, 360, 8, 60)}
      {turbine(620, 360, 8, 60)}

      {/* pistons */}
      {[0, 1, 2, 3, 4].map((i) => {
        const x = 320 + i * 40;
        const top = 250 + Math.sin(tick * 0.3 + i) * 12 - a.bass * 18;
        return (
          <g key={i}>
            <rect x={x - 8} y={top} width={16} height={60} fill="rgba(34,211,238,0.3)" stroke="#22d3ee" strokeWidth="0.6" />
            <rect x={x - 12} y={top - 6} width={24} height={6} fill="#22d3ee" opacity={0.7 + a.beat * 0.3} />
          </g>
        );
      })}

      {/* BPM tick markers */}
      {Array.from({ length: 16 }).map((_, i) => {
        const x = i * 50 + 25;
        const active = ((tick / 8) | 0) % 16 === i;
        return (
          <g key={i}>
            <line x1={x} y1="450" x2={x} y2="470" stroke={active ? '#22d3ee' : '#475569'} strokeWidth={active ? 2 : 1} />
            {active && <circle cx={x} cy="460" r="3" fill="#22d3ee" />}
          </g>
        );
      })}
      <text x="400" y="495" textAnchor="middle" fill="#94a3b8" fontSize="9">BPM {a.bpm}</text>
    </svg>
  );
}

// ============================================================
// 4. Bass Core — heavy bass orb + shockwaves
// ============================================================
function BassCore({ a, tick }: { a: AudioFrame; tick: number }) {
  const cx = 400, cy = 260;
  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <radialGradient id="bass-orb">
          <stop offset="0%" stopColor="#f472b6" stopOpacity="1" />
          <stop offset="40%" stopColor="#a855f7" stopOpacity="0.8" />
          <stop offset="100%" stopColor="#1e1b4b" stopOpacity="0" />
        </radialGradient>
        <filter id="bass-blur"><feGaussianBlur stdDeviation="6" /></filter>
      </defs>

      {/* low-frequency pressure rings */}
      {[0, 1, 2, 3, 4, 5].map((i) => {
        const phase = ((tick + i * 12) % 80) / 80;
        const r = 100 + phase * 280;
        const op = (1 - phase) * 0.7;
        return <circle key={i} cx={cx} cy={cy} r={r} fill="none" stroke="#f472b6" strokeWidth={2} opacity={op * a.bass} />;
      })}

      {/* low frequency horizontal bars */}
      {Array.from({ length: 18 }).map((_, i) => {
        const y = 30 + i * 28;
        const w = 80 + a.bass * 280 * (1 - Math.abs(i - 9) / 9);
        return (
          <g key={i}>
            <rect x={cx - w / 2} y={y - 3} width={w} height={6} fill="rgba(244,114,182,0.4)" />
            <rect x={cx - w / 2} y={y - 3} width={w} height={2} fill="#f472b6" opacity={0.6 + a.beat * 0.4} />
          </g>
        );
      })}

      {/* central bass orb */}
      <circle cx={cx} cy={cy} r={120 + a.bass * 30 + a.kick * 20} fill="url(#bass-orb)" filter="url(#bass-blur)" />
      <circle cx={cx} cy={cy} r={70 + a.kick * 18} fill="rgba(244,114,182,0.85)" />
      <circle cx={cx} cy={cy} r={70 + a.kick * 18} fill="none" stroke="white" strokeWidth="1.2" opacity="0.7" />
      <ellipse cx={cx - 18} cy={cy - 22} rx="20" ry="10" fill="rgba(255,255,255,0.5)" />

      {/* energy meter arc */}
      <path d={`M 80 460 A 320 320 0 0 1 720 460`} fill="none" stroke="#1e293b" strokeWidth="6" />
      <path d={`M 80 460 A 320 320 0 0 1 ${80 + a.energy * 640} ${460 - Math.sin(a.energy * Math.PI) * 80}`}
        fill="none" stroke="#f472b6" strokeWidth="4" strokeLinecap="round" />
      <text x={cx} y="495" textAnchor="middle" fill="#f9a8d4" fontSize="10">ENERGY · {(a.energy * 100).toFixed(0)}%</text>
    </svg>
  );
}

// ============================================================
// 5. Neural Audio Map — structured AI graph
// ============================================================
function NeuralAudioMap({ a, tick, focus }: { a: AudioFrame; tick: number; focus: Focus }) {
  const edges: Array<[string, string]> = [
    ['a1','a2'],['a1','a4'],['a2','a3'],['a2','a4'],['a3','a6'],
    ['a4','a5'],['a5','a6'],['a3','a5'],['a1','a5'],
  ];
  const nodeMap = Object.fromEntries(ARTIST_NODES.map(n => [n.id, n]));

  // genre lanes (vertical bands)
  const lanes = [
    { x: 100, label: 'Synthwave',  c: '#22d3ee' },
    { x: 320, label: 'Electronic', c: '#a78bfa' },
    { x: 540, label: 'Dream Pop',  c: '#f472b6' },
    { x: 700, label: 'Ambient',    c: '#34d399' },
  ];

  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <filter id="neural-glow"><feGaussianBlur stdDeviation="2.5" /></filter>
      </defs>

      {/* genre lanes */}
      {lanes.map((l) => (
        <g key={l.label}>
          <rect x={l.x - 70} y={40} width={140} height={400} fill={l.c} opacity="0.04" rx="20" />
          <text x={l.x} y={32} textAnchor="middle" fill={l.c} fontSize="10" opacity="0.85">{l.label}</text>
        </g>
      ))}

      {/* edges */}
      {edges.map(([a1, b1], i) => {
        const n1 = nodeMap[a1], n2 = nodeMap[b1];
        const t = ((tick + i * 7) % 60) / 60;
        const px = n1.x + (n2.x - n1.x) * t;
        const py = n1.y + (n2.y - n1.y) * t;
        return (
          <g key={i}>
            <line x1={n1.x} y1={n1.y} x2={n2.x} y2={n2.y} stroke="rgba(34,211,238,0.25)" strokeWidth="1" />
            {/* signal pulse */}
            <circle cx={px} cy={py} r={3 + a.beat * 2} fill="#22d3ee" filter="url(#neural-glow)" />
          </g>
        );
      })}

      {/* nodes (artists) */}
      {ARTIST_NODES.map((n) => {
        const dim = focus !== 'None' && focus !== 'Artist' ? 0.35 : 1;
        return (
          <g key={n.id} opacity={dim}>
            <circle cx={n.x} cy={n.y} r={20 + a.mid * 6} fill={n.c} opacity="0.2" filter="url(#neural-glow)" />
            <circle cx={n.x} cy={n.y} r={12} fill="rgba(15,23,42,0.95)" stroke={n.c} strokeWidth="1.5" />
            <circle cx={n.x} cy={n.y} r={4 + a.beat * 2} fill={n.c} />
            <text x={n.x} y={n.y + 30} textAnchor="middle" fill="#cbd5e1" fontSize="9">{n.name}</text>
          </g>
        );
      })}

      {/* AI route highlight */}
      <path d="M 160 120 Q 280 50 360 90 Q 440 130 540 160 Q 580 230 460 280" fill="none" stroke="#22d3ee" strokeWidth="1.2" strokeDasharray="4 4" opacity="0.7" />
      <text x="700" y="500" textAnchor="end" fill="#22d3ee" fontSize="9" opacity="0.7">AI route · 92% confidence</text>
    </svg>
  );
}

// ============================================================
// 6. Lyrics Projection — modern HUD
// ============================================================
function LyricsProjection({ a, tick }: { a: AudioFrame; tick: number }) {
  const idx = Math.floor(tick / 60) % LYRICS.length;
  const current = LYRICS[idx];
  const next = LYRICS[(idx + 1) % LYRICS.length];

  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center px-12 select-none">
      {/* vocal frequency glow */}
      <div className="absolute" style={{
        width: 600, height: 200, borderRadius: '50%',
        background: `radial-gradient(circle, rgba(34,211,238,${0.18 + a.vocal * 0.25}) 0%, transparent 70%)`,
        filter: 'blur(20px)',
      }} />

      {/* HUD frame */}
      <div className="absolute top-8 left-8 right-8 flex items-center justify-between text-cyan-300/80 text-[10px]">
        <span>VOCAL · {(a.vocal * 100).toFixed(0)}%</span>
        <span>BAR {((tick / 60) | 0) + 1}</span>
        <span>LYRICS · LIVE</span>
      </div>

      <div className="relative z-10 text-center">
        <div className="text-cyan-100" style={{ fontSize: 38, letterSpacing: '0.02em', textShadow: `0 0 ${10 + a.vocal * 30}px rgba(34,211,238,0.7)` }}>
          {current.split(' ').map((w, i) => (
            <span key={i} style={{
              display: 'inline-block', margin: '0 6px',
              transform: `translateY(${Math.sin(tick * 0.2 + i) * a.beat * 3}px)`,
              opacity: 0.85 + (i === Math.floor(tick / 12) % current.split(' ').length ? 0.15 : 0),
            }}>{w}</span>
          ))}
        </div>
        <div className="mt-6 text-slate-400" style={{ fontSize: 18, opacity: 0.5 }}>{next}</div>
      </div>

      {/* corner brackets */}
      {[
        { t: 0, l: 0, b: '', r: '', rot: 0 },
        { t: 0, l: '', b: '', r: 0, rot: 90 },
        { t: '', l: 0, b: 0, r: '', rot: -90 },
        { t: '', l: '', b: 0, r: 0, rot: 180 },
      ].map((c, i) => (
        <div key={i} className="absolute w-8 h-8 border-cyan-400/50" style={{
          top: c.t as any, left: c.l as any, bottom: c.b as any, right: c.r as any,
          margin: 24, borderTop: '2px solid', borderLeft: '2px solid', transform: `rotate(${c.rot}deg)`,
        }} />
      ))}
    </div>
  );
}

// ============================================================
// 7. Album Coverflow Pulse
// ============================================================
function AlbumCoverflowPulse({ a, tick }: { a: AudioFrame; tick: number }) {
  const idx = Math.floor(tick / 200) % COVERS.length;
  const offsets = [-3, -2, -1, 0, 1, 2, 3];
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center" style={{ perspective: 1400 }}>
      {/* ambient backdrop */}
      <div className="absolute inset-0 opacity-30" style={{
        background: `radial-gradient(circle at 50% 40%, rgba(34,211,238,${0.3 + a.beat * 0.2}) 0%, transparent 60%)`,
        filter: 'blur(40px)',
      }} />
      <div className="relative h-[300px] w-full" style={{ transformStyle: 'preserve-3d' }}>
        {offsets.map((o) => {
          const i = (idx + o + COVERS.length * 3) % COVERS.length;
          const isCenter = o === 0;
          const rotateY = o * -28;
          const tx = o * 130;
          const scale = isCenter ? 1 + a.beat * 0.04 : 0.78 - Math.abs(o) * 0.06;
          const opacity = isCenter ? 1 : 0.7 - Math.abs(o) * 0.12;
          const blur = isCenter ? 0 : Math.abs(o) * 1.2;
          return (
            <div key={o}
              className="absolute left-1/2 top-1/2 rounded-xl overflow-hidden"
              style={{
                width: 220, height: 220, marginLeft: -110, marginTop: -110,
                transform: `translateX(${tx}px) rotateY(${rotateY}deg) scale(${scale})`,
                filter: `blur(${blur}px) brightness(${isCenter ? 1 : 0.7})`,
                opacity, transition: 'transform 220ms',
                boxShadow: isCenter ? `0 0 ${30 + a.beat * 40}px rgba(34,211,238,0.6)` : '0 10px 30px rgba(0,0,0,0.5)',
                outline: isCenter ? '2px solid rgba(34,211,238,0.7)' : 'none',
              }}>
              <ImageWithFallback src={COVERS[i]} alt="" className="w-full h-full object-cover" />
            </div>
          );
        })}
      </div>

      {/* waveform ribbon under */}
      <svg viewBox="0 0 800 80" className="w-full h-20 mt-2">
        <path
          d={Array.from({ length: 81 }).map((_, i) => {
            const x = i * 10;
            const y = 40 + Math.sin(i * 0.18 + tick * 0.1) * (10 + a.mid * 16) + Math.sin(i * 0.07 + tick * 0.05) * (4 + a.bass * 10);
            return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
          }).join(' ')}
          fill="none" stroke="#22d3ee" strokeWidth="2" opacity="0.85" />
      </svg>

      <div className="flex items-center gap-2 mt-2">
        <button className="px-3 py-1 rounded-full bg-cyan-500/90 text-slate-950 text-xs flex items-center gap-1"><Play size={11} fill="currentColor" /> Play</button>
        <button className="px-3 py-1 rounded-full bg-slate-800/80 border border-slate-600/60 text-slate-200 text-xs">Queue</button>
        <button className="px-3 py-1 rounded-full bg-slate-800/80 border border-slate-600/60 text-slate-200 text-xs">Lyrics</button>
      </div>
    </div>
  );
}

// ============================================================
// 8. Stage Light Grid — concert lighting
// ============================================================
function StageLightGrid({ a, tick }: { a: AudioFrame; tick: number }) {
  const lights = 12;
  return (
    <svg viewBox="0 0 800 520" className="w-full h-full">
      <defs>
        <linearGradient id="beam" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="rgba(34,211,238,0.6)" />
          <stop offset="100%" stopColor="rgba(34,211,238,0)" />
        </linearGradient>
        <linearGradient id="beam2" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="rgba(244,114,182,0.6)" />
          <stop offset="100%" stopColor="rgba(244,114,182,0)" />
        </linearGradient>
      </defs>

      {/* stage strobe flash on snare */}
      {a.snare > 0.6 && <rect x="0" y="0" width="800" height="520" fill="white" opacity={(a.snare - 0.6) * 0.6} />}

      {/* upper truss */}
      <rect x="40" y="40" width="720" height="14" fill="#1e293b" stroke="rgba(34,211,238,0.4)" strokeWidth="0.6" rx="2" />
      {Array.from({ length: lights }).map((_, i) => {
        const x = 60 + i * (700 / lights);
        const sweep = Math.sin(tick * 0.08 + i * 0.7) * 70;
        const beamColor = i % 3 === 0 ? 'url(#beam2)' : 'url(#beam)';
        const isOn = a.beat > 0.3 || (i + (tick / 6 | 0)) % 3 === 0;
        return (
          <g key={i}>
            <circle cx={x} cy={50} r={6} fill={isOn ? '#22d3ee' : '#0f172a'} stroke="rgba(34,211,238,0.5)" strokeWidth="0.6" />
            {isOn && (
              <polygon points={`${x - 6},54 ${x + 6},54 ${x + 6 + sweep + 80},520 ${x - 6 + sweep - 80},520`}
                fill={beamColor} opacity={0.6 + a.beat * 0.3} />
            )}
          </g>
        );
      })}

      {/* lower stage panels */}
      {Array.from({ length: 8 }).map((_, i) => {
        const w = 90, gap = 4;
        const x = 30 + i * (w + gap);
        const v = i < 3 ? a.bass : i < 5 ? a.mid : a.treble;
        const h = 12 + v * 60 + a.beat * 14;
        const c = i < 3 ? '#f472b6' : i < 5 ? '#a78bfa' : '#22d3ee';
        return (
          <g key={i}>
            <rect x={x} y={460 - h} width={w} height={h} fill={c} opacity="0.7" />
            <rect x={x} y={460 - h} width={w} height={3} fill="white" opacity={0.4 + a.beat * 0.4} />
          </g>
        );
      })}

      {/* laser sweeps */}
      {Array.from({ length: 6 }).map((_, i) => {
        const ang = (tick * (i % 2 === 0 ? 1 : -1) * 0.04 + i * 60) % 360;
        const rad = (ang * Math.PI) / 180;
        const x2 = 400 + Math.cos(rad) * 600;
        const y2 = 460 + Math.sin(rad) * 600;
        return <line key={i} x1="400" y1="460" x2={x2} y2={y2} stroke={i % 2 === 0 ? '#22d3ee' : '#f472b6'} strokeWidth="1" opacity={0.4 + a.beat * 0.4} />;
      })}

      {/* crowd energy meter */}
      <text x="20" y="500" fill="#22d3ee" fontSize="9" opacity="0.7">CROWD ENERGY</text>
      <rect x="120" y="492" width="200" height="6" fill="#1e293b" rx="3" />
      <rect x="120" y="492" width={a.energy * 200} height="6" fill="#22d3ee" rx="3" />
    </svg>
  );
}

// ============================================================
// AI Overlay
// ============================================================
function AIOverlay({ aiView, a }: { aiView: AIView; a: AudioFrame }) {
  if (aiView === 'None') return null;
  const data: Record<Exclude<AIView, 'None'>, string[]> = {
    'Similar Vibe':       ['18 tracks found', '4 matching artists', '92% vibe match'],
    'Mood Path':          ['Chill → Synthwave → Cinematic', '6 mood links', '38 min path'],
    'Artist Network':     ['12 connected artists', '3 collaborator clusters', 'Centre · The Weeknd'],
    'Genre Blend':        ['Synthwave 48%', 'Dream Pop 32%', 'Electronic 20%'],
    'Listening History':  ['142 plays this month', 'Peak hour · 23:00', 'Top mood · Late Night'],
    'Recommended Next':   ['Daft Punk · Solar Sailer', 'M83 · Outro', 'Tame Impala · Borderline'],
    'Karaoke Match':      ['Vocal range matched', 'Key · F minor', '14 karaoke tracks ready'],
  };
  const lines = data[aiView as Exclude<AIView, 'None'>];
  return (
    <div className="absolute top-4 right-4 w-60 rounded-xl border border-violet-400/30 bg-slate-950/80 backdrop-blur-md p-3 shadow-xl ring-1 ring-violet-500/20 z-20">
      <div className="flex items-center gap-1.5 text-violet-300 text-[10px] mb-2">
        <Brain size={11} /> AI Layer · {aiView}
      </div>
      {lines.map((l, i) => (
        <div key={i} className="text-slate-200 text-xs py-0.5 flex items-center gap-2">
          <span className="w-1 h-1 rounded-full bg-violet-400" /> {l}
        </div>
      ))}
      <div className="mt-2 pt-2 border-t border-slate-700/50 flex items-center justify-between text-[10px] text-slate-400">
        <span>Confidence</span>
        <span className="text-violet-300">{(70 + a.energy * 25).toFixed(0)}%</span>
      </div>
    </div>
  );
}

// ============================================================
// HUDs and panels
// ============================================================
function MetricsHUD({ a }: { a: AudioFrame }) {
  const items: Array<[string, string, string]> = [
    ['BASS',   `${(a.bass * 100).toFixed(0)}%`,  '#f472b6'],
    ['MID',    `${(a.mid * 100).toFixed(0)}%`,   '#a78bfa'],
    ['TREBLE', `${(a.treble * 100).toFixed(0)}%`, '#22d3ee'],
    ['BPM',    `${a.bpm}`,                        '#34d399'],
    ['KEY',    'F minor',                         '#fbbf24'],
    ['ENERGY', `${(a.energy * 100).toFixed(0)}%`, '#22d3ee'],
    ['STEREO', `${((a.stereoL + a.stereoR) / 2 * 100).toFixed(0)}%`, '#a78bfa'],
    ['PEAK',   `${(a.peak * 100).toFixed(0)}%`,   '#f472b6'],
  ];
  return (
    <div className="absolute top-4 left-4 grid grid-cols-2 gap-1.5 z-10">
      {items.map(([k, v, c]) => (
        <div key={k} className="rounded-md border border-slate-700/40 bg-slate-950/60 backdrop-blur px-2 py-1 min-w-[88px]">
          <div className="text-[9px] tracking-wider" style={{ color: c, opacity: 0.85 }}>{k}</div>
          <div className="text-slate-100 text-xs">{v}</div>
        </div>
      ))}
      <div className="col-span-2 flex items-center gap-1.5 text-[9px] text-slate-400">
        <span className={`w-1.5 h-1.5 rounded-full ${a.beat > 0.3 ? 'bg-cyan-400' : 'bg-slate-600'}`} /> Beat
        <span className={`w-1.5 h-1.5 rounded-full ${a.vocal > 0.5 ? 'bg-violet-400' : 'bg-slate-600'}`} /> Vocal
      </div>
    </div>
  );
}

function LyricsPanel({ a, tick }: { a: AudioFrame; tick: number }) {
  const idx = Math.floor(tick / 60) % LYRICS.length;
  return (
    <div className="absolute bottom-4 left-4 w-80 rounded-xl border border-cyan-400/20 bg-slate-950/80 backdrop-blur-md p-3 z-10">
      <div className="flex items-center gap-1.5 text-cyan-300 text-[10px] mb-1.5"><Mic2 size={11} /> Lyrics</div>
      {LYRICS.slice(idx, idx + 3).map((l, i) => (
        <div key={i} className={i === 0 ? 'text-slate-100 text-sm' : 'text-slate-500 text-xs mt-1'}
          style={i === 0 ? { textShadow: `0 0 ${8 + a.vocal * 14}px rgba(34,211,238,0.6)` } : {}}>
          {l}
        </div>
      ))}
    </div>
  );
}

function QueuePanel({ onContextMenu }: { onContextMenu: (e: React.MouseEvent, label: string) => void }) {
  return (
    <div className="absolute top-4 right-4 w-64 rounded-xl border border-slate-700/40 bg-slate-950/85 backdrop-blur-md p-2 z-10">
      <div className="flex items-center gap-1.5 text-slate-300 text-[10px] mb-1.5 px-1"><ListMusic size={11} /> Queue</div>
      {QUEUE.map((q, i) => (
        <div key={i}
          onContextMenu={(e) => onContextMenu(e, `${q.title} — ${q.artist}`)}
          className="flex items-center justify-between px-2 py-1 rounded hover:bg-cyan-500/10 cursor-pointer text-xs">
          <div className="min-w-0">
            <div className="text-slate-100 truncate">{q.title}</div>
            <div className="text-slate-500 text-[10px] truncate">{q.artist}</div>
          </div>
          <div className="text-slate-500 text-[10px] ml-2">{q.dur}</div>
        </div>
      ))}
    </div>
  );
}

function HelpPanel() {
  const items = [
    'ESC · exit full screen / close',
    'Right-click · open AI menu',
    'Scroll · zoom',
    'Drag · rotate visuals',
    'Click node · play track',
  ];
  return (
    <div className="absolute bottom-4 right-4 w-56 rounded-xl border border-slate-700/40 bg-slate-950/85 backdrop-blur-md p-2.5 z-10">
      <div className="text-slate-300 text-[10px] mb-1.5 flex items-center gap-1.5"><Activity size={10} /> Shortcuts</div>
      {items.map((h) => <div key={h} className="text-slate-400 text-[10px] py-0.5">{h}</div>)}
    </div>
  );
}

function NowPlaying({ a }: { a: AudioFrame }) {
  const [playing, setPlaying] = useState(true);
  return (
    <div className="relative z-20 flex items-center gap-3 px-4 py-2.5 border-t border-cyan-400/10 bg-slate-950/70 backdrop-blur-md">
      <div className="w-10 h-10 rounded-md overflow-hidden ring-1 ring-cyan-400/40 flex-shrink-0">
        <ImageWithFallback src={COVERS[0]} alt="" className="w-full h-full object-cover" />
      </div>
      <div className="min-w-0 max-w-[160px]">
        <div className="text-slate-100 text-xs truncate">Blinding Lights</div>
        <div className="text-slate-500 text-[10px] truncate">The Weeknd · After Hours</div>
      </div>
      <div className="flex items-center gap-1">
        <button className="p-1 rounded hover:bg-slate-800 text-slate-300"><SkipBack size={14} /></button>
        <button onClick={() => setPlaying(!playing)} className="p-1.5 rounded-full bg-cyan-500 text-slate-950 hover:bg-cyan-400">
          {playing ? <Pause size={12} fill="currentColor" /> : <Play size={12} fill="currentColor" />}
        </button>
        <button className="p-1 rounded hover:bg-slate-800 text-slate-300"><SkipForward size={14} /></button>
      </div>
      {/* mini reactive timeline */}
      <div className="flex-1 flex items-center gap-2 min-w-0">
        <span className="text-[10px] text-slate-500">1:24</span>
        <div className="flex-1 flex items-center gap-[2px] h-6">
          {Array.from({ length: 60 }).map((_, i) => {
            const seed = Math.sin(i * 0.6) * 0.5 + 0.5;
            const live = (i / 60 < 0.4) ? 1 : 0.4;
            const v = (i / 60 < 0.4 ? a.energy * 0.5 + seed * 0.5 : seed) * live;
            return <div key={i} className="flex-1 rounded-sm" style={{
              height: 4 + v * 18,
              background: i / 60 < 0.4 ? '#22d3ee' : '#334155',
              opacity: i / 60 < 0.4 ? 0.8 + a.beat * 0.2 : 1,
            }} />;
          })}
        </div>
        <span className="text-[10px] text-slate-500">3:20</span>
      </div>
      <button className="p-1 rounded hover:bg-slate-800 text-slate-300"><Mic2 size={13} /></button>
      <button className="p-1 rounded hover:bg-slate-800 text-slate-300"><ListMusic size={13} /></button>
      <button className="p-1 rounded hover:bg-slate-800 text-slate-300"><Volume2 size={13} /></button>
    </div>
  );
}

// ============================================================
// Mini preview for closed state
// ============================================================
function MiniPreview({ a }: { a: AudioFrame }) {
  return (
    <svg viewBox="0 0 128 80" className="w-full h-full">
      <defs>
        <radialGradient id="mini-glow"><stop offset="0%" stopColor="#22d3ee" stopOpacity="0.7" /><stop offset="100%" stopColor="#22d3ee" stopOpacity="0" /></radialGradient>
      </defs>
      <rect width="128" height="80" fill="#05070f" />
      <circle cx="64" cy="40" r={20 + a.bass * 6} fill="url(#mini-glow)" />
      <circle cx="64" cy="40" r={10 + a.beat * 3} fill="#22d3ee" opacity="0.9" />
      {Array.from({ length: 18 }).map((_, i) => {
        const ang = (i / 18) * Math.PI * 2;
        const r1 = 22, r2 = r1 + 4 + (i % 3 === 0 ? a.bass : i % 3 === 1 ? a.mid : a.treble) * 12;
        return <line key={i} x1={64 + Math.cos(ang) * r1} y1={40 + Math.sin(ang) * r1}
          x2={64 + Math.cos(ang) * r2} y2={40 + Math.sin(ang) * r2}
          stroke="#22d3ee" strokeWidth="1" opacity="0.8" />;
      })}
    </svg>
  );
}

// ============================================================
// UI primitives
// ============================================================
function PillDropdown({ icon, label, active, open, onToggle, children }: {
  icon: React.ReactNode; label: string; active?: boolean; open: boolean; onToggle: () => void; children: React.ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  return (
    <div ref={ref} className="relative">
      <button onClick={onToggle}
        className={`px-2.5 py-1 rounded-full text-[11px] flex items-center gap-1.5 border transition-colors ${
          active
            ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40'
            : 'bg-slate-900/70 text-slate-200 border-slate-700/60 hover:bg-slate-800'
        }`}>
        {icon} {label} <ChevronDown size={10} className={`transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div className="absolute top-full left-0 mt-1 w-64 rounded-xl border border-cyan-400/20 bg-slate-950/95 backdrop-blur-xl py-1 shadow-2xl z-40 ring-1 ring-cyan-500/10">
          {children}
        </div>
      )}
    </div>
  );
}

function MenuItem({ children, onClick, active }: { children: React.ReactNode; onClick?: () => void; active?: boolean }) {
  return (
    <button onClick={onClick}
      className={`w-full text-left px-3 py-1.5 text-[11px] flex items-center gap-2 ${
        active ? 'bg-cyan-500/15 text-cyan-200' : 'text-slate-200 hover:bg-cyan-500/10 hover:text-cyan-100'
      }`}>
      {active && <Check size={10} className="text-cyan-300" />}
      <span className={active ? '' : 'ml-[18px]'}>{children}</span>
    </button>
  );
}

function MenuToggle({ on, onClick, children }: { on: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick}
      className="w-full text-left px-3 py-1.5 text-[11px] text-slate-200 hover:bg-cyan-500/10 hover:text-cyan-100 flex items-center justify-between">
      <span>{children}</span>
      <span className={`w-7 h-3.5 rounded-full relative transition-colors ${on ? 'bg-cyan-500' : 'bg-slate-700'}`}>
        <span className={`absolute top-0.5 w-2.5 h-2.5 rounded-full bg-white transition-all ${on ? 'left-3.5' : 'left-0.5'}`} />
      </span>
    </button>
  );
}

function Divider() { return <div className="my-1 mx-2 border-t border-slate-700/50" />; }

function Chip({ children, color }: { children: React.ReactNode; color: 'cyan' | 'violet' | 'slate' }) {
  const cls = color === 'cyan'
    ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/40'
    : color === 'violet'
    ? 'bg-violet-500/15 text-violet-200 border-violet-400/40'
    : 'bg-slate-800/80 text-slate-300 border-slate-600/60';
  return <span className={`px-2 py-0.5 rounded-full text-[10px] border ${cls}`}>{children}</span>;
}
