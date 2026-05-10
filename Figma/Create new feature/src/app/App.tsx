import { useState, useEffect, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import type { Album, Track } from './data/albums';
import { LibraryGridView } from './components/LibraryGridView';
import { LibraryCarouselView } from './components/LibraryCarouselView';
import { LibraryListView } from './components/LibraryListView';
import { AlbumDetailView } from './components/AlbumDetailView';
import { MediaBar } from './components/MediaBar';
import { Visualizer } from './components/Visualizer';
import { FullScreenVisualizer } from './components/FullScreenVisualizer';
import { LyricsView } from './components/LyricsView';
import { KaraokePartyModal } from './components/KaraokePartyModal';

type ViewMode = 'grid' | 'carousel' | 'list';
type VisualizerType = 'waveform' | 'circular' | 'particles' | 'bars' | 'blob' | 'spectrum' | 'neonGrid';

export default function App() {
  const [albums, setAlbums] = useState<Album[]>([]);
  const [viewMode, setViewMode] = useState<ViewMode>('grid');
  const [selectedAlbum, setSelectedAlbum] = useState<Album | null>(null);
  const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
  const [currentAlbum, setCurrentAlbum] = useState<Album | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [volume, setVolume] = useState(75);
  const [progress, setProgress] = useState(0);
  const [currentTimeText, setCurrentTimeText] = useState('0:00');
  const [totalTimeText, setTotalTimeText] = useState('0:00');
  const [showLyrics, setShowLyrics] = useState(false);
  const [showKaraokeParty, setShowKaraokeParty] = useState(false);
  const [instrumentalMode, setInstrumentalMode] = useState(false);
  const [showFullScreenVisualizer, setShowFullScreenVisualizer] = useState(false);
  const [visualizerType, setVisualizerType] = useState<VisualizerType>('waveform');
  const [lyricsLines, setLyricsLines] = useState<Array<{ timeSeconds: number; text: string }>>([]);
  const [lyricsTrack, setLyricsTrack] = useState<Track | null>(null);
  const [playbackSeconds, setPlaybackSeconds] = useState(0);
  const [totalSeconds, setTotalSeconds] = useState(0);
  const [nowPlayingFilePath, setNowPlayingFilePath] = useState('');
  const nowPlayingFilePathRef = useRef('');
  const lastLyricsRequestFilePathRef = useRef('');
  const [albumMenu, setAlbumMenu] = useState<{ x: number; y: number; album: Album } | null>(null);
  const [usagePanel, setUsagePanel] = useState<{ text: string } | null>(null);
  const [toasts, setToasts] = useState<Array<{ id: string; text: string; level: 'info' | 'success' | 'error' }>>([]);
  const albumsRef = useRef<Album[]>([]);
  const selectedAlbumRef = useRef<Album | null>(null);
  const currentAlbumRef = useRef<Album | null>(null);
  const currentTrackRef = useRef<Track | null>(null);
  const pendingLyricsAlbumIdRef = useRef<string>('');

  useEffect(() => {
    albumsRef.current = albums;
  }, [albums]);

  useEffect(() => {
    selectedAlbumRef.current = selectedAlbum;
  }, [selectedAlbum]);

  useEffect(() => {
    currentAlbumRef.current = currentAlbum;
  }, [currentAlbum]);

  useEffect(() => {
    currentTrackRef.current = currentTrack;
  }, [currentTrack]);

  const postHost = useCallback((msg: any) => {
    try {
      const w = window as any;
      if (w?.chrome?.webview?.postMessage) {
        w.chrome.webview.postMessage(msg);
      }
    } catch {
    }
  }, []);

  useEffect(() => {
    const onDown = () => setAlbumMenu(null);
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, []);

  const pickDominant = useCallback((id: string) => {
    const palette = ['#3B82F6', '#8B5CF6', '#EC4899', '#06B6D4', '#10B981', '#F59E0B', '#EF4444'];
    let h = 0;
    for (let i = 0; i < (id || '').length; i++) h = (h * 31 + (id.charCodeAt(i) || 0)) >>> 0;
    return palette[h % palette.length];
  }, []);

  const handleNext = useCallback(() => {
    postHost({ type: 'play.next' });
  }, [postHost]);

  const handlePrevious = useCallback(() => {
    postHost({ type: 'play.prev' });
  }, [postHost]);

  useEffect(() => {
    const w = window as any;
    const webview = w?.chrome?.webview;
    if (!webview?.addEventListener) return;

    const onMessage = (event: any) => {
      const msg = event?.data;
      if (!msg || typeof msg !== 'object') return;

      if (msg.type === 'media.library.updated') {
        const payload = msg.payload || {};
        const list = Array.isArray(payload.albums) ? payload.albums : [];
        const mapped: Album[] = list.map((a: any) => {
          const id = String(a?.id || '');
          const dominant = pickDominant(id);
          const genre = String(a?.genre || '').trim();
          return {
            id,
            title: String(a?.title || ''),
            artist: String(a?.artist || ''),
            year: String(a?.year || 'Unknown'),
            genre: genre ? [genre] : [],
            cover: String(a?.cover || ''),
            tracks: [],
            duration: String(a?.duration || ''),
            mood: { energetic: 70, melancholic: 35, uplifting: 65, aggressive: 25 },
            dominantColor: dominant,
          };
        });

        setAlbums((prev) => {
          const byId = new Map<string, Album>();
          for (const a of prev) byId.set(a.id, a);
          return mapped.map((a) => {
            const existing = byId.get(a.id);
            if (!existing) return a;
            return {
              ...a,
              tracks: (existing.tracks && existing.tracks.length > 0) ? existing.tracks : a.tracks,
              detailsStatusText: (existing as any).detailsStatusText || (a as any).detailsStatusText,
              mbRelease: (existing as any).mbRelease || (a as any).mbRelease,
            } as any;
          });
        });

        setSelectedAlbum((prev) => {
          if (!prev) return null;
          const found = mapped.find((x) => x.id === prev.id);
          if (!found) return prev;
          return {
            ...found,
            tracks: prev.tracks || [],
            detailsStatusText: (prev as any).detailsStatusText,
            mbRelease: (prev as any).mbRelease,
          } as any;
        });
        setCurrentAlbum((prev) => {
          if (!prev) return null;
          const found = mapped.find((x) => x.id === prev.id);
          if (!found) return prev;
          return {
            ...found,
            tracks: prev.tracks || [],
            detailsStatusText: (prev as any).detailsStatusText,
            mbRelease: (prev as any).mbRelease,
          } as any;
        });
      }

      if (msg.type === 'media.album.details') {
        const payload = msg.payload || {};
        const id = String(payload.id || '');
        const detailsStatusText = String(payload.detailsStatusText || '');
        const metaProvider = String(payload.metaProvider || (payload.mbRelease as any)?.provider || '');
        const metaUrl = String(payload.metaUrl || (payload.mbRelease as any)?.url || '');
        const mbRelease = payload.mbRelease && typeof payload.mbRelease === 'object' ? payload.mbRelease : undefined;
        const mbTrackList = Array.isArray(payload.mbTrackList) ? payload.mbTrackList : [];
        const mbKeyToRecording: Record<string, string> = {};
        for (const t of mbTrackList) {
          const disc = Number((t as any)?.discNumber || 1);
          const num = Number((t as any)?.trackNumber || 0);
          const recId = String((t as any)?.recordingId || '').trim();
          if (num > 0 && recId) mbKeyToRecording[`${disc}:${num}`] = recId;
        }
        const list = Array.isArray(payload.trackList) ? payload.trackList : [];
        const tracks: Track[] = list.map((t: any, idx: number) => {
          const title = String(t?.title || '');
          const discNumber = Number(t?.discNumber || 1);
          const trackNumber = Number(t?.trackNumber || 0);
          const number = Number(trackNumber || idx + 1);
          const audioUrl = String(t?.audioUrl || '');
          const filePath = String(t?.filePath || '');
          const artist = String(t?.artist || '');
          const year = typeof t?.year === 'number' ? Number(t.year) : Number(t?.year || 0);
          const genres = Array.isArray(t?.genres) ? t.genres.map((g: any) => String(g || '')).filter((g: string) => g.trim()) : [];
          const mbRecordingId = mbKeyToRecording[`${discNumber}:${trackNumber}`] || '';
          return {
            id: filePath || `${id}:${number}:${idx}`,
            number,
            title,
            duration: String(t?.duration || ''),
            audioUrl,
            ...(filePath ? { filePath } : {}),
            discNumber,
            trackNumber,
            artist,
            year: year > 0 ? year : undefined,
            genres: genres.length > 0 ? genres : undefined,
            mbRecordingId: mbRecordingId || undefined,
          };
        });

        setAlbums((prev) =>
          prev.map((a) =>
            a.id === id
              ? {
                  ...a,
                  title: String(payload.title || a.title),
                  artist: String(payload.artist || a.artist),
                  year: String(payload.year || a.year),
                  genre: String(payload.genre || '').trim() ? [String(payload.genre)] : a.genre,
                  duration: String(payload.duration || a.duration),
                  tracks,
                  detailsStatusText: detailsStatusText || a.detailsStatusText,
                  metaProvider: metaProvider || a.metaProvider,
                  metaUrl: metaUrl || a.metaUrl,
                  mbRelease: mbRelease ? {
                    id: String((mbRelease as any).id || ''),
                    provider: String((mbRelease as any).provider || metaProvider || ''),
                    url: String((mbRelease as any).url || metaUrl || ''),
                    date: String((mbRelease as any).date || ''),
                    country: String((mbRelease as any).country || ''),
                    label: String((mbRelease as any).label || ''),
                    barcode: String((mbRelease as any).barcode || ''),
                    status: String((mbRelease as any).status || ''),
                    packaging: String((mbRelease as any).packaging || ''),
                  } : a.mbRelease,
                }
              : a
          )
        );

        setSelectedAlbum((prev) => (prev && prev.id === id ? { ...prev, tracks, detailsStatusText, metaProvider: metaProvider || prev.metaProvider, metaUrl: metaUrl || prev.metaUrl, mbRelease: mbRelease ? {
          id: String((mbRelease as any).id || ''),
          provider: String((mbRelease as any).provider || metaProvider || ''),
          url: String((mbRelease as any).url || metaUrl || ''),
          date: String((mbRelease as any).date || ''),
          country: String((mbRelease as any).country || ''),
          label: String((mbRelease as any).label || ''),
          barcode: String((mbRelease as any).barcode || ''),
          status: String((mbRelease as any).status || ''),
          packaging: String((mbRelease as any).packaging || ''),
        } : prev.mbRelease } : prev));
        setCurrentAlbum((prev) => (prev && prev.id === id ? { ...prev, tracks, detailsStatusText, metaProvider: metaProvider || prev.metaProvider, metaUrl: metaUrl || prev.metaUrl, mbRelease: mbRelease ? {
          id: String((mbRelease as any).id || ''),
          provider: String((mbRelease as any).provider || metaProvider || ''),
          url: String((mbRelease as any).url || metaUrl || ''),
          date: String((mbRelease as any).date || ''),
          country: String((mbRelease as any).country || ''),
          label: String((mbRelease as any).label || ''),
          barcode: String((mbRelease as any).barcode || ''),
          status: String((mbRelease as any).status || ''),
          packaging: String((mbRelease as any).packaging || ''),
        } : prev.mbRelease } : prev));

        if (!currentTrackRef.current && tracks.length > 0) setCurrentTrack(tracks[0]);

        if (pendingLyricsAlbumIdRef.current && pendingLyricsAlbumIdRef.current === id && tracks.length > 0) {
          pendingLyricsAlbumIdRef.current = '';
          setCurrentTrack(tracks[0]);
          openLyricsForTrack(tracks[0]);
        }
      }

      if (msg.type === 'playback.state') {
        const payload = msg.payload || {};
        const playing = Boolean(payload.isPlaying);
        const filePath = String(payload.filePath || '');
        const pSec = Number(payload.progressSeconds || 0);
        const tSec = Number(payload.totalSeconds || 0);

        setIsPlaying(playing);
        setVolume(Number(payload.volume || 0));
        setPlaybackSeconds(pSec);
        setTotalSeconds(tSec);
        const prevPath = nowPlayingFilePathRef.current;
        nowPlayingFilePathRef.current = filePath;
        setNowPlayingFilePath(filePath);
        const fmt = (seconds: number) => {
          const s = Math.max(0, Math.floor(seconds || 0));
          const mins = Math.floor(s / 60);
          const secs = s % 60;
          return `${mins}:${secs.toString().padStart(2, '0')}`;
        };
        setCurrentTimeText(String(payload.progressText || payload.currentTimeText || '') || fmt(pSec));
        setTotalTimeText(String(payload.totalText || payload.totalTimeText || '') || (tSec > 0 ? fmt(tSec) : '0:00'));

        if (tSec > 0) setProgress(Math.max(0, Math.min(100, (pSec / tSec) * 100)));
        else setProgress(0);

        if (filePath && filePath !== prevPath) {
          const tryMatch = (a: Album | null) => a?.tracks?.find((t: any) => String((t as any).filePath || '') === filePath) ?? null;
          const inCurrent = tryMatch(currentAlbumRef.current);
          const inSelected = tryMatch(selectedAlbumRef.current);
          const inAny =
            inCurrent ||
            inSelected ||
            albumsRef.current
              .find((a) => a.tracks.some((t: any) => String((t as any).filePath || '') === filePath))
              ?.tracks.find((t: any) => String((t as any).filePath || '') === filePath) ||
            null;

          const already = currentTrackRef.current && String((currentTrackRef.current as any)?.filePath || '') === filePath;
          if (inAny && !already) setCurrentTrack(inAny);
        }
      }

      if (msg.type === 'lyrics.result') {
        const payload = msg.payload || {};
        const filePath = String(payload.filePath || '');
        const want = String(lastLyricsRequestFilePathRef.current || '').trim();
        const ok =
          !!filePath &&
          !!want &&
          (filePath.toLowerCase() === want.toLowerCase() ||
            filePath.toLowerCase().endsWith(`\\${want.toLowerCase().split('\\').pop() || ''}`) ||
            want.toLowerCase().endsWith(`\\${filePath.toLowerCase().split('\\').pop() || ''}`));

        if (ok) {
          const lines = Array.isArray(payload.lines) ? payload.lines : [];
          const mapped = lines.map((l: any) => ({ timeSeconds: Number(l?.timeSeconds || 0), text: String(l?.text || '') }));
          setLyricsLines(mapped.length > 0 ? mapped : [{ timeSeconds: 0, text: 'No lyrics found' }]);
        }
      }

      if (msg.type === 'ai.usage') {
        const payload = msg.payload || {};
        const text = String(payload.text || '').trim();
        if (text) setUsagePanel({ text });
      }

      if (msg.type === 'toast') {
        const payload = msg.payload || {};
        const text = String(payload.text || '').trim();
        const levelRaw = String(payload.level || 'info').trim().toLowerCase();
        const level = (levelRaw === 'success' || levelRaw === 'error' || levelRaw === 'info') ? (levelRaw as any) : 'info';
        if (text) {
          const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
          setToasts((prev) => [...prev, { id, text, level }].slice(-4));
          window.setTimeout(() => {
            setToasts((prev) => prev.filter((t) => t.id !== id));
          }, 3200);
        }
      }
    };

    webview.addEventListener('message', onMessage);
    postHost({ type: 'media.init' });

    return () => {
      try { webview.removeEventListener('message', onMessage); } catch { }
    };
  }, [pickDominant, postHost]);

  const handleAlbumClick = (album: Album) => {
    setSelectedAlbum(album);
    setCurrentAlbum(album);
    if (!currentTrack) {
      setCurrentTrack(album.tracks[0] ?? null);
    }
    postHost({ type: 'media.album.selected', payload: { id: album.id } });
  };

  const openAlbumMenu = (e: React.MouseEvent, album: Album) => {
    const width = 260;
    const height = 520;
    const x = Math.max(8, Math.min(window.innerWidth - width - 8, e.clientX));
    const y = Math.max(8, Math.min(window.innerHeight - height - 8, e.clientY));
    setAlbumMenu({ x, y, album });
  };

  const handleCloseAlbum = () => {
    setSelectedAlbum(null);
  };

  const handleTrackSelect = (track: Track) => {
    setCurrentTrack(track);
    const fp = String((track as any).filePath || '');
    if (fp) postHost({ type: 'play.track', payload: { filePath: fp } });
  };

  const handlePlayPause = () => {
    postHost({ type: 'play.toggle' });
  };

  const openLyricsForTrack = (track?: Track | null) => {
    const t = track ?? currentTrack;
    if (!t) return;
    setShowLyrics(true);
    setLyricsTrack(t);
    const fp = String((t as any)?.filePath || nowPlayingFilePathRef.current || '');
    if (fp) {
      setLyricsLines([]);
      lastLyricsRequestFilePathRef.current = fp;
      postHost({ type: 'lyrics.get', payload: { filePath: fp } });
    }
  };

  const closeLyrics = () => {
    setShowLyrics(false);
    setLyricsTrack(null);
  };

  const openKaraokeParty = () => {
    setShowKaraokeParty(true);
  };

  const closeKaraokeParty = () => {
    setShowKaraokeParty(false);
  };

  const handleToggleInstrumental = () => {
    const next = !instrumentalMode;
    setInstrumentalMode(next);
    postHost({ type: 'ai.instrumental.toggle', payload: { enabled: next } });
  };

  const handleVolumeChange = (v: number) => {
    setVolume(v);
    postHost({ type: 'play.volume', payload: { value: v } });
  };

  const handleSeekToSeconds = (sec: number) => {
    const s = Math.max(0, Number(sec || 0));
    postHost({ type: 'play.seek', payload: { seconds: s } });
  };

  const handleOpenVisualizer = () => {
    setShowFullScreenVisualizer(true);
  };

  const handleCloseVisualizer = () => {
    setShowFullScreenVisualizer(false);
  };

  const aiActive = showLyrics || instrumentalMode;

  // Dynamic background color based on current album
  const backgroundColor = currentAlbum?.dominantColor || '#0B0F14';

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#0B0F14]">
      {/* Dynamic background gradient */}
      <div
        className="absolute inset-0 transition-all duration-1000"
        style={{
          background: `radial-gradient(circle at 50% 50%, ${backgroundColor}15 0%, #0B0F14 60%)`,
        }}
      />

      {/* Background visualizer */}
      {currentAlbum && isPlaying && (
        <Visualizer
          type={visualizerType}
          dominantColor={currentAlbum.dominantColor}
          isPlaying={isPlaying}
          intensity={0.22}
        />
      )}

      {/* Main content area */}
      <div className="relative h-full pb-16">
        <AnimatePresence mode="wait">
          {selectedAlbum ? (
            <AlbumDetailView
              key="album-detail"
              album={selectedAlbum}
              onClose={handleCloseAlbum}
              currentTrack={currentTrack}
              onTrackSelect={handleTrackSelect}
              showLyrics={showLyrics}
              onOpenLyrics={openLyricsForTrack}
              instrumentalMode={instrumentalMode}
              onToggleInstrumental={handleToggleInstrumental}
              onOpenVisualizer={handleOpenVisualizer}
            />
          ) : (
            <motion.div
              key="library"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="h-full flex flex-col"
            >
              {/* Library views */}
              <div className="flex-1 overflow-auto custom-scrollbar">
                <AnimatePresence mode="wait">
                  {viewMode === 'grid' && (
                    <motion.div
                      key="grid"
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -20 }}
                    >
                      <LibraryGridView albums={albums} onAlbumClick={handleAlbumClick} onAlbumContextMenu={openAlbumMenu} />
                    </motion.div>
                  )}

                  {viewMode === 'carousel' && (
                    <motion.div
                      key="carousel"
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      className="h-full"
                    >
                      <LibraryCarouselView albums={albums} onAlbumClick={handleAlbumClick} onAlbumContextMenu={openAlbumMenu} />
                    </motion.div>
                  )}

                  {viewMode === 'list' && (
                    <motion.div
                      key="list"
                      initial={{ opacity: 0, y: 20 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -20 }}
                    >
                      <LibraryListView albums={albums} onAlbumClick={handleAlbumClick} onAlbumContextMenu={openAlbumMenu} />
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      <AnimatePresence>
        {albumMenu && (
          <motion.div
            initial={{ opacity: 0, scale: 0.98 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.98 }}
            className="fixed z-[80] min-w-[240px] rounded-xl bg-black/90 backdrop-blur-xl border border-white/10 overflow-hidden"
            style={{ left: albumMenu.x, top: albumMenu.y }}
          >
            <div className="px-4 py-3 border-b border-white/10">
              <div className="text-sm text-white truncate">{albumMenu.album.title}</div>
              <div className="text-xs text-white/60 truncate">{albumMenu.album.artist}</div>
            </div>
            <div className="py-2">
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  handleAlbumClick(albumMenu.album);
                  setAlbumMenu(null);
                }}
              >
                Open
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  pendingLyricsAlbumIdRef.current = albumMenu.album.id;
                  handleAlbumClick(albumMenu.album);
                  setAlbumMenu(null);
                }}
              >
                Lyrics AI
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  handleOpenVisualizer();
                  setAlbumMenu(null);
                }}
              >
                Visualizer
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'queue.add.album', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Add album to queue
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.open_folder', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Open folder
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.edit', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Edit album info
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.cover', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Get / change cover
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.cover.itunes', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Fetch cover (iTunes)
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.ai_tag', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Optimize (AI)
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  const name = window.prompt('Create playlist name', albumMenu.album.title) || '';
                  if (name.trim()) postHost({ type: 'playlist.create.album', payload: { id: albumMenu.album.id, name: name.trim() } });
                  setAlbumMenu(null);
                }}
              >
                Create playlist
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  const name = window.prompt('Add to playlist name') || '';
                  if (name.trim()) postHost({ type: 'playlist.add.album', payload: { id: albumMenu.album.id, name: name.trim() } });
                  setAlbumMenu(null);
                }}
              >
                Add to playlist
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'media.album.playlist', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Export playlist (album folder)
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-white/80 hover:bg-white/5 hover:text-white"
                onClick={() => {
                  postHost({ type: 'ai.usage.get' });
                  setAlbumMenu(null);
                }}
              >
                AI / API usage
              </button>
              <button
                className="w-full text-left px-4 py-2 text-sm text-red-300/90 hover:bg-white/5 hover:text-red-200"
                onClick={() => {
                  const ok = window.confirm(`Remove album from library?\n\n${albumMenu.album.artist} — ${albumMenu.album.title}\n\n(You can Undo from Actions)`);
                  if (ok) postHost({ type: 'media.album.remove', payload: { id: albumMenu.album.id } });
                  setAlbumMenu(null);
                }}
              >
                Remove album
              </button>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {usagePanel && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[90] flex items-center justify-center p-8"
          >
            <div className="absolute inset-0 bg-black/60" onClick={() => setUsagePanel(null)} />
            <div className="relative w-full max-w-2xl rounded-2xl bg-black/85 backdrop-blur-xl border border-white/10 p-6">
              <div className="flex items-center justify-between mb-4">
                <div className="text-lg text-white">AI / API usage</div>
                <button
                  className="px-3 py-1 rounded-lg bg-white/5 border border-white/10 text-white/70 hover:text-white hover:bg-white/10"
                  onClick={() => setUsagePanel(null)}
                >
                  Close
                </button>
              </div>
              <pre className="text-sm text-white/80 whitespace-pre-wrap leading-relaxed">{usagePanel.text}</pre>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="fixed top-4 right-4 z-[95] flex flex-col gap-2">
        <AnimatePresence>
          {toasts.map((t) => (
            <motion.div
              key={t.id}
              initial={{ opacity: 0, y: -8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              className={`max-w-sm rounded-xl border px-4 py-3 backdrop-blur-xl ${
                t.level === 'error'
                  ? 'bg-red-500/10 border-red-500/25 text-white'
                  : t.level === 'success'
                    ? 'bg-emerald-500/10 border-emerald-500/25 text-white'
                    : 'bg-black/60 border-white/10 text-white'
              }`}
            >
              <div className="text-sm leading-snug whitespace-pre-wrap">{t.text}</div>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>

      {/* Media bar */}
      <MediaBar
        currentTrack={currentTrack}
        currentAlbum={currentAlbum}
        isPlaying={isPlaying}
        onPlayPause={handlePlayPause}
        onNext={handleNext}
        onPrevious={handlePrevious}
        volume={volume}
        onVolumeChange={handleVolumeChange}
        onOpenVisualizer={handleOpenVisualizer}
        visualizerType={visualizerType}
        onVisualizerTypeChange={setVisualizerType}
        aiActive={aiActive}
        progress={progress}
        currentTimeText={currentTimeText}
        totalTimeText={totalTimeText}
        totalSeconds={totalSeconds}
        onSeekToSeconds={handleSeekToSeconds}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
      />

      {/* Fullscreen visualizer */}
      <FullScreenVisualizer
        isOpen={showFullScreenVisualizer}
        onClose={handleCloseVisualizer}
        album={currentAlbum}
        currentTrack={currentTrack}
        isPlaying={isPlaying}
        visualizerType={visualizerType}
      />

      {/* Lyrics view */}
      <LyricsView
        isOpen={showLyrics}
        onClose={closeLyrics}
        onOpenParty={openKaraokeParty}
        track={lyricsTrack}
        lines={lyricsLines}
        currentSeconds={playbackSeconds}
      />

      <KaraokePartyModal isOpen={showKaraokeParty} onClose={closeKaraokeParty} />
    </div>
  );
}
