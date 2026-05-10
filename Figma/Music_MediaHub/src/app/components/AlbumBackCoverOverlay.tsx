import { AnimatePresence, motion } from "motion/react";
import { Disc3, Library, Music2, Radio, Sparkles, X } from "lucide-react";
import { ImageWithFallback } from "./figma/ImageWithFallback";

interface ProviderStatus {
  name: string;
  isConfigured: boolean;
  requiresKey?: boolean;
  status?: string;
}

interface Song {
  id: string;
  title: string;
  artist: string;
  album: string;
  duration: string;
  artwork: string;
  genre: string;
  year: number;
  audioUrl?: string;
  filePath?: string;
}

interface Album {
  id: string;
  artwork: string;
  title: string;
  artist: string;
  year: number;
  genre: string;
  aiScore?: number;
  tracks?: number;
  duration?: string;
  songs?: Song[];
  detailsStatusText?: string;
  summaryText?: string;
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

interface AlbumBackCoverOverlayProps {
  album: Album | null;
  providers: ProviderStatus[];
  onClose: () => void;
  onPlayAlbum: () => void;
  onRefresh: () => void;
  onPlayTrack: (song: Song) => void;
}

export function AlbumBackCoverOverlay({
  album,
  providers,
  onClose,
  onPlayAlbum,
  onRefresh,
  onPlayTrack,
}: AlbumBackCoverOverlayProps) {
  if (!album) return null;

  const tracks = album.songs ?? [];
  const connectedProviders = providers.filter((provider) => provider.isConfigured || provider.status === "available");
  const displayTrackCount = album.tracks ?? tracks.length ?? 0;
  const logoText = album.title.trim() || "Album";
  const heroStats = [
    { label: "Atlas", value: album.aiScore && album.aiScore > 0 ? album.aiScore.toFixed(1) : "Live" },
    { label: "Tracks", value: String(displayTrackCount) },
    { label: "Year", value: album.year ? String(album.year) : "Unknown" },
    { label: "Runtime", value: album.duration || "Unknown" },
  ];
  const summaryText = album.summaryText?.trim()
    || [
      album.mbRelease?.date ? `Released ${album.mbRelease.date}` : "",
      album.mbRelease?.country ? `in ${album.mbRelease.country}` : "",
      album.mbRelease?.label ? `through ${album.mbRelease.label}` : "",
      album.mbRelease?.status ? `status: ${album.mbRelease.status}` : "",
      tracks.length > 0 ? `${tracks.length} track${tracks.length === 1 ? "" : "s"} loaded.` : "Track list still loading.",
    ].filter(Boolean).join(" ");

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-[70] bg-[radial-gradient(circle_at_top,rgba(15,23,42,0.9),rgba(2,6,23,0.98)_56%,rgba(0,0,0,1)_100%)] backdrop-blur-md"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="relative flex h-full w-full flex-col overflow-hidden"
          initial={{ opacity: 0, y: 24 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: 24 }}
          onClick={(event) => event.stopPropagation()}
        >
          <div className="absolute inset-0 opacity-20">
            <ImageWithFallback src={album.artwork} alt={album.title} className="h-full w-full object-cover blur-3xl scale-110" />
          </div>
          <div className="absolute inset-0 bg-[linear-gradient(90deg,rgba(2,6,23,0.96)_0%,rgba(2,6,23,0.88)_35%,rgba(2,6,23,0.92)_100%)]" />

          <div className="relative z-10 mx-auto flex h-full w-full max-w-[1520px] flex-col px-5 pb-6 pt-4 lg:px-8">
            <div className="pointer-events-none absolute right-5 top-4 z-20 lg:right-8">
              <button
                type="button"
                className="pointer-events-auto rounded-full border border-white/15 bg-black/45 p-3 text-white transition hover:bg-black/70"
                onClick={onClose}
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="grid min-h-0 flex-1 gap-5 pt-2 lg:grid-cols-[minmax(0,1.12fr)_minmax(360px,0.88fr)]">
              <div className="flex min-h-0 flex-col rounded-[34px] border border-white/10 bg-white/[0.045] p-4 shadow-[0_28px_100px_rgba(0,0,0,0.36)] backdrop-blur-2xl lg:p-5">
                <div className="rounded-[28px] border border-white/10 bg-[linear-gradient(145deg,rgba(15,23,42,0.7),rgba(8,15,32,0.48))] p-4">
                  <div className="mb-4 inline-flex max-w-full items-center gap-3 rounded-full border border-cyan-300/25 bg-cyan-300/10 px-4 py-2 text-cyan-100 shadow-[0_10px_30px_rgba(34,211,238,0.12)]">
                    <Sparkles className="h-4 w-4 shrink-0" />
                    <span className="truncate text-[11px] font-semibold uppercase tracking-[0.34em]">{logoText}</span>
                  </div>

                  <div className="grid gap-4 lg:grid-cols-[128px_minmax(0,1fr)] lg:items-start">
                    <div className="overflow-hidden rounded-[24px] border border-white/10 bg-black/25 p-2.5 shadow-[0_18px_48px_rgba(0,0,0,0.28)]">
                      <ImageWithFallback src={album.artwork} alt={album.title} className="aspect-[1/1] w-full object-contain" />
                    </div>

                    <div className="min-w-0">
                      <h2 className="text-3xl font-bold tracking-tight text-white lg:text-[2.3rem]">{album.title}</h2>
                      <p className="mt-1.5 text-lg text-slate-300">{album.artist}</p>

                      <div className="mt-4 flex flex-wrap gap-2">
                        <span className="rounded-full border border-fuchsia-400/22 bg-fuchsia-400/10 px-3 py-1 text-xs font-semibold text-fuchsia-100">{album.genre || "Unknown genre"}</span>
                        {album.mbRelease?.label ? <span className="rounded-full border border-white/12 bg-white/6 px-3 py-1 text-xs font-semibold text-white/84">{album.mbRelease.label}</span> : null}
                        {album.mbRelease?.country ? <span className="rounded-full border border-white/12 bg-white/6 px-3 py-1 text-xs font-semibold text-white/84">{album.mbRelease.country}</span> : null}
                        {connectedProviders.length > 0 ? <span className="rounded-full border border-cyan-400/24 bg-cyan-400/10 px-3 py-1 text-xs font-semibold text-cyan-100">{connectedProviders.length} metadata source{connectedProviders.length === 1 ? "" : "s"}</span> : null}
                      </div>

                      <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                        {heroStats.map((stat) => (
                          <div key={stat.label} className="rounded-2xl border border-white/10 bg-black/20 px-4 py-3">
                            <div className="text-[11px] uppercase tracking-[0.24em] text-white/45">{stat.label}</div>
                            <div className="mt-2 text-sm font-semibold text-white">{stat.value}</div>
                          </div>
                        ))}
                      </div>
                    </div>
                  </div>

                  <div className="mt-4 rounded-[24px] border border-white/10 bg-black/18 p-4">
                    <div className="flex items-center gap-3 text-cyan-200">
                      <Library className="h-5 w-5" />
                      <span className="text-xs font-semibold uppercase tracking-[0.28em]">Album Summary</span>
                    </div>
                    <p className="mt-3 text-[15px] leading-7 text-slate-200/92">{summaryText || "Atlas is still loading album metadata for this release."}</p>
                  </div>
                </div>

                <div className="mt-5 grid gap-5 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
                  <div className="rounded-[28px] border border-white/10 bg-white/[0.04] p-5 backdrop-blur-xl">
                    <div className="flex items-center gap-3 text-cyan-200">
                      <Radio className="h-5 w-5" />
                      <span className="text-xs font-semibold uppercase tracking-[0.28em]">Ratings And Sources</span>
                    </div>
                    <div className="mt-4 grid gap-3 sm:grid-cols-2">
                      <div className="rounded-2xl border border-white/10 bg-black/18 p-4">
                        <div className="text-xs uppercase tracking-[0.22em] text-white/45">Atlas score</div>
                        <div className="mt-2 text-sm font-semibold text-white">{album.aiScore && album.aiScore > 0 ? album.aiScore.toFixed(1) : "Metadata loading"}</div>
                      </div>
                      <div className="rounded-2xl border border-white/10 bg-black/18 p-4">
                        <div className="text-xs uppercase tracking-[0.22em] text-white/45">Metadata status</div>
                        <div className="mt-2 text-sm font-semibold text-white">{album.detailsStatusText || "Library only"}</div>
                      </div>
                    </div>
                    <div className="mt-4 flex flex-wrap gap-2">
                      {connectedProviders.length > 0 ? connectedProviders.map((provider) => (
                        <span key={provider.name} className="rounded-full border border-cyan-400/24 bg-cyan-400/10 px-3 py-1 text-xs font-semibold text-cyan-100">
                          {provider.name} {provider.status ?? "connected"}
                        </span>
                      )) : (
                        <span className="text-sm text-slate-400">No metadata providers active.</span>
                      )}
                    </div>
                  </div>

                  <div className="rounded-[28px] border border-white/10 bg-white/[0.04] p-5 backdrop-blur-xl">
                    <div className="flex items-center gap-3 text-cyan-200">
                      <Music2 className="h-5 w-5" />
                      <span className="text-xs font-semibold uppercase tracking-[0.28em]">Album Info</span>
                    </div>
                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                      {[
                        ["Release date", album.mbRelease?.date || "Unknown"],
                        ["Country", album.mbRelease?.country || "Unknown"],
                        ["Label", album.mbRelease?.label || "Unknown"],
                        ["Barcode", album.mbRelease?.barcode || "Unknown"],
                        ["Status", album.mbRelease?.status || "Unknown"],
                        ["Packaging", album.mbRelease?.packaging || "Unknown"],
                      ].map(([label, value]) => (
                        <div key={label} className="rounded-2xl border border-white/10 bg-black/18 p-4">
                          <div className="text-xs uppercase tracking-[0.22em] text-white/45">{label}</div>
                          <div className="mt-2 text-sm font-semibold text-white">{value}</div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="mt-5 flex flex-wrap gap-3">
                  <button
                    type="button"
                    className="rounded-2xl bg-cyan-400 px-5 py-3 text-sm font-semibold text-slate-950 transition hover:bg-cyan-300"
                    onClick={onPlayAlbum}
                  >
                    Play album
                  </button>
                  <button
                    type="button"
                    className="rounded-2xl border border-white/15 bg-white/8 px-5 py-3 text-sm font-semibold text-white transition hover:bg-white/14"
                    onClick={onRefresh}
                  >
                    Refresh metadata
                  </button>
                </div>
              </div>

              <div className="flex min-h-0 flex-col rounded-[34px] border border-white/10 bg-white/[0.045] p-5 backdrop-blur-2xl lg:p-6">
                <div className="flex items-center gap-3 text-cyan-200">
                  <Disc3 className="h-5 w-5" />
                  <span className="text-xs font-semibold uppercase tracking-[0.28em]">Full Track List</span>
                </div>
                <div className="mt-5 flex-1 space-y-2 overflow-y-auto pr-1">
                    {tracks.length > 0 ? tracks.map((song, index) => (
                      <button
                        key={`${song.id}-${index}`}
                        type="button"
                        className="flex w-full items-center gap-4 rounded-2xl border border-white/10 bg-black/18 px-4 py-3 text-left transition hover:border-cyan-400/30 hover:bg-white/10"
                        onClick={() => onPlayTrack(song)}
                      >
                        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-cyan-400/12 text-sm font-semibold text-cyan-100">
                          {String(index + 1).padStart(2, "0")}
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-semibold text-white">{song.title}</div>
                          <div className="truncate text-xs text-slate-400">{song.artist}</div>
                        </div>
                        <div className="text-xs text-slate-400">{song.duration || "--:--"}</div>
                      </button>
                    )) : (
                      <div className="rounded-2xl border border-dashed border-white/10 bg-black/18 px-4 py-6 text-sm text-slate-400">
                        Track metadata is still loading. Atlas will fill this album page as soon as the album details arrive.
                      </div>
                    )}
                </div>
                <div className="mt-4 text-xs text-slate-400">Only the track list scrolls here. The album details stay fixed in view.</div>
              </div>
            </div>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}