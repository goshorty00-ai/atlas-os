import { X, ChevronLeft, ChevronRight, Play, Film, Info, Plus, Star, Pause, Check } from 'lucide-react';
import { MediaItem } from './server-card';
import { useState, useEffect, useCallback, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router';
import { ImageWithFallback } from './figma/ImageWithFallback';

function postBridge(msg: object) {
  try { (window as any).chrome?.webview?.postMessage(msg); } catch { /* no bridge */ }
}

const YT_API_KEY = (window as any).__ATLAS_YT_KEY ?? import.meta.env.VITE_YT_API_KEY ?? '';

function extractYouTubeVideoId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/)([^&?/]{11})/);
  return m ? m[1] : null;
}

async function fetchYouTubeVideoId(title: string, type: string): Promise<string | null> {
  const query = encodeURIComponent(`${title} ${type === 'TV' ? 'series' : 'movie'} official trailer`);
  // Try YouTube Data API if key is set
  if (YT_API_KEY) {
    try {
      const res = await fetch(
        `https://www.googleapis.com/youtube/v3/search?part=snippet&q=${query}&type=video&key=${YT_API_KEY}&maxResults=1`
      );
      const data = await res.json();
      const id = data?.items?.[0]?.id?.videoId;
      if (id) return id;
    } catch { /* fall through to Invidious */ }
  }
  // Fallback: Invidious API (no key required, CORS-enabled)
  for (const base of ['https://inv.riverside.rocks', 'https://yewtu.be']) {
    try {
      const res = await fetch(`${base}/api/v1/search?q=${query}&type=video&fields=videoId`, { signal: AbortSignal.timeout(6000) });
      if (!res.ok) continue;
      const data = await res.json();
      if (Array.isArray(data) && data[0]?.videoId) return data[0].videoId as string;
    } catch { continue; }
  }
  return null;
}

function TrailerModal({ videoId, title, onClose }: { videoId: string; title: string; onClose: () => void }) {
  const embedUrl = `https://www.youtube.com/embed/${videoId}?autoplay=1&rel=0&modestbranding=1`;
  return createPortal(
    <div className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/85 backdrop-blur-sm" onClick={onClose}>
      <div
        className="relative w-[860px] max-w-[92vw] rounded-xl overflow-hidden shadow-2xl border border-slate-700/60"
        style={{ aspectRatio: '16/9' }}
        onClick={(e) => e.stopPropagation()}
      >
        <iframe src={embedUrl} title={`${title} Trailer`} className="w-full h-full"
          allow="autoplay; encrypted-media; fullscreen" allowFullScreen />
        <button onClick={onClose}
          className="absolute top-2 right-2 p-1.5 rounded-full bg-black/70 hover:bg-black text-white border border-white/20">
          <X size={16} />
        </button>
      </div>
    </div>,
    document.body
  );
}

interface CarouselOverlayProps {
  isOpen: boolean;
  items: MediaItem[];
  initialIndex?: number;
  shelfName: string;
  onClose: () => void;
}

