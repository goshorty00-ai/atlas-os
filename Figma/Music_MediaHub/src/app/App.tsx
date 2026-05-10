import { useEffect, useMemo, useRef, useState } from "react";
import { motion } from "motion/react";
import {
  Search,
  Sparkles,
  Clock,
  TrendingUp,
  Disc,
  Music2,
  Library,
  Heart,
} from "lucide-react";
import { AlbumCard } from "./components/AlbumCard";
import { NowPlayingPanel } from "./components/NowPlayingPanel";
import { HorizontalShelf } from "./components/HorizontalShelf";
import {
  ViewModeSelector,
  ViewMode,
} from "./components/ViewModeSelector";
import { GridView } from "./components/GridView";
import { CarouselView } from "./components/CarouselView";
import { MusicGalaxyView } from "./components/MusicGalaxyView";
import { AIAnalysisPanel } from "./components/AIAnalysisPanel";
import { AlbumBackCoverOverlay } from "./components/AlbumBackCoverOverlay";
import { AllSongsView } from "./components/AllSongsView";
import { LyricsPanel } from "./components/LyricsPanel";
import { HolographicWallView } from "./components/HolographicWallView";
import { WaveSpectrumView } from "./components/WaveSpectrumView";

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

interface ProviderStatus {
  name: string;
  isConfigured: boolean;
  requiresKey?: boolean;
  status?: string;
}

interface MbRelease {
  id?: string;
  date?: string;
  country?: string;
  label?: string;
  barcode?: string;
  status?: string;
  packaging?: string;
}

interface Album {
  id: string;
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
  songs?: Song[];
  detailsStatusText?: string;
  summaryText?: string;
  mbRelease?: MbRelease;
}

interface HostLibraryAlbum {
  id?: string;
  title?: string;
  artist?: string;
  artwork?: string;
  cover?: string;
  year?: number | string;
  genre?: string;
  tracks?: number;
  duration?: string;
  detailsStatusText?: string;
  summaryText?: string;
  mbRelease?: MbRelease;
  songs?: Array<{
    id?: string;
    title?: string;
    artist?: string;
    album?: string;
    duration?: string;
    artwork?: string;
    genre?: string;
    year?: number | string;
    audioUrl?: string;
    filePath?: string;
  }>;
}

interface HostMessage {
  type?: string;
  payload?: {
    albums?: HostLibraryAlbum[];
    id?: string;
    title?: string;
    artist?: string;
    year?: number | string;
    genre?: string;
    trackList?: Array<{
      title?: string;
      duration?: string;
      audioUrl?: string;
      filePath?: string;
      artist?: string;
      year?: number | string;
      genres?: string[];
    }>;
    detailsStatusText?: string;
    mbRelease?: MbRelease;
    filePath?: string;
    lines?: Array<{
      timeSeconds?: number;
      text?: string;
    }>;
    isPlaying?: boolean;
    progressSeconds?: number;
    totalSeconds?: number;
    progressText?: string;
    totalText?: string;
    volume?: number;
    shuffleEnabled?: boolean;
    repeatEnabled?: boolean;
    providers?: ProviderStatus[];
    bars?: number[];
  };
}

function normalizeYear(value?: number | string): number {
  const year = typeof value === "number" ? value : Number.parseInt(String(value ?? "0"), 10);
  return Number.isFinite(year) ? year : 0;
}

function formatSeconds(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds || 0));
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function mapHostAlbum(album: HostLibraryAlbum): Album {
  const title = String(album.title ?? "Unknown Album").trim() || "Unknown Album";
  const artist = String(album.artist ?? "Unknown Artist").trim() || "Unknown Artist";
  const artwork = String(album.artwork ?? album.cover ?? "").trim();
  const genre = String(album.genre ?? "Unknown").trim() || "Unknown";
  const year = normalizeYear(album.year);
  const songs: Song[] = Array.isArray(album.songs)
    ? album.songs.map((song, index) => ({
        id: String(song.id ?? song.filePath ?? `${album.id ?? title}-song-${index}`),
        title: String(song.title ?? `Track ${index + 1}`),
        artist: String(song.artist ?? artist),
        album: String(song.album ?? title),
        duration: String(song.duration ?? "0:00"),
        artwork: String(song.artwork ?? artwork),
        genre: String(song.genre ?? genre),
        year: normalizeYear(song.year ?? year),
        audioUrl: String(song.audioUrl ?? ""),
        filePath: String(song.filePath ?? song.id ?? ""),
      }))
    : [];

  return {
    id: String(album.id ?? title),
    artwork,
    title,
    artist,
    year,
    genre,
    tracks: typeof album.tracks === "number" ? album.tracks : songs.length,
    duration: String(album.duration ?? "0:00"),
    detailsStatusText: String(album.detailsStatusText ?? "").trim() || undefined,
    summaryText: String(album.summaryText ?? "").trim() || undefined,
    mbRelease: album.mbRelease,
    songs,
  };
}

