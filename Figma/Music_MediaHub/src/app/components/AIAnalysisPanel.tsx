import { motion, AnimatePresence } from "motion/react";
import { X, Database, Music, Radio, Sparkles } from "lucide-react";
import { ImageWithFallback } from "./figma/ImageWithFallback";

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
  tracks?: number;
  duration?: string;
  songs?: Array<{
    id: string;
    title: string;
    duration: string;
    artist: string;
  }>;
  detailsStatusText?: string;
  mbRelease?: {
    id?: string;
    date?: string;
    country?: string;
    label?: string;
    barcode?: string;
    status?: string;
    packaging?: string;
  };
}

interface ProviderStatus {
  name: string;
  isConfigured: boolean;
}

interface AIAnalysisPanelProps {
  album: Album | null;
  providers: ProviderStatus[];
  onClose: () => void;
}

export function AIAnalysisPanel({ album, providers, onClose }: AIAnalysisPanelProps) {
  if (!album) return null;
  const configuredProviders = providers.filter((provider) => provider.isConfigured);
  
  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="bg-gradient-to-br from-gray-900 via-black to-gray-900 rounded-3xl border border-white/10 max-w-4xl w-full max-h-[90vh] overflow-auto"
          initial={{ scale: 0.9, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0.9, opacity: 0 }}
          onClick={(e) => e.stopPropagation()}
        >
          {/* Header with album art */}
          <div className="relative h-64 overflow-hidden">
            <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black z-10" />
            <ImageWithFallback
              src={album.artwork}
              alt={album.title}
              className="w-full h-full object-cover blur-2xl scale-110 opacity-50"
            />
            
            <div className="absolute inset-0 z-20 flex items-end p-8">
              <div className="flex items-end gap-6">
                <div className="relative w-40 h-40 flex-shrink-0">
                  <div className="absolute inset-0 bg-gradient-to-r from-cyan-500/30 to-purple-500/30 rounded-2xl blur-xl" />
                  <ImageWithFallback
                    src={album.artwork}
                    alt={album.title}
                    className="relative w-full h-full object-cover rounded-2xl border border-white/20"
                  />
                </div>
                
                <div className="flex-1">
                  <div className="flex items-center gap-2 mb-2">
                    <Sparkles className="w-5 h-5 text-cyan-400" />
                    <span className="text-cyan-400 text-sm font-semibold">Atlas Metadata Analysis</span>
                  </div>
                  <h2 className="text-3xl font-bold text-white mb-2">{album.title}</h2>
                  <p className="text-xl text-gray-300">{album.artist}</p>
                  <div className="flex items-center gap-3 mt-2">
                    <span className="text-sm text-gray-400">{album.year}</span>
                    <span className="text-gray-600">•</span>
                    <span className="px-3 py-1 rounded-full bg-purple-500/20 text-purple-400 text-sm border border-purple-500/30">
                      {album.genre}
                    </span>
                  </div>
                </div>
              </div>
            </div>
            
            <button
              className="absolute top-4 right-4 z-30 w-10 h-10 rounded-full bg-black/60 backdrop-blur-sm flex items-center justify-center text-white hover:bg-black/80 transition-colors"
              onClick={onClose}
            >
              <X className="w-5 h-5" />
            </button>
          </div>
          
          {/* Content */}
          <div className="p-8 space-y-8">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                <div className="text-sm text-gray-400 mb-2">Tracks</div>
                <div className="text-2xl font-semibold text-white">{album.tracks ?? album.songs?.length ?? 0}</div>
              </div>
              <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                <div className="text-sm text-gray-400 mb-2">Runtime</div>
                <div className="text-2xl font-semibold text-white">{album.duration ?? "0:00"}</div>
              </div>
              <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                <div className="text-sm text-gray-400 mb-2">Metadata Status</div>
                <div className="text-sm font-medium text-cyan-300">{album.detailsStatusText || "Library only"}</div>
              </div>
            </div>

            <div>
              <h3 className="text-white font-semibold mb-3 flex items-center gap-2">
                <Database className="w-5 h-5 text-cyan-400" />
                Connected Music APIs
              </h3>
              <div className="flex flex-wrap gap-2">
                {providers.map((provider) => (
                  <div
                    key={provider.name}
                    className={`px-4 py-2 rounded-full border text-sm ${
                      provider.isConfigured
                        ? "bg-cyan-500/15 border-cyan-500/30 text-cyan-300"
                        : "bg-white/5 border-white/10 text-gray-500"
                    }`}
                  >
                    {provider.name}
                  </div>
                ))}
              </div>
              <p className="text-xs text-gray-500 mt-3">{configuredProviders.length} provider(s) available to enrich this album.</p>
            </div>

            <div>
              <h3 className="text-white font-semibold mb-3 flex items-center gap-2">
                <Radio className="w-5 h-5 text-purple-400" />
                Release Metadata
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                  <div className="text-sm text-gray-400 mb-2">Release Date</div>
                  <div className="text-white">{album.mbRelease?.date || "Unknown"}</div>
                </div>
                <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                  <div className="text-sm text-gray-400 mb-2">Country</div>
                  <div className="text-white">{album.mbRelease?.country || "Unknown"}</div>
                </div>
                <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                  <div className="text-sm text-gray-400 mb-2">Label</div>
                  <div className="text-white">{album.mbRelease?.label || "Unknown"}</div>
                </div>
                <div className="p-4 rounded-xl bg-white/5 border border-white/10">
                  <div className="text-sm text-gray-400 mb-2">Packaging</div>
                  <div className="text-white">{album.mbRelease?.packaging || "Unknown"}</div>
                </div>
              </div>
            </div>

            <div>
              <h3 className="text-white font-semibold mb-3 flex items-center gap-2">
                <Music className="w-5 h-5 text-cyan-400" />
                Track Preview
              </h3>
              <div className="space-y-2">
                {(album.songs ?? []).slice(0, 8).map((song, index) => (
                  <div key={song.id} className="p-3 rounded-xl bg-white/5 border border-white/10 flex items-center justify-between gap-4">
                    <div className="min-w-0">
                      <div className="text-sm text-white truncate">{index + 1}. {song.title}</div>
                      <div className="text-xs text-gray-500 truncate">{song.artist}</div>
                    </div>
                    <div className="text-xs text-gray-400">{song.duration}</div>
                  </div>
                ))}
                {(album.songs?.length ?? 0) === 0 && (
                  <div className="p-3 rounded-xl bg-white/5 border border-white/10 text-sm text-gray-400">
                    Track metadata will appear here after Atlas finishes loading the album details.
                  </div>
                )}
              </div>
            </div>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