export function CarouselOverlay({ isOpen, items, initialIndex = 0, shelfName, onClose }: CarouselOverlayProps) {
  const navigate = useNavigate();
  const [currentIndex, setCurrentIndex] = useState(initialIndex);
  const [autoRotate, setAutoRotate] = useState(false);
  const [speedMs, setSpeedMs] = useState(3000);
  const [speedDisplay, setSpeedDisplay] = useState(3);
  const [minRating, setMinRating] = useState(0);
  const [ratingDisplay, setRatingDisplay] = useState(0);
  const [watchlisted, setWatchlisted] = useState<Set<string>>(new Set());
  const [trailerVideoId, setTrailerVideoId] = useState<string | null>(null);
  const [trailerLoading, setTrailerLoading] = useState(false);

  // Filtered items based on rating slider — falls back to all if filter removes everything
  const filteredItems = useMemo(() => {
    if (minRating === 0) return items;
    const f = items.filter(it => (it.rating ?? 0) >= minRating);
    return f.length > 0 ? f : items;
  }, [items, minRating]);

  // Clamp index when filter changes — don't hard-reset to 0
  useEffect(() => {
    setCurrentIndex(prev => Math.min(prev, Math.max(0, filteredItems.length - 1)));
  }, [filteredItems.length]);

  const goToNext = useCallback(() => {
    setCurrentIndex((p) => (p + 1) % filteredItems.length);
  }, [filteredItems.length]);
  const goToPrevious = useCallback(() => {
    setCurrentIndex((p) => (p - 1 + filteredItems.length) % filteredItems.length);
  }, [filteredItems.length]);

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowLeft') goToPrevious();
      if (e.key === 'ArrowRight') goToNext();
    };
    let wheelLock = false;
    const onWheel = (e: WheelEvent) => {
      if (wheelLock) return;
      wheelLock = true;
      if (e.deltaY > 0) goToNext();
      else if (e.deltaY < 0) goToPrevious();
      setTimeout(() => { wheelLock = false; }, 250);
    };
    window.addEventListener('keydown', onKey);
    window.addEventListener('wheel', onWheel, { passive: true });
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('wheel', onWheel);
    };
  }, [isOpen, goToNext, goToPrevious, onClose]);

  useEffect(() => {
    if (!autoRotate || !isOpen) return;
    const id = setInterval(goToNext, speedMs);
    return () => clearInterval(id);
  }, [autoRotate, isOpen, goToNext, speedMs]);

  if (items.length === 0) return null;
  const safeIndex = Math.min(currentIndex, filteredItems.length - 1);
  const currentItem = filteredItems[safeIndex];
  const speedLabel = speedMs >= 1000 ? `${(speedMs / 1000).toFixed(speedMs % 1000 === 0 ? 0 : 1)}s` : `${speedMs}ms`;
  const isWatchlisted = watchlisted.has(currentItem.id);

  // Button actions
  const handlePlay = () => postBridge({ type: 'servers.playItem', payload: { id: currentItem.id, server: currentItem.server, title: currentItem.title } });
  const handleTrailer = async () => {
    // Try direct YouTube URL from item data first (no API needed)
    if (currentItem.trailerUrl) {
      const directId = extractYouTubeVideoId(currentItem.trailerUrl);
      if (directId) { setTrailerVideoId(directId); return; }
    }
    setTrailerLoading(true);
    const videoId = await fetchYouTubeVideoId(currentItem.title, currentItem.type ?? 'Movie');
    setTrailerLoading(false);
    if (videoId) setTrailerVideoId(videoId);
  };
  const handleDetails = () => { onClose(); setTimeout(() => navigate(`/details/${currentItem.id}`, { state: { item: currentItem } }), 80); };
  const handleWatchlist = () => setWatchlisted(prev => {
    const s = new Set(prev);
    if (s.has(currentItem.id)) s.delete(currentItem.id); else s.add(currentItem.id);
    return s;
  });

  // 7 visible: -3..+3
  const visible = [-3, -2, -1, 0, 1, 2, 3].map((offset) => {
    const index = (currentIndex + offset + filteredItems.length) % filteredItems.length;
    return { item: filteredItems[index], offset, index };
  });

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-slate-950/98 backdrop-blur-2xl overflow-hidden" style={{ display: isOpen ? 'flex' : 'none' }}>
      {/* Blurred poster backdrop */}
      {currentItem.posterUrl && (
        <div
          className="absolute inset-0 bg-cover bg-center opacity-20 blur-3xl scale-110"
          style={{ backgroundImage: `url(${currentItem.posterUrl})` }}
        />
      )}
      <div className="absolute inset-0 bg-gradient-to-b from-slate-950/60 via-transparent to-slate-950/90" />

      {/* ── Top bar ── */}
      <div className="relative z-10 flex items-center gap-3 px-5 py-2.5 border-b border-white/5 flex-wrap">
        {/* Left: close + title */}
        <div className="flex items-center gap-3 flex-shrink-0">
          <button onClick={onClose} className="p-1.5 rounded-md bg-slate-800/60 hover:bg-slate-700 text-slate-200 border border-slate-700/50 transition-colors" title="Close (Esc)">
            <X size={16} />
          </button>
          <span className="text-slate-100 text-sm font-medium">{shelfName}</span>
          <span className="text-xs text-slate-400">{safeIndex + 1} / {filteredItems.length}{minRating > 0 ? ` (★${minRating}+)` : ''}</span>
        </div>

        <div className="flex-1" />

        {/* Rating filter — commits on pointer-up so covers don't jump while dragging */}
        <div className="flex items-center gap-2 flex-shrink-0">
          <Star size={11} className="text-amber-400 flex-shrink-0" fill="currentColor" />
          <span className="text-[11px] text-slate-400 w-[28px] text-right">{ratingDisplay > 0 ? ratingDisplay.toFixed(1) : 'All'}</span>
          <input
            type="range"
            min={0} max={10} step={0.5}
            value={ratingDisplay}
            onChange={(e) => setRatingDisplay(parseFloat(e.target.value))}
            onPointerUp={(e) => setMinRating(parseFloat((e.target as HTMLInputElement).value))}
            className="w-24 accent-amber-400 cursor-pointer"
            title="Min rating — release to apply"
          />
        </div>

        {/* Speed slider */}
        <div className="flex items-center gap-2 flex-shrink-0">
          <span className="text-[11px] text-slate-500">Speed</span>
          <input
            type="range"
            min={0.5} max={12} step={0.5}
            value={speedDisplay}
            onChange={(e) => {
              const v = parseFloat(e.target.value);
              setSpeedDisplay(v);
              setSpeedMs(Math.round(v * 1000));
            }}
            className="w-20 accent-cyan-400 cursor-pointer"
            title="Autoplay interval"
          />
          <span className="text-[11px] text-slate-400 w-8">{speedLabel}</span>
        </div>

        {/* Auto Rotate toggle */}
        <button
          onClick={() => setAutoRotate((v) => !v)}
          className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs border transition-colors flex-shrink-0 ${
            autoRotate
              ? 'bg-cyan-500/20 border-cyan-400/50 text-cyan-200'
              : 'bg-slate-800/60 border-slate-700/50 text-slate-300 hover:border-slate-600'
          }`}
        >
          {autoRotate ? <Pause size={11} /> : <Play size={11} fill="currentColor" />}
          Auto
        </button>

        <span className="hidden lg:inline text-[10px] text-slate-500 flex-shrink-0">ESC · ← → · scroll</span>
      </div>

      {/* ── Coverflow stage — pointerEvents:none so 3D cards can't block the bottom panel ── */}
      <div className="relative z-10 flex-1 flex items-center justify-center" style={{ perspective: '1800px', pointerEvents: 'none' }}>
        <button onClick={goToPrevious} style={{ pointerEvents: 'auto' }} className="absolute left-4 z-20 p-2 rounded-full bg-slate-900/70 hover:bg-slate-800 text-slate-200 border border-slate-700/50 backdrop-blur-md transition-colors">
          <ChevronLeft size={22} />
        </button>
        <button onClick={goToNext} style={{ pointerEvents: 'auto' }} className="absolute right-4 z-20 p-2 rounded-full bg-slate-900/70 hover:bg-slate-800 text-slate-200 border border-slate-700/50 backdrop-blur-md transition-colors">
          <ChevronRight size={22} />
        </button>

        <div className="relative w-full h-full flex items-center justify-center" style={{ transformStyle: 'preserve-3d' }}>
          {visible.map(({ item, offset, index }) => {
            const abs = Math.abs(offset);
            const isCenter = offset === 0;
            const translateX = offset * 165;
            const translateZ = isCenter ? 0 : -abs * 160;
            const rotateY = isCenter ? 0 : offset > 0 ? -38 : 38;
            const scale = isCenter ? 1 : Math.max(0.65, 1 - abs * 0.12);
            const opacity = abs > 3 ? 0 : 1 - abs * 0.18;
            const blur = isCenter ? 0 : Math.min(abs * 1.2, 4);
            const brightness = isCenter ? 1 : Math.max(0.55, 1 - abs * 0.18);

            return (
              <div
                key={item.id}
                className="absolute transition-all duration-500 ease-out cursor-pointer"
                style={{
                  transform: `translateX(${translateX}px) translateZ(${translateZ}px) rotateY(${rotateY}deg) scale(${scale})`,
                  opacity, zIndex: 50 - abs,
                  filter: `blur(${blur}px) brightness(${brightness})`,
                  transformStyle: 'preserve-3d',
                  pointerEvents: 'auto',
                }}
                onClick={() => setCurrentIndex(index)}
              >
                <div className={`relative w-[260px] h-[390px] rounded-2xl overflow-hidden bg-slate-900 ${
                  isCenter ? 'ring-1 ring-cyan-400/60 shadow-[0_0_60px_-5px_rgba(34,211,238,0.55)]' : 'ring-1 ring-slate-700/40 shadow-2xl'
                }`}>
                  <ImageWithFallback src={item.posterUrl ?? `https://images.metahub.space/poster/medium/${item.imdbId ?? item.id}/img`} alt={item.title} className="w-full h-full object-cover" mediaType={item.type === 'Movie' ? 'movie' : item.type === 'TV' ? 'series' : 'both'} />
                  <div className="absolute top-2 left-2 flex flex-col gap-1">
                    <span className="px-1.5 py-0.5 rounded bg-slate-900/80 text-slate-100 text-[10px] backdrop-blur-sm">{item.type}</span>
                  </div>
                  <div className="absolute top-2 right-2 flex flex-col items-end gap-1">
                    {item.rating !== undefined && (
                      <span className="flex items-center gap-1 px-1.5 py-0.5 rounded bg-slate-900/80 text-amber-300 text-[10px] backdrop-blur-sm">
                        <Star size={9} fill="currentColor" /> {item.rating.toFixed(1)}
                      </span>
                    )}
                    <span className="px-1.5 py-0.5 rounded bg-violet-500/30 text-violet-100 text-[10px] backdrop-blur-sm border border-violet-400/30">{item.server}</span>
                  </div>
                  {isCenter && (
                    <div className="absolute inset-0 opacity-0 hover:opacity-100 transition-opacity bg-gradient-to-t from-slate-950/80 via-slate-950/20 to-transparent flex items-end justify-center gap-2 p-4">
                      <button onClick={(e) => { e.stopPropagation(); handlePlay(); }} className="p-2 rounded-full bg-cyan-500 text-white shadow-lg hover:bg-cyan-400">
                        <Play size={16} fill="currentColor" />
                      </button>
                      <button onClick={(e) => { e.stopPropagation(); handleTrailer(); }} className="p-2 rounded-full bg-slate-800/90 text-slate-100 border border-slate-700 hover:bg-slate-700">
                        <Film size={16} />
                      </button>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* ── Info panel — z-index 100 ensures 3D cards never block button clicks ── */}
      <div
        className="border-t border-white/5 bg-slate-950/90 backdrop-blur-xl flex-shrink-0"
        style={{ position: 'relative', zIndex: 100 }}
      >
        <div className="max-w-5xl mx-auto px-6 py-4 flex items-center gap-6">
          {/* Metadata block */}
          <div className="flex-1 min-w-0 flex flex-col gap-1.5">
            {/* Row 1: title + year + runtime + rating */}
            <div className="flex items-center gap-2 min-w-0 flex-wrap">
              <h3 className="text-white font-semibold text-base">{currentItem.title}</h3>
              {currentItem.year && <span className="text-sm text-slate-400">{currentItem.year}</span>}
              {currentItem.runtime && <span className="text-sm text-slate-500">{currentItem.runtime}</span>}
              {currentItem.rating !== undefined && (
                <span className="flex items-center gap-1 text-amber-300 text-sm">
                  <Star size={12} fill="currentColor" />{currentItem.rating.toFixed(1)}
                </span>
              )}
            </div>
            {/* Row 2: type + genre + server + quality badges */}
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="px-2 py-0.5 rounded text-xs bg-slate-800 text-slate-300 border border-slate-700">{currentItem.type}</span>
              {currentItem.genre && (
                <span className="px-2 py-0.5 rounded text-xs bg-slate-800 text-slate-300 border border-slate-700">{currentItem.genre}</span>
              )}
              <span className="px-2 py-0.5 rounded text-xs bg-violet-600/20 text-violet-200 border border-violet-500/30">{currentItem.server}</span>
              {currentItem.quality && (
                <span className="px-2 py-0.5 rounded text-xs bg-cyan-600/20 text-cyan-200 border border-cyan-500/30">{currentItem.quality}</span>
              )}
            </div>
            {/* Row 3: description — 2 lines */}
            <p className="text-xs text-slate-400 line-clamp-2">
              {currentItem.description || 'No description available.'}
            </p>
          </div>

          {/* Action buttons */}
          <div className="flex items-center gap-2 flex-shrink-0">
            <button
              onClick={handlePlay}
              className="flex items-center gap-2 px-4 py-2 rounded-full bg-gradient-to-r from-cyan-500 to-blue-500 text-white text-sm font-semibold hover:shadow-lg hover:shadow-cyan-500/40 transition-all active:scale-95"
            >
              <Play size={14} fill="currentColor" /> Play
            </button>
            <button
              onClick={handleTrailer}
              disabled={trailerLoading}
              className="flex items-center gap-2 px-4 py-2 rounded-full bg-slate-800 text-slate-200 text-sm border border-slate-700 hover:border-violet-400/60 hover:text-violet-200 transition-colors active:scale-95 disabled:opacity-60"
            >
              <Film size={14} /> {trailerLoading ? 'Loading…' : 'Trailer'}
            </button>
            <button
              onClick={handleDetails}
              className="flex items-center gap-2 px-4 py-2 rounded-full bg-slate-800 text-slate-200 text-sm border border-slate-700 hover:border-slate-500 transition-colors active:scale-95"
            >
              <Info size={14} /> Details
            </button>
            <button
              onClick={handleWatchlist}
              className={`flex items-center gap-2 px-4 py-2 rounded-full text-sm border transition-colors active:scale-95 ${
                isWatchlisted
                  ? 'bg-cyan-500/15 border-cyan-400/50 text-cyan-200 hover:bg-cyan-500/25'
                  : 'bg-slate-800 text-slate-200 border-slate-700 hover:border-cyan-400/40 hover:text-cyan-200'
              }`}
            >
              {isWatchlisted ? <Check size={14} /> : <Plus size={14} />}
              {isWatchlisted ? 'Watchlisted' : 'Watchlist'}
            </button>
          </div>
        </div>
      </div>
    {trailerVideoId && (
      <TrailerModal videoId={trailerVideoId} title={currentItem.title} onClose={() => setTrailerVideoId(null)} />
    )}
    </div>
  );
}
