import type { CSSProperties, ReactNode } from "react";
import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import { Calendar, ChevronLeft, ChevronRight, Clock3, Clapperboard, Headphones, Pause, Play, Radio, Shield, Star, Users, Users2, Waves, X } from "lucide-react";

export type LocalLibraryMode = "movies" | "music";

export interface LocalLibraryMovie {
  id: string;
  type: "movie";
  title: string;
  subtitle?: string;
  year: number;
  certification: string;
  runtime: number;
  genres: string[];
  rating: number;
  resolution: string[];
  audio: string[];
  director: string;
  cast: string[];
  plot: string;
  releaseDate: string;
  progress?: number;
  coverUrl: string;
  backdropUrl?: string;
}

export interface LocalLibraryAlbum {
  id: string;
  type: "album";
  title: string;
  artist: string;
  year: number;
  genre: string[];
  trackCount: number;
  duration: string;
  label: string;
  popularity: number;
  isFavorite: boolean;
  progress?: number;
  tracks: string[];
  coverUrl: string;
}

interface LocalLibraryOverlayProps {
  open: boolean;
  mode: LocalLibraryMode;
  onModeChange: (mode: LocalLibraryMode) => void;
  movies: LocalLibraryMovie[];
  albums: LocalLibraryAlbum[];
  topSlot?: ReactNode;
  onClose?: () => void;
  onPlayMovie?: (movieId: string) => void;
  onPlayAlbum?: (albumId: string) => void;
}

type CarouselPosition = "prev" | "current" | "next";

interface CarouselStageConfig {
  currentWidth: number;
  currentHeight: number;
  sideWidth: number;
  sideHeight: number;
  sideOffset?: string;
  sideScale?: number;
  sideRotateY?: number;
  sideOpacity?: number;
  perspective?: number;
  glowClassName?: string;
  spotlightClassName?: string;
}

export function LocalLibraryOverlay({
  open,
  mode,
  onModeChange,
  movies,
  albums,
  topSlot,
  onClose,
  onPlayMovie,
  onPlayAlbum,
}: LocalLibraryOverlayProps) {
  useEffect(() => {
    if (!open || !onClose) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose, open]);

  const controls = (
    <div className="flex flex-wrap items-center gap-3">
      {topSlot}
      <div className="inline-flex rounded-2xl border border-white/10 bg-black/35 p-1.5 shadow-[0_0_40px_rgba(0,0,0,0.35)] backdrop-blur-xl">
        <button
          type="button"
          className={`rounded-xl px-4 py-2 text-sm font-semibold transition ${mode === "movies" ? "bg-cyan-400/18 text-cyan-100" : "text-slate-300 hover:bg-white/8"}`}
          onClick={() => onModeChange("movies")}
        >
          Movies
        </button>
        <button
          type="button"
          className={`rounded-xl px-4 py-2 text-sm font-semibold transition ${mode === "music" ? "bg-fuchsia-400/18 text-fuchsia-100" : "text-slate-300 hover:bg-white/8"}`}
          onClick={() => onModeChange("music")}
        >
          Music
        </button>
      </div>
      {onClose ? (
        <button
          type="button"
          className="inline-flex items-center gap-2 rounded-2xl border border-white/10 bg-black/35 px-4 py-2 text-sm font-semibold text-slate-200 shadow-[0_0_40px_rgba(0,0,0,0.35)] backdrop-blur-xl transition hover:bg-white/10"
          onClick={onClose}
        >
          <X className="h-4 w-4" /> Close
        </button>
      ) : null}
    </div>
  );

  const content = mode === "music"
    ? (albums.length > 0 ? (
      <MusicCarouselOverlay items={albums} topSlot={controls} onPlayAlbum={onPlayAlbum} />
    ) : (
      <EmptyOverlay variant="music" topSlot={controls} title="No local albums found" description="Scan or refresh your music library to populate the Servers local overlay." />
    ))
    : (movies.length > 0 ? (
      <MoviesCarouselOverlay items={movies} topSlot={controls} onPlayMovie={onPlayMovie} />
    ) : (
      <EmptyOverlay variant="movie" topSlot={controls} title="No local movies found" description="Scan or refresh your local movie library to populate the Servers local overlay." />
    ));

  return (
    <AnimatePresence>
      {open ? (
        <motion.div
          className="fixed inset-0 z-[90] bg-slate-950/74 p-4 backdrop-blur-md"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
          onClick={() => onClose?.()}
        >
          <motion.div
            className="relative h-full w-full overflow-hidden rounded-[36px] border border-white/10 bg-[#020409] shadow-[0_40px_120px_rgba(0,0,0,0.58)]"
            initial={{ opacity: 0, y: 18, scale: 0.985 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.985 }}
            transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
            onClick={(event) => event.stopPropagation()}
          >
            {content}
          </motion.div>
        </motion.div>
      ) : null}
    </AnimatePresence>
  );
}

