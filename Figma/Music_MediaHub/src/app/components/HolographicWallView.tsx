import { motion } from "motion/react";
import { Play, Plus } from "lucide-react";
import { useState, useRef } from "react";
import { ImageWithFallback } from "./figma/ImageWithFallback";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "./ui/context-menu";

type AlbumAction = "play" | "playlist" | "lyrics" | "edit" | "cover" | "aiCover" | "optimize" | "refresh" | "openFolder" | "remove";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
}

interface HolographicWallViewProps {
  albums: Album[];
  onAlbumClick?: (album: Album) => void;
  onAlbumAction?: (album: Album, action: AlbumAction) => void;
}

export function HolographicWallView({ albums, onAlbumClick, onAlbumAction }: HolographicWallViewProps) {
  const [scrollY, setScrollY] = useState(0);
  const containerRef = useRef<HTMLDivElement>(null);
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);

  const handleScroll = (e: React.UIEvent<HTMLDivElement>) => {
    const target = e.target as HTMLDivElement;
    setScrollY(target.scrollTop);
  };

  return (
    <div className="relative h-[700px] rounded-2xl overflow-hidden">
      {/* Instructions */}
      <div className="absolute top-4 left-4 z-20 bg-black/60 backdrop-blur-sm rounded-xl px-4 py-2 border border-white/10">
        <p className="text-sm text-gray-300">
          <span className="text-cyan-400 font-semibold">Scroll</span> to explore • <span className="text-cyan-400 font-semibold">Hover</span> to highlight
        </p>
      </div>

      {/* Scrollable wall container */}
      <div
        ref={containerRef}
        className="h-full overflow-y-auto perspective-2000 px-8 py-20"
        onScroll={handleScroll}
        style={{
          scrollbarWidth: "thin",
          scrollbarColor: "rgba(6, 182, 212, 0.3) transparent",
        }}
      >
        <motion.div
          className="grid grid-cols-2 gap-4 sm:grid-cols-4 sm:gap-5 lg:grid-cols-6 xl:grid-cols-8 xl:gap-6 transform-gpu min-h-[1400px]"
          style={{
            transformStyle: "preserve-3d",
          }}
        >
          {albums.map((album, index) => {
            const row = Math.floor(index / 8);
            const col = index % 8;
            
            // Calculate wave effect based on position and scroll
            const waveOffset = Math.sin((row * 0.3 + col * 0.2 + scrollY * 0.003)) * 50;
            const rotateX = Math.sin((row * 0.2 + scrollY * 0.002)) * 10;
            const rotateY = Math.cos((col * 0.3 + scrollY * 0.002)) * 10;
            
            const isHovered = hoveredIndex === index;

            return (
              <ContextMenu key={album.id}>
                <ContextMenuTrigger asChild>
                  <motion.button
                    type="button"
                    className="aspect-square cursor-pointer relative text-left"
                    initial={{ opacity: 0, scale: 0, rotateY: -180 }}
                    animate={{
                      opacity: 1,
                      scale: isHovered ? 1.16 : 1,
                      z: waveOffset + (isHovered ? 100 : 0),
                      rotateX: isHovered ? 0 : rotateX,
                      rotateY: isHovered ? 0 : rotateY,
                    }}
                    transition={{
                      delay: index * 0.01,
                      duration: 0.5,
                      z: { duration: 0.3 },
                      scale: { duration: 0.3 },
                    }}
                    style={{
                      transformStyle: "preserve-3d",
                    }}
                    onMouseEnter={() => setHoveredIndex(index)}
                    onMouseLeave={() => setHoveredIndex(null)}
                    onClick={() => onAlbumClick?.(album)}
                  >
                {/* Holographic glow effect */}
                <motion.div
                  className="pointer-events-none absolute -inset-2 rounded-xl blur-xl opacity-0"
                  animate={{
                    opacity: isHovered ? 0.8 : 0,
                    background: isHovered
                      ? `radial-gradient(circle, rgba(6, 182, 212, 0.4), rgba(168, 85, 247, 0.4))`
                      : "transparent",
                  }}
                  transition={{ duration: 0.3 }}
                />

                {/* Album card */}
                <div
                  className={`relative w-full h-full rounded-xl overflow-hidden border transition-all duration-300 ${
                    isHovered
                      ? "border-cyan-500 shadow-2xl shadow-cyan-500/50"
                      : "border-white/20 hover:border-cyan-500/50"
                  }`}
                >
                  <ImageWithFallback src={album.artwork} alt={album.title} className="w-full h-full object-cover" />

                  {/* Scanline effect */}
                  <motion.div
                    className="absolute inset-0 pointer-events-none"
                    style={{
                      background: `repeating-linear-gradient(
                        0deg,
                        rgba(6, 182, 212, 0.03) 0px,
                        transparent 2px,
                        transparent 4px,
                        rgba(6, 182, 212, 0.03) 6px
                      )`,
                    }}
                    animate={{
                      opacity: isHovered ? 1 : 0.3,
                    }}
                  />

                  {/* Holographic shimmer */}
                  <motion.div
                    className="absolute inset-0 pointer-events-none"
                    style={{
                      background: `linear-gradient(
                        ${(index * 45) % 360}deg,
                        transparent 0%,
                        rgba(6, 182, 212, 0.1) 50%,
                        transparent 100%
                      )`,
                    }}
                    animate={{
                      x: isHovered ? ["-100%", "100%"] : 0,
                      opacity: isHovered ? [0, 1, 0] : 0,
                    }}
                    transition={{
                      duration: 1.5,
                      repeat: isHovered ? Infinity : 0,
                      ease: "linear",
                    }}
                  />

                  {/* Info overlay on hover */}
                  <motion.div
                    className="absolute inset-0 bg-gradient-to-t from-black via-black/70 to-transparent flex flex-col items-center justify-end p-4"
                    initial={{ opacity: 0 }}
                    animate={{ opacity: isHovered ? 1 : 0 }}
                    transition={{ duration: 0.2 }}
                  >
                    <h3 className="text-white font-semibold text-sm text-center truncate w-full">
                      {album.title}
                    </h3>
                    <p className="text-gray-300 text-xs text-center truncate w-full">
                      {album.artist}
                    </p>
                    <div className="flex items-center gap-2 mt-2">
                      <span className="text-xs px-2 py-0.5 rounded-full bg-cyan-500/20 text-cyan-400 border border-cyan-500/30">
                        {album.year}
                      </span>
                    </div>

                    <div className="mt-3 flex items-center gap-3">
                      <motion.button
                        type="button"
                        className="flex h-11 w-11 items-center justify-center rounded-full bg-cyan-400 text-black shadow-lg shadow-cyan-500/40"
                        whileHover={{ scale: 1.08 }}
                        whileTap={{ scale: 0.96 }}
                        onClick={(event) => {
                          event.stopPropagation();
                          onAlbumAction?.(album, "play");
                        }}
                      >
                        <Play className="h-4 w-4 fill-current" />
                      </motion.button>
                      <motion.button
                        type="button"
                        className="flex h-10 w-10 items-center justify-center rounded-full bg-white/12 text-white"
                        whileHover={{ scale: 1.08 }}
                        whileTap={{ scale: 0.96 }}
                        onClick={(event) => {
                          event.stopPropagation();
                          onAlbumAction?.(album, "playlist");
                        }}
                      >
                        <Plus className="h-4 w-4" />
                      </motion.button>
                    </div>
                  </motion.div>

                  {/* Corner accents */}
                  {isHovered && (
                    <>
                      <motion.div
                        className="absolute top-0 left-0 w-8 h-8 border-t-2 border-l-2 border-cyan-400"
                        initial={{ scale: 0, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        transition={{ duration: 0.2 }}
                      />
                      <motion.div
                        className="absolute top-0 right-0 w-8 h-8 border-t-2 border-r-2 border-cyan-400"
                        initial={{ scale: 0, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        transition={{ duration: 0.2, delay: 0.05 }}
                      />
                      <motion.div
                        className="absolute bottom-0 left-0 w-8 h-8 border-b-2 border-l-2 border-cyan-400"
                        initial={{ scale: 0, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        transition={{ duration: 0.2, delay: 0.1 }}
                      />
                      <motion.div
                        className="absolute bottom-0 right-0 w-8 h-8 border-b-2 border-r-2 border-cyan-400"
                        initial={{ scale: 0, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        transition={{ duration: 0.2, delay: 0.15 }}
                      />
                    </>
                  )}
                </div>

                {/* Floating particles around hovered card */}
                {isHovered && (
                  <>
                    {[...Array(6)].map((_, i) => (
                      <motion.div
                        key={i}
                        className="pointer-events-none absolute h-1 w-1 rounded-full bg-cyan-400"
                        initial={{
                          x: "50%",
                          y: "50%",
                          opacity: 0,
                        }}
                        animate={{
                          x: `${50 + Math.cos((i / 6) * Math.PI * 2) * 150}%`,
                          y: `${50 + Math.sin((i / 6) * Math.PI * 2) * 150}%`,
                          opacity: [0, 1, 0],
                        }}
                        transition={{
                          duration: 2,
                          repeat: Infinity,
                          delay: i * 0.2,
                        }}
                      />
                    ))}
                  </>
                )}
                  </motion.button>
                </ContextMenuTrigger>
                <ContextMenuContent className="w-56">
                  <ContextMenuLabel>{album.title}</ContextMenuLabel>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "play")}>Play album</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "playlist")}>Create playlist</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "lyrics")}>Find lyrics</ContextMenuItem>
                  <ContextMenuSeparator />
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "edit")}>Edit metadata</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "cover")}>Set custom cover</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "aiCover")}>Generate AI cover</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "optimize")}>AI optimize</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "refresh")}>Refresh metadata</ContextMenuItem>
                  <ContextMenuItem onClick={() => onAlbumAction?.(album, "openFolder")}>Open folder</ContextMenuItem>
                  <ContextMenuSeparator />
                  <ContextMenuItem className="text-red-300 focus:text-red-200" onClick={() => onAlbumAction?.(album, "remove")}>Remove album</ContextMenuItem>
                </ContextMenuContent>
              </ContextMenu>
            );
          })}
        </motion.div>
      </div>

      {/* Gradient overlays for depth */}
      <div className="absolute top-0 inset-x-0 h-32 bg-gradient-to-b from-black to-transparent pointer-events-none z-10" />
      <div className="absolute bottom-0 inset-x-0 h-32 bg-gradient-to-t from-black to-transparent pointer-events-none z-10" />
    </div>
  );
}
