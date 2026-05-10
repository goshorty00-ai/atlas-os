import { useEffect, useRef, useState } from 'react';
import { Mic } from 'lucide-react';
import { recommendDeckForTrack } from '../../dj/selectors';
import type {
  DeckLabel,
  DjActions,
  DjBrowserSection,
  DjBrowserState,
  DjConsoleMode,
  DjDeckState,
  DjLibraryTrack,
  DjSample,
} from '../../dj/types';

/* ── Sidebar sections ────────────────────────────────────────────── */
const sections: Array<{ key: DjBrowserSection; label: string; icon: string }> = [
  { key: 'all', label: 'All Tracks', icon: '♫' },
  { key: 'favorites', label: 'Favorites', icon: '★' },
  { key: 'recent', label: 'Recent', icon: '◷' },
  { key: 'playlists', label: 'Playlists', icon: '☰' },
  { key: 'crates', label: 'Crates', icon: '▤' },
  { key: 'local-files', label: 'Local Files', icon: '📁' },
];

/* ── Streaming services ──────────────────────────────────────────── */
const streamingServices = [
  { id: 'spotify', label: 'Spotify', color: 'bg-green-500', textColor: 'text-green-400', url: 'https://accounts.spotify.com/en/login' },
  { id: 'apple-music', label: 'Apple Music', color: 'bg-pink-500', textColor: 'text-pink-400', url: 'https://music.apple.com/' },
  { id: 'soundcloud', label: 'SoundCloud', color: 'bg-orange-500', textColor: 'text-orange-400', url: 'https://soundcloud.com/signin' },
  { id: 'tidal', label: 'Tidal', color: 'bg-blue-400', textColor: 'text-blue-400', url: 'https://login.tidal.com/' },
] as const;

/* ── Props ────────────────────────────────────────────────────────── */
interface BrowserProps {
  browser: DjBrowserState;
  decks: Record<DeckLabel, DjDeckState>;
  tracks: DjLibraryTrack[];
  sources: string[];
  samples?: DjSample[];
  onAddFolder?: () => void;
  onAddFiles?: () => void;
  onAddSamplesFolder?: () => void;
  onPlaySample?: (path: string) => void;
  consoleMode?: DjConsoleMode;
  actions: Pick<
    DjActions,
    | 'loadTrack'
    | 'setBrowserSearch'
    | 'setBrowserSection'
    | 'setBrowserSource'
    | 'cycleBrowserFilter'
    | 'cycleBrowserSort'
  >;
}

/* ══════════════════════════════════════════════════════════════════ */
/*  Browser                                                          */
/* ══════════════════════════════════════════════════════════════════ */