function EmptyOverlay(props: { variant: "movie" | "music"; topSlot?: ReactNode; title: string; description: string }) {
  return (
    <div className="relative h-full w-full overflow-hidden bg-[#020409] text-white">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(34,211,238,0.16),transparent_24%),linear-gradient(160deg,#020409_0%,#08101d_48%,#03050b_100%)]" />
      <div className="relative z-10 flex h-full flex-col px-6 pb-5 pt-4 lg:px-8">
        <div className="flex h-[68px] items-center justify-between gap-4">
          <div>{props.topSlot}</div>
        </div>
        <div className="flex flex-1 items-center justify-center">
          <div className="max-w-2xl rounded-[32px] border border-white/10 bg-black/35 px-8 py-10 text-center backdrop-blur-xl">
            <div className={`mx-auto mb-4 inline-flex rounded-full border px-3 py-1 text-xs uppercase tracking-[0.2em] ${props.variant === "movie" ? "border-cyan-400/20 bg-cyan-500/10 text-cyan-100" : "border-fuchsia-400/20 bg-fuchsia-500/10 text-fuchsia-100"}`}>
              Local Library
            </div>
            <h2 className="text-3xl font-semibold text-white">{props.title}</h2>
            <p className="mt-3 text-sm leading-7 text-slate-300/84">{props.description}</p>
          </div>
        </div>
      </div>
    </div>
  );
}

function useOverlayCarousel(itemCount: number, autoplayInterval: number) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isAutoplay, setIsAutoplay] = useState(true);

  useEffect(() => {
    if (itemCount <= 0) return;
    if (currentIndex >= itemCount) setCurrentIndex(0);
  }, [currentIndex, itemCount]);

  useEffect(() => {
    if (!isAutoplay || itemCount <= 1) return;
    const timer = window.setInterval(() => {
      setCurrentIndex((prev) => (prev + 1) % itemCount);
    }, autoplayInterval);
    return () => window.clearInterval(timer);
  }, [autoplayInterval, isAutoplay, itemCount]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (itemCount <= 1) return;
      if (event.key === "ArrowLeft") {
        event.preventDefault();
        setCurrentIndex((prev) => (prev === 0 ? itemCount - 1 : prev - 1));
      }
      if (event.key === "ArrowRight") {
        event.preventDefault();
        setCurrentIndex((prev) => (prev + 1) % itemCount);
      }
      if (event.key === " ") {
        event.preventDefault();
        setIsAutoplay((prev) => !prev);
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [itemCount]);

  return {
    currentIndex,
    isAutoplay,
    setCurrentIndex,
    setIsAutoplay,
    goPrevious: () => setCurrentIndex((prev) => (prev === 0 ? itemCount - 1 : prev - 1)),
    goNext: () => setCurrentIndex((prev) => (prev + 1) % itemCount),
  };
}

