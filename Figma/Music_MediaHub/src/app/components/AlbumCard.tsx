import { motion } from "motion/react";
import { FlipHorizontal, Play, Plus } from "lucide-react";
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

interface AlbumCardProps {
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
  progress?: number;
  aiScore?: number;
  isPlaying?: boolean;
  tracks?: number;
  duration?: string;
  songs?: Array<{
    id: string;
    title: string;
    artist: string;
    duration: string;
  }>;
  detailsStatusText?: string;
  mbRelease?: {
    date?: string;
    country?: string;
    label?: string;
    status?: string;
  };
  onClick?: () => void;
  onAction?: (action: AlbumAction) => void;
}

export function AlbumCard({
  artwork,
  title,
  artist,
  year,
  genre,
  progress = 0,
  aiScore = 0,
  isPlaying = false,
  tracks = 0,
  duration,
  songs,
  detailsStatusText,
  mbRelease,
  onClick,
  onAction,
}: AlbumCardProps) {
  const trackCount = tracks > 0 ? tracks : songs?.length ?? 0;
  const releaseHint = mbRelease?.date || duration || "Metadata loading";

  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <motion.div
          className="relative group cursor-pointer flex-shrink-0 w-[200px]"
          whileHover={{ scale: 1.05, y: -8 }}
          transition={{ duration: 0.3 }}
          onClick={onClick}
        >
          <div className="absolute inset-0 bg-gradient-to-b from-cyan-500/20 via-purple-500/20 to-transparent rounded-2xl blur-xl opacity-0 group-hover:opacity-100 transition-opacity duration-500" />

          <div className="relative min-h-[296px] overflow-hidden rounded-2xl border border-white/10 bg-white/5 backdrop-blur-xl transition-all duration-300 group-hover:border-cyan-500/50">
            <div className="relative aspect-square overflow-hidden">
              <ImageWithFallback
                src={artwork}
                alt={title}
                className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
              />

              {isPlaying && (
                <motion.div
                  className="absolute inset-0 bg-gradient-to-t from-cyan-500/30 via-transparent to-transparent"
                  animate={{ opacity: [0.3, 0.6, 0.3] }}
                  transition={{ duration: 2, repeat: Infinity }}
                />
              )}

              {progress > 0 && (
                <div className="absolute top-3 right-3">
                  <svg className="w-10 h-10 rotate-[-90deg]">
                    <circle
                      cx="20"
                      cy="20"
                      r="16"
                      stroke="rgba(255,255,255,0.2)"
                      strokeWidth="2"
                      fill="none"
                    />
                    <circle
                      cx="20"
                      cy="20"
                      r="16"
                      stroke="url(#progressGradient)"
                      strokeWidth="2"
                      fill="none"
                      strokeDasharray={`${progress * 100.5} 100.5`}
                      strokeLinecap="round"
                    />
                    <defs>
                      <linearGradient id="progressGradient">
                        <stop offset="0%" stopColor="#06b6d4" />
                        <stop offset="100%" stopColor="#a855f7" />
                      </linearGradient>
                    </defs>
                  </svg>
                </div>
              )}

              <button
                type="button"
                className="absolute right-3 top-3 rounded-full border border-white/15 bg-black/74 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.22em] text-white/88 transition hover:bg-black/88"
                onClick={(event) => {
                  event.stopPropagation();
                  onClick?.();
                }}
              >
                <span className="inline-flex items-center gap-1">
                  <FlipHorizontal className="h-3.5 w-3.5" />
                  Back cover
                </span>
              </button>

              <motion.div
                className="absolute inset-0 flex items-center justify-center gap-3 bg-black/60 backdrop-blur-sm opacity-0 transition-opacity duration-300 group-hover:opacity-100"
                initial={false}
              >
                <motion.button
                  className="flex h-12 w-12 items-center justify-center rounded-full bg-cyan-500 shadow-lg shadow-cyan-500/50 transition-colors hover:bg-cyan-400"
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.95 }}
                  onClick={(event) => {
                    event.stopPropagation();
                    onAction?.("play");
                  }}
                >
                  <Play className="h-5 w-5 fill-black text-black" />
                </motion.button>
                <motion.button
                  className="flex h-10 w-10 items-center justify-center rounded-full bg-white/10 transition-colors hover:bg-white/20"
                  whileHover={{ scale: 1.1 }}
                  whileTap={{ scale: 0.95 }}
                  onClick={(event) => {
                    event.stopPropagation();
                    onAction?.("playlist");
                  }}
                >
                  <Plus className="h-4 w-4 text-white" />
                </motion.button>
              </motion.div>
            </div>

            <div className="space-y-2 p-3">
              <div className="flex items-start justify-between gap-2">
                <h3 className="truncate text-sm font-medium text-white">{title}</h3>
                {aiScore > 0 && (
                  <div className="flex-shrink-0 rounded-full border border-cyan-500/30 bg-gradient-to-r from-cyan-500/20 to-purple-500/20 px-2 py-0.5">
                    <span className="text-[10px] font-semibold text-cyan-400">{aiScore}</span>
                  </div>
                )}
              </div>

              <p className="truncate text-xs text-gray-400">{artist}</p>

              <div className="flex items-center gap-2 text-[10px] text-gray-400">
                <span>{trackCount} tracks</span>
                <span className="text-gray-600">•</span>
                <span className="truncate">{releaseHint}</span>
              </div>

              <div className="flex items-center gap-2 text-[10px]">
                <span className="text-gray-500">{year}</span>
                <span className="text-gray-600">•</span>
                <span className="rounded-full border border-purple-500/20 bg-purple-500/10 px-2 py-0.5 text-purple-400">
                  {genre}
                </span>
              </div>

              {detailsStatusText ? (
                <div className="truncate text-[10px] uppercase tracking-[0.18em] text-cyan-200/72">{detailsStatusText}</div>
              ) : null}
            </div>
          </div>
        </motion.div>
      </ContextMenuTrigger>
      <ContextMenuContent className="w-56">
        <ContextMenuLabel>{title}</ContextMenuLabel>
        <ContextMenuItem onClick={() => onAction?.("play")}>Play album</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("playlist")}>Create playlist</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("lyrics")}>Find lyrics</ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={() => onAction?.("edit")}>Edit metadata</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("cover")}>Set custom cover</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("aiCover")}>Generate AI cover</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("optimize")}>AI optimize</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("refresh")}>Refresh metadata</ContextMenuItem>
        <ContextMenuItem onClick={() => onAction?.("openFolder")}>Open folder</ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem className="text-red-300 focus:text-red-200" onClick={() => onAction?.("remove")}>Remove album</ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}
