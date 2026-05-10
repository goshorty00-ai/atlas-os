import React, { useEffect, useMemo, useRef, useState } from "react";

type AlbumSummary = {
  id: string;
  title?: string;
  artist?: string;
  cover?: string;
  year?: string;
  genre?: string;
  tracks?: number;
  duration?: string;
};

type Track = {
  title: string;
  duration?: string;
  audioUrl: string;
};

type HostMessage =
  | { type: "media.library.updated"; payload?: { albums?: AlbumSummary[] } }
  | {
      type: "media.album.details";
      payload?: {
        id?: string;
        title?: string;
        artist?: string;
        year?: string;
        genre?: string;
        tracks?: number;
        duration?: string;
        trackList?: Track[];
      };
    };

function postToHost(message: any) {
  try {
    const w = window as any;
    if (w?.chrome?.webview?.postMessage) {
      w.chrome.webview.postMessage(message);
      return;
    }
  } catch {
    // ignore
  }

  try {
    window.parent?.postMessage(message, "*");
  } catch {
    // ignore
  }
}

export function MusicHostView() {
  const [albums, setAlbums] = useState<AlbumSummary[]>([]);
  const [selectedAlbumId, setSelectedAlbumId] = useState<string | null>(null);
  const [selectedAlbumDetails, setSelectedAlbumDetails] = useState<{
    id?: string;
    title?: string;
    artist?: string;
    year?: string;
    genre?: string;
    tracks?: number;
    duration?: string;
    trackList?: Track[];
  } | null>(null);

  const [currentTrackUrl, setCurrentTrackUrl] = useState<string | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const selectedAlbum = useMemo(() => {
    if (!selectedAlbumId) return null;
    return albums.find((a) => a.id === selectedAlbumId) ?? null;
  }, [albums, selectedAlbumId]);

  useEffect(() => {
    const handler = (event: any) => {
      const data = event?.data as HostMessage | undefined;
      if (!data || typeof (data as any).type !== "string") return;

      if (data.type === "media.library.updated") {
        const nextAlbums = data.payload?.albums ?? [];
        setAlbums(nextAlbums);

        // Auto-select first album if none selected.
        if (!selectedAlbumId && nextAlbums.length > 0) {
          const firstId = nextAlbums[0]?.id;
          if (firstId) {
            setSelectedAlbumId(firstId);
            postToHost({ type: "media.album.selected", payload: { id: firstId } });
          }
        }
      }

      if (data.type === "media.album.details") {
        setSelectedAlbumDetails(data.payload ?? null);
      }
    };

    try {
      const w = window as any;
      if (w?.chrome?.webview?.addEventListener) {
        w.chrome.webview.addEventListener("message", handler);
      }
    } catch {
      // ignore
    }

    window.addEventListener("message", handler);

    // Kick off host sync.
    postToHost({ type: "media.init" });

    return () => {
      try {
        const w = window as any;
        if (w?.chrome?.webview?.removeEventListener) {
          w.chrome.webview.removeEventListener("message", handler);
        }
      } catch {
        // ignore
      }
      window.removeEventListener("message", handler);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!audioRef.current) return;
    const audio = audioRef.current;
    const onPlay = () => setIsPlaying(true);
    const onPause = () => setIsPlaying(false);
    const onEnded = () => setIsPlaying(false);
    audio.addEventListener("play", onPlay);
    audio.addEventListener("pause", onPause);
    audio.addEventListener("ended", onEnded);
    return () => {
      audio.removeEventListener("play", onPlay);
      audio.removeEventListener("pause", onPause);
      audio.removeEventListener("ended", onEnded);
    };
  }, []);

  const onSelectAlbum = (albumId: string) => {
    setSelectedAlbumId(albumId);
    setSelectedAlbumDetails(null);
    setCurrentTrackUrl(null);
    postToHost({ type: "media.album.selected", payload: { id: albumId } });
  };

  const onPlayTrack = (track: Track) => {
    setCurrentTrackUrl(track.audioUrl);
    queueMicrotask(() => {
      try {
        audioRef.current?.play();
      } catch {
        // ignore
      }
    });
  };

  const togglePlayPause = () => {
    try {
      const audio = audioRef.current;
      if (!audio) return;
      if (audio.paused) audio.play();
      else audio.pause();
    } catch {
      // ignore
    }
  };

  return (
    <div className="fixed inset-0 w-full h-full overflow-hidden bg-[#0A0E14]">
      <div className="w-full h-full flex flex-col">
        <div className="h-14 shrink-0 flex items-center justify-between px-5 border-b border-cyan-500/10 bg-[#0A0E14]">
          <div className="flex items-baseline gap-3">
            <div className="text-cyan-300 font-mono tracking-wider text-sm">MUSIC</div>
            <div className="text-slate-400 font-mono text-xs">
              {albums.length > 0 ? `${albums.length} albums` : "Waiting for library..."}
            </div>
          </div>

          <button
            className="px-3 py-1.5 rounded-md border border-cyan-500/20 text-cyan-200 font-mono text-xs hover:border-cyan-500/40"
            onClick={() => {
              if (!selectedAlbumId) return;
              postToHost({ type: "media.album.refresh", payload: { id: selectedAlbumId } });
            }}
            disabled={!selectedAlbumId}
            title="Refresh"
          >
            Refresh
          </button>
        </div>

        <div className="flex-1 min-h-0 grid grid-cols-[360px_1fr]">
          <div className="min-h-0 border-r border-cyan-500/10 bg-[#0A0E14]">
            <div className="h-full overflow-auto p-3 space-y-2">
              {albums.map((a) => {
                const active = a.id === selectedAlbumId;
                return (
                  <button
                    key={a.id}
                    className={`w-full text-left rounded-lg border px-3 py-2 transition-colors ${
                      active
                        ? "border-cyan-500/40 bg-cyan-500/10"
                        : "border-white/5 bg-white/0 hover:border-cyan-500/20"
                    }`}
                    onClick={() => onSelectAlbum(a.id)}
                    title={a.title ?? a.id}
                  >
                    <div className="flex gap-3">
                      <div className="w-12 h-12 rounded-md overflow-hidden bg-white/5 border border-white/5 shrink-0">
                        {a.cover ? (
                          // cover is a host-served URL
                          <img
                            src={a.cover}
                            alt=""
                            className="w-full h-full object-cover"
                            draggable={false}
                          />
                        ) : null}
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="text-slate-100 font-mono text-xs truncate">
                          {a.title || "(untitled album)"}
                        </div>
                        <div className="text-slate-400 font-mono text-[11px] truncate mt-1">
                          {a.artist || "Unknown artist"}
                        </div>
                        <div className="text-slate-500 font-mono text-[10px] mt-1">
                          {(a.year && a.year !== "Unknown" ? a.year : "")}
                          {a.year && a.genre ? " • " : ""}
                          {(a.genre && a.genre !== "Unknown" ? a.genre : "")}
                        </div>
                      </div>
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="min-h-0 flex flex-col">
            <div className="p-5 border-b border-cyan-500/10 bg-[#0A0E14]">
              <div className="flex items-center justify-between gap-4">
                <div className="min-w-0">
                  <div className="text-slate-100 font-mono text-sm truncate">
                    {selectedAlbumDetails?.title || selectedAlbum?.title || "Select an album"}
                  </div>
                  <div className="text-slate-400 font-mono text-xs truncate mt-1">
                    {selectedAlbumDetails?.artist || selectedAlbum?.artist || ""}
                  </div>
                  <div className="text-slate-500 font-mono text-[11px] mt-1">
                    {selectedAlbumDetails?.tracks != null
                      ? `${selectedAlbumDetails.tracks} tracks`
                      : selectedAlbum?.tracks != null
                        ? `${selectedAlbum.tracks} tracks`
                        : ""}
                    {(selectedAlbumDetails?.duration || selectedAlbum?.duration) ? " • " : ""}
                    {selectedAlbumDetails?.duration || selectedAlbum?.duration || ""}
                  </div>
                </div>

                <div className="flex items-center gap-2 shrink-0">
                  <button
                    className="px-3 py-1.5 rounded-md border border-cyan-500/20 text-cyan-200 font-mono text-xs hover:border-cyan-500/40"
                    onClick={togglePlayPause}
                    disabled={!currentTrackUrl}
                    title={isPlaying ? "Pause" : "Play"}
                  >
                    {isPlaying ? "Pause" : "Play"}
                  </button>
                  <button
                    className="px-3 py-1.5 rounded-md border border-white/10 text-slate-200 font-mono text-xs hover:border-white/20"
                    onClick={() => {
                      if (!selectedAlbumId) return;
                      postToHost({ type: "media.album.edit", payload: { id: selectedAlbumId } });
                    }}
                    disabled={!selectedAlbumId}
                    title="Edit album"
                  >
                    Edit
                  </button>
                </div>
              </div>

              <audio
                ref={audioRef}
                src={currentTrackUrl ?? undefined}
                className="w-full mt-4"
                controls
              />
            </div>

            <div className="flex-1 min-h-0 overflow-auto p-3">
              <div className="space-y-2">
                {(selectedAlbumDetails?.trackList ?? []).map((t, idx) => (
                  <button
                    key={`${t.audioUrl}-${idx}`}
                    className="w-full text-left rounded-lg border border-white/5 hover:border-cyan-500/20 px-3 py-2"
                    onClick={() => onPlayTrack(t)}
                    title={t.title}
                  >
                    <div className="flex items-center justify-between gap-4">
                      <div className="text-slate-100 font-mono text-xs truncate">
                        {idx + 1}. {t.title}
                      </div>
                      <div className="text-slate-500 font-mono text-[11px] shrink-0">{t.duration || ""}</div>
                    </div>
                  </button>
                ))}

                {selectedAlbumId && !selectedAlbumDetails?.trackList ? (
                  <div className="text-slate-500 font-mono text-xs p-3">Loading tracks...</div>
                ) : null}

                {!selectedAlbumId ? (
                  <div className="text-slate-500 font-mono text-xs p-3">Pick an album on the left.</div>
                ) : null}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