function OverlayShell(props: {
  overlayKey: string;
  backgroundUrl: string;
  variant: "movie" | "music";
  isAutoplay: boolean;
  currentIndex: number;
  itemCount: number;
  onPrevious: () => void;
  onNext: () => void;
  onJumpTo: (index: number) => void;
  onToggleAutoplay: () => void;
  topSlot?: ReactNode;
  stage: ReactNode;
  dock: ReactNode;
}) {
  const panelTone = props.variant === "movie"
    ? "border-cyan-400/20 bg-cyan-500/10 text-cyan-100"
    : "border-fuchsia-400/20 bg-fuchsia-500/10 text-fuchsia-100";
  const dotTone = props.variant === "movie" ? "bg-cyan-400" : "bg-fuchsia-400";

  return (
    <div className="relative h-full w-full overflow-hidden bg-[#020409] text-white">
      <AnimatePresence mode="wait">
        <motion.div
          key={props.overlayKey}
          className="absolute inset-0"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.6 }}
        >
          <div
            className="absolute inset-0 bg-center bg-cover"
            style={{
              backgroundImage: `url(${props.backgroundUrl})`,
              filter: props.variant === "movie"
                ? "blur(128px) saturate(1.16) brightness(0.22)"
                : "blur(130px) saturate(1.25) brightness(0.24)",
              transform: "scale(1.18)",
            }}
          />
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(8,18,32,0.18)_0%,rgba(1,4,10,0.78)_56%,rgba(1,3,8,0.95)_100%)]" />
          <div className="absolute inset-0 bg-gradient-to-b from-black/35 via-transparent to-black/65" />
        </motion.div>
      </AnimatePresence>

      <div className="relative z-10 flex h-full flex-col px-6 pb-5 pt-4 lg:px-8">
        <div className="flex h-[68px] items-center justify-between gap-4">
          <div>{props.topSlot}</div>
          <div className="flex items-center gap-3 rounded-2xl border border-white/10 bg-black/35 px-3 py-2 shadow-[0_0_40px_rgba(0,0,0,0.35)] backdrop-blur-xl">
            <span className={`rounded-full border px-3 py-1 text-xs uppercase tracking-[0.2em] ${panelTone}`}>
              {props.variant === "movie" ? "Local Movies" : "Local Music"}
            </span>
            <button onClick={props.onToggleAutoplay} className={`flex items-center gap-2 rounded-xl border px-3.5 py-2 text-sm transition-all ${panelTone}`}>
              {props.isAutoplay ? <Pause className="h-4 w-4" /> : <Play className="h-4 w-4" />} Auto
            </button>
            <div className="rounded-xl border border-white/10 bg-white/5 px-3.5 py-2 text-sm text-slate-300">
              {String(props.currentIndex + 1).padStart(2, "0")} / {String(props.itemCount).padStart(2, "0")}
            </div>
          </div>
        </div>

        <div className="relative flex min-h-0 flex-1 items-center justify-center">
          <button onClick={props.onPrevious} className="absolute left-0 top-1/2 z-20 -translate-y-1/2 rounded-full border border-white/10 bg-black/35 p-4 text-white/90 backdrop-blur-xl transition hover:scale-105 hover:bg-white/10" aria-label="Previous">
            <ChevronLeft className="h-6 w-6" />
          </button>

          <div className="h-full w-full max-w-[1480px] px-16">{props.stage}</div>

          <button onClick={props.onNext} className="absolute right-0 top-1/2 z-20 -translate-y-1/2 rounded-full border border-white/10 bg-black/35 p-4 text-white/90 backdrop-blur-xl transition hover:scale-105 hover:bg-white/10" aria-label="Next">
            <ChevronRight className="h-6 w-6" />
          </button>

          <div className="absolute bottom-1 left-1/2 z-20 flex -translate-x-1/2 items-center gap-2">
            {Array.from({ length: props.itemCount }).map((_, index) => (
              <button
                key={index}
                onClick={() => props.onJumpTo(index)}
                className={`h-2 rounded-full transition-all ${index === props.currentIndex ? `w-8 ${dotTone}` : "w-2 bg-white/25 hover:bg-white/40"}`}
                aria-label={`Go to item ${index + 1}`}
              />
            ))}
          </div>
        </div>

        <div className="h-[270px] pt-4">
          <AnimatePresence mode="wait">{props.dock}</AnimatePresence>
        </div>
      </div>
    </div>
  );
}

