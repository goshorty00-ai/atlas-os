import { motion, AnimatePresence } from "motion/react";
import { useState, useEffect } from "react";
import { ChevronLeft, ChevronRight, Play, Pause } from "lucide-react";
import { ImageWithFallback } from "./figma/ImageWithFallback";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
}

interface CarouselViewProps {
  albums: Album[];
  onAlbumClick?: (album: Album) => void;
}

export function CarouselView({ albums, onAlbumClick }: CarouselViewProps) {
  const [currentIndex, setCurrentIndex] = useState(2);
  const [isAutoplay, setIsAutoplay] = useState(true);
  const [isFlipped, setIsFlipped] = useState<Set<number>>(new Set());
  
  const visibleAlbums = 5;
  const halfVisible = Math.floor(visibleAlbums / 2);
  const cardSize = 280; // Consistent size for all cards
  
  const getVisibleAlbums = () => {
    const items = [];
    for (let i = -halfVisible; i <= halfVisible; i++) {
      const index = (currentIndex + i + albums.length) % albums.length;
      items.push({
        album: albums[index],
        offset: i,
        actualIndex: index,
      });
    }
    return items;
  };
  
  const next = () => {
    setCurrentIndex((prev) => (prev + 1) % albums.length);
  };
  
  const previous = () => {
    setCurrentIndex((prev) => (prev - 1 + albums.length) % albums.length);
  };

  const toggleFlip = (index: number) => {
    setIsFlipped((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(index)) {
        newSet.delete(index);
      } else {
        newSet.add(index);
      }
      return newSet;
    });
  };
  
  // Auto-rotate
  useEffect(() => {
    if (!isAutoplay) return;
    
    const interval = setInterval(next, 4000);
    return () => clearInterval(interval);
  }, [isAutoplay, currentIndex]);
  
  return (
    <div className="relative h-[600px] flex items-center justify-center overflow-hidden">
      {/* Background glow */}
      <div className="absolute inset-0 bg-gradient-radial from-cyan-500/10 via-purple-500/5 to-transparent" />
      
      {/* Autoplay toggle */}
      <div className="absolute top-4 right-4 z-20">
        <motion.button
          className={`px-4 py-2 rounded-xl flex items-center gap-2 backdrop-blur-xl border transition-colors ${
            isAutoplay
              ? "bg-cyan-500/20 border-cyan-500/50 text-cyan-400"
              : "bg-white/10 border-white/20 text-white"
          }`}
          whileHover={{ scale: 1.05 }}
          whileTap={{ scale: 0.95 }}
          onClick={() => setIsAutoplay(!isAutoplay)}
        >
          {isAutoplay ? (
            <>
              <Pause className="w-4 h-4" />
              <span className="text-sm font-medium">Autoplay</span>
            </>
          ) : (
            <>
              <Play className="w-4 h-4" />
              <span className="text-sm font-medium">Autoplay</span>
            </>
          )}
        </motion.button>
      </div>
      
      {/* 3D Carousel with consistent sizing */}
      <div className="relative w-full h-full flex items-center justify-center perspective-2000">
        {getVisibleAlbums().map(({ album, offset, actualIndex }) => {
          const isCenter = offset === 0;
          const distance = Math.abs(offset);
          const isFlippedCard = isFlipped.has(actualIndex);
          
          return (
            <motion.div
              key={`${album.id}-${offset}`}
              className="absolute cursor-pointer"
              initial={false}
              animate={{
                x: offset * (cardSize + 40), // Consistent spacing
                z: -distance * 200,
                scale: isCenter ? 1 : 0.85 - distance * 0.05,
                rotateY: isFlippedCard ? 180 : offset * -12,
                opacity: distance > 2 ? 0 : 1 - distance * 0.15,
              }}
              transition={{ 
                duration: 0.6, 
                ease: "easeOut",
                rotateY: { duration: 0.6 }
              }}
              style={{
                transformStyle: "preserve-3d",
              }}
              onClick={() => isCenter && toggleFlip(actualIndex)}
            >
              <div
                style={{
                  width: cardSize,
                  height: cardSize,
                  transformStyle: "preserve-3d",
                }}
              >
                {/* Glow for center item */}
                {isCenter && (
                  <motion.div
                    className="absolute -inset-12 bg-gradient-to-b from-cyan-500/30 via-purple-500/30 to-orange-500/20 rounded-3xl blur-3xl"
                    animate={{
                      opacity: [0.5, 0.8, 0.5],
                      scale: [1, 1.1, 1],
                    }}
                    transition={{
                      duration: 3,
                      repeat: Infinity,
                      ease: "easeInOut",
                    }}
                  />
                )}
                
                {/* Front side */}
                <motion.div
                  className={`absolute inset-0 bg-white/10 backdrop-blur-xl rounded-2xl overflow-hidden border backface-hidden ${
                    isCenter ? "border-cyan-500/50 shadow-2xl shadow-cyan-500/30" : "border-white/10"
                  }`}
                  style={{
                    backfaceVisibility: "hidden",
                  }}
                >
                  <ImageWithFallback
                    src={album.artwork}
                    alt={album.title}
                    className="w-full h-full object-cover"
                  />
                  
                  {/* Gradient overlay */}
                  <div className="absolute inset-0 bg-gradient-to-t from-black via-black/50 to-transparent opacity-0 hover:opacity-100 transition-opacity duration-300">
                    <div className="absolute bottom-0 inset-x-0 p-6">
                      <h3 className="text-white font-semibold text-lg truncate">{album.title}</h3>
                      <p className="text-gray-300 text-sm truncate">{album.artist}</p>
                      <div className="flex items-center gap-2 mt-2">
                        <span className="text-xs text-gray-400">{album.year}</span>
                        <span className="text-xs text-gray-600">•</span>
                        <span className="text-xs px-2 py-0.5 rounded-full bg-purple-500/20 text-purple-400 border border-purple-500/30">
                          {album.genre}
                        </span>
                      </div>
                    </div>
                  </div>
                  
                  {/* Play button for center */}
                  {isCenter && (
                    <motion.div
                      className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-16 h-16 rounded-full bg-cyan-500/90 backdrop-blur-sm flex items-center justify-center shadow-lg shadow-cyan-500/50"
                      whileHover={{ scale: 1.1 }}
                      whileTap={{ scale: 0.95 }}
                      onClick={(e) => {
                        e.stopPropagation();
                        onAlbumClick?.(album);
                      }}
                    >
                      <Play className="w-7 h-7 text-black fill-black ml-1" />
                    </motion.div>
                  )}

                  {/* Flip hint */}
                  {isCenter && (
                    <div className="absolute top-4 right-4 px-3 py-1 rounded-full bg-black/60 backdrop-blur-sm border border-white/20">
                      <span className="text-xs text-white">Click to flip</span>
                    </div>
                  )}
                </motion.div>

                {/* Back side */}
                <motion.div
                  className={`absolute inset-0 bg-gradient-to-br from-gray-900 via-black to-gray-900 backdrop-blur-xl rounded-2xl overflow-hidden border p-6 ${
                    isCenter ? "border-cyan-500/50 shadow-2xl shadow-cyan-500/30" : "border-white/10"
                  }`}
                  style={{
                    backfaceVisibility: "hidden",
                    transform: "rotateY(180deg)",
                  }}
                >
                  <div className="h-full flex flex-col">
                    <h3 className="text-white font-bold text-xl mb-2">{album.title}</h3>
                    <p className="text-gray-300 text-sm mb-4">{album.artist}</p>
                    
                    <div className="space-y-3 flex-1">
                      <div className="p-3 rounded-lg bg-white/5 border border-white/10">
                        <p className="text-xs text-gray-400 mb-1">Year</p>
                        <p className="text-white font-semibold">{album.year}</p>
                      </div>
                      
                      <div className="p-3 rounded-lg bg-white/5 border border-white/10">
                        <p className="text-xs text-gray-400 mb-1">Genre</p>
                        <p className="text-white font-semibold">{album.genre}</p>
                      </div>
                      
                      <div className="p-3 rounded-lg bg-gradient-to-r from-cyan-500/20 to-purple-500/20 border border-cyan-500/30">
                        <p className="text-xs text-gray-400 mb-1">AI Score</p>
                        <p className="text-2xl font-bold bg-gradient-to-r from-cyan-400 to-purple-400 bg-clip-text text-transparent">
                          {(Math.random() * 2 + 8).toFixed(1)}
                        </p>
                      </div>
                    </div>

                    <motion.button
                      className="w-full py-3 rounded-xl bg-cyan-500 text-black font-semibold mt-4"
                      whileHover={{ scale: 1.02 }}
                      whileTap={{ scale: 0.98 }}
                      onClick={(e) => {
                        e.stopPropagation();
                        onAlbumClick?.(album);
                      }}
                    >
                      View Details
                    </motion.button>
                  </div>
                </motion.div>
              </div>
            </motion.div>
          );
        })}
      </div>
      
      {/* Navigation */}
      <motion.button
        className="absolute left-8 top-1/2 -translate-y-1/2 w-14 h-14 rounded-full bg-white/10 backdrop-blur-sm flex items-center justify-center border border-white/20 text-white hover:bg-white/20 transition-colors z-10"
        whileHover={{ scale: 1.1 }}
        whileTap={{ scale: 0.95 }}
        onClick={previous}
      >
        <ChevronLeft className="w-7 h-7" />
      </motion.button>
      
      <motion.button
        className="absolute right-8 top-1/2 -translate-y-1/2 w-14 h-14 rounded-full bg-white/10 backdrop-blur-sm flex items-center justify-center border border-white/20 text-white hover:bg-white/20 transition-colors z-10"
        whileHover={{ scale: 1.1 }}
        whileTap={{ scale: 0.95 }}
        onClick={next}
      >
        <ChevronRight className="w-7 h-7" />
      </motion.button>
      
      {/* Indicators */}
      <div className="absolute bottom-8 left-1/2 -translate-x-1/2 flex gap-2 z-10">
        {albums.slice(0, Math.min(albums.length, 10)).map((_, index) => (
          <button
            key={index}
            className={`h-2 rounded-full transition-all ${
              index === currentIndex % albums.length
                ? "bg-cyan-500 w-8"
                : "bg-white/30 hover:bg-white/50 w-2"
            }`}
            onClick={() => setCurrentIndex(index)}
          />
        ))}
      </div>
    </div>
  );
}
