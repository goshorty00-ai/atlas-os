import { motion, AnimatePresence } from 'motion/react';
import { Album, Track } from '../data/albums';
import { useEffect, useMemo, useState } from 'react';
import { Sparkles, ChevronLeft } from 'lucide-react';

interface AlbumDetailViewProps {
  album: Album;
  onClose: () => void;
  currentTrack: Track | null;
  onTrackSelect: (track: Track) => void;
  showLyrics: boolean;
  onOpenLyrics: (track?: Track | null) => void;
  instrumentalMode: boolean;
  onToggleInstrumental: () => void;
  onOpenVisualizer: () => void;
}

export function AlbumDetailView({
  album,
  onClose,
  currentTrack,
  onTrackSelect,
  showLyrics,
  onOpenLyrics,
  instrumentalMode,
  onToggleInstrumental,
  onOpenVisualizer,
}: AlbumDetailViewProps) {
  const [menu, setMenu] = useState<{ x: number; y: number; kind: 'track' | 'album'; filePath?: string; recordingId?: string } | null>(null);

  const postHost = (msg: any) => {
    try {
      const w = window as any;
      if (w?.chrome?.webview?.postMessage) w.chrome.webview.postMessage(msg);
    } catch {
    }
  };

  const allAlbumFilePaths = useMemo(() => {
    return (album.tracks || [])
      .map((t: any) => String(t?.filePath || '').trim())
      .filter((fp) => fp.length > 0);
  }, [album.tracks]);

  useEffect(() => {
    const onDown = () => setMenu(null);
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, []);

  const openMenu = (e: React.MouseEvent, next: { kind: 'track' | 'album'; filePath?: string; recordingId?: string }) => {
    e.preventDefault();
    const width = 240;
    const height = next.kind === 'album' ? 520 : 420;
    const x = Math.max(8, Math.min(window.innerWidth - width - 8, e.clientX));
    const y = Math.max(8, Math.min(window.innerHeight - height - 8, e.clientY));
    setMenu({ x, y, ...next });
  };

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      className="relative h-full w-full p-8 overflow-hidden"
    >
      <div className="absolute inset-0">
        <div
          className="absolute inset-0"
          style={{
            backgroundImage: `url(${album.cover})`,
            backgroundSize: 'cover',
            backgroundPosition: 'center',
            filter: 'blur(4px) saturate(1.2) contrast(1.05)',
            transform: 'scale(1.08)',
            opacity: 0.86,
          }}
        />
        <div className="absolute inset-0 bg-gradient-to-br from-[#0B0F14]/82 via-[#0B0F14]/40 to-[#0B0F14]/88" />
        <div className="absolute inset-0" style={{ background: `radial-gradient(circle at 35% 40%, ${album.dominantColor}14 0%, transparent 55%)` }} />
      </div>

      {/* Main content */}
      <div className="relative w-full max-w-7xl h-full min-h-0 grid grid-cols-[1fr_520px] gap-10">
        {/* Left Panel - Tracklist */}
        <motion.div
          initial={{ x: -50, opacity: 0 }}
          animate={{ x: 0, opacity: 1 }}
          transition={{ delay: 0.2 }}
          className="flex flex-col min-h-0"
        >
          <div className="mb-4 flex items-center gap-3">
            <button
              onClick={onClose}
              className="w-11 h-11 rounded-full bg-white/5 backdrop-blur-md border border-white/10 flex items-center justify-center text-white/70 hover:text-white hover:bg-white/10 hover:border-[#3B82F6]/50 transition-all duration-300"
            >
              <ChevronLeft className="w-6 h-6" />
            </button>
            <div className="flex-1 min-w-0">
              <h2 className="text-2xl text-white leading-tight">Tracklist</h2>
              <div className="h-px bg-gradient-to-r from-white/20 via-white/10 to-transparent mt-2" />
            </div>
          </div>

          <div className="flex-1 min-h-0 overflow-y-auto pr-2 custom-scrollbar rounded-2xl bg-white/0 border border-white/5">
            <div className="p-4">
              <div className="space-y-1.5" style={{ fontSize: (album.tracks.length > 80 ? 11 : 13) }}>
                {album.tracks.map((track, index) => {
                  const isActive = currentTrack?.id === track.id;
                  const disc = Number((track as any).discNumber || 1);
                  const tn = Number((track as any).trackNumber || track.number || index + 1);
                  const numText = disc > 1 ? `${disc}-${String(tn).padStart(2, '0')}` : String(tn).padStart(2, '0');
                  const trackArtist = String((track as any).artist || '').trim();
                  const artistSuffix = trackArtist && trackArtist.toLowerCase() !== (album.artist || '').toLowerCase() ? ` · ${trackArtist}` : '';
                  const recId = String((track as any).mbRecordingId || '').trim();
                  return (
                    <div
                      key={track.id}
                      onClick={() => onTrackSelect(track)}
                      onContextMenu={(e) => {
                        const fp = String((track as any).filePath || '').trim();
                        openMenu(e, { kind: 'track', filePath: fp || undefined, recordingId: recId || undefined });
                      }}
                      className={`group cursor-pointer rounded-lg px-2 py-1 transition-colors ${
                        isActive
                          ? 'bg-gradient-to-r from-[#3B82F6]/22 to-[#8B5CF6]/18 border border-[#3B82F6]/35'
                          : 'bg-white/0 hover:bg-white/5 border border-white/0 hover:border-white/10'
                      }`}
                    >
                      <div className="flex items-center gap-2">
                        <div className={`w-10 text-right text-[11px] tabular-nums ${
                          isActive ? 'text-[#3B82F6]' : 'text-white/45 group-hover:text-white/65'
                        }`}>
                          {numText}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className={`truncate ${
                            isActive ? 'text-white' : 'text-white/85 group-hover:text-white'
                          }`}>
                            {track.title}{artistSuffix}
                          </div>
                        </div>
                        <div className={`text-[11px] tabular-nums ${
                          isActive ? 'text-[#3B82F6]' : 'text-white/50'
                        }`}>
                          {track.duration}
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        </motion.div>

        {/* Right Panel - Album Info */}
        <motion.div
          initial={{ x: 50, opacity: 0 }}
          animate={{ x: 0, opacity: 1 }}
          transition={{ delay: 0.25 }}
          className="flex flex-col gap-6 min-h-0"
          onContextMenu={(e) => {
            openMenu(e, { kind: 'album' });
          }}
        >
          {/* Album Info */}
          <div className="space-y-4">
            <div className="flex items-start gap-4">
              <div className="w-24 h-24 rounded-2xl overflow-hidden bg-white/5 border border-white/10 flex-shrink-0">
                <img src={album.cover} alt={album.title} className="w-full h-full object-cover" />
              </div>
              <div className="min-w-0">
                <h1 className="text-4xl text-white mb-2 truncate">{album.title}</h1>
                <p className="text-xl text-white/70 truncate">{album.artist}</p>
              </div>
            </div>

            <div className="flex flex-wrap gap-3">
              {album.genre.map((genre) => (
                <span
                  key={genre}
                  className="px-4 py-2 rounded-xl bg-white/5 backdrop-blur-sm border border-white/10 text-sm text-white/80"
                  style={{
                    boxShadow: `0 0 20px ${album.dominantColor}20`,
                  }}
                >
                  {genre}
                </span>
              ))}
            </div>

            <div className="flex items-center gap-6 text-sm text-white/60">
              <span>{album.year}</span>
              <span>•</span>
              <span>{album.tracks.length} tracks</span>
              <span>•</span>
              <span>{album.duration}</span>
            </div>

            {/* AI Badge */}
            <div className="flex items-center gap-2 px-4 py-3 rounded-xl bg-gradient-to-r from-[#3B82F6]/18 to-[#8B5CF6]/14 border border-[#3B82F6]/25">
              <Sparkles className="w-4 h-4 text-[#3B82F6]" />
              <span className="text-sm text-white/90">AI features</span>
              <div className="ml-auto w-2 h-2 rounded-full bg-[#3B82F6] animate-pulse" />
            </div>
          </div>

          {(album.detailsStatusText || album.mbRelease) && (
            <div className="rounded-2xl bg-white/5 border border-white/10 p-4">
              {album.detailsStatusText && (
                <div className="text-sm text-white/70 mb-2">{album.detailsStatusText}</div>
              )}
              {album.mbRelease && (
                <div className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs text-white/70">
                  {album.mbRelease.date && (<div><span className="text-white/45">Date</span> · {album.mbRelease.date}</div>)}
                  {album.mbRelease.country && (<div><span className="text-white/45">Country</span> · {album.mbRelease.country}</div>)}
                  {album.mbRelease.label && (<div><span className="text-white/45">Label</span> · {album.mbRelease.label}</div>)}
                  {album.mbRelease.status && (<div><span className="text-white/45">Status</span> · {album.mbRelease.status}</div>)}
                  {album.mbRelease.packaging && (<div><span className="text-white/45">Pack</span> · {album.mbRelease.packaging}</div>)}
                  {album.mbRelease.barcode && (<div><span className="text-white/45">Barcode</span> · {album.mbRelease.barcode}</div>)}
                </div>
              )}
            </div>
          )}
        </motion.div>
      </div>


      <AnimatePresence>
        {menu && (
          <motion.div
            initial={{ opacity: 0, scale: 0.98 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.98 }}
            className="fixed z-[60] min-w-[220px] rounded-xl bg-black/90 backdrop-blur-xl border border-white/10 overflow-hidden"
            style={{ left: menu.x, top: menu.y }}
          >
            {menu.kind === 'track' && (
              <div className="py-2">
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'play.track', payload: { filePath: fp } });
                    setMenu(null);
                  }}
                >
                  Play
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) {
                      const t = album.tracks.find((x: any) => String(x?.filePath || '').trim() === fp) || null;
                      if (t) onTrackSelect(t as any);
                      onOpenLyrics(t as any);
                    }
                    setMenu(null);
                  }}
                >
                  Lyrics AI
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'queue.add.track', payload: { filePath: fp } });
                    setMenu(null);
                  }}
                >
                  Add to queue
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'queue.next.track', payload: { filePath: fp } });
                    setMenu(null);
                  }}
                >
                  Play next
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    onOpenVisualizer();
                    setMenu(null);
                  }}
                >
                  Visualizer
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    const name = window.prompt('Create playlist name', album.title) || '';
                    if (fp && name.trim()) postHost({ type: 'playlist.create', payload: { name: name.trim(), filePaths: [fp] } });
                    setMenu(null);
                  }}
                >
                  Create playlist
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    const name = window.prompt('Add to playlist name') || '';
                    if (fp && name.trim()) postHost({ type: 'playlist.add', payload: { name: name.trim(), filePaths: [fp] } });
                    setMenu(null);
                  }}
                >
                  Add to playlist
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'media.track.edit', payload: { filePath: fp } });
                    setMenu(null);
                  }}
                >
                  Edit info
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'media.track.open_folder', payload: { filePath: fp } });
                    setMenu(null);
                  }}
                >
                  Open folder
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const fp = menu.filePath || '';
                    if (fp) postHost({ type: 'media.track.remove', payload: { filePath: fp } });
                    setMenu(null);
                    onClose();
                  }}
                >
                  Remove from library
                </button>
              </div>
            )}

            {menu.kind === 'album' && (
              <div className="py-2">
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    onOpenLyrics(currentTrack);
                    setMenu(null);
                  }}
                >
                  Lyrics AI
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.ai_tag', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Optimize (AI)
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    onOpenVisualizer();
                    setMenu(null);
                  }}
                >
                  Visualizer
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.edit', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Edit album info
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.cover', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Get / change cover
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.cover.itunes', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Fetch cover (iTunes)
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    if (album.metaUrl) postHost({ type: 'open.url', payload: { url: album.metaUrl } });
                    else if (album.mbRelease?.url) postHost({ type: 'open.url', payload: { url: album.mbRelease.url } });
                    else if (album.mbRelease?.id) postHost({ type: 'open.url', payload: { url: `https://musicbrainz.org/release/${album.mbRelease?.id}` } });
                    setMenu(null);
                  }}
                >
                  Open release
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.refresh', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Refresh
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.open_folder', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Open folder
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'queue.add.album', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Add album to queue
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.playlist', payload: { id: album.id } });
                    setMenu(null);
                  }}
                >
                  Export playlist (album folder)
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const name = window.prompt('Create playlist name', album.title) || '';
                    if (name.trim() && allAlbumFilePaths.length > 0)
                      postHost({ type: 'playlist.create', payload: { name: name.trim(), filePaths: allAlbumFilePaths } });
                    setMenu(null);
                  }}
                >
                  Create playlist
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    const name = window.prompt('Add to playlist name') || '';
                    if (name.trim() && allAlbumFilePaths.length > 0)
                      postHost({ type: 'playlist.add', payload: { name: name.trim(), filePaths: allAlbumFilePaths } });
                    setMenu(null);
                  }}
                >
                  Add to playlist
                </button>
                <button
                  className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                  onClick={() => {
                    postHost({ type: 'media.album.remove', payload: { id: album.id } });
                    setMenu(null);
                    onClose();
                  }}
                >
                  Remove album
                </button>
              </div>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}
