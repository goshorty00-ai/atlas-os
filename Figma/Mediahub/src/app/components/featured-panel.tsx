import { Play, Film, Plus, Bell, Info, Star, Loader, X } from 'lucide-react';
import { useNavigate } from 'react-router';
import { useState } from 'react';
import { createPortal } from 'react-dom';
import { ImageWithFallback } from './figma/ImageWithFallback';

// YouTube Data API key — must be set via VITE_YT_API_KEY in .env.local (never hardcode)
const YT_API_KEY = import.meta.env.VITE_YT_API_KEY ?? '';

async function fetchYouTubeVideoId(title: string, type: string): Promise<string | null> {
  if (!YT_API_KEY) return null;
  const query = encodeURIComponent(`${title} ${type === 'TV' ? 'series' : 'movie'} official trailer`);
  try {
    const res = await fetch(
      `https://www.googleapis.com/youtube/v3/search?part=snippet&q=${query}&type=video&key=${YT_API_KEY}&maxResults=1`
    );
    const data = await res.json();
    return data?.items?.[0]?.id?.videoId ?? null;
  } catch {
    return null;
  }
}

export interface FeaturedItem {
  id?: string;
  imdbId?: string;
  title: string;
  backdropUrl: string;
  rating?: number;
  runtime?: string;
  release?: string;
  genres: string[];
  source: string;
  description: string;
  primaryAction?: 'Play' | 'Watchlist' | 'Add Reminder';
  type?: 'Movie' | 'TV' | 'Game' | 'Mixed';
  trailerUrl?: string;
}

function TrailerModal({ videoId, title, onClose }: { videoId: string; title: string; onClose: () => void }) {
  const embedUrl = `https://www.youtube.com/embed/${videoId}?autoplay=1&rel=0&modestbranding=1`;
  return createPortal(
    <div
      className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/85 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="relative w-[860px] max-w-[92vw] rounded-xl overflow-hidden shadow-2xl border border-slate-700/60"
        style={{ aspectRatio: '16/9' }}
        onClick={(e) => e.stopPropagation()}
      >
        <iframe
          src={embedUrl}
          title={`${title} Trailer`}
          className="w-full h-full"
          allow="autoplay; encrypted-media; fullscreen"
          allowFullScreen
        />
        <button
          onClick={onClose}
          className="absolute top-2 right-2 p-1.5 rounded-full bg-black/70 hover:bg-black text-white border border-white/20"
        >
          <X size={16} />
        </button>
      </div>
    </div>,
    document.body
  );
}

export function FeaturedPanel({ item }: { item: FeaturedItem }) {
  const navigate = useNavigate();
  const [trailerLoading, setTrailerLoading] = useState(false);
  const [trailerVideoId, setTrailerVideoId] = useState<string | null>(null);
  const primary = item.primaryAction ?? 'Play';
  const PrimaryIcon = primary === 'Play' ? Play : primary === 'Add Reminder' ? Bell : Plus;

  const handleDetailsClick = () => {
    navigate(`/details/${item.id || 'dune-part-two'}`);
  };

  const handleTitleClick = () => {
    navigate(`/details/${item.id || 'dune-part-two'}`);
  };

  const handleTrailerClick = async (e: React.MouseEvent) => {
    e.stopPropagation();
    setTrailerLoading(true);
    const videoId = await fetchYouTubeVideoId(item.title, item.type ?? 'Movie');
    setTrailerLoading(false);
    if (videoId) {
      setTrailerVideoId(videoId);
    }
  };

  return (
    <>
      {trailerVideoId && (
        <TrailerModal
          videoId={trailerVideoId}
          title={item.title}
          onClose={() => setTrailerVideoId(null)}
        />
      )}
      <div onClick={handleTitleClick} className="relative rounded-2xl overflow-hidden border border-cyan-400/15 ring-1 ring-cyan-500/10 cursor-pointer" style={{ height: 280 }}>
        <ImageWithFallback src={item.backdropUrl} alt={item.title} className="absolute inset-0 w-full h-full object-cover" />
        {/* cinematic gradients */}
        <div className="absolute inset-0 bg-gradient-to-r from-slate-950 via-slate-950/80 to-slate-950/10" />
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/95 via-transparent to-slate-950/40" />
        <div className="absolute inset-0" style={{ background: 'radial-gradient(ellipse at 75% 50%, rgba(34,211,238,0.12) 0%, transparent 60%)' }} />

        <div className="relative h-full flex flex-col justify-end p-5 max-w-[58%]">
          <div className="flex items-center gap-2 mb-2">
            <span className="px-2 py-0.5 rounded-md bg-cyan-500/20 border border-cyan-400/40 text-cyan-200 text-[10px] flex items-center gap-1">
              <Star size={9} /> Featured
            </span>
            <span className="px-2 py-0.5 rounded-md bg-slate-900/80 border border-slate-700/60 text-slate-200 text-[10px]">
              {item.source}
            </span>
            {item.type && (
              <span className="px-2 py-0.5 rounded-md bg-violet-500/15 border border-violet-400/30 text-violet-200 text-[10px]">
                {item.type}
              </span>
            )}
          </div>
          <div className="text-slate-50" style={{ fontSize: 30, letterSpacing: '-0.01em', textShadow: '0 2px 24px rgba(0,0,0,0.7)' }}>
            {item.title}
          </div>
          <div className="flex items-center gap-3 mt-1.5 text-slate-300 text-xs">
            {item.rating !== undefined && (
              <span className="flex items-center gap-1 text-amber-300"><Star size={11} fill="currentColor" /> {item.rating.toFixed(1)}</span>
            )}
            {item.runtime && <span>{item.runtime}</span>}
            {item.release && <span>{item.release}</span>}
          </div>
          <div className="flex flex-wrap gap-1.5 mt-2">
            {item.genres.map((g) => (
              <span key={g} className="px-2 py-0.5 rounded-full bg-slate-900/70 border border-slate-700/60 text-slate-300 text-[10px]">{g}</span>
            ))}
          </div>
          <p className="text-slate-300/90 text-xs mt-2.5 line-clamp-2 max-w-[520px]">{item.description}</p>
          <div className="flex items-center gap-2 mt-3">
            <button onClick={(e) => e.stopPropagation()} className="px-3 py-1.5 rounded-full bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs flex items-center gap-1.5 shadow-lg shadow-cyan-500/30">
              <PrimaryIcon size={12} fill={primary === 'Play' ? 'currentColor' : undefined} /> {primary}
            </button>
            <button
              onClick={handleTrailerClick}
              disabled={trailerLoading}
              className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5 disabled:opacity-60"
            >
              {trailerLoading ? <Loader size={11} className="animate-spin" /> : <Film size={11} />} Trailer
            </button>
            <button onClick={(e) => { e.stopPropagation(); handleDetailsClick(); }} className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5">
              <Info size={11} /> Details
            </button>
            <button onClick={(e) => e.stopPropagation()} className="px-3 py-1.5 rounded-full bg-slate-900/80 hover:bg-slate-800 border border-slate-700/60 text-slate-200 text-xs flex items-center gap-1.5">
              <Plus size={11} /> Watchlist
            </button>
          </div>
        </div>
      </div>
    </>
  );
}
