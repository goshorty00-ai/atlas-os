import { Play, Star } from 'lucide-react';
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
  className?: string;
}

export function ServerCard({ item, onPlay, onTrailer, className }: ServerCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const navigate = useNavigate();

  const handleCardClick = () => {
    navigate(`/details/${item.id}`);
  };

  return (
    <div
      onClick={handleCardClick}
      className={`group relative flex-shrink-0 w-[230px] cursor-pointer${className ? ' ' + className : ''}`}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div className="relative aspect-[3/4] bg-gradient-to-br from-slate-800 to-slate-900 rounded-xl overflow-hidden ring-[1.5px] ring-cyan-400/50 shadow-[0_0_10px_rgba(34,211,238,0.18)] group-hover:ring-cyan-400/90 group-hover:shadow-[0_0_22px_rgba(34,211,238,0.5)] transition-all duration-300">
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

      {/* Text below cover */}
      <div className="pt-2 px-0.5 pb-1 bg-transparent">
        <div className="text-slate-100 text-xs font-medium truncate leading-tight">{item.title}</div>
        {item.rating !== undefined && (
          <div className="flex items-center gap-1 mt-1 text-[10px] text-amber-300">
            <Star size={9} fill="currentColor" />
            <span>{item.rating.toFixed(1)}</span>
          </div>
        )}
      </div>
    </div>
  );
}
