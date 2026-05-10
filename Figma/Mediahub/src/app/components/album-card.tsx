import { Play, Star, MoreHorizontal } from 'lucide-react';
import { useState } from 'react';
import { ImageWithFallback } from './figma/ImageWithFallback';

export interface AlbumItem {
  id: string;
  title: string;
  artist: string;
  year: string;
  tracks: number;
  genre: string;
  rating?: number;
  source?: string;
  mood?: string;
  coverUrl?: string;
  bpm?: number;
}

interface AlbumCardProps {
  album: AlbumItem;
  onContextMenu?: (e: React.MouseEvent, album: AlbumItem) => void;
  onClick?: () => void;
}

export function AlbumCard({ album, onContextMenu, onClick }: AlbumCardProps) {
  const [hover, setHover] = useState(false);
  return (
    <div
      className="group relative flex-shrink-0 w-[150px] cursor-pointer"
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      onContextMenu={(e) => {
        e.preventDefault();
        onContextMenu?.(e, album);
      }}
      onClick={onClick}
    >
      <div className="relative aspect-square rounded-xl overflow-hidden ring-1 ring-slate-700/40 group-hover:ring-cyan-400/60 transition-all group-hover:-translate-y-1 group-hover:shadow-[0_10px_30px_-5px_rgba(34,211,238,0.4)]">
        {album.coverUrl ? (
          <ImageWithFallback src={album.coverUrl} alt={album.title} className="w-full h-full object-cover" />
        ) : (
          <div className="w-full h-full bg-gradient-to-br from-purple-700 to-pink-600 flex items-center justify-center text-white/30 text-3xl">♪</div>
        )}

        {album.mood && (
          <div className="absolute top-1.5 left-1.5 px-1.5 py-0.5 rounded bg-slate-950/70 text-violet-200 text-[10px] backdrop-blur-sm border border-violet-400/30">
            {album.mood}
          </div>
        )}
        {album.rating !== undefined && (
          <div className="absolute top-1.5 right-1.5 flex items-center gap-0.5 px-1.5 py-0.5 rounded bg-slate-950/70 text-amber-300 text-[10px] backdrop-blur-sm">
            <Star size={9} fill="currentColor" /> {album.rating.toFixed(1)}
          </div>
        )}

        {hover && (
          <>
            <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-slate-950/30 to-transparent" />
            <button
              onClick={(e) => { e.stopPropagation(); }}
              className="absolute bottom-2 left-2 p-2 rounded-full bg-cyan-500 text-white shadow-lg hover:bg-cyan-400"
            >
              <Play size={12} fill="currentColor" />
            </button>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onContextMenu?.(e, album);
              }}
              className="absolute bottom-2 right-2 p-1.5 rounded-full bg-slate-900/80 text-slate-200 hover:bg-slate-800"
            >
              <MoreHorizontal size={12} />
            </button>
          </>
        )}
      </div>

      <div className="mt-2">
        <div className="text-slate-100 text-xs truncate">{album.title}</div>
        <div className="text-slate-400 text-[10px] truncate">{album.artist}</div>
        <div className="text-slate-500 text-[10px]">{album.year} · {album.tracks} tracks</div>
      </div>
    </div>
  );
}