export function Browser({ browser, decks, tracks, sources, samples, actions, onAddFolder, onAddFiles, onAddSamplesFolder, onPlaySample, consoleMode }: BrowserProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
    const [micNote, setMicNote] = useState('');
    const micTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

    const handleMicClick = () => {
      // IsMicWired("DJ") = false — mic is not wired; never auto-loads or plays a track.
      setMicNote('Mic not wired');
      if (micTimer.current) clearTimeout(micTimer.current);
      micTimer.current = setTimeout(() => setMicNote(''), 2400);
    };
  const [connectedServices, setConnectedServices] = useState<Set<string>>(() => {
    try {
      const raw = window.localStorage.getItem('atlas.dj.connectedServices.v1');
      if (!raw) return new Set();
      const parsed = JSON.parse(raw) as string[];
      return new Set(parsed);
    } catch {
      return new Set();
    }
  });
  const [showAddMenu, setShowAddMenu] = useState(false);
  const [signingIn, setSigningIn] = useState<string | null>(null);
  const [openingService, setOpeningService] = useState(false);

  useEffect(() => {
    if (!signingIn) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setSigningIn(null);
        setOpeningService(false);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [signingIn]);

  useEffect(() => {
    try {
      window.localStorage.setItem('atlas.dj.connectedServices.v1', JSON.stringify(Array.from(connectedServices)));
    } catch {
      /* ignore persistence failures */
    }
  }, [connectedServices]);

  useEffect(() => {
    if (tracks.length === 0) { setSelectedId(null); return; }
    if (!selectedId || !tracks.some((t) => t.id === selectedId)) {
      setSelectedId(tracks[0].id);
    }
  }, [selectedId, tracks]);

  const selected = tracks.find((t) => t.id === selectedId) ?? null;
  const rec = selected ? recommendDeckForTrack(selected, decks) : null;
  const connectedServiceLabels = streamingServices
    .filter((service) => connectedServices.has(service.id))
    .map((service) => service.label);
  const visibleSources = Array.from(new Set(['All Sources', ...sources.filter(Boolean), ...connectedServiceLabels]));

  const toggleService = (id: string) => {
    if (connectedServices.has(id)) {
      setConnectedServices((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    } else {
      setSigningIn(id);
      setOpeningService(false);
    }
  };

  const handleSignIn = () => {
    if (!signingIn) return;
    const currentService = signingIn;
    const currentServiceLabel = signingInService?.label ?? currentService;
    setConnectedServices((prev) => {
      const next = new Set(prev);
      next.add(currentService);
      return next;
    });
    try {
      (window as any).chrome?.webview?.postMessage(JSON.stringify({ type: 'dj.streaming.connect', service: currentService }));
    } catch { /* no webview */ }
    actions.setBrowserSource(currentServiceLabel);
    setSigningIn(null);
    setOpeningService(false);
  };

  const handleOpenService = () => {
    if (!signingInService) return;
    setOpeningService(true);
    try {
      window.open(signingInService.url, '_blank', 'noopener,noreferrer');
    } catch {
      /* ignore popup failures */
    }
  };

  const signingInService = signingIn ? streamingServices.find((s) => s.id === signingIn) : null;

  return (
    <section className="relative flex min-h-0 overflow-hidden border-t border-white/8 bg-[#090b0f]">
      {/* ── Left sidebar ── */}
      <aside className="flex w-[176px] shrink-0 flex-col border-r border-white/8 overflow-y-auto">
        {/* My Files header + add button */}
        <div className="flex items-center justify-between px-2 pt-2 pb-0.5">
          <span className="text-[9px] font-semibold text-zinc-300">My Files</span>
          <div className="relative">
            <button onClick={() => setShowAddMenu((v) => !v)} className="flex h-5 w-5 items-center justify-center rounded border border-white/10 text-[11px] text-zinc-400 hover:bg-white/[0.05] hover:text-white" title="Add music">+</button>
            {showAddMenu && (
              <div className="absolute left-0 top-full z-50 mt-1 w-32 border border-white/10 bg-[#0c0e13] shadow-xl">
                <button onClick={() => { onAddFolder?.(); setShowAddMenu(false); }}
                  className="flex w-full items-center gap-1.5 px-2 py-1.5 text-left text-[9px] text-zinc-300 hover:bg-white/[0.05] hover:text-white">
                  <span>📁</span> Add Folder
                </button>
                <button onClick={() => { onAddFiles?.(); setShowAddMenu(false); }}
                  className="flex w-full items-center gap-1.5 px-2 py-1.5 text-left text-[9px] text-zinc-300 hover:bg-white/[0.05] hover:text-white">
                  <span>🎵</span> Add Files
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Section list */}
        <div className="space-y-0 px-1 pb-1">
          {sections.map(({ key, label, icon }) => (
            <button
              key={key}
              onClick={() => actions.setBrowserSection(key)}
              className={`flex h-6 w-full items-center gap-1.5 rounded-sm px-2 text-left text-[10px] ${
                browser.activeSection === key
                  ? 'bg-cyan-400/10 text-white'
                  : 'text-zinc-400 hover:bg-white/[0.03] hover:text-white'
              }`}
            >
              <span className="text-[10px] w-4 text-center">{icon}</span>
              {label}
            </button>
          ))}
        </div>

        {/* Streaming services */}
        <div className="border-t border-white/8 px-1 pt-1 pb-1">
          <span className="text-[7px] font-semibold uppercase tracking-widest text-zinc-600 px-1">
            Streaming
          </span>
          <div className="mt-0.5 space-y-0">
            {streamingServices.map((svc) => {
              const connected = connectedServices.has(svc.id);
              return (
                <button
                  key={svc.id}
                  onClick={() => toggleService(svc.id)}
                  className={`flex h-6 w-full items-center gap-1.5 rounded-sm px-2 text-[9px] ${
                    connected
                      ? `${svc.textColor} bg-white/[0.04]`
                      : 'text-zinc-500 hover:text-zinc-300 hover:bg-white/[0.03]'
                  }`}
                >
                  <div className={`h-2 w-2 rounded-full ${connected ? svc.color : 'bg-zinc-700'}`} />
                  <span className="flex-1 text-left">{svc.label}</span>
                  <span className="text-[7px] uppercase tracking-wider">
                    {connected ? 'On' : 'Off'}
                  </span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Sources */}
        <div className="border-t border-white/8 px-1 pt-1">
          <span className="text-[7px] font-semibold uppercase tracking-widest text-zinc-600 px-1">
            Sources
          </span>
          <div className="mt-0.5 space-y-0">
            {visibleSources.map((s) => (
              <button
                key={s}
                onClick={() => actions.setBrowserSource(s)}
                className={`flex h-5 w-full items-center justify-between rounded-sm px-2 text-[9px] ${
                  browser.selectedSource === s
                    ? 'bg-cyan-400/10 text-cyan-100'
                    : 'text-zinc-400 hover:bg-white/[0.03] hover:text-white'
                }`}
              >
                <span className="truncate">{s}</span>
                <span className={`h-1.5 w-1.5 rounded-full ${browser.selectedSource === s ? 'bg-cyan-400' : 'bg-zinc-700'}`} />
              </button>
            ))}
          </div>
        </div>

        {/* Samples & FX */}
        <div className="border-t border-white/8 px-1 pt-1 pb-1">
          <div className="flex items-center justify-between px-1">
            <span className="text-[7px] font-semibold uppercase tracking-widest text-zinc-600">Samples &amp; FX</span>
            <button onClick={onAddSamplesFolder} className="text-[10px] text-zinc-500 hover:text-white" title="Add samples folder">+</button>
          </div>
          <div className="mt-1 grid grid-cols-2 gap-1">
            {(samples ?? []).slice(0, 8).map((sample) => (
              <button
                key={sample.path}
                onClick={() => onPlaySample?.(sample.path)}
                className="flex h-10 flex-col items-center justify-center rounded-sm border border-purple-400/20 bg-purple-500/10 px-1 text-center hover:bg-purple-500/20"
              >
                <span className="max-w-full truncate text-[7px] font-bold text-purple-200">{sample.name}</span>
                <span className="max-w-full truncate text-[6px] text-zinc-500">{sample.category}</span>
              </button>
            ))}
            {(samples?.length ?? 0) === 0 && (
              <div className="col-span-2 px-1 py-2 text-[8px] text-zinc-500">
                No samples loaded
              </div>
            )}
          </div>
        </div>
      </aside>

      {/* ── Right main area ── */}
      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        {/* Search + filter + sort */}
        <div className="flex h-[32px] shrink-0 items-center gap-1.5 border-b border-white/8 px-2">
          <input
            value={browser.search}
            onChange={(e) => actions.setBrowserSearch(e.target.value)}
            className="h-6 flex-1 border border-white/8 bg-black/20 px-2 text-[10px] text-white outline-none placeholder:text-zinc-600 focus:border-cyan-400/30"
            placeholder="Search tracks, artists, keys…"
          />
          <button
            type="button"
            onClick={handleMicClick}
            title="Search mic"
            className="flex h-6 w-6 shrink-0 items-center justify-center border border-white/8 bg-black/20 text-zinc-500 hover:border-cyan-400/30 hover:text-cyan-300"
          >
            <Mic className="h-3 w-3" />
          </button>
          {micNote && (
            <span className="text-[8px] text-zinc-400 whitespace-nowrap">{micNote}</span>
          )}
          <button onClick={actions.cycleBrowserFilter}
            className="h-6 border border-white/8 px-1.5 text-[8px] font-semibold uppercase tracking-wider text-zinc-300">
            {browser.filterMode}
          </button>
          <button onClick={actions.cycleBrowserSort}
            className="h-6 border border-white/8 px-1.5 text-[8px] font-semibold uppercase tracking-wider text-zinc-300">
            {browser.sortMode}
          </button>
        </div>

        {/* Source chips */}
        <div className="flex h-[22px] shrink-0 items-center gap-0.5 overflow-x-auto border-b border-white/8 px-2">
          {visibleSources.map((s) => (
            <button
              key={s}
              onClick={() => actions.setBrowserSource(s)}
              className={`h-5 border px-2 text-[8px] font-semibold uppercase tracking-wider whitespace-nowrap ${
                browser.selectedSource === s
                  ? 'border-cyan-400/30 bg-cyan-400/10 text-cyan-100'
                  : 'border-white/8 text-zinc-400'
              }`}
            >
              {s}
            </button>
          ))}
        </div>

        {/* Section label + count */}
        <div className="flex h-[18px] shrink-0 items-center justify-between border-b border-white/8 px-2 text-[7px] uppercase tracking-wider text-zinc-500">
          <span>{browser.activeSection.replace('-', ' ')}</span>
          <span>{tracks.length} tracks</span>
        </div>

        {/* Column headers */}
        <div className="grid h-[20px] shrink-0 grid-cols-[28px_minmax(0,1.5fr)_minmax(0,1.2fr)_minmax(0,1fr)_minmax(0,0.8fr)_48px_44px_44px_64px] items-center border-b border-white/8 bg-[#07080b] px-1 text-[7px] font-medium uppercase tracking-widest text-zinc-600">
          <span className="text-center">#</span>
          <span className="px-2">Title</span>
          <span className="px-2">Artist</span>
          <span className="px-2">Album</span>
          <span className="px-2">Genre</span>
          <span className="text-center">Time</span>
          <span className="text-center">BPM</span>
          <span className="text-center">Key</span>
          <span className="text-center">Load</span>
        </div>

        {/* Track rows */}
        <div className="min-h-0 flex-1 overflow-y-auto overflow-x-auto">
          {tracks.length > 0 ? (
            tracks.map((t, idx) => {
              const sel = selectedId === t.id;
              const sugDeck = sel ? rec?.deck ?? 'A' : 'A';
              return (
                <div
                  key={t.id}
                  draggable
                  onDragStart={(e) => {
                    e.dataTransfer.setData('application/x-dj-track', t.path);
                    e.dataTransfer.effectAllowed = 'copy';
                  }}
                  onClick={() => setSelectedId(t.id)}
                  onDoubleClick={() => actions.loadTrack(sugDeck, t.path)}
                  className={`grid cursor-pointer grid-cols-[28px_minmax(0,1.5fr)_minmax(0,1.2fr)_minmax(0,1fr)_minmax(0,0.8fr)_48px_44px_44px_64px] items-center border-b border-white/[0.04] px-1 text-[10px] hover:bg-white/[0.03] ${
                    sel ? 'bg-white/[0.06]' : ''
                  }`}
                >
                  <span className="py-1.5 text-center text-zinc-500">{idx + 1}</span>
                  <span className="truncate px-2 py-1.5 font-medium text-zinc-100">{t.title}</span>
                  <span className="truncate px-2 py-1.5 text-zinc-400">{t.artist || 'Unknown'}</span>
                  <span className="truncate px-2 py-1.5 text-zinc-500">—</span>
                  <span className="truncate px-2 py-1.5 text-zinc-500">—</span>
                  <span className="py-1.5 text-center text-zinc-400">{t.duration || '--:--'}</span>
                  <span className="py-1.5 text-center text-cyan-300">{t.bpm > 0 ? t.bpm : '--'}</span>
                  <span className="py-1.5 text-center text-amber-300">{t.key || '--'}</span>
                  <div className="flex items-center justify-center gap-0.5 py-1">
                    <button
                      onClick={(e) => { e.stopPropagation(); actions.loadTrack('A', t.path); }}
                      className="h-5 border border-cyan-400/30 bg-cyan-400/10 px-1.5 text-[8px] font-bold text-cyan-100"
                    >A</button>
                    <button
                      onClick={(e) => { e.stopPropagation(); actions.loadTrack('B', t.path); }}
                      className="h-5 border border-amber-400/30 bg-amber-400/10 px-1.5 text-[8px] font-bold text-amber-100"
                    >B</button>
                    {consoleMode === 'four-deck' && (
                      <>
                        <button
                          onClick={(e) => { e.stopPropagation(); actions.loadTrack('C', t.path); }}
                          className="h-5 border border-cyan-400/30 bg-cyan-400/10 px-1 text-[7px] font-bold text-cyan-100"
                        >C</button>
                        <button
                          onClick={(e) => { e.stopPropagation(); actions.loadTrack('D', t.path); }}
                          className="h-5 border border-amber-400/30 bg-amber-400/10 px-1 text-[7px] font-bold text-amber-100"
                        >D</button>
                      </>
                    )}
                  </div>
                </div>
              );
            })
          ) : (
            <div className="flex h-full items-center justify-center text-[10px] uppercase tracking-wider text-zinc-600">
              {browser.activeSection === 'playlists' || browser.activeSection === 'crates'
                ? `${browser.activeSection} not populated`
                : connectedServiceLabels.includes(browser.selectedSource)
                  ? `${browser.selectedSource} connected - catalog integration pending`
                : browser.activeSection === 'recent'
                  ? 'Load a track to build history'
                  : 'No tracks match current filters'}
            </div>
          )}
        </div>
      </div>

      {/* ── Streaming QR code sign-in ── */}
      {signingIn && signingInService && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm" onClick={() => { setSigningIn(null); setOpeningService(false); }}>
          <div className="w-[320px] rounded-lg border border-white/10 bg-[#0e1015] shadow-2xl overflow-hidden" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-4 py-3 border-b border-white/8">
              <div className="flex items-center gap-2">
                <div className={`h-3 w-3 rounded-full ${signingInService.color}`} />
                <span className="text-[11px] font-bold text-zinc-100">Connect {signingInService.label}</span>
              </div>
              <button onClick={() => { setSigningIn(null); setOpeningService(false); }} className="text-zinc-500 hover:text-white text-[14px] leading-none">✕</button>
            </div>
            <div className="flex flex-col items-center gap-3 px-4 py-4">
              <div className="flex w-full flex-col items-center rounded-md border border-white/10 bg-black/20 px-4 py-4">
                <div className={`mb-3 flex h-14 w-14 items-center justify-center rounded-full ${signingInService.color} text-[18px] font-black text-white`}>
                  {signingInService.label.charAt(0)}
                </div>
                <div className="text-center text-[10px] font-medium text-zinc-200">
                  Open {signingInService.label} sign-in in your browser
                </div>
                <div className="mt-2 break-all text-center text-[8px] leading-relaxed text-zinc-500">
                  {signingInService.url}
                </div>
              </div>
              <div className="text-center text-[10px] text-zinc-300 font-medium">
                Use browser sign-in, then return here
              </div>
              <div className="text-center text-[8px] text-zinc-500 leading-relaxed">
                This flow now opens the real service page and lets you back out cleanly at any time.
              </div>
              <div className="mt-1 flex w-full gap-2">
                <button
                  onClick={handleOpenService}
                  className={`h-8 flex-1 rounded-sm text-[9px] font-bold uppercase tracking-wider text-white ${signingInService.color} hover:opacity-90`}
                >
                  {openingService ? 'Open Again' : 'Open Sign-In'}
                </button>
                <button
                  onClick={handleSignIn}
                  className="h-8 flex-1 rounded-sm border border-white/10 bg-white/[0.05] text-[9px] font-bold uppercase tracking-wider text-zinc-200 hover:bg-white/[0.08]"
                >
                  I Signed In
                </button>
              </div>
              <button
                onClick={() => { setSigningIn(null); setOpeningService(false); }}
                className="h-7 w-full rounded-sm border border-white/10 bg-transparent text-[8px] font-bold uppercase tracking-wider text-zinc-400 hover:bg-white/[0.05] hover:text-zinc-200"
              >
                Back
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