function MediaCarouselStage<T extends { id: string }>(props: {
  items: T[];
  currentIndex: number;
  onNavigate: (index: number) => void;
  config: CarouselStageConfig;
  renderCard: (args: { item: T; isCurrent: boolean; position: CarouselPosition }) => ReactNode;
}) {
  const prevIndex = props.currentIndex === 0 ? props.items.length - 1 : props.currentIndex - 1;
  const nextIndex = props.currentIndex === props.items.length - 1 ? 0 : props.currentIndex + 1;
  const visibleItems = [
    { item: props.items[prevIndex], position: "prev" as const, index: prevIndex },
    { item: props.items[props.currentIndex], position: "current" as const, index: props.currentIndex },
    { item: props.items[nextIndex], position: "next" as const, index: nextIndex },
  ];
  const sideOffset = props.config.sideOffset ?? "34%";
  const sideScale = props.config.sideScale ?? 0.78;
  const sideRotateY = props.config.sideRotateY ?? 28;
  const sideOpacity = props.config.sideOpacity ?? 0.72;
  const perspective = props.config.perspective ?? 2400;

  const getMotion = (position: CarouselPosition) => {
    switch (position) {
      case "prev":
        return { x: `-${sideOffset}`, scale: sideScale, rotateY: sideRotateY, opacity: sideOpacity, zIndex: 1 };
      case "current":
        return { x: "0%", scale: 1, rotateY: 0, opacity: 1, zIndex: 3 };
      case "next":
        return { x: sideOffset, scale: sideScale, rotateY: -sideRotateY, opacity: sideOpacity, zIndex: 1 };
    }
  };

  return (
    <div className="relative flex h-full w-full items-center justify-center" style={{ perspective }}>
      {visibleItems.map(({ item, position, index }) => {
        const isCurrent = position === "current";
        return (
          <motion.button
            key={`${item.id}-${position}`}
            type="button"
            className="absolute m-0 border-0 bg-transparent p-0"
            initial={false}
            animate={getMotion(position)}
            transition={{ duration: 0.65, ease: [0.22, 1, 0.36, 1] }}
            style={{ transformStyle: "preserve-3d" }}
            onClick={() => !isCurrent && props.onNavigate(index)}
          >
            <motion.div
              className={`relative overflow-hidden rounded-[30px] border ${isCurrent ? "border-white/20" : "border-white/8"} bg-[linear-gradient(180deg,rgba(18,24,36,0.92),rgba(8,12,18,0.98))] shadow-[0_34px_110px_rgba(0,0,0,0.58)]`}
              style={{
                width: isCurrent ? props.config.currentWidth : props.config.sideWidth,
                height: isCurrent ? props.config.currentHeight : props.config.sideHeight,
              }}
              whileHover={!isCurrent ? { y: -4 } : { y: -8 }}
              transition={{ duration: 0.22 }}
            >
              <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,rgba(255,255,255,0.08),transparent_42%)]" />
              <div className="absolute inset-[1px] rounded-[29px] bg-[linear-gradient(180deg,rgba(255,255,255,0.04),rgba(255,255,255,0.0))]" />
              {props.renderCard({ item, isCurrent, position })}
              <div className="pointer-events-none absolute inset-x-5 bottom-4 h-12 rounded-full bg-black/25 blur-2xl" />
              {isCurrent ? (
                <>
                  <motion.div
                    className={`pointer-events-none absolute -inset-6 -z-10 rounded-[38px] blur-3xl ${props.config.glowClassName ?? "bg-cyan-400/16"}`}
                    animate={{ opacity: [0.24, 0.55, 0.24] }}
                    transition={{ duration: 2.8, repeat: Infinity, ease: "easeInOut" }}
                  />
                  <motion.div
                    className={`pointer-events-none absolute inset-y-0 -left-1/3 w-1/3 bg-gradient-to-r from-transparent via-white/12 to-transparent ${props.config.spotlightClassName ?? ""}`}
                    animate={{ x: ["0%", "420%"] }}
                    transition={{ duration: 2.4, repeat: Infinity, repeatDelay: 1.2, ease: "easeInOut" }}
                  />
                </>
              ) : null}
            </motion.div>
          </motion.button>
        );
      })}
    </div>
  );
}

