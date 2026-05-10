import { motion, useMotionValue, useTransform } from 'motion/react';
import { Album } from '../data/albums';
import { useState, useEffect } from 'react';
import type React from 'react';

interface LibraryCarouselViewProps {
  albums: Album[];
  onAlbumClick: (album: Album) => void;
  onAlbumContextMenu?: (e: React.MouseEvent, album: Album) => void;
}

export function LibraryCarouselView({ albums, onAlbumClick, onAlbumContextMenu }: LibraryCarouselViewProps) {
  const [centerIndex, setCenterIndex] = useState(0);
  const [mouseX, setMouseX] = useState(0);

  const handlePrev = () => {
    setCenterIndex((prev) => (prev > 0 ? prev - 1 : albums.length - 1));
  };

  const handleNext = () => {
    setCenterIndex((prev) => (prev < albums.length - 1 ? prev + 1 : 0));
  };

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft') handlePrev();
      if (e.key === 'ArrowRight') handleNext();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const handleMouseMove = (e: React.MouseEvent) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const x = e.clientX - rect.left - rect.width / 2;
    setMouseX(x);
  };

  return (
    <div 
      className="relative h-full flex items-center justify-center overflow-hidden px-32"
      onMouseMove={handleMouseMove}
    >
      {/* Albums */}
      <div className="relative w-full h-[500px] flex items-center justify-center">
        {albums.map((album, index) => {
          const offset = index - centerIndex;
          const absOffset = Math.abs(offset);
          const isCenter = offset === 0;
          
          // Calculate position
          const translateX = offset * 280;
          const parallax = isCenter ? mouseX * 0.02 : 0;
          
          // Calculate scale
          const scale = isCenter ? 1.2 : Math.max(0.7, 1 - absOffset * 0.15);
          
          // Calculate opacity and blur
          const opacity = Math.max(0.3, 1 - absOffset * 0.3);
          const blur = absOffset > 0 ? Math.min(absOffset * 2, 8) : 0;
          const zIndex = 100 - absOffset;

          return (
            <motion.div
              key={album.id}
              className="absolute cursor-pointer"
              style={{
                zIndex,
              }}
              animate={{
                x: translateX + parallax,
                scale,
                opacity,
                filter: `blur(${blur}px)`,
              }}
              transition={{
                type: 'spring',
                stiffness: 300,
                damping: 30,
              }}
              onClick={() => {
                if (isCenter) {
                  onAlbumClick(album);
                } else {
                  setCenterIndex(index);
                }
              }}
              onContextMenu={(e) => {
                if (!onAlbumContextMenu) return;
                e.preventDefault();
                onAlbumContextMenu(e, album);
              }}
            >
              <div className="relative w-64 h-64 rounded-2xl overflow-hidden bg-gradient-to-br from-white/5 to-white/0 backdrop-blur-sm border border-white/10">
                <img
                  src={album.cover}
                  alt={album.title}
                  className="w-full h-full object-cover"
                />
                {isCenter && (
                  <div 
                    className="absolute inset-0 pointer-events-none"
                    style={{
                      boxShadow: `inset 0 0 60px ${album.dominantColor}60, 0 0 80px ${album.dominantColor}40`,
                    }}
                  />
                )}
              </div>
              
              {isCenter && (
                <motion.div
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="mt-6 text-center"
                >
                  <h3 className="text-xl text-white mb-1">{album.title}</h3>
                  <p className="text-white/70">{album.artist}</p>
                  <p className="text-sm text-white/50 mt-2">{album.year} • {album.genre.join(', ')}</p>
                </motion.div>
              )}
            </motion.div>
          );
        })}
      </div>

      {/* Navigation buttons */}
      <button
        onClick={handlePrev}
        className="absolute left-8 top-1/2 -translate-y-1/2 w-12 h-12 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 hover:border-[#3B82F6]/50 transition-all duration-300 z-50"
        style={{
          boxShadow: '0 0 20px rgba(59, 130, 246, 0.2)',
        }}
      >
        <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
        </svg>
      </button>
      
      <button
        onClick={handleNext}
        className="absolute right-8 top-1/2 -translate-y-1/2 w-12 h-12 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 hover:border-[#3B82F6]/50 transition-all duration-300 z-50"
        style={{
          boxShadow: '0 0 20px rgba(59, 130, 246, 0.2)',
        }}
      >
        <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
        </svg>
      </button>

      {/* Indicators */}
      <div className="absolute bottom-8 left-1/2 -translate-x-1/2 flex gap-2">
        {albums.map((_, index) => (
          <button
            key={index}
            onClick={() => setCenterIndex(index)}
            className={`w-2 h-2 rounded-full transition-all duration-300 ${
              index === centerIndex 
                ? 'w-8 bg-[#3B82F6]' 
                : 'bg-white/30 hover:bg-white/50'
            }`}
          />
        ))}
      </div>
    </div>
  );
}
