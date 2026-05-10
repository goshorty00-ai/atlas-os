import { motion } from 'motion/react';
import { Album } from '../data/albums';
import type React from 'react';

interface LibraryListViewProps {
  albums: Album[];
  onAlbumClick: (album: Album) => void;
  onAlbumContextMenu?: (e: React.MouseEvent, album: Album) => void;
}

export function LibraryListView({ albums, onAlbumClick, onAlbumContextMenu }: LibraryListViewProps) {
  return (
    <div className="p-8">
      <div className="space-y-2">
        {albums.map((album, index) => (
          <motion.div
            key={album.id}
            initial={{ opacity: 0, x: -20 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ delay: index * 0.03, duration: 0.3 }}
            onClick={() => onAlbumClick(album)}
            onContextMenu={(e) => {
              if (!onAlbumContextMenu) return;
              e.preventDefault();
              onAlbumContextMenu(e, album);
            }}
            className="group cursor-pointer rounded-2xl px-6 py-4 bg-white/0 hover:bg-white/5 border border-transparent hover:border-white/10 transition-all duration-300 flex items-center gap-4"
            style={{
              boxShadow: '0 0 0 0 transparent',
            }}
            whileHover={{
              boxShadow: `0 0 30px ${album.dominantColor}20`,
            }}
          >
            {/* Album Cover Thumbnail */}
            <div className="relative w-16 h-16 rounded-xl overflow-hidden flex-shrink-0 bg-gradient-to-br from-white/5 to-white/0 border border-white/10">
              <img
                src={album.cover}
                alt={album.title}
                className="w-full h-full object-cover"
              />
            </div>

            {/* Album Info */}
            <div className="flex-1 min-w-0">
              <h3 className="text-white group-hover:text-[#3B82F6] transition-colors duration-300 truncate">
                {album.title}
              </h3>
              <p className="text-sm text-white/60 truncate">
                {album.artist}
              </p>
            </div>

            {/* Metadata */}
            <div className="flex items-center gap-8 text-sm text-white/50">
              <div className="flex gap-2">
                {album.genre.slice(0, 2).map((genre) => (
                  <span
                    key={genre}
                    className="px-2 py-1 rounded-lg bg-white/5 border border-white/10"
                  >
                    {genre}
                  </span>
                ))}
              </div>
              
              <div className="flex items-center gap-6">
                <span>{album.tracks.length} tracks</span>
                <span>{album.duration}</span>
                <span>{album.year}</span>
              </div>
            </div>

            {/* Arrow indicator */}
            <div className="opacity-0 group-hover:opacity-100 transition-opacity duration-300">
              <svg
                className="w-5 h-5 text-[#3B82F6]"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M9 5l7 7-7 7"
                />
              </svg>
            </div>
          </motion.div>
        ))}
      </div>
    </div>
  );
}