function OverlayDockShell(props: { accent: "movie" | "music"; children: ReactNode }) {
  const accentBackdrop = props.accent === "movie"
    ? "bg-[radial-gradient(circle_at_top_right,rgba(34,211,238,0.14),transparent_28%),radial-gradient(circle_at_bottom_left,rgba(59,130,246,0.12),transparent_32%)]"
    : "bg-[radial-gradient(circle_at_top_right,rgba(217,70,239,0.16),transparent_28%),radial-gradient(circle_at_bottom_left,rgba(34,211,238,0.12),transparent_34%)]";

  return (
    <motion.div
      className="relative h-full w-full overflow-hidden rounded-[30px] border border-white/10 bg-[linear-gradient(135deg,rgba(5,10,18,0.9),rgba(11,19,31,0.86))] px-7 py-5 backdrop-blur-2xl"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 14 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className={`absolute inset-0 ${accentBackdrop}`} />
      <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(255,255,255,0.02),transparent_24%,rgba(255,255,255,0.02))]" />
      <div className="relative z-10 h-full">{props.children}</div>
    </motion.div>
  );
}

function OverlayBadge(props: { children: ReactNode; tone?: "neutral" | "movie" | "music" | "warning" }) {
  const tone = props.tone ?? "neutral";
  const toneClass = {
    neutral: "border-white/10 bg-white/5 text-slate-200",
    movie: "border-cyan-400/20 bg-cyan-400/10 text-cyan-200",
    music: "border-fuchsia-400/20 bg-fuchsia-400/10 text-fuchsia-200",
    warning: "border-amber-400/20 bg-amber-400/10 text-amber-200",
  }[tone];
  return <span className={`rounded-full border px-2.5 py-1 text-xs ${toneClass}`}>{props.children}</span>;
}

function OverlayProgressBar(props: { label: string; value: number; tone?: "movie" | "music" }) {
  const width = `${Math.max(0, Math.min(100, (props.value ?? 0) * 100))}%`;
  const gradientClass = props.tone === "music"
    ? "bg-gradient-to-r from-fuchsia-500 via-violet-500 to-cyan-400"
    : "bg-gradient-to-r from-cyan-400 to-blue-500";

  return (
    <div>
      <div className="mb-2 flex items-center justify-between text-xs text-slate-400">
        <span>{props.label}</span>
        <span>{Math.round((props.value ?? 0) * 100)}%</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-white/8">
        <div className={`h-full rounded-full ${gradientClass}`} style={{ width } as CSSProperties} />
      </div>
    </div>
  );
}

function MoviesCarouselOverlay(props: { items: LocalLibraryMovie[]; topSlot?: ReactNode; onPlayMovie?: (movieId: string) => void }) {
  const { currentIndex, isAutoplay, setCurrentIndex, setIsAutoplay, goPrevious, goNext } = useOverlayCarousel(props.items.length, 5200);
  const movie = props.items[currentIndex];

  return (
    <OverlayShell
      overlayKey={movie.id}
      backgroundUrl={movie.backdropUrl || movie.coverUrl}
      variant="movie"
      currentIndex={currentIndex}
      itemCount={props.items.length}
      isAutoplay={isAutoplay}
      onPrevious={goPrevious}
      onNext={goNext}
      onJumpTo={setCurrentIndex}
      onToggleAutoplay={() => setIsAutoplay((prev) => !prev)}
      topSlot={props.topSlot}
      stage={
        <MediaCarouselStage
          items={props.items}
          currentIndex={currentIndex}
          onNavigate={setCurrentIndex}
          config={{ currentWidth: 370, currentHeight: 555, sideWidth: 305, sideHeight: 458, glowClassName: "bg-cyan-400/16" }}
          renderCard={({ item }) => (
            <div className="relative flex h-full w-full items-center justify-center p-4">
              <img src={item.coverUrl} alt={item.title} className="h-full w-full rounded-[22px] object-contain" />
            </div>
          )}
        />
      }
      dock={<MovieDock movie={movie} onPlayMovie={props.onPlayMovie} />}
    />
  );
}

