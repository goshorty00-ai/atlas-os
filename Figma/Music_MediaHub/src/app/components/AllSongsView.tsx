import { motion } from "motion/react";
import { Play, MoreVertical, Heart, Plus } from "lucide-react";
import { useState } from "react";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "./ui/context-menu";

interface Song {
  id: string;
  title: string;
  artist: string;
  album: string;
  duration: string;
  artwork: string;
  genre: string;
  year: number;
  filePath?: string;
  isLiked?: boolean;
}

interface AllSongsViewProps {
  songs: Song[];
  onSongClick?: (song: Song) => void;
  onPlaySong?: (song: Song) => void;
  onOpenLyrics?: (song: Song) => void;
  onCreatePlaylist?: (song: Song) => void;
}

export function AllSongsView({ songs, onSongClick, onPlaySong, onOpenLyrics, onCreatePlaylist }: AllSongsViewProps) {
  const [hoveredRow, setHoveredRow] = useState<string | null>(null);
  const [likedSongs, setLikedSongs] = useState<Set<string>>(new Set());

  const toggleLike = (songId: string) => {
    setLikedSongs((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(songId)) {
        newSet.delete(songId);
      } else {
        newSet.add(songId);
      }
      return newSet;
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-semibold text-white">All Songs</h2>
          <p className="text-gray-400 text-sm mt-1">{songs.length} tracks</p>
        </div>
      </div>

      {/* Table Header */}
      <div className="grid grid-cols-[40px_2fr_2fr_1.5fr_100px_80px_60px] gap-4 px-4 py-3 border-b border-white/10 text-sm text-gray-400">
        <div>#</div>
        <div>Title</div>
        <div>Artist</div>
        <div>Album</div>
        <div>Genre</div>
        <div>Duration</div>
        <div></div>
      </div>

      {/* Song List */}
      <div className="space-y-1">
        {songs.map((song, index) => (
          <ContextMenu key={song.id}>
            <ContextMenuTrigger>
              <motion.div
                className={`grid grid-cols-[40px_2fr_2fr_1.5fr_100px_80px_60px] gap-4 px-4 py-3 rounded-lg transition-all cursor-pointer ${
                  hoveredRow === song.id
                    ? "bg-white/10 border border-cyan-500/30"
                    : "bg-white/5 border border-transparent hover:bg-white/10"
                }`}
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: index * 0.01 }}
                onMouseEnter={() => setHoveredRow(song.id)}
                onMouseLeave={() => setHoveredRow(null)}
                onClick={() => onSongClick?.(song)}
              >
                <div className="flex items-center justify-center">
                  {hoveredRow === song.id ? (
                    <motion.button
                      className="w-8 h-8 rounded-full bg-cyan-500 flex items-center justify-center hover:bg-cyan-400 transition-colors"
                      whileHover={{ scale: 1.1 }}
                      whileTap={{ scale: 0.95 }}
                      onClick={(e) => {
                        e.stopPropagation();
                        onPlaySong?.(song);
                      }}
                    >
                      <Play className="w-4 h-4 text-black fill-black ml-0.5" />
                    </motion.button>
                  ) : (
                    <span className="text-gray-400">{index + 1}</span>
                  )}
                </div>

                <div className="flex items-center gap-3 min-w-0">
                  <div className="relative w-10 h-10 flex-shrink-0 rounded overflow-hidden">
                    <img
                      src={song.artwork}
                      alt={song.title}
                      className="w-full h-full object-cover"
                    />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p
                      className={`truncate font-medium transition-colors ${
                        hoveredRow === song.id ? "text-cyan-400" : "text-white"
                      }`}
                    >
                      {song.title}
                    </p>
                  </div>
                </div>

                <div className="flex items-center min-w-0">
                  <p className="text-gray-300 truncate">{song.artist}</p>
                </div>

                <div className="flex items-center min-w-0">
                  <p className="text-gray-400 truncate text-sm">{song.album}</p>
                </div>

                <div className="flex items-center">
                  <span className="px-2 py-1 rounded-full bg-purple-500/20 text-purple-400 text-xs border border-purple-500/30 truncate">
                    {song.genre}
                  </span>
                </div>

                <div className="flex items-center justify-center">
                  <span className="text-gray-400 text-sm">{song.duration}</span>
                </div>

                <div className="flex items-center gap-2">
                  <motion.button
                    className={`transition-colors ${
                      likedSongs.has(song.id) ? "text-pink-500" : "text-gray-400 hover:text-white"
                    }`}
                    whileHover={{ scale: 1.1 }}
                    whileTap={{ scale: 0.95 }}
                    onClick={(e) => {
                      e.stopPropagation();
                      toggleLike(song.id);
                    }}
                  >
                    <Heart
                      className="w-4 h-4"
                      fill={likedSongs.has(song.id) ? "currentColor" : "none"}
                    />
                  </motion.button>

                  <motion.button
                    className="text-gray-400 hover:text-white transition-colors"
                    whileHover={{ scale: 1.1 }}
                    whileTap={{ scale: 0.95 }}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <MoreVertical className="w-4 h-4" />
                  </motion.button>
                </div>
              </motion.div>
            </ContextMenuTrigger>
            <ContextMenuContent className="w-52">
              <ContextMenuLabel>{song.title}</ContextMenuLabel>
              <ContextMenuItem onClick={() => onPlaySong?.(song)}>Play track</ContextMenuItem>
              <ContextMenuItem onClick={() => onSongClick?.(song)}>Open album</ContextMenuItem>
              <ContextMenuItem onClick={() => onOpenLyrics?.(song)}>Find lyrics</ContextMenuItem>
              <ContextMenuSeparator />
              <ContextMenuItem onClick={() => onCreatePlaylist?.(song)}>Create album playlist</ContextMenuItem>
            </ContextMenuContent>
          </ContextMenu>
        ))}
      </div>
    </div>
  );
}
