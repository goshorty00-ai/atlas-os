import { Play, Film, Star } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { ImageWithFallback } from './figma/ImageWithFallback';

export interface MediaItem {
  id: string;
  title: string;
  year: string;
  type: 'Movie' | 'TV' | 'Music Video' | 'Game';
  server: string;
  rating?: number;
  quality?: string;
  posterUrl?: string;
  hasMetadata: boolean;
  hasArtwork: boolean;
  runtime?: string;
  genre?: string;
  description?: string;
}

interface ServerCardProps {
  item: MediaItem;
  onPlay?: () => void;
  onTrailer?: () => void;
}

export function ServerCard({ item, onPlay, onTrailer }: ServerCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const navigate = useNavigate();

  const handleCardClick = () => {
    navigate(`/details/${item.id}`);
  };

  return (
    <div
      onClick={handleCardClick}
      className="group relative flex-shrink-0 w-[160px] rounded-lg overflow-hidden transition-all duration-300 hover:-translate-y-1 hover:shadow-xl hover:shadow-cyan-500/20 ring-1 ring-slate-700/40 hover:ring-cyan-400/50 cursor-pointer"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div className="relative aspect-[2/3] bg-gradient-to-br from-slate-800 to-slate-900">
        {item.posterUrl ? (
          <ImageWithFallback
            src={item.posterUrl}
            alt={item.title}
            className="w-full h-full object-cover"
          />
        ) : (
          <div className="w-full h-full flex items-center justify-center text-slate-600 text-xs">
            {item.title}
          </div>
        )}

        {/* Quality badge */}
        {item.quality && (
          <div className="absolute top-1.5 right-1.5 px-1.5 py-0.5 rounded bg-cyan-500/90 text-white text-[10px] backdrop-blur-sm">
            {item.quality}
          </div>
        )}

        {/* Type badge */}
        <div className="absolute top-1.5 left-1.5 px-1.5 py-0.5 rounded bg-slate-900/80 text-slate-200 text-[10px] backdrop-blur-sm">
          {item.type}
        </div>

        {/* Bottom gradient with rating + trailer */}
        <div className="absolute inset-x-0 bottom-0 p-2 bg-gradient-to-t from-slate-950 via-slate-950/70 to-transparent flex items-end justify-between">
          {item.rating !== undefined && (
            <div className="flex items-center gap-1 text-[10px] text-amber-300">
              <Star size={10} fill="currentColor" />
              {item.rating.toFixed(1)}
            </div>
          )}
          <button
            onClick={(e) => { e.stopPropagation(); onTrailer?.(); }}
            className="ml-auto p-1 rounded bg-slate-900/80 text-slate-200 hover:bg-violet-500/80 hover:text-white transition-colors"
            title="Trailer"
          >
            <Film size={12} />
          </button>
        </div>

        {/* Hover play */}
        {isHovered && (
          <button
            onClick={(e) => {
              e.stopPropagation();
              if (onPlay) onPlay();
              else navigate(`/details/${item.id}`);
            }}
            className="absolute inset-0 m-auto w-10 h-10 rounded-full bg-cyan-500/90 hover:bg-cyan-400 text-white flex items-center justify-center shadow-lg backdrop-blur-sm"
          >
            <Play size={16} fill="currentColor" />
          </button>
        )}
      </div>

      {/* Info strip */}
      <div className="p-2 bg-slate-900/80 backdrop-blur-xl border-t border-slate-700/30">
        <div className="text-slate-100 text-xs truncate">{item.title}</div>
        <div className="flex items-center justify-between text-[10px] text-slate-400 mt-0.5">
          <span>{item.year}</span>
          <span className="text-violet-300 truncate ml-2">{item.server}</span>
        </div>
      </div>
    </div>
  );
}