function MovieDock(props: { movie: LocalLibraryMovie; onPlayMovie?: (movieId: string) => void }) {
  const plot = props.movie.plot.trim();
  const detailCards = [
    props.movie.director ? { title: "Director", value: props.movie.director, icon: Clapperboard } : null,
    props.movie.cast.length > 0 ? { title: "Cast", value: props.movie.cast.join(", "), icon: Users, clamp: true } : null,
    props.movie.releaseDate ? { title: "Release", value: props.movie.releaseDate, icon: Calendar } : null,
    props.movie.rating > 0 ? { title: "Rating", value: props.movie.rating.toFixed(1), icon: Star, emphasize: true } : null,
  ].filter(Boolean) as Array<{ title: string; value: string; icon: typeof Clapperboard; clamp?: boolean; emphasize?: boolean }>;

  return (
    <OverlayDockShell accent="movie">
      <div className="grid h-full grid-cols-[1.1fr_1fr_0.9fr] gap-6">
        <div className="min-w-0">
          <div className="mb-3 flex flex-wrap items-center gap-2 text-xs text-slate-300/90">
            <OverlayBadge tone="movie">Local Movie</OverlayBadge>
            {props.movie.year > 0 ? <span>{props.movie.year}</span> : null}
            {props.movie.runtime > 0 ? <><span className="text-slate-500">•</span><span>{props.movie.runtime} min</span></> : null}
            {props.movie.certification ? <><span className="text-slate-500">•</span><span>{props.movie.certification}</span></> : null}
          </div>
          <h1 className="text-[2rem] font-semibold leading-none text-white">{props.movie.title}</h1>
          {props.movie.subtitle ? <p className="mt-2 text-sm text-slate-300/85">{props.movie.subtitle}</p> : null}
          <div className="mt-4 flex flex-wrap gap-2">
            {props.movie.genres.map((genre) => <OverlayBadge key={genre}>{genre}</OverlayBadge>)}
          </div>
          {props.movie.resolution.length > 0 || props.movie.audio.length > 0 ? (
            <div className="mt-4 flex flex-wrap gap-2">
              {props.movie.resolution.map((badge) => <OverlayBadge key={badge} tone="movie">{badge}</OverlayBadge>)}
              {props.movie.audio.map((badge) => <OverlayBadge key={badge} tone="warning">{badge}</OverlayBadge>)}
            </div>
          ) : null}
          {plot ? (
            <div className="mt-5 rounded-[22px] border border-white/8 bg-black/15 p-4">
              <p className="line-clamp-4 text-sm leading-6 text-slate-200/90">{plot}</p>
            </div>
          ) : null}
        </div>

        <div className="grid content-start gap-3 text-sm text-slate-300">
          {detailCards.length > 0 ? detailCards.map((card) => {
            const Icon = card.icon;
            return (
              <div key={card.title} className="rounded-[22px] border border-white/8 bg-white/5 p-4">
                <div className="mb-3 flex items-center gap-2 text-slate-400"><Icon className="h-4 w-4" /><span className="text-xs uppercase tracking-[0.18em]">{card.title}</span></div>
                <div className={card.emphasize ? "text-2xl font-semibold leading-none text-white" : `text-sm text-slate-200 ${card.clamp ? "line-clamp-2" : ""}`}>{card.value}</div>
              </div>
            );
          }) : (
            <div className="rounded-[22px] border border-white/8 bg-white/5 p-4 text-sm text-slate-300/84">
              Atlas will launch this title directly from your local library.
            </div>
          )}
        </div>

        <div className="flex min-w-0 flex-col justify-between">
          <div className="space-y-4 rounded-[24px] border border-white/8 bg-white/5 p-4">
            <div className="flex items-center gap-2 text-slate-400"><Shield className="h-4 w-4" /><span className="text-xs uppercase tracking-[0.18em]">Playback</span></div>
            <OverlayProgressBar label="Watch progress" value={props.movie.progress ?? 0} tone="movie" />
          </div>
          <div className="mt-5 flex items-center gap-2">
            <button
              className="flex flex-1 items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white shadow-[0_12px_30px_rgba(14,165,233,0.28)] transition hover:brightness-110"
              onClick={() => props.onPlayMovie?.(props.movie.id)}
            >
              <Play className="h-4 w-4 fill-white" /> Play
            </button>
          </div>
        </div>
      </div>
    </OverlayDockShell>
  );
}