function postHostMessage(message: Record<string, unknown>) {
  try {
    const host = (window as Window & { chrome?: { webview?: { postMessage?: (message: unknown) => void } } }).chrome?.webview;
    if (host?.postMessage) {
      host.postMessage(message);
    }
  } catch {
  }
}

export default function App() {
  const [viewMode, setViewMode] = useState<ViewMode>("grid");
  const [albums, setAlbums] = useState<Album[]>([]);
  const [songs, setSongs] = useState<Song[]>([]);
  const [detailAlbum, setDetailAlbum] = useState<Album | null>(null);
  const [analysisAlbum, setAnalysisAlbum] = useState<Album | null>(null);
  const [currentAlbum, setCurrentAlbum] = useState<Album | null>(null);
  const [currentTrack, setCurrentTrack] = useState<Song | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [showLyrics, setShowLyrics] = useState(false);
  const [lyricsLines, setLyricsLines] = useState<Array<{ timeSeconds: number; text: string }>>([]);
  const [isPlaying, setIsPlaying] = useState(false);
  const [progressPercent, setProgressPercent] = useState(0);
  const [volume, setVolume] = useState(75);
  const [playbackSeconds, setPlaybackSeconds] = useState(0);
  const [totalSeconds, setTotalSeconds] = useState(0);
  const [currentTimeText, setCurrentTimeText] = useState("0:00");
  const [totalTimeText, setTotalTimeText] = useState("0:00");
  const [shuffleEnabled, setShuffleEnabled] = useState(false);
  const [repeatEnabled, setRepeatEnabled] = useState(false);
  const [apiProviders, setApiProviders] = useState<ProviderStatus[]>([]);
  const [spectrumBars, setSpectrumBars] = useState<number[]>(() => Array.from({ length: 64 }, () => 0));

  const nowPlayingFilePathRef = useRef("");
  const lastLyricsRequestFilePathRef = useRef("");
  const albumsRef = useRef<Album[]>([]);
  const lastSpectrumUpdateAtRef = useRef(0);
  const pendingSpectrumBarsRef = useRef<number[] | null>(null);
  const spectrumAnimationFrameRef = useRef<number | null>(null);

  useEffect(() => {
    albumsRef.current = albums;
  }, [albums]);

  useEffect(() => () => {
    if (spectrumAnimationFrameRef.current !== null) {
      window.cancelAnimationFrame(spectrumAnimationFrameRef.current);
    }
  }, []);

  useEffect(() => {
    const webview = (window as Window & { chrome?: { webview?: { addEventListener?: (type: string, listener: (event: { data?: HostMessage }) => void) => void; removeEventListener?: (type: string, listener: (event: { data?: HostMessage }) => void) => void } } }).chrome?.webview;

    const handleMessage = (event: { data?: HostMessage }) => {
      const data = event?.data;
      if (!data?.type) return;

      if (data.type === "media.library.updated") {
        const nextAlbums = Array.isArray(data.payload?.albums)
          ? data.payload.albums.map(mapHostAlbum)
          : [];
        const nextSongs = nextAlbums.flatMap((album) => album.songs ?? []);

        setAlbums(nextAlbums);
        setSongs(nextSongs);
        setDetailAlbum((current) => current ? nextAlbums.find((album) => album.id === current.id) ?? current : null);
        setAnalysisAlbum((current) => current ? nextAlbums.find((album) => album.id === current.id) ?? current : null);
        setCurrentAlbum((current) => current ? nextAlbums.find((album) => album.id === current.id) ?? current : null);
        setCurrentTrack((current) => {
          if (!current) return current;
          return nextSongs.find((song) => song.filePath === current.filePath || song.id === current.id) ?? current;
        });
        return;
      }

      if (data.type === "media.album.details") {
        const albumId = String(data.payload?.id ?? "");
        if (!albumId) return;

        const trackList = Array.isArray(data.payload?.trackList)
          ? data.payload.trackList.map((track, index) => ({
              id: String(track.filePath ?? `${albumId}-track-${index}`),
              title: String(track.title ?? `Track ${index + 1}`),
              artist: String(track.artist ?? data.payload?.artist ?? "Unknown Artist"),
              album: String(data.payload?.title ?? "Unknown Album"),
              duration: String(track.duration ?? "0:00"),
              artwork: "",
              genre: String(track.genres?.[0] ?? data.payload?.genre ?? "Unknown"),
              year: normalizeYear(track.year ?? data.payload?.year),
              audioUrl: String(track.audioUrl ?? ""),
              filePath: String(track.filePath ?? ""),
            }))
          : [];

        const detailsStatusText = String(data.payload?.detailsStatusText ?? "").trim();
        const summaryText = String(data.payload?.summaryText ?? "").trim();
        const nextRelease = data.payload?.mbRelease;
        const nextTitle = String(data.payload?.title ?? "Unknown Album");

        setAlbums((current) => current.map((album) => {
          if (album.id !== albumId) return album;
          return {
            ...album,
            title: nextTitle,
            artist: String(data.payload?.artist ?? album.artist),
            year: normalizeYear(data.payload?.year ?? album.year),
            genre: String(data.payload?.genre ?? album.genre),
            duration: String(data.payload?.duration ?? album.duration ?? "0:00"),
            tracks: trackList.length > 0 ? trackList.length : album.tracks,
            songs: trackList.length > 0
              ? trackList.map((track) => ({ ...track, artwork: album.artwork || track.artwork }))
              : album.songs,
            detailsStatusText: detailsStatusText || album.detailsStatusText,
            summaryText: summaryText || album.summaryText,
            mbRelease: nextRelease ?? album.mbRelease,
          };
        }));

        setCurrentAlbum((current) => current && current.id === albumId
          ? {
              ...current,
              title: nextTitle,
              artist: String(data.payload?.artist ?? current.artist),
              year: normalizeYear(data.payload?.year ?? current.year),
              genre: String(data.payload?.genre ?? current.genre),
              duration: String(data.payload?.duration ?? current.duration ?? "0:00"),
              tracks: trackList.length > 0 ? trackList.length : current.tracks,
              songs: trackList.length > 0
                ? trackList.map((track) => ({ ...track, artwork: current.artwork || track.artwork }))
                : current.songs,
              detailsStatusText: detailsStatusText || current.detailsStatusText,
              summaryText: summaryText || current.summaryText,
              mbRelease: nextRelease ?? current.mbRelease,
            }
          : current);

        setDetailAlbum((current) => current && current.id === albumId
          ? {
              ...current,
              title: nextTitle,
              artist: String(data.payload?.artist ?? current.artist),
              year: normalizeYear(data.payload?.year ?? current.year),
              genre: String(data.payload?.genre ?? current.genre),
              duration: String(data.payload?.duration ?? current.duration ?? "0:00"),
              tracks: trackList.length > 0 ? trackList.length : current.tracks,
              songs: trackList.length > 0
                ? trackList.map((track) => ({ ...track, artwork: current.artwork || track.artwork }))
                : current.songs,
              detailsStatusText: detailsStatusText || current.detailsStatusText,
              summaryText: summaryText || current.summaryText,
              mbRelease: nextRelease ?? current.mbRelease,
            }
          : current);

        setAnalysisAlbum((current) => current && current.id === albumId
          ? {
              ...current,
              title: nextTitle,
              artist: String(data.payload?.artist ?? current.artist),
              year: normalizeYear(data.payload?.year ?? current.year),
              genre: String(data.payload?.genre ?? current.genre),
              duration: String(data.payload?.duration ?? current.duration ?? "0:00"),
              tracks: trackList.length > 0 ? trackList.length : current.tracks,
              songs: trackList.length > 0
                ? trackList.map((track) => ({ ...track, artwork: current.artwork || track.artwork }))
                : current.songs,
              detailsStatusText: detailsStatusText || current.detailsStatusText,
              summaryText: summaryText || current.summaryText,
              mbRelease: nextRelease ?? current.mbRelease,
            }
          : current);

        setSongs((current) => {
          if (trackList.length === 0) return current;
          const others = current.filter((song) => song.album !== nextTitle);
          return [...others, ...trackList];
        });
        return;
      }

      if (data.type === "playback.state") {
        const filePath = String(data.payload?.filePath ?? "");
        const progressSeconds = Number(data.payload?.progressSeconds ?? 0);
        const total = Number(data.payload?.totalSeconds ?? 0);

        setIsPlaying(Boolean(data.payload?.isPlaying));
        setVolume(Math.max(0, Math.min(100, Number(data.payload?.volume ?? 75))));
        setPlaybackSeconds(progressSeconds);
        setTotalSeconds(total);
        setCurrentTimeText(String(data.payload?.progressText ?? formatSeconds(progressSeconds)));
        setTotalTimeText(String(data.payload?.totalText ?? formatSeconds(total)));
        setShuffleEnabled(Boolean(data.payload?.shuffleEnabled));
        setRepeatEnabled(Boolean(data.payload?.repeatEnabled));
        setProgressPercent(total > 0 ? Math.max(0, Math.min(100, (progressSeconds / total) * 100)) : 0);

        const previousPath = nowPlayingFilePathRef.current;
        nowPlayingFilePathRef.current = filePath;
        if (filePath && filePath !== previousPath) {
          const track = albumsRef.current.flatMap((album) => album.songs ?? []).find((candidate) => candidate.filePath === filePath) ?? null;
          if (track) {
            setCurrentTrack(track);
            setCurrentAlbum(albumsRef.current.find((album) => (album.songs ?? []).some((candidate) => candidate.filePath === filePath)) ?? null);
          }
        }
        return;
      }

      if (data.type === "lyrics.result") {
        const filePath = String(data.payload?.filePath ?? "");
        const expected = String(lastLyricsRequestFilePathRef.current ?? "");
        if (expected && filePath && expected.toLowerCase() !== filePath.toLowerCase()) {
          return;
        }

        const lines = Array.isArray(data.payload?.lines)
          ? data.payload.lines.map((line) => ({
              timeSeconds: Number(line.timeSeconds ?? 0),
              text: String(line.text ?? ""),
            }))
          : [];
        setLyricsLines(lines);
        return;
      }

      if (data.type === "media.api.status") {
        setApiProviders(Array.isArray(data.payload?.providers) ? data.payload.providers : []);
        return;
      }

      if (data.type === "media.spectrum") {
        pendingSpectrumBarsRef.current = Array.isArray(data.payload?.bars)
          ? data.payload.bars.map((value) => Math.max(0, Math.min(1, Number(value ?? 0))))
          : [];

        if (spectrumAnimationFrameRef.current !== null) {
          return;
        }

        spectrumAnimationFrameRef.current = window.requestAnimationFrame(() => {
          spectrumAnimationFrameRef.current = null;
          const now = performance.now();
          if (now - lastSpectrumUpdateAtRef.current < 33) {
            return;
          }

          lastSpectrumUpdateAtRef.current = now;
          setSpectrumBars(pendingSpectrumBarsRef.current ?? []);
        });
      }
    };

    if (webview?.addEventListener) {
      webview.addEventListener("message", handleMessage);
    }

    postHostMessage({ type: "media.init" });

    return () => {
      if (webview?.removeEventListener) {
        webview.removeEventListener("message", handleMessage);
      }
    };
  }, []);

  const filteredAlbums = useMemo(() => {
    const query = searchQuery.trim().toLowerCase();
    if (!query) return albums;

    return albums.filter((album) =>
      album.title.toLowerCase().includes(query) ||
      album.artist.toLowerCase().includes(query) ||
      album.genre.toLowerCase().includes(query),
    );
  }, [albums, searchQuery]);

  const filteredSongs = useMemo(() => {
    const source = songs;
    const query = searchQuery.trim().toLowerCase();
    if (!query) return source;

    return source.filter((song) =>
      song.title.toLowerCase().includes(query) ||
      song.artist.toLowerCase().includes(query) ||
      song.album.toLowerCase().includes(query) ||
      song.genre.toLowerCase().includes(query),
    );
  }, [filteredAlbums, searchQuery, songs]);

  const activeAlbums = filteredAlbums;
  const activeSongs = filteredSongs;
  const decoratedAlbums = activeAlbums.map((album) => ({
    ...album,
    isPlaying: isPlaying && currentAlbum?.id === album.id,
  }));
  const currentDisplayTrack = currentTrack ?? activeSongs[0] ?? null;
  const currentDisplayAlbum = currentAlbum
    ?? (currentDisplayTrack
      ? decoratedAlbums.find((album) => album.title === currentDisplayTrack.album || album.id === currentDisplayTrack.album) ?? null
      : null)
    ?? decoratedAlbums[0]
    ?? null;
  const configuredProviderCount = apiProviders.filter((provider) => provider.isConfigured).length;
  const realProviderCount = apiProviders.length;
  const artistSpotlightAlbums = useMemo(() => {
    if (!currentDisplayAlbum?.artist) return [] as Album[];
    return decoratedAlbums.filter((album) => album.artist === currentDisplayAlbum.artist && album.id !== currentDisplayAlbum.id).slice(0, 10);
  }, [currentDisplayAlbum?.artist, currentDisplayAlbum?.id, decoratedAlbums]);
  const genreSpotlightAlbums = useMemo(() => {
    if (!currentDisplayAlbum?.genre) return [] as Album[];
    return decoratedAlbums.filter((album) => album.genre === currentDisplayAlbum.genre && album.id !== currentDisplayAlbum.id).slice(0, 10);
  }, [currentDisplayAlbum?.genre, currentDisplayAlbum?.id, decoratedAlbums]);
  const visibleTrackAlbum = currentAlbum ?? decoratedAlbums[0] ?? null;
  const visibleTracks = (visibleTrackAlbum?.songs?.length ?? 0) > 0
    ? visibleTrackAlbum?.songs ?? []
    : activeSongs;

  const openAlbum = (album: Album) => {
    setDetailAlbum(album);
    setCurrentAlbum(album);
    if (!currentTrack && (album.songs?.length ?? 0) > 0) {
      setCurrentTrack(album.songs?.[0] ?? null);
    }
    postHostMessage({ type: "media.album.selected", payload: { id: album.id } });
  };

  const openLyricsForSong = (song: Song) => {
    setCurrentTrack(song);
    const filePath = String(song.filePath ?? song.id ?? "").trim();
    if (!filePath) return;
    setShowLyrics(true);
    lastLyricsRequestFilePathRef.current = filePath;
    setLyricsLines([]);
    postHostMessage({ type: "lyrics.get", payload: { filePath } });
  };

  const runAlbumAction = (album: Album, action: "play" | "playlist" | "lyrics" | "edit" | "cover" | "aiCover" | "optimize" | "refresh" | "openFolder" | "remove") => {
    if (action === "play") {
      setCurrentAlbum(album);
      setCurrentTrack(album.songs?.[0] ?? null);
      postHostMessage({ type: "media.album.play", payload: { id: album.id } });
      return;
    }

    if (action === "playlist") {
      postHostMessage({ type: "media.album.playlist", payload: { id: album.id } });
      return;
    }

    if (action === "lyrics") {
      const firstTrack = album.songs?.find((song) => Boolean(song.filePath));
      if (firstTrack) openLyricsForSong(firstTrack);
      return;
    }

    if (action === "optimize") {
      setAnalysisAlbum(album);
    }

    const messageTypeByAction: Record<Exclude<typeof action, "play" | "playlist" | "lyrics">, string> = {
      edit: "media.album.edit",
      cover: "media.album.cover",
      aiCover: "media.album.ai_cover",
      optimize: "media.album.ai_tag",
      refresh: "media.album.refresh",
      openFolder: "media.album.open_folder",
      remove: "media.album.remove",
    };

    postHostMessage({ type: messageTypeByAction[action], payload: { id: album.id } });
  };

  const playSong = (song: Song) => {
    setCurrentTrack(song);
    const album = albums.find((candidate) => candidate.title === song.album || candidate.id === song.album)
      ?? albums.find((candidate) => (candidate.songs ?? []).some((candidateSong) => candidateSong.filePath === song.filePath))
      ?? null;
    if (album) {
      setCurrentAlbum(album);
    }

    const filePath = (song.filePath ?? song.id ?? "").trim();
    if (filePath) {
      postHostMessage({ type: "play.track", payload: { filePath } });
    }
  };

  const openLyrics = () => {
    if (!currentDisplayTrack) return;
    openLyricsForSong(currentDisplayTrack);
  };

  const stepTrack = (direction: 1 | -1) => {
    const pool = (currentDisplayAlbum?.songs?.length ?? 0) > 0
      ? currentDisplayAlbum?.songs ?? []
      : activeSongs;
    if (pool.length === 0) return;

    const currentIndex = pool.findIndex((song) => song.filePath === currentDisplayTrack?.filePath || song.id === currentDisplayTrack?.id);
    const nextIndex = currentIndex >= 0
      ? (currentIndex + direction + pool.length) % pool.length
      : 0;
    const nextTrack = pool[nextIndex] ?? null;
    if (!nextTrack) return;

    setCurrentTrack(nextTrack);
    const album = albums.find((candidate) => candidate.title === nextTrack.album || candidate.id === nextTrack.album)
      ?? albums.find((candidate) => (candidate.songs ?? []).some((candidateSong) => candidateSong.filePath === nextTrack.filePath))
      ?? null;
    if (album) setCurrentAlbum(album);
  };

  return (
    <div className="min-h-screen bg-black text-white overflow-hidden">
      <div className="fixed inset-0 bg-gradient-to-br from-black via-gray-900 to-black">
        <div className="absolute inset-0 opacity-30">
          <div className="absolute top-0 left-1/4 w-96 h-96 bg-cyan-500/20 rounded-full blur-3xl animate-pulse" />
          <div
            className="absolute bottom-0 right-1/4 w-96 h-96 bg-purple-500/20 rounded-full blur-3xl animate-pulse"
            style={{ animationDelay: "1s" }}
          />
          <div
            className="absolute top-1/2 left-1/2 w-96 h-96 bg-orange-500/10 rounded-full blur-3xl animate-pulse"
            style={{ animationDelay: "2s" }}
          />
        </div>
      </div>

      <div className="relative z-10 pb-32">
        <header className="sticky top-0 z-40 bg-black/60 backdrop-blur-2xl border-b border-white/10">
          <div className="max-w-[1800px] mx-auto px-6 py-4">
            <div className="flex items-center justify-between gap-6">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500 flex items-center justify-center">
                  <Music2 className="w-6 h-6 text-white" />
                </div>
                <div>
                  <h1 className="text-xl font-bold bg-gradient-to-r from-cyan-400 to-purple-400 bg-clip-text text-transparent">
                    AI Music Vault
                  </h1>
                  <p className="text-xs text-gray-400">
                    Cinematic Media Centre
                  </p>
                </div>
              </div>

              <div className="flex-1 max-w-2xl">
                <div className="relative">
                  <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
                  <input
                    type="text"
                    placeholder="Search albums, artists, genres..."
                    value={searchQuery}
                    onChange={(event) => setSearchQuery(event.target.value)}
                    className="w-full h-12 pl-12 pr-4 bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 text-white placeholder-gray-500 focus:outline-none focus:border-cyan-500/50 transition-colors"
                  />
                </div>
              </div>

              <ViewModeSelector
                currentMode={viewMode}
                onModeChange={setViewMode}
              />
            </div>
          </div>
        </header>

        <main className="max-w-[1800px] mx-auto px-6 py-8 space-y-12">
          {albums.length === 0 && songs.length === 0 && (
            <div className="rounded-3xl border border-white/10 bg-white/5 px-8 py-12 text-center backdrop-blur-xl">
              <h2 className="text-2xl font-semibold text-white">No music loaded yet</h2>
              <p className="mt-3 text-sm text-gray-400">
                Atlas is waiting for your real library data. This view no longer falls back to demo albums.
              </p>
            </div>
          )}

          {viewMode === "grid" && (
            <>
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <div className="rounded-3xl border border-white/10 bg-white/5 p-5 backdrop-blur-xl">
                  <div className="flex items-center gap-3 text-cyan-300">
                    <Library className="h-5 w-5" />
                    <span className="text-sm font-semibold uppercase tracking-[0.24em]">Library</span>
                  </div>
                  <div className="mt-4 text-3xl font-bold text-white">{decoratedAlbums.length}</div>
                  <p className="mt-1 text-sm text-gray-400">Albums loaded from your real folders</p>
                </div>

                <div className="rounded-3xl border border-white/10 bg-white/5 p-5 backdrop-blur-xl">
                  <div className="flex items-center gap-3 text-orange-300">
                    <Music2 className="h-5 w-5" />
                    <span className="text-sm font-semibold uppercase tracking-[0.24em]">Tracks</span>
                  </div>
                  <div className="mt-4 text-3xl font-bold text-white">{activeSongs.length}</div>
                  <p className="mt-1 text-sm text-gray-400">Tracks visible in the current filter</p>
                </div>

                <div className="rounded-3xl border border-white/10 bg-white/5 p-5 backdrop-blur-xl">
                  <div className="flex items-center gap-3 text-purple-300">
                    <Sparkles className="h-5 w-5" />
                    <span className="text-sm font-semibold uppercase tracking-[0.24em]">Metadata APIs</span>
                  </div>
                  <div className="mt-4 text-3xl font-bold text-white">{configuredProviderCount}/{realProviderCount}</div>
                  <p className="mt-1 text-sm text-gray-400">Connected or keyless metadata providers available</p>
                </div>

                <div className="rounded-3xl border border-white/10 bg-white/5 p-5 backdrop-blur-xl">
                  <div className="flex items-center gap-3 text-green-300">
                    <Disc className="h-5 w-5" />
                    <span className="text-sm font-semibold uppercase tracking-[0.24em]">Focused Album</span>
                  </div>
                  <div className="mt-4 text-lg font-semibold text-white truncate">{currentDisplayAlbum?.title ?? "None selected"}</div>
                  <p className="mt-1 text-sm text-gray-400 truncate">{currentDisplayAlbum?.artist ?? "Pick an album to inspect tracks and metadata"}</p>
                </div>
              </div>

              {apiProviders.length > 0 ? (
                <div className="rounded-3xl border border-white/10 bg-white/5 p-5 backdrop-blur-xl">
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="text-sm font-semibold text-white">Provider status</span>
                    {apiProviders.map((provider) => (
                      <span
                        key={provider.name}
                        className={`rounded-full border px-3 py-1 text-xs font-medium ${provider.isConfigured ? "border-cyan-500/30 bg-cyan-500/10 text-cyan-300" : "border-white/10 bg-white/5 text-gray-400"}`}
                      >
                        {provider.name} {provider.status ?? (provider.isConfigured ? "connected" : provider.requiresKey ? "setup required" : "available")}
                      </span>
                    ))}
                  </div>
                </div>
              ) : null}

              <HorizontalShelf title="Library Albums">
                {decoratedAlbums.slice(0, 12).map((album) => (
                  <AlbumCard key={album.id} {...album} onClick={() => openAlbum(album)} onAction={(action) => runAlbumAction(album, action)} />
                ))}
              </HorizontalShelf>

              {artistSpotlightAlbums.length > 0 ? (
                <HorizontalShelf title={`More From ${currentDisplayAlbum?.artist ?? "This Artist"}`}>
                  {artistSpotlightAlbums.map((album) => (
                    <AlbumCard key={album.id} {...album} onClick={() => openAlbum(album)} onAction={(action) => runAlbumAction(album, action)} />
                  ))}
                </HorizontalShelf>
              ) : null}

              {genreSpotlightAlbums.length > 0 ? (
                <HorizontalShelf title={`${currentDisplayAlbum?.genre ?? "Genre"} Albums`}>
                  {genreSpotlightAlbums.map((album) => (
                    <AlbumCard key={album.id} {...album} onClick={() => openAlbum(album)} onAction={(action) => runAlbumAction(album, action)} />
                  ))}
                </HorizontalShelf>
              ) : null}

              <div>
                <h2 className="text-2xl font-semibold text-white mb-6">
                  All Albums
                </h2>
                <GridView albums={decoratedAlbums} onAlbumClick={openAlbum} onAlbumAction={runAlbumAction} />
              </div>

              <div className="rounded-3xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl">
                <div className="flex items-center justify-between gap-4 mb-5">
                  <div>
                    <h2 className="text-2xl font-semibold text-white">
                      {visibleTrackAlbum ? `Tracks in ${visibleTrackAlbum.title}` : "Library Tracks"}
                    </h2>
                    <p className="mt-1 text-sm text-gray-400">
                      {visibleTrackAlbum
                        ? `${visibleTracks.length} track${visibleTracks.length === 1 ? "" : "s"} visible in the main music view`
                        : `${visibleTracks.length} track${visibleTracks.length === 1 ? "" : "s"} available`}
                    </p>
                  </div>
                  {visibleTrackAlbum && (
                    <button
                      type="button"
                      onClick={() => setViewMode("songs")}
                      className="rounded-2xl border border-cyan-500/30 bg-cyan-500/10 px-4 py-2 text-sm font-medium text-cyan-300 transition-colors hover:bg-cyan-500/20"
                    >
                      Open all songs view
                    </button>
                  )}
                </div>

                {visibleTracks.length > 0 ? (
                  <div className="space-y-2">
                    {visibleTracks.slice(0, 18).map((song, index) => (
                      <button
                        key={`${song.id}-${index}`}
                        type="button"
                        onClick={() => playSong(song)}
                        className="flex w-full items-center gap-4 rounded-2xl border border-white/10 bg-black/20 px-4 py-3 text-left transition-all hover:border-cyan-500/30 hover:bg-white/10"
                      >
                        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-cyan-500/20 to-purple-500/20 text-sm font-semibold text-cyan-200">
                          {String(index + 1).padStart(2, "0")}
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-semibold text-white">{song.title}</div>
                          <div className="truncate text-xs text-gray-400">{song.artist}</div>
                        </div>
                        <div className="hidden min-w-0 flex-1 text-sm text-gray-400 md:block">
                          <div className="truncate">{song.album}</div>
                        </div>
                        <div className="rounded-full border border-purple-500/30 bg-purple-500/10 px-3 py-1 text-xs text-purple-300">
                          {song.genre}
                        </div>
                        <div className="w-16 text-right text-sm text-gray-400">{song.duration}</div>
                      </button>
                    ))}
                  </div>
                ) : (
                  <div className="rounded-2xl border border-dashed border-white/15 px-4 py-6 text-sm text-gray-400">
                    No track rows are available for this album yet.
                  </div>
                )}
              </div>
            </>
          )}

          {viewMode === "songs" && (
            <AllSongsView
              songs={activeSongs}
              onSongClick={(song) => {
                const album = albums.find((candidate) => candidate.title === song.album || candidate.id === song.album);
                if (album) openAlbum(album);
              }}
              onPlaySong={playSong}
              onOpenLyrics={openLyricsForSong}
              onCreatePlaylist={(song) => {
                const album = albums.find((candidate) => candidate.title === song.album || candidate.id === song.album)
                  ?? albums.find((candidate) => (candidate.songs ?? []).some((candidateSong) => candidateSong.filePath === song.filePath));
                if (album) runAlbumAction(album, "playlist");
              }}
            />
          )}

          {viewMode === "list" && (
            <div className="space-y-6">
              <h2 className="text-2xl font-semibold text-white">
                All Albums - List View
              </h2>
              <div className="space-y-2">
                {decoratedAlbums.map((album, index) => (
                  <motion.div
                    key={album.id}
                    className="flex items-center gap-4 p-4 rounded-xl bg-white/5 backdrop-blur-sm border border-white/10 hover:border-cyan-500/30 hover:bg-white/10 transition-all cursor-pointer group"
                    initial={{ opacity: 0, x: -20 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: index * 0.03 }}
                    onClick={() => openAlbum(album)}
                  >
                    <span className="text-gray-500 w-8 text-right">{index + 1}</span>
                    <div className="relative w-12 h-12 flex-shrink-0">
                      <img src={album.artwork} alt={album.title} className="w-full h-full object-cover rounded-lg" />
                      {album.isPlaying && (
                        <div className="absolute inset-0 bg-cyan-500/20 rounded-lg flex items-center justify-center">
                          <div className="flex gap-0.5">
                            {[0, 1, 2].map((barIndex) => (
                              <motion.div
                                key={barIndex}
                                className="w-1 bg-cyan-400 rounded-full"
                                animate={{ height: ["4px", "16px", "4px"] }}
                                transition={{ duration: 0.8, repeat: Infinity, delay: barIndex * 0.2 }}
                              />
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                    <div className="flex-1 min-w-0">
                      <h3 className="text-white font-medium truncate group-hover:text-cyan-400 transition-colors">{album.title}</h3>
                      <p className="text-gray-400 text-sm truncate">{album.artist}</p>
                    </div>
                    <div className="text-gray-400 text-sm w-32 truncate">{album.genre}</div>
                    <div className="text-gray-500 text-sm w-16">{album.year}</div>
                  </motion.div>
                ))}
              </div>
            </div>
          )}

          {viewMode === "carousel" && (
            <div className="space-y-6">
              <h2 className="text-2xl font-semibold text-white text-center">3D Album Carousel</h2>
              <CarouselView albums={decoratedAlbums} onAlbumClick={openAlbum} />
            </div>
          )}

          {viewMode === "wall" && (
            <div className="space-y-6">
              <h2 className="text-2xl font-semibold text-white text-center">Holographic Music Wall</h2>
              <HolographicWallView albums={decoratedAlbums} onAlbumClick={openAlbum} onAlbumAction={runAlbumAction} />
            </div>
          )}

          {viewMode === "spectrum" && (
            <div className="space-y-6">
              <h2 className="text-2xl font-semibold text-white text-center">Wave Spectrum</h2>
              <WaveSpectrumView albums={decoratedAlbums} spectrumBars={spectrumBars} onAlbumClick={openAlbum} />
            </div>
          )}

          {viewMode === "galaxy" && (
            <div className="space-y-6">
              <h2 className="text-2xl font-semibold text-white text-center">Music Galaxy Explorer</h2>
              <MusicGalaxyView albums={decoratedAlbums} />
            </div>
          )}
        </main>
      </div>

      <NowPlayingPanel
        artwork={currentDisplayTrack?.artwork ?? currentDisplayAlbum?.artwork ?? ""}
        title={currentDisplayTrack?.title ?? currentDisplayAlbum?.title ?? "Nothing Playing"}
        artist={currentDisplayTrack?.artist ?? currentDisplayAlbum?.artist ?? "Atlas Music"}
        album={currentDisplayTrack?.album ?? currentDisplayAlbum?.title ?? "Library"}
        isPlaying={isPlaying}
        progress={progressPercent}
        volume={volume}
        currentTimeText={currentTimeText}
        totalTimeText={totalTimeText}
        shuffleEnabled={shuffleEnabled}
        repeatEnabled={repeatEnabled}
        spectrumBars={spectrumBars}
        onTogglePlayPause={() => {
          setIsPlaying((current) => !current);
          postHostMessage({ type: "play.toggle" });
        }}
        onNext={() => {
          stepTrack(1);
          postHostMessage({ type: "play.next" });
        }}
        onPrevious={() => {
          stepTrack(-1);
          postHostMessage({ type: "play.prev" });
        }}
        onToggleShuffle={() => {
          setShuffleEnabled((current) => !current);
          postHostMessage({ type: "play.shuffle" });
        }}
        onToggleRepeat={() => {
          setRepeatEnabled((current) => !current);
          postHostMessage({ type: "play.repeat" });
        }}
        onVolumeChange={(nextVolume) => {
          setVolume(nextVolume);
          postHostMessage({ type: "play.volume", payload: { value: nextVolume } });
        }}
        onSeekChange={(nextProgress) => {
          setProgressPercent(nextProgress);
          const seconds = totalSeconds > 0 ? (nextProgress / 100) * totalSeconds : 0;
          setPlaybackSeconds(seconds);
          setCurrentTimeText(formatSeconds(seconds));
          postHostMessage({ type: "play.seek", payload: { seconds } });
        }}
        onLyricsClick={openLyrics}
      />

      <AlbumBackCoverOverlay
        album={detailAlbum}
        providers={apiProviders}
        onClose={() => setDetailAlbum(null)}
        onPlayAlbum={() => {
          if (!detailAlbum) return;
          runAlbumAction(detailAlbum, "play");
        }}
        onRefresh={() => {
          if (!detailAlbum) return;
          runAlbumAction(detailAlbum, "refresh");
        }}
        onPlayTrack={playSong}
      />

      <AIAnalysisPanel album={analysisAlbum} providers={apiProviders} onClose={() => setAnalysisAlbum(null)} />

      <LyricsPanel
        isOpen={showLyrics}
        onClose={() => setShowLyrics(false)}
        songTitle={currentDisplayTrack?.title ?? currentDisplayAlbum?.title ?? "Nothing Playing"}
        artist={currentDisplayTrack?.artist ?? currentDisplayAlbum?.artist ?? "Atlas Music"}
        artwork={currentDisplayTrack?.artwork ?? currentDisplayAlbum?.artwork ?? ""}
        currentSeconds={playbackSeconds}
        lines={lyricsLines}
      />
    </div>
  );
}