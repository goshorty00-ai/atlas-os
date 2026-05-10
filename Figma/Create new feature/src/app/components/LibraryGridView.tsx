import { motion } from 'motion/react';
import { Album } from '../data/albums';
import type React from 'react';

interface LibraryGridViewProps {
  albums: Album[];
  onAlbumClick: (album: Album) => void;
  onAlbumContextMenu?: (e: React.MouseEvent, album: Album) => void;
}

export function LibraryGridView({ albums, onAlbumClick, onAlbumContextMenu }: LibraryGridViewProps) {
  return (
    <div className="grid grid-cols-5 gap-8 p-8">
      {albums.map((album, index) => (
        <motion.div
          key={album.id}
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: index * 0.05, duration: 0.4 }}
          whileHover={{ scale: 1.03 }}
          className="group cursor-pointer"
          onClick={() => onAlbumClick(album)}
          onContextMenu={(e) => {
            if (!onAlbumContextMenu) return;
            e.preventDefault();
            onAlbumContextMenu(e, album);
          }}
        >
          <div className="relative aspect-square rounded-2xl overflow-hidden bg-gradient-to-br from-white/5 to-white/0 backdrop-blur-sm border border-white/10">
            <img
              src={album.cover}
              alt={album.title}
              className="w-full h-full object-cover transition-all duration-500 group-hover:scale-105"
            />
            <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/0 to-black/0 opacity-0 group-hover:opacity-100 transition-all duration-300">
              <div className="absolute bottom-4 left-4 right-4">
                <h3 className="text-white mb-1 line-clamp-1">{album.title}</h3>
                <p className="text-sm text-white/70 line-clamp-1">{album.artist}</p>
              </div>
            </div>
            {/* Glass glow effect on hover */}
            <div 
              className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none"
              style={{
                boxShadow: `inset 0 0 40px ${album.dominantColor}40, 0 0 40px ${album.dominantColor}20`,
              }}
            />
          </div>
        </motion.div>
      ))}
    </div>
  );
}