function MusicCarouselOverlay(props: { items: LocalLibraryAlbum[]; topSlot?: ReactNode; onPlayAlbum?: (albumId: string) => void }) {
  const { currentIndex, isAutoplay, setCurrentIndex, setIsAutoplay, goPrevious, goNext } = useOverlayCarousel(props.items.length, 4200);
  const album = props.items[currentIndex];

  return (
    <OverlayShell
      overlayKey={album.id}
      backgroundUrl={album.coverUrl}
      variant="music"
      currentIndex={currentIndex}
      itemCount={props.items.length}
      isAutoplay={isAutoplay}
      onPrevious={goPrevious}
      onNext={goNext}
      onJumpTo={setCurrentIndex}
      onToggleAutoplay={() => setIsAutoplay((prev) => !prev)}
      topSlot={props.topSlot}
      stage={
        <MediaCarouselStage
          items={props.items}
          currentIndex={currentIndex}
          onNavigate={setCurrentIndex}
          config={{ currentWidth: 430, currentHeight: 430, sideWidth: 330, sideHeight: 330, glowClassName: "bg-fuchsia-400/20", spotlightClassName: "mix-blend-screen" }}
          renderCard={({ item, isCurrent }) => (
            <div className="relative flex h-full w-full items-center justify-center p-4">
              <img src={item.coverUrl} alt={item.title} className="h-full w-full rounded-[22px] object-contain" />
              {isCurrent ? (
                <div className="pointer-events-none absolute inset-x-8 bottom-8 flex items-end justify-center gap-1 opacity-70">
                  {[12, 18, 26, 16, 22, 14, 20].map((height, index) => (
                    <span key={`${item.id}-${index}`} className="block w-1.5 rounded-full bg-gradient-to-t from-fuchsia-500 via-violet-400 to-cyan-300" style={{ height }} />
                  ))}
                </div>
              ) : null}
            </div>
          )}
        />
      }
      dock={<MusicDock album={album} onPlayAlbum={props.onPlayAlbum} />}
    />
  );
}

function MusicDock(props: { album: LocalLibraryAlbum; onPlayAlbum?: (albumId: string) => void }) {
  const detailCards = [
    props.album.artist ? { title: "Artist", value: props.album.artist } : null,
    props.album.label ? { title: "Label", value: props.album.label } : null,
    props.album.year > 0 ? { title: "Year", value: String(props.album.year) } : null,
    props.album.duration ? { title: "Duration", value: props.album.duration } : null,
    props.album.popularity > 0 ? { title: "Popularity", value: `${props.album.popularity}%` } : null,
  ].filter(Boolean) as Array<{ title: string; value: string }>;

  return (
    <OverlayDockShell accent="music">
      <div className="grid h-full grid-cols-[1.05fr_1fr_0.95fr] gap-6">
        <div className="min-w-0">
          <div className="mb-3 flex flex-wrap items-center gap-2 text-xs text-slate-300/90">
            <OverlayBadge tone="music">Local Album</OverlayBadge>
            {props.album.year > 0 ? <span>{props.album.year}</span> : null}
            <span className="text-slate-500">•</span>
            <span>{props.album.trackCount} tracks</span>
            <span className="text-slate-500">•</span>
            <span>{props.album.duration}</span>
          </div>
          <h1 className="text-[2rem] font-semibold leading-none text-white">{props.album.title}</h1>
          <p className="mt-2 text-sm text-slate-300/85">{props.album.artist}</p>
          <div className="mt-4 flex flex-wrap gap-2">
            {props.album.genre.map((genre) => <OverlayBadge key={genre}>{genre}</OverlayBadge>)}
            {props.album.label ? <OverlayBadge tone="music">{props.album.label}</OverlayBadge> : null}
          </div>
          {props.album.tracks.length > 0 ? (
            <div className="mt-5 rounded-[22px] border border-white/8 bg-black/15 p-4">
              <div className="mb-3 flex items-center gap-2 text-slate-400"><Headphones className="h-4 w-4" /><span className="text-xs uppercase tracking-[0.18em]">Track Preview</span></div>
              <div className="space-y-2">
                {props.album.tracks.slice(0, 5).map((track, index) => (
                  <div key={track} className="flex items-center gap-3 rounded-xl border border-white/6 bg-white/5 px-3 py-2 text-sm text-slate-200/90">
                    <span className="w-4 text-slate-500">{index + 1}</span>
                    <span className="truncate">{track}</span>
                  </div>
                ))}
              </div>
            </div>
          ) : null}
        </div>

        <div className="grid content-start gap-3 text-sm text-slate-300">
          {detailCards.length > 0 ? (
            <div className="rounded-[22px] border border-white/8 bg-white/5 p-4">
              <div className="mb-3 flex items-center gap-2 text-slate-400"><Radio className="h-4 w-4" /><span className="text-xs uppercase tracking-[0.18em]">Details</span></div>
              <div className="grid grid-cols-2 gap-3">
                {detailCards.map((card) => (
                  <div key={card.title}><div className="text-xs text-slate-500">{card.title}</div><div>{card.value}</div></div>
                ))}
              </div>
            </div>
          ) : (
            <div className="rounded-[22px] border border-white/8 bg-white/5 p-4 text-sm text-slate-300/84">
              Atlas will play this album directly from your local collection.
            </div>
          )}
        </div>

        <div className="flex min-w-0 flex-col justify-between">
          <div className="space-y-4 rounded-[24px] border border-white/8 bg-white/5 p-4">
            <div className="flex items-center gap-2 text-slate-400"><Waves className="h-4 w-4" /><span className="text-xs uppercase tracking-[0.18em]">Playback</span></div>
            <OverlayProgressBar label="Playback progress" value={props.album.progress ?? 0} tone="music" />
            <div className="grid grid-cols-2 gap-2 text-xs text-slate-400">
              <div className="rounded-2xl border border-white/8 bg-black/15 px-3 py-2"><div className="mb-1 flex items-center gap-2"><Clock3 className="h-3.5 w-3.5" /> Runtime</div><div className="text-sm text-slate-200">{props.album.duration}</div></div>
              <div className="rounded-2xl border border-white/8 bg-black/15 px-3 py-2"><div className="mb-1 flex items-center gap-2"><Users2 className="h-3.5 w-3.5" /> Reach</div><div className="text-sm text-slate-200">{(props.album.popularity / 10).toFixed(1)}</div></div>
            </div>
          </div>
          <div className="mt-5 flex items-center gap-2">
            <button
              className="flex flex-1 items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-fuchsia-500 via-violet-500 to-cyan-500 px-4 py-3 text-sm font-medium text-white shadow-[0_12px_30px_rgba(217,70,239,0.28)] transition hover:brightness-110"
              onClick={() => props.onPlayAlbum?.(props.album.id)}
            >
              <Play className="h-4 w-4 fill-white" /> Play
            </button>
          </div>
        </div>
      </div>
    </OverlayDockShell>
  );
}