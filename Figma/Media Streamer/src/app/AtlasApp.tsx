import { useEffect, useMemo, useRef, useState } from "react";
import { LocalLibraryOverlay, type LocalLibraryAlbum, type LocalLibraryMode, type LocalLibraryMovie } from "./components/LocalLibraryOverlay";
import { MovieInfoPage } from "./components/MovieInfoPage";

function FilterSelect(props: {
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
  showCopyButton?: boolean;
}) {
  const { value, options, onChange, showCopyButton } = props;
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState<string | null>(null);
  const [revealedLink, setRevealedLink] = useState<string | null>(null);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const revealedInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (!open) return;

    const onPointerDown = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    window.addEventListener("mousedown", onPointerDown);
    return () => window.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  useEffect(() => {
    if (!revealedLink) return;

    const frame = window.requestAnimationFrame(() => {
      try {
        revealedInputRef.current?.focus();
        revealedInputRef.current?.select();
      } catch {
      }
    });

    return () => window.cancelAnimationFrame(frame);
  }, [revealedLink]);

  const selected = options.find((option) => option.value === value) ?? options[0];

  const copyToClipboard = async (text: string, event: { preventDefault: () => void; stopPropagation: () => void }) => {
    event.preventDefault();
    event.stopPropagation();
    setRevealedLink(text);

    let copiedSuccessfully = false;

    try {
      if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        copiedSuccessfully = true;
      }
    } catch {
    }

    if (!copiedSuccessfully) {
      try {
        const ta = document.createElement("textarea");
        ta.value = text;
        ta.setAttribute("readonly", "true");
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        document.execCommand("copy");
        document.body.removeChild(ta);
        copiedSuccessfully = true;
      } catch {
      }
    }

    if (hasBridge()) {
      post({ type: "servers.copyServerLink", payload: { url: text } });
    }

    if (copiedSuccessfully || hasBridge()) {
      setCopied(text);
      window.setTimeout(() => setCopied(null), 1500);
    }
  };

  return (
    <div ref={rootRef} className={`relative ${open ? "z-50" : "z-10"}`}>
      <button
        type="button"
        className="flex w-full items-center justify-between rounded-2xl border border-white/10 bg-slate-900/85 px-4 py-3 text-left text-sm text-white outline-none backdrop-blur-md"
        onClick={() => setOpen((current) => !current)}
      >
        <span>{selected?.label ?? value}</span>
        <span className={`text-slate-400 transition-transform ${open ? "rotate-180" : ""}`}>⌄</span>
      </button>

      {open ? (
        <div className="absolute left-0 right-0 z-50 mt-2 overflow-hidden rounded-2xl border border-white/10 bg-slate-950/96 shadow-[0_24px_60px_rgba(0,0,0,0.45)] backdrop-blur-xl">
          {options.map((option) => {
            const showCopyAction = showCopyButton && option.value !== "All servers" && option.value !== "Local Library";

            return (
              <div
                key={option.value}
                className={`px-4 py-3 text-sm transition ${option.value === value ? "bg-cyan-300/15 text-cyan-100" : "text-white hover:bg-white/8"}`}
              >
                <div className="flex items-center justify-between gap-3">
                  <button
                    type="button"
                    className="flex-1 text-left"
                    onClick={() => {
                      onChange(option.value);
                      setOpen(false);
                    }}
                  >
                    <div>{option.label}</div>
                    {showCopyAction ? (
                      <div className="mt-1 text-[10px] uppercase tracking-[0.22em] text-slate-500">Select this addon or reveal its link</div>
                    ) : null}
                  </button>
                  {showCopyAction ? (
                    <button
                      type="button"
                      className="rounded-lg border border-cyan-300/20 px-2.5 py-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-cyan-200 hover:bg-white/10 transition"
                      title={`Copy: ${option.value}`}
                      onMouseDown={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                      }}
                      onContextMenu={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                      }}
                      onClick={(e) => {
                        void copyToClipboard(option.value, e);
                      }}
                    >
                      {copied === option.value ? "Copied" : "Copy Link"}
                    </button>
                  ) : null}
                </div>
                {showCopyAction && revealedLink === option.value ? (
                  <div className="mt-3 rounded-xl border border-cyan-300/15 bg-slate-900/70 p-3">
                    <div className="mb-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-cyan-200/80">
                      Link selected below. Press Ctrl+C if copy did not happen automatically.
                    </div>
                    <input
                      ref={revealedInputRef}
                      type="text"
                      readOnly
                      value={option.value}
                      onFocus={(e) => e.currentTarget.select()}
                      onClick={(e) => e.currentTarget.select()}
                      className="w-full rounded-lg border border-white/10 bg-black/30 px-3 py-2 text-xs text-slate-100 outline-none"
                    />
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}

interface Actor {
  name: string;
  character: string;
  imageUrl: string;
}

interface Movie {
  id: string;
  metaId?: string;
  imdbId?: string;
  title: string;
  type: "movie" | "tv";
  posterUrl: string;
  backdropUrl: string;
  logoUrl?: string;
  trailerUrl: string;
  rating: number;
  aiRating: number;
  year: string;
  runtime: string;
  genres: string[];
  description: string;
  director: string;
  actors: Actor[];
  popularity: number;
  releaseDate?: string;
  progress?: number;
  ratingBadges: RatingBadge[];
  originalLanguage?: string;
}

interface RatingBadge {
  key: string;
  label: string;
  value: string;
  accentClassName: string;
}

interface StreamInfoCard {
  id: string;
  title: string;
  providerName: string;
  description: string;
  metadata: Record<string, string>;
}

type Page = "home" | "grid" | "info" | "customize";

type AtlasServersState = {
  type: "atlas:servers:state";
  viewMode?: "servers" | "local";
  mode?: "shelves" | "grid";
  selectedServer?: string;
  clientShelfOrderIds?: string[];
  serverOptions?: string[];
  selectedType?: string;
  isBusy?: boolean;
  canLoadMore?: boolean;
  statusText?: string;
  activeShelfKey?: string;
  serverShelves?: Array<{
    id: string;
    title: string;
    type?: "movie" | "series" | "tv";
    items?: Array<{
      id: string;
      title: string;
      type?: "movie" | "series" | "tv";
      logoUrl?: string;
      duration?: string;
      rating?: number;
      year?: number;
      genres?: string[];
      description?: string;
      thumbnail?: string;
      backdrop?: string;
      trailer?: string;
      aiScore?: number;
      popularity?: number;
      progress?: number;
    }>;
  }>;
    localSelection?: LocalLibraryMode;
    localMovies?: LocalLibraryMovie[];
    localAlbums?: LocalLibraryAlbum[];
    preferredContentLanguage?: string;
  catalogItems?: Array<{
    id: string;
    title: string;
    type?: "movie" | "series" | "tv";
    logoUrl?: string;
    duration?: string;
    rating?: number;
    year?: number;
    genres?: string[];
    description?: string;
    thumbnail?: string;
    backdrop?: string;
    trailer?: string;
    aiScore?: number;
    popularity?: number;
    progress?: number;
  }>;
  preview?: IncomingServerItem | null;
};

type IncomingServerItem = {
  id?: string;
  metaId?: string;
  imdbId?: string;
  originalLanguage?: string;
  title?: string;
  type?: "movie" | "series" | "tv";
  logoUrl?: string;
  duration?: string;
  runtimeMinutes?: number;
  rating?: number;
  year?: number;
  genres?: string[];
  description?: string;
  overview?: string;
  summary?: string;
  thumbnail?: string;
  coverUrl?: string;
  poster?: string;
  originalPoster?: string;
  backdrop?: string;
  backdropUrl?: string;
  trailer?: string;
  trailerUrl?: string;
  releaseDate?: string;
  aiScore?: number;
  popularity?: number;
  progress?: number;
  ratings?: Record<string, number>;
  rpdbRatings?: Record<string, string>;
  cast?: string[] | string;
  director?: string[] | string;
  directors?: string[] | string;
};

type ShelfMovie = Movie & {
  shelfId: string;
  bridgeShelfId: string;
};

function buildBridgeItemPayload(movie: ShelfMovie | null | undefined) {
  if (!movie) return {};

  const runtimeMinutes = (() => {
    const match = String(movie.runtime ?? "").match(/(\d+)/);
    return match ? Number(match[1]) : undefined;
  })();

  return {
    id: movie.id,
    metaId: movie.metaId || movie.id,
    imdbId: movie.imdbId || "",
    title: movie.title,
    type: movie.type,
    coverUrl: movie.posterUrl,
    backdropUrl: movie.backdropUrl,
    logoUrl: movie.logoUrl || "",
    trailerUrl: movie.trailerUrl || "",
    year: Number(movie.year || 0) || undefined,
    rating: movie.rating || 0,
    aiScore: movie.aiRating || 0,
    runtimeMinutes,
    genres: movie.genres,
    description: movie.description,
    summary: movie.description,
    director: movie.director || "",
    cast: movie.actors.map((actor) => actor.name).filter(Boolean),
    popularity: movie.popularity || 0,
    ratings: Object.fromEntries(movie.ratingBadges.map((badge) => [badge.key, Number.parseFloat(String(badge.value).replace(/[^\d.]/g, "")) || 0])),
  };
}

type Shelf = {
  id: string;
  title: string;
  viewAll: boolean;
  items: ShelfMovie[];
  persistOrderToBridge?: boolean;
};

type AtlasStreamsState = {
  type: "atlas:streams:state" | "servers.streams.state";
  mediaId?: string;
  isBusy?: boolean;
  statusText?: string;
  sources?: Array<{
    sourceId: string;
    name: string;
    providerName: string;
    urlOrPath?: string;
    quality?: string;
    sizeText?: string;
    seedersText?: string;
    isInfoOnly?: boolean;
    isPlayable?: boolean;
    metadata?: any;
  }>;
};

type AtlasAiResultMessage = {
  type: "servers.ai.result";
  payload?: {
    id?: string;
    content?: string;
    shelfTitle?: string;
    recommendations?: IncomingServerItem[];
  };
};

type AiDetailResult = {
  title: string;
  content: string;
};

type SeriesEpisode = {
  id: string;
  metaId: string;
  imdbId: string;
  title: string;
  season: number;
  episode: number;
  overview: string;
  thumbnail: string;
  released: string;
  runtime: string;
};

type SeriesSeason = {
  seasonNumber: number;
  label: string;
};

type SeriesState = {
  isOpen: boolean;
  isBusy: boolean;
  statusText: string;
  rootTitle: string;
  rootId: string;
  rootType: string;
  rootBackdrop: string;
  rootPoster: string;
  seasons: SeriesSeason[];
  selectedSeason: number;
  episodes: SeriesEpisode[];
};

type StreamSource = {
  _sourceId: string;
  sourceName: string;
  providerName: string;
  copyableLink?: string;
  quality: string;
  fileSize: string;
  audioLanguage: string;
  subtitles: string[];
  seederCount?: number;
  isInfoOnly?: boolean;
  isPlayable?: boolean;
  metadata?: Record<string, string>;
};

const RATING_BADGE_META: Record<string, { label: string; order: number; accentClassName: string; mode: "ten" | "percent" | "raw" }> = {
  imdb: { label: "IMDb", order: 1, accentClassName: "border-amber-300/80 bg-black/95 text-amber-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "ten" },
  metacritic: { label: "Metacritic", order: 2, accentClassName: "border-sky-300/80 bg-slate-950/95 text-sky-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  metacriticscore: { label: "Metacritic", order: 2, accentClassName: "border-sky-300/80 bg-slate-950/95 text-sky-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rotten: { label: "RT", order: 3, accentClassName: "border-rose-300/80 bg-slate-950/95 text-rose-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rottentomatoes: { label: "RT", order: 3, accentClassName: "border-rose-300/80 bg-slate-950/95 text-rose-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rottencritics: { label: "RT Critics", order: 3, accentClassName: "border-rose-300/80 bg-slate-950/95 text-rose-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rottentomatoescritics: { label: "RT Critics", order: 3, accentClassName: "border-rose-300/80 bg-slate-950/95 text-rose-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rottenaudience: { label: "RT Audience", order: 4, accentClassName: "border-pink-300/80 bg-slate-950/95 text-pink-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  rottentomatoesaudience: { label: "RT Audience", order: 4, accentClassName: "border-pink-300/80 bg-slate-950/95 text-pink-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  tomatoes: { label: "RT", order: 3, accentClassName: "border-rose-300/80 bg-slate-950/95 text-rose-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  tmdb: { label: "TMDb", order: 5, accentClassName: "border-cyan-300/80 bg-slate-950/95 text-cyan-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "ten" },
  trakt: { label: "Trakt", order: 6, accentClassName: "border-violet-300/80 bg-slate-950/95 text-violet-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "ten" },
  letterboxd: { label: "Letterboxd", order: 7, accentClassName: "border-emerald-300/80 bg-slate-950/95 text-emerald-100 shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "ten" },
  critic: { label: "Critic", order: 90, accentClassName: "border-white/45 bg-slate-950/95 text-white shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
  audience: { label: "Audience", order: 91, accentClassName: "border-white/45 bg-slate-950/95 text-white shadow-[0_10px_30px_rgba(0,0,0,0.45)]", mode: "percent" },
};

const WATCHLIST_STORAGE_KEY = "atlas.media.watchlist";
const AUTO_PLAY_STREAMS_STORAGE_KEY = "atlas.media.autoPlayStreams.v1";
const HIDDEN_SHELVES_STORAGE_KEY = "atlas.media.hiddenShelves.v2";
const DELETED_SHELVES_STORAGE_KEY = "atlas.media.deletedShelves.v2";
const GLOBAL_SEARCH_SHELF_ID = "atlas-global-search";
const INITIAL_CAROUSEL_COUNT = 30;
const MAX_CAROUSEL_COUNT = 120;
const VISIBLE_CAROUSEL_RADIUS = 4;

const RATING_KEY_ALIASES: Record<string, string> = {
  rottencritic: "rottencritics",
  rottentomatocritic: "rottentomatoescritics",
  rottentomatometer: "rottencritics",
  tomatometer: "rottencritics",
  rtcritic: "rottencritics",
  rtaudience: "rottenaudience",
  rottenuser: "rottenaudience",
  audiencepopcorn: "rottenaudience",
  imdbrating: "imdb",
  imdbscore: "imdb",
  metascore: "metacritic",
  metacriticrating: "metacritic",
  lb: "letterboxd",
};

const PREFERRED_RATING_KEYS = new Set([
  "imdb",
  "metacritic",
  "metacriticscore",
  "rotten",
  "rottencritics",
  "rottentomatoes",
  "rottentomatoescritics",
  "rottenaudience",
  "rottentomatoesaudience",
  "tmdb",
  "trakt",
  "letterboxd",
]);

const EXCLUDED_GENERAL_GENRES = ["anime", "animation", "animated", "cartoon"];
const KIDS_ANIMATION_GENRES = ["animation", "animated", "cartoon"];
const ANIME_GENRES = ["anime"];

const CACHE_KEY = "atlas.media.home.cache.v7";
const CACHE_TTL_MS = 6 * 60 * 60 * 1000;
const LEGACY_AI_SHELF_STORAGE_KEY = "atlas.media.aiShelf.v2";
const CUSTOM_SHELVES_STORAGE_KEY = "atlas.media.customShelves.v1";
const SHELF_ORDER_STORAGE_KEY = "atlas.media.shelfOrder.v2";
const CAROUSEL_SHELF_STORAGE_KEY = "atlas.media.carouselShelves.v2";
const LEGACY_AI_SHELF_MATCHERS = [
  "ai search movie",
  "ai search movies",
  "ai search series",
  "ai movies",
  "ai series",
];

function hasBridge(): boolean {
  return typeof window !== "undefined" && !!(window as any).chrome?.webview?.postMessage;
}

function post(msg: any) {
  try {
    (window as any).chrome?.webview?.postMessage(msg);
  } catch {
  }
}

function readWindowScrollTop(): number {
  try {
    return Math.max(window.scrollY || 0, document.documentElement?.scrollTop || 0, document.body?.scrollTop || 0);
  } catch {
    return 0;
  }
}

function readScrollTop(host: HTMLElement | null): number {
  return Math.max(host?.scrollTop ?? 0, readWindowScrollTop());
}

function writeScrollTop(host: HTMLElement | null, top: number) {
  const nextTop = Math.max(0, top);

  try {
    if (host) {
      host.scrollTo({ top: nextTop, behavior: "auto" });
    }
  } catch {
    try {
      if (host) host.scrollTop = nextTop;
    } catch {
    }
  }

  try {
    window.scrollTo({ top: nextTop, left: 0, behavior: "auto" });
  } catch {
    try {
      window.scrollTo(0, nextTop);
    } catch {
    }
  }

  try {
    document.documentElement.scrollTop = nextTop;
    document.body.scrollTop = nextTop;
  } catch {
  }
}

function normalizeMediaType(type?: string): "movie" | "tv" {
  return type === "series" || type === "tv" ? "tv" : "movie";
}

function resolveMediaType(type?: string | null, fallbackType?: string | null): "movie" | "tv" {
  const normalized = (type ?? "").trim().toLowerCase();
  if (normalized === "series" || normalized === "tv") return "tv";
  if (normalized === "movie") return "movie";

  const fallback = (fallbackType ?? "").trim().toLowerCase();
  if (fallback === "series" || fallback === "tv") return "tv";
  return "movie";
}

function mediaKey(item: { id?: string; title?: string; year?: string; type?: string }): string {
  const title = (item.title ?? "").trim().toLowerCase();
  const year = (item.year ?? "").trim();
  const type = (item.type ?? "").trim().toLowerCase();
  return `${item.id ?? ""}|${title}|${year}|${type}`;
}

function dedupeMovies(items: ShelfMovie[]): ShelfMovie[] {
  const seen = new Set<string>();
  return items.filter((item) => {
    const key = mediaKey(item);
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function normalizeIdentityToken(value?: string): string {
  return String(value ?? "").trim().toLowerCase();
}

function identityMatches(
  left: { id?: string; metaId?: string; imdbId?: string; title?: string; year?: string; type?: string },
  right: { id?: string; metaId?: string; imdbId?: string; title?: string; year?: string; type?: string },
): boolean {
  const leftTokens = [left.id, left.metaId, left.imdbId]
    .map(normalizeIdentityToken)
    .filter(Boolean);
  const rightTokens = [right.id, right.metaId, right.imdbId]
    .map(normalizeIdentityToken)
    .filter(Boolean);

  const leftTokenSet = new Set(leftTokens);

  if (rightTokens.some((token) => leftTokenSet.has(token))) {
    return true;
  }

  if (leftTokens.some((leftToken) => rightTokens.some((rightToken) => sharesRootIdentity(leftToken, rightToken)))) {
    return true;
  }

  const leftTitle = normalizeIdentityToken(left.title);
  const rightTitle = normalizeIdentityToken(right.title);
  const leftYear = normalizeIdentityToken(left.year);
  const rightYear = normalizeIdentityToken(right.year);
  const leftType = normalizeIdentityToken(left.type);
  const rightType = normalizeIdentityToken(right.type);

  return Boolean(leftTitle && rightTitle && leftTitle === rightTitle && leftYear === rightYear && leftType === rightType);
}

function sharesRootIdentity(leftToken: string, rightToken: string): boolean {
  if (!leftToken || !rightToken) return false;
  if (leftToken === rightToken) return true;

  const leftRoot = leftToken.split(":")[0] ?? "";
  const rightRoot = rightToken.split(":")[0] ?? "";

  return Boolean(leftRoot && rightRoot && leftRoot === rightRoot);
}

function normalizeRatingKey(rawKey: string): string {
  const normalized = String(rawKey ?? "").trim().toLowerCase().replace(/[^a-z0-9]+/g, "");
  return RATING_KEY_ALIASES[normalized] ?? normalized;
}

function formatNumericRating(mode: "ten" | "percent" | "raw", value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "";
  if (mode === "percent") return `${Math.round(value)}%`;
  if (mode === "ten") return value > 10 ? `${Math.round(value)}%` : `${value.toFixed(1)}`;
  return `${value}`;
}

function toPlainObject(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function looksLikeLocalMediaName(value: string, allowBareFileNames: boolean): boolean {
  const trimmed = value.trim();
  if (!trimmed) return false;

  const lower = trimmed.toLowerCase();
  if (lower === "downloads" || lower === "download") return true;
  if (/^[a-z]:\\/i.test(trimmed) || trimmed.startsWith("\\\\")) return true;
  if (trimmed.includes("\\downloads\\") || trimmed.includes("/downloads/")) return true;

  return false;
}

function isServerCandidateItem(item: IncomingServerItem | null | undefined): item is IncomingServerItem {
  if (!item || typeof item !== "object") return false;

  const id = String(item.id ?? item.metaId ?? item.imdbId ?? "").trim();
  const title = String(item.title ?? "").trim();

  if (!title) return false;
  if (looksLikeLocalMediaName(title, false)) return false;
  if (id && looksLikeLocalMediaName(id, true)) return false;

  return true;
}

function toStringList(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value
      .map((entry) => String(entry ?? "").trim())
      .filter(Boolean);
  }

  if (typeof value === "string") {
    return value
      .split(/[|,]/)
      .map((entry) => entry.trim())
      .filter(Boolean);
  }

  return [];
}

function sanitizeMetadataText(value: unknown): string {
  const text = String(value ?? "").trim();
  if (!text) return "";

  const normalized = text.toLowerCase();
  if (
    normalized === "loading metadata..." ||
    normalized === "summary is still loading from the metadata source." ||
    normalized === "no information available."
  ) {
    return "";
  }

  return text;
}

function formatServerOptionLabel(value: string): string {
  const text = String(value ?? "").trim();
  if (!text) return "All servers";
  if (text.toLowerCase() === "all servers") return "All servers";

  try {
    const parsed = new URL(text);
    return parsed.hostname.replace(/^www\./i, "");
  } catch {
    return text;
  }
}

function actorMonogram(name: string): string {
  const parts = String(name ?? "")
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2);

  if (parts.length === 0) return "AT";
  return parts.map((part) => part[0]?.toUpperCase() ?? "").join("") || "AT";
}

function extractPrimaryRatingValue(rawValue: string, mode: "ten" | "percent" | "raw"): string {
  const text = String(rawValue ?? "").trim();
  if (!text) return "";

  const numericMatch = text.match(/\d+(?:\.\d+)?/);
  if (!numericMatch) return text;

  const numeric = Number(numericMatch[0]);
  if (!Number.isFinite(numeric) || numeric <= 0) return "";
  return formatNumericRating(mode, numeric);
}

function buildRatingBadges(item: IncomingServerItem, fallbackRating: number): RatingBadge[] {
  const badges: RatingBadge[] = [];
  const seen = new Set<string>();
  const rpdbRatings = toPlainObject(item.rpdbRatings);
  const ratings = toPlainObject(item.ratings);

  const pushBadge = (key: string, value: string) => {
    const meta = RATING_BADGE_META[key];
    if (!meta || !value || seen.has(key) || !PREFERRED_RATING_KEYS.has(key)) return;
    seen.add(key);
    badges.push({ key, label: meta.label, value, accentClassName: meta.accentClassName });
  };

  const tmdbFromRpdb = extractPrimaryRatingValue(String(rpdbRatings.tmdb ?? ""), RATING_BADGE_META.tmdb.mode);
  const tmdbFromRatings = formatNumericRating(RATING_BADGE_META.tmdb.mode, Number(ratings.tmdb));
  const tmdbFallback = item.tmdbId && fallbackRating > 0
    ? formatNumericRating(RATING_BADGE_META.tmdb.mode, fallbackRating)
    : "";

  pushBadge("tmdb", tmdbFromRpdb || tmdbFromRatings || tmdbFallback);

  for (const [rawKey, rawValue] of Object.entries(rpdbRatings)) {
    const key = normalizeRatingKey(rawKey);
    const meta = RATING_BADGE_META[key];
    const value = extractPrimaryRatingValue(String(rawValue ?? ""), meta?.mode ?? "raw");
    if (!meta || key === "tmdb") continue;
    pushBadge(key, value);
  }

  for (const [rawKey, rawValue] of Object.entries(ratings)) {
    const key = normalizeRatingKey(rawKey);
    const meta = RATING_BADGE_META[key];
    if (!meta || key === "tmdb") continue;
    const value = formatNumericRating(meta.mode, Number(rawValue));
    pushBadge(key, value);
  }

  if (badges.length === 0 && fallbackRating > 0) {
    pushBadge("tmdb", formatNumericRating(RATING_BADGE_META.tmdb.mode, fallbackRating));
  }

  return badges
    .sort((left, right) => (RATING_BADGE_META[left.key]?.order ?? 999) - (RATING_BADGE_META[right.key]?.order ?? 999))
    .slice(0, 5);
}

function defaultFeaturedRatingBadges(movie: ShelfMovie | null): RatingBadge[] {
  if (!movie) return [];
  if (movie.ratingBadges.length > 0) return movie.ratingBadges;
  return [];
}

function getReleaseRank(item: ShelfMovie): number {
  const parsed = Date.parse(String(item.releaseDate ?? ""));
  if (Number.isFinite(parsed)) return parsed;
  const year = Number(item.year || 0);
  return Number.isFinite(year) ? year * 1000 : 0;
}

function compareByMetadataPriority(left: ShelfMovie, right: ShelfMovie): number {
  return getReleaseRank(right) - getReleaseRank(left)
    || right.popularity - left.popularity
    || right.ratingBadges.length - left.ratingBadges.length
    || right.rating - left.rating
    || left.title.localeCompare(right.title);
}

function toEmbedUrl(url: string): string {
  const raw = (url ?? "").trim();
  if (!raw) return "";

  try {
    const parsed = new URL(raw);
    if (parsed.hostname.includes("youtube.com")) {
      const videoId = parsed.searchParams.get("v") ?? "";
      return videoId ? `https://www.youtube.com/embed/${videoId}?autoplay=1&mute=1&controls=0&rel=0&playsinline=1` : "";
    }

    if (parsed.hostname.includes("youtu.be")) {
      const videoId = parsed.pathname.replace(/^\//, "").trim();
      return videoId ? `https://www.youtube.com/embed/${videoId}?autoplay=1&mute=1&controls=0&rel=0&playsinline=1` : "";
    }
  } catch {
  }

  return "";
}

function mapServerItem(
  shelfId: string,
  item: IncomingServerItem,
  fallbackType?: string,
): ShelfMovie {
  const title = String(item?.title ?? "").trim() || "Untitled";
  const poster = String(item?.thumbnail ?? item?.coverUrl ?? item?.poster ?? "").trim();
  const originalPoster = String(item?.originalPoster ?? "").trim() || poster;
  const backdrop = String(item?.backdrop ?? item?.backdropUrl ?? "").trim() || originalPoster;
  const year = typeof item?.year === "number" && item.year > 0 ? String(item.year) : String(item?.year ?? "").trim();
  const rating = typeof item?.rating === "number" ? item.rating : Number(item?.rating ?? 0) || 0;
  const ai = typeof item?.aiScore === "number" ? item.aiScore : Number(item?.aiScore ?? 0) || 0;
  const runtimeSource = item?.duration ?? (typeof item?.runtimeMinutes === "number" && item.runtimeMinutes > 0 ? `${item.runtimeMinutes} min` : "");
  const runtime = sanitizeMetadataText(runtimeSource);
  const genres = toStringList(item?.genres).slice(0, 5);
  const description = sanitizeMetadataText(item?.summary ?? item?.description ?? item?.overview ?? "");
  const popularity = typeof item?.popularity === "number" ? item.popularity : Number(item?.popularity ?? 0) || 0;
  const ratingBadges = buildRatingBadges(item, rating);
  const stableId = String(item?.metaId ?? item?.imdbId ?? item?.id ?? item?.title ?? "").trim();
  const castNames = toStringList(item?.cast).slice(0, 8);
  const directorNames = toStringList(item?.director ?? item?.directors).slice(0, 2);
  const originalLanguage = String(item?.originalLanguage ?? "").trim().toLowerCase();

  return {
    id: stableId,
    metaId: String(item?.metaId ?? "").trim(),
    imdbId: String(item?.imdbId ?? "").trim(),
    shelfId,
    bridgeShelfId: shelfId,
    title,
    type: resolveMediaType(item?.type, fallbackType),
    posterUrl: poster,
    backdropUrl: backdrop,
    logoUrl: String(item?.logoUrl ?? "").trim(),
    trailerUrl: String(item?.trailer ?? item?.trailerUrl ?? "").trim(),
    rating,
    aiRating: normalizeAtlasScore(rating, ai, popularity),
    year,
    releaseDate: String(item?.releaseDate ?? "").trim(),
    runtime,
    genres,
    description,
    director: directorNames[0] ?? "",
    actors: castNames.map((name) => ({ name, character: "", imageUrl: "" })),
    popularity,
    progress: typeof item?.progress === "number" ? item.progress : Number(item?.progress ?? 0) || 0,
    ratingBadges,
    originalLanguage: originalLanguage || undefined,
  };
}

function coerceServersState(raw: any): AtlasServersState | null {
  if (!raw || typeof raw !== "object") return null;

  const source = raw.payload && typeof raw.payload === "object" ? raw.payload : raw;
  const rawShelves = Array.isArray(source.serverShelves)
    ? source.serverShelves
    : Array.isArray(source.shelves)
      ? source.shelves
      : [];
  const rawCatalogItems = Array.isArray(source.catalogItems) ? source.catalogItems : [];

  return {
    type: "atlas:servers:state",
    viewMode: source.viewMode === "local" ? "local" : "servers",
    mode: source.mode === "grid" ? "grid" : "shelves",
    selectedServer: String(source.selectedServer ?? ""),
    clientShelfOrderIds: Array.isArray(source.clientShelfOrderIds)
      ? source.clientShelfOrderIds.map((value: unknown) => String(value ?? "").trim()).filter(Boolean)
      : [],
    serverOptions: Array.isArray(source.serverOptions)
      ? source.serverOptions.map((option: unknown) => String(option ?? "").trim()).filter(Boolean)
      : [],
    selectedType: String(source.selectedType ?? ""),
    isBusy: Boolean(source.isBusy),
    canLoadMore: Boolean(source.canLoadMore),
    statusText: String(source.statusText ?? ""),
    activeShelfKey: String(source.activeShelfKey ?? ""),
    serverShelves: rawShelves.map((shelf: any) => {
      const rawItems = Array.isArray(shelf?.items) ? shelf.items : [];
      const filteredItems = rawItems.filter((item: any) => isServerCandidateItem(item));
      return {
      id: String(shelf?.id ?? shelf?.key ?? ""),
      title: String(shelf?.title ?? ""),
      type: String(shelf?.type ?? "") as "movie" | "series" | "tv",
      items: filteredItems
        .map((item: any) => ({
          ...item,
          id: String(item?.id ?? item?.metaId ?? item?.title ?? ""),
          title: String(item?.title ?? ""),
          logoUrl: String(item?.logoUrl ?? ""),
        })),
    };
    }),
    catalogItems: rawCatalogItems
      .filter((item: any) => isServerCandidateItem(item))
      .map((item: any) => ({
        ...item,
        id: String(item?.id ?? item?.metaId ?? item?.title ?? ""),
        title: String(item?.title ?? ""),
        logoUrl: String(item?.logoUrl ?? ""),
      })),
    preview: source.preview && typeof source.preview === "object"
      ? {
          ...source.preview,
          id: String(source.preview?.metaId ?? source.preview?.id ?? source.preview?.imdbId ?? source.preview?.title ?? ""),
          title: String(source.preview?.title ?? ""),
          logoUrl: String(source.preview?.logoUrl ?? ""),
        }
      : null,
    localSelection: source.localSelection === "music" ? "music" : "movies",
    localMovies: Array.isArray(source.localMovies)
      ? source.localMovies
          .map((item: any) => ({
            id: String(item?.id ?? ""),
            type: "movie" as const,
            title: String(item?.title ?? "Untitled"),
            subtitle: String(item?.subtitle ?? ""),
            year: Number(item?.year ?? 0) || 0,
            certification: String(item?.certification ?? ""),
            runtime: Number(item?.runtime ?? 0) || 0,
            genres: Array.isArray(item?.genres) ? item.genres.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            rating: Number(item?.rating ?? 0) || 0,
            resolution: Array.isArray(item?.resolution) ? item.resolution.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            audio: Array.isArray(item?.audio) ? item.audio.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            director: String(item?.director ?? ""),
            cast: Array.isArray(item?.cast) ? item.cast.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            plot: String(item?.plot ?? ""),
            releaseDate: String(item?.releaseDate ?? ""),
            progress: typeof item?.progress === "number" ? item.progress : Number(item?.progress ?? 0) || 0,
            coverUrl: String(item?.coverUrl ?? ""),
            backdropUrl: String(item?.backdropUrl ?? ""),
          }))
          .filter((item: LocalLibraryMovie) => item.id.length > 0)
      : [],
    localAlbums: Array.isArray(source.localAlbums)
      ? source.localAlbums
          .map((item: any) => ({
            id: String(item?.id ?? ""),
            type: "album" as const,
            title: String(item?.title ?? "Untitled Album"),
            artist: String(item?.artist ?? "Unknown Artist"),
            year: Number(item?.year ?? 0) || 0,
            genre: Array.isArray(item?.genre) ? item.genre.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            trackCount: Number(item?.trackCount ?? 0) || 0,
            duration: String(item?.duration ?? "0:00"),
            label: String(item?.label ?? ""),
            popularity: Number(item?.popularity ?? 0) || 0,
            isFavorite: Boolean(item?.isFavorite),
            progress: typeof item?.progress === "number" ? item.progress : Number(item?.progress ?? 0) || 0,
            tracks: Array.isArray(item?.tracks) ? item.tracks.map((value: unknown) => String(value ?? "").trim()).filter(Boolean) : [],
            coverUrl: String(item?.coverUrl ?? ""),
          }))
          .filter((item: LocalLibraryAlbum) => item.id.length > 0)
      : [],
    preferredContentLanguage: String(source.preferredContentLanguage ?? "en").trim() || "en",
  };
}

function buildRecommendationShelf(shelves: Shelf[]): Shelf | null {
  const continueShelf = shelves.find((shelf) => shelf.title.toLowerCase() === "continue watching");
  const anchor = continueShelf?.items[0] ?? shelves.flatMap((shelf) => shelf.items)[0] ?? null;
  if (!anchor) return null;

  const anchorGenres = new Set(anchor.genres.map((genre) => genre.toLowerCase()));
  if (anchorGenres.size === 0) return null;

  const candidates = shelves
    .flatMap((shelf) => shelf.items)
    .filter((item) => mediaKey(item) !== mediaKey(anchor))
    .map((item) => ({
      item,
      overlap: item.genres.filter((genre) => anchorGenres.has(genre.toLowerCase())).length,
    }))
    .filter((entry) => entry.overlap > 0)
    .sort((left, right) =>
      right.overlap - left.overlap ||
      right.aiRating - left.aiRating ||
      right.rating - left.rating ||
      right.popularity - left.popularity,
    )
    .slice(0, 24)
    .map((entry) => ({ ...entry.item, shelfId: "because-you-watched", bridgeShelfId: entry.item.bridgeShelfId }));

  if (candidates.length === 0) return null;

  return {
    id: "because-you-watched",
    title: `Because You Watched ${anchor.title}`,
    viewAll: true,
    items: candidates,
  };
}

function buildAllMoviesShelf(shelves: Shelf[]): Shelf | null {
  const movies = shelves
    .flatMap((shelf) => shelf.items)
    .filter((item) => item.type === "movie")
    .sort((left, right) =>
      right.popularity - left.popularity ||
      right.aiRating - left.aiRating ||
      right.rating - left.rating ||
      left.title.localeCompare(right.title),
    );

  if (movies.length === 0) return null;

  const seen = new Set<string>();
  const items = movies
    .filter((item) => {
      const key = mediaKey(item);
      if (!key || seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, 40)
    .map((item) => ({ ...item, shelfId: "all-movies", bridgeShelfId: item.bridgeShelfId }));

  if (items.length === 0) return null;

  return {
    id: "all-movies",
    title: "All Movies",
    viewAll: true,
    items,
    persistOrderToBridge: false,
  };
}

function buildAllSeriesShelf(shelves: Shelf[]): Shelf | null {
  const series = shelves
    .flatMap((shelf) => shelf.items)
    .filter((item) => item.type === "tv")
    .sort((left, right) =>
      right.popularity - left.popularity ||
      right.aiRating - left.aiRating ||
      right.rating - left.rating ||
      left.title.localeCompare(right.title),
    );

  if (series.length === 0) return null;

  const seen = new Set<string>();
  const items = series
    .filter((item) => {
      const key = mediaKey(item);
      if (!key || seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, 40)
    .map((item) => ({ ...item, shelfId: "all-series", bridgeShelfId: item.bridgeShelfId }));

  if (items.length === 0) return null;

  return {
    id: "all-series",
    title: "All Series",
    viewAll: true,
    items,
    persistOrderToBridge: false,
  };
}

function dedupeShelves(shelves: Shelf[]): Shelf[] {
  const result: Shelf[] = [];

  for (const shelf of shelves) {
    const seenWithinShelf = new Set<string>();
    const uniqueItems = shelf.items.filter((item) => {
      const key = mediaKey(item);
      if (!key) return false;
      if (seenWithinShelf.has(key)) return false;
      seenWithinShelf.add(key);
      return true;
    });

    if (uniqueItems.length === 0) continue;
    result.push({ ...shelf, items: uniqueItems });
  }

  return result;
}

function isLegacyAiSearchShelf(shelf: { id?: string; title?: string } | null | undefined): boolean {
  const id = String(shelf?.id ?? "").trim().toLowerCase();
  const title = String(shelf?.title ?? "").trim().toLowerCase();
  const haystack = `${id} ${title}`;
  return LEGACY_AI_SHELF_MATCHERS.some((matcher) => haystack.includes(matcher));
}

// ── Language content filter ─────────────────────────────────────────
// Only block genres that are EXCLUSIVELY foreign-language content
const FOREIGN_ONLY_GENRES = new Set([
  "bollywood", "k-drama", "kdrama", "c-drama", "cdrama", "j-drama", "jdrama",
  "telenovela", "nollywood", "wuxia", "donghua",
  "animation (cn)", "animation (jp)",
]);
const FOREIGN_METADATA_KEYWORDS = /\b(hindi|tamil|telugu|malayalam|kannada|marathi|bengali|punjabi|bhojpuri|urdu|mandarin|cantonese|korean|japanese|chinese|thai|vietnamese|indonesian|arabic|persian|turkish|italian|italiano|french|francais|german|deutsch|spanish|espanol|portuguese|portugues|dublado|subtitulado|dubbed|subbed)\b/i;
// CJK / Devanagari / Arabic / Thai character range in titles
const CJK_TITLE_RANGE = /[\u3000-\u9fff\uac00-\ud7af\u0900-\u097f\u0600-\u06ff\u0e00-\u0e7f]/;

function isPreferredLanguage(item: ShelfMovie, lang: string): boolean {
  if (!lang || lang === "any") return true;
  const t = item.title ?? "";
  if (!t) return false;
  if (lang === "en") {
    // Use original_language from backend if available (most reliable)
    if (item.originalLanguage) {
      const orig = item.originalLanguage.toLowerCase().trim();
      // Only allow "en", "en-*" variants, or unknown (empty)
      if (orig && !orig.startsWith("en")) return false;
      return true;
    }
    // Strict fallback for items without language metadata
    const desc = item.description ?? "";
    const genres = (item.genres ?? []).map((g) => g.toLowerCase().trim());
    const metadata = `${t} ${desc} ${genres.join(" ")}`;
    if (FOREIGN_METADATA_KEYWORDS.test(metadata)) return false;
    if (genres.some((g) => FOREIGN_ONLY_GENRES.has(g))) return false;

    // If source does not provide language metadata, require IMDB identity to avoid low-quality foreign catalog noise.
    const hasImdbIdentity = /^tt\d+$/i.test(item.imdbId ?? "") || /^tt\d+$/i.test(item.metaId ?? "");
    if (!hasImdbIdentity) return false;

    // Heuristic checks for script/title quality
    // 1. Title must be predominantly Latin characters
    const titleNonSpace = t.replace(/\s/g, "");
    const latinChars = t.replace(/[^a-zA-Z]/g, "").length;
    if (titleNonSpace.length > 0 && latinChars / titleNonSpace.length < 0.55) return false;
    // 2. Reject titles containing CJK / Devanagari / Arabic / Thai script
    if (CJK_TITLE_RANGE.test(t)) return false;
    // Allow remaining titles.
    return true;
  }
  return true;
}

function normalizeShelves(serverState: AtlasServersState | null): Shelf[] {
  const preferredLang = (serverState?.preferredContentLanguage ?? "en").trim() || "en";
  const priority = [
    "continue watching",
    "still watching",
    "latest movies",
    "latest series",
    "trending this week",
    "marvel collection",
    "popular marvel",
    "popular dc",
    "dc collection",
    "all time greats",
    "best rated movies of all time",
  ];

  const mappedShelves = (serverState?.serverShelves ?? [])
    .map((shelf) => {
      const shelfId = (shelf.id ?? "").trim() || (shelf.title ?? "").trim().toLowerCase().replace(/[^a-z0-9]+/g, "-");
      const title = (shelf.title ?? "").trim() || "Shelf";
      const isKidsShelfTitle = /kids|family|animation|cartoon/i.test(title);
      const isContinueShelf = /continue watching|still watching/i.test(title);
      const rawItems = (shelf.items ?? []);
      const mapped = rawItems
          .map((item) => mapServerItem(shelfId, item, shelf.type))
          .filter((item) => (isKidsShelfTitle ? isKidsMovie(item) : !isExcludedFromGeneralShelves(item)))
          .filter((item) => isContinueShelf || isPreferredLanguage(item, preferredLang));
      return {
        id: shelfId,
        title,
        viewAll: true,
        items: mapped,
        persistOrderToBridge: true,
      } satisfies Shelf;
    })
    .filter((shelf) => !isLegacyAiSearchShelf(shelf))
    .filter((shelf) => shelf.items.length > 0)
    .sort((left, right) => {
      const li = priority.indexOf(left.title.toLowerCase());
      const ri = priority.indexOf(right.title.toLowerCase());
      const leftScore = li === -1 ? 999 : li;
      const rightScore = ri === -1 ? 999 : ri;
      return leftScore - rightScore || left.title.localeCompare(right.title);
    });

  if (mappedShelves.length === 0 && serverState?.catalogItems?.length) {
    return [
      {
        id: "catalog",
        title: "Catalog",
        viewAll: true,
        items: serverState.catalogItems.map((item) => mapServerItem("catalog", item)),
        persistOrderToBridge: true,
      },
    ];
  }

  const continueIndex = mappedShelves.findIndex((shelf) => shelf.title.toLowerCase() === "continue watching");
  const continueInsertIndex = continueIndex >= 0 ? continueIndex + 1 : 1;
  const withAllMovies = [...mappedShelves];
  const allMoviesShelf = buildAllMoviesShelf(mappedShelves);
  if (allMoviesShelf) {
    withAllMovies.splice(Math.min(continueInsertIndex, withAllMovies.length), 0, allMoviesShelf);
  }

  const allSeriesShelf = buildAllSeriesShelf(mappedShelves);
  if (allSeriesShelf) {
    const insertIndex = Math.min(continueInsertIndex + (allMoviesShelf ? 1 : 0), withAllMovies.length);
    withAllMovies.splice(insertIndex, 0, allSeriesShelf);
  }

  const kidsMoviesShelf = buildKidsShelf(mappedShelves, "movie", "kids-choice-movies", "Kids Choice Movies");
  if (kidsMoviesShelf) {
    withAllMovies.push(kidsMoviesShelf);
  }

  const kidsSeriesShelf = buildKidsShelf(mappedShelves, "tv", "kids-choice-series", "Kids Choice Series");
  if (kidsSeriesShelf) {
    withAllMovies.push(kidsSeriesShelf);
  }

  return dedupeShelves(withAllMovies);
}

function defaultGridTypeForShelf(shelf: Shelf): string {
  const movieCount = shelf.items.filter((item) => item.type === "movie").length;
  const tvCount = shelf.items.filter((item) => item.type === "tv").length;
  if (movieCount > tvCount && movieCount > 0) return "movie";
  if (tvCount > movieCount && tvCount > 0) return "tv";

  const label = `${shelf.id} ${shelf.title}`.toLowerCase();
  if (label.includes("series") || label.includes(" tv") || label.includes("latest series")) return "tv";
  if (label.includes("movie") || label.includes("film") || label.includes("cinema")) return "movie";
  return "all";
}

function normalizeGridTypeSelection(value?: string | null): "all" | "movie" | "tv" {
  const normalized = (value ?? "").trim().toLowerCase();
  if (normalized === "movie" || normalized === "movies") return "movie";
  if (normalized === "tv" || normalized === "series" || normalized === "show" || normalized === "shows") return "tv";
  return "all";
}

function toBridgeCatalogType(value: "all" | "movie" | "tv"): "movie" | "series" | "" {
  if (value === "tv") return "series";
  if (value === "movie") return "movie";
  return "";
}

function isLatestShelf(shelf: Shelf | null | undefined): boolean {
  const label = `${shelf?.id ?? ""} ${shelf?.title ?? ""}`.toLowerCase();
  return label.includes("latest") || label.includes("new release") || label.includes("recently added");
}

function isLatestMoviesShelf(shelf: Shelf | null | undefined): boolean {
  const label = `${shelf?.id ?? ""} ${shelf?.title ?? ""}`.toLowerCase();
  const isLatest = label.includes("latest") || label.includes("new release") || label.includes("recently added");
  const isMovieLike = label.includes("movie") || label.includes("cinema") || label.includes("film");
  return isLatest && isMovieLike;
}

function isLatestSeriesShelf(shelf: Shelf | null | undefined): boolean {
  const label = `${shelf?.id ?? ""} ${shelf?.title ?? ""}`.toLowerCase();
  const isLatest = label.includes("latest") || label.includes("new release") || label.includes("recently added");
  const isSeriesLike = label.includes("series") || label.includes(" tv") || label.includes("show");
  return isLatest && isSeriesLike;
}

function isAtlasLatestMoviesShelf(shelf: Shelf | null | undefined): boolean {
  const id = `${shelf?.id ?? ""}`.trim().toLowerCase();
  const title = `${shelf?.title ?? ""}`.trim().toLowerCase();
  return id === "latest-movies" || title === "latest movies";
}

function isAtlasLatestSeriesShelf(shelf: Shelf | null | undefined): boolean {
  const id = `${shelf?.id ?? ""}`.trim().toLowerCase();
  const title = `${shelf?.title ?? ""}`.trim().toLowerCase();
  return id === "latest-tv" || title === "latest tv" || title === "latest series";
}

function isPlaybackHistoryShelf(shelf: Shelf | null | undefined): boolean {
  const label = `${shelf?.id ?? ""} ${shelf?.title ?? ""}`.toLowerCase();
  return label.includes("continue watching") || label.includes("still watching");
}

function isCustomShelfId(shelfId: string): boolean {
  return shelfId.startsWith("ai-shelf-");
}

function isSyntheticShelfId(shelfId: string): boolean {
  return shelfId === "all-movies" || shelfId === "all-series" || shelfId === "because-you-watched";
}

function readCachedShelves(): AtlasServersState | null {
  try {
    const raw = window.localStorage.getItem(CACHE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { timestamp?: number; payload?: AtlasServersState };
    if (!parsed?.timestamp || !parsed?.payload) return null;
    if (Date.now() - parsed.timestamp > CACHE_TTL_MS) return null;
    return coerceServersState(parsed.payload);
  } catch {
    return null;
  }
}

function writeCachedShelves(payload: AtlasServersState) {
  try {
    window.localStorage.setItem(CACHE_KEY, JSON.stringify({ timestamp: Date.now(), payload }));
  } catch {
  }
}

function clearSavedAiShelf() {
  try {
    window.localStorage.removeItem(LEGACY_AI_SHELF_STORAGE_KEY);
  } catch {
  }
}

type CustomShelfEntry = { id: string; title: string; query: string; items: IncomingServerItem[] };

function readCustomShelves(): CustomShelfEntry[] {
  try {
    const raw = window.localStorage.getItem(CUSTOM_SHELVES_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function writeCustomShelves(shelves: CustomShelfEntry[]) {
  try {
    window.localStorage.setItem(CUSTOM_SHELVES_STORAGE_KEY, JSON.stringify(shelves));
  } catch {}
}

function readShelfOrder(): string[] {
  try {
    const raw = window.localStorage.getItem(SHELF_ORDER_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.map((value) => String(value)) : [];
  } catch {
    return [];
  }
}

function writeShelfOrder(shelfIds: string[]) {
  try {
    window.localStorage.setItem(SHELF_ORDER_STORAGE_KEY, JSON.stringify(shelfIds));
  } catch {
  }
}

function readCarouselShelfIds(): string[] {
  try {
    const raw = window.localStorage.getItem(CAROUSEL_SHELF_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.map((value) => String(value)) : [];
  } catch {
    return [];
  }
}

function writeCarouselShelfIds(shelfIds: string[]) {
  try {
    window.localStorage.setItem(CAROUSEL_SHELF_STORAGE_KEY, JSON.stringify(shelfIds));
  } catch {
  }
}

function readDeletedShelfIds(): string[] {
  try {
    const raw = window.localStorage.getItem(DELETED_SHELVES_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.map((value) => String(value)) : [];
  } catch {
    return [];
  }
}

function writeDeletedShelfIds(shelfIds: string[]) {
  try {
    window.localStorage.setItem(DELETED_SHELVES_STORAGE_KEY, JSON.stringify(shelfIds));
  } catch {
  }
}

function dedupeIds(ids: string[]): string[] {
  const seen = new Set<string>();
  return ids.filter((id) => {
    if (!id || seen.has(id)) return false;
    seen.add(id);
    return true;
  });
}

function sameStringArray(left: string[], right: string[]): boolean {
  if (left.length !== right.length) return false;
  return left.every((value, index) => value === right[index]);
}

function orderShelves(shelfList: Shelf[], shelfOrderIds: string[]): Shelf[] {
  const orderLookup = new Map(shelfOrderIds.map((id, index) => [id, index]));
  const originalLookup = new Map(shelfList.map((shelf, index) => [shelf.id, index]));
  return [...shelfList].sort((left, right) => {
    const leftIndex = orderLookup.get(left.id) ?? (originalLookup.get(left.id) ?? Number.MAX_SAFE_INTEGER);
    const rightIndex = orderLookup.get(right.id) ?? (originalLookup.get(right.id) ?? Number.MAX_SAFE_INTEGER);
    return leftIndex - rightIndex || left.title.localeCompare(right.title);
  });
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function normalizeAtlasScore(rating: number, aiScore: number, popularity: number): number {
  const hasRating = Number.isFinite(rating) && rating > 0;
  const hasAi = Number.isFinite(aiScore) && aiScore > 0;
  if (!hasRating && !hasAi) return 0;

  const safeRating = hasRating ? rating : 0;
  const safeAi = hasAi ? aiScore : 0;
  const weight = (hasRating ? 0.78 : 0) + (hasAi ? 0.18 : 0);
  const popularityAdjustment = clamp(popularity / 1000, 0, 0.35);
  const blended = ((safeRating * 0.78) + (safeAi * 0.18)) / Math.max(weight, 0.18) + popularityAdjustment * 0.25;
  return Number(clamp(blended, 0, 9.4).toFixed(1));
}

function includesGenre(genres: string[], keywords: string[]): boolean {
  const haystack = genres.map((genre) => genre.toLowerCase());
  return keywords.some((keyword) => haystack.some((genre) => genre.includes(keyword)));
}

function isKidsMovie(item: Movie): boolean {
  return includesGenre(item.genres, KIDS_ANIMATION_GENRES) && !includesGenre(item.genres, ANIME_GENRES);
}

function isExcludedFromGeneralShelves(item: Movie): boolean {
  return includesGenre(item.genres, EXCLUDED_GENERAL_GENRES);
}

function buildKidsShelf(shelves: Shelf[], type: "movie" | "tv", id: string, title: string): Shelf | null {
  const seen = new Set<string>();
  const items = shelves
    .flatMap((shelf) => shelf.items)
    .filter((item) => item.type === type)
    .filter((item) => isKidsMovie(item))
    .filter((item) => {
      const key = mediaKey(item);
      if (!key || seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .sort((left, right) =>
      Number(right.year || 0) - Number(left.year || 0) ||
      right.popularity - left.popularity ||
      right.rating - left.rating,
    )
    .slice(0, 60)
    .map((item) => ({ ...item, shelfId: id, bridgeShelfId: item.bridgeShelfId }));

  if (items.length === 0) return null;

  return {
    id,
    title,
    viewAll: true,
    items,
    persistOrderToBridge: false,
  };
}

function normalizeAddonBadgeKey(raw: string): string {
  return normalizeRatingKey(raw.replace(/[^a-z0-9]+/gi, ""));
}

function buildAddonRatingBadges(cards: StreamInfoCard[]): RatingBadge[] {
  const badges: RatingBadge[] = [];
  const seen = new Set<string>();

  for (const card of cards) {
    const text = `${card.title} ${card.description} ${Object.values(card.metadata).join(" ")}`.trim();
    if (!text) continue;

    for (const [key, meta] of Object.entries(RATING_BADGE_META)) {
      if (!PREFERRED_RATING_KEYS.has(key) || seen.has(key)) continue;
      const labelPattern = meta.label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      const keyPattern = key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      const match = text.match(new RegExp(`(?:${labelPattern}|${keyPattern})[^0-9]{0,8}(\\d+(?:\\.\\d+)?)`, "i"));
      if (!match) continue;

      const value = formatNumericRating(meta.mode, Number(match[1]));
      if (!value) continue;
      seen.add(key);
      badges.push({ key, label: meta.label, value, accentClassName: meta.accentClassName });
    }

    for (const [rawKey, rawValue] of Object.entries(card.metadata)) {
      const key = normalizeAddonBadgeKey(rawKey);
      const meta = RATING_BADGE_META[key];
      if (!meta || seen.has(key) || !PREFERRED_RATING_KEYS.has(key)) continue;
      const value = extractPrimaryRatingValue(String(rawValue ?? ""), meta.mode);
      if (!value) continue;
      seen.add(key);
      badges.push({ key, label: meta.label, value, accentClassName: meta.accentClassName });
    }
  }

  return badges
    .sort((left, right) => (RATING_BADGE_META[left.key]?.order ?? 999) - (RATING_BADGE_META[right.key]?.order ?? 999))
    .slice(0, 8);
}

function formatShelfJson(shelves: Shelf[]) {
  return {
    shelves: shelves.map((shelf) => ({
      title: shelf.title,
      view_all: shelf.viewAll,
      items: shelf.items.map((item) => ({
        title: item.title,
        type: item.type,
        tmdb_id: item.id,
        poster: item.posterUrl,
        year: item.year,
        rating: item.rating,
        genre: item.genres,
      })),
    })),
  };
}

function MediaPosterCard(props: {
  movie: ShelfMovie;
  preload?: boolean;
  inGrid?: boolean;
  inCarousel?: boolean;
  isFocused?: boolean;
  isSelected?: boolean;
  isWatchlisted: boolean;
  onSelect?: (movie: ShelfMovie) => void;
  onActivate?: (movie: ShelfMovie) => void;
  onOpen: (movie: ShelfMovie) => void;
  onPlay?: (movie: ShelfMovie) => void;
  onPlayTrailer?: (movie: ShelfMovie) => void;
  onWatchlistToggle: (movie: ShelfMovie) => void;
}) {
  const { movie, preload, inGrid, inCarousel, isFocused, isSelected, isWatchlisted, onSelect, onActivate, onOpen, onPlay, onPlayTrailer, onWatchlistToggle } = props;
  const [hovered, setHovered] = useState(false);
  const [imageSrc, setImageSrc] = useState(movie.posterUrl || movie.backdropUrl || "");
  const showProgress = typeof movie.progress === "number" && movie.progress > 0;

  const width = inGrid ? "min(100%, 172px)" : inCarousel ? "228px" : "188px";
  const height = inGrid ? "252px" : inCarousel ? "342px" : "284px";

  useEffect(() => {
    setImageSrc(movie.posterUrl || movie.backdropUrl || "");
  }, [movie.backdropUrl, movie.id, movie.posterUrl]);

  return (
    <button
      type="button"
      className={`group relative shrink-0 text-left ${inGrid ? "w-full justify-self-center" : "w-auto flex-none"}`}
      onClick={(event) => {
        if (inCarousel && onActivate) {
          onActivate(movie);
          return;
        }

        if (!isSelected && onSelect) {
          event.currentTarget.scrollIntoView({ block: "nearest", inline: "center", behavior: "smooth" });
          onSelect(movie);
          return;
        }

        onOpen(movie);
      }}
      onMouseEnter={() => {
        setHovered(true);
      }}
      onMouseLeave={() => {
        setHovered(false);
      }}
      style={{ width }}
    >
      <div
        className={`relative overflow-hidden rounded-[26px] transition-all duration-500 ease-out ${
          isFocused || isSelected
            ? "shadow-[0_30px_82px_rgba(0,0,0,0.46)]"
            : hovered
              ? "shadow-[0_24px_64px_rgba(0,0,0,0.4)]"
              : "shadow-[0_18px_44px_rgba(0,0,0,0.34)]"
        }`}
        style={{
          height,
          transform: inCarousel
            ? `${isFocused ? "rotateY(0deg) rotateX(0deg) scale(1.03)" : hovered ? "rotateY(-8deg) rotateX(1deg) scale(1.01)" : "rotateY(-4deg) rotateX(0deg) scale(1)"}`
            : hovered
              ? "translateY(-8px) scale(1.03)"
              : undefined,
          background: "linear-gradient(180deg, rgba(17,24,39,0.35) 0%, rgba(2,6,23,0.9) 100%)",
          transformStyle: inCarousel ? "preserve-3d" : undefined,
        }}
      >
        <div
          className={`pointer-events-none absolute inset-[-18px] rounded-[34px] transition-all duration-500 ${
            isFocused
              ? "bg-white/10 opacity-70 blur-2xl"
              : hovered || inGrid
                ? "bg-white/6 opacity-80 blur-2xl"
                : "bg-white/0 opacity-0 blur-2xl"
          }`}
        />
        {imageSrc ? (
          <>
            {inCarousel ? (
              <img
                src={imageSrc}
                alt=""
                loading={preload ? "eager" : "lazy"}
                fetchPriority={preload ? "high" : "auto"}
                className="absolute inset-0 h-full w-full scale-[1.06] object-cover object-center opacity-40 blur-xl"
              />
            ) : null}
            <img
              src={imageSrc}
              alt={movie.title}
              loading={preload ? "eager" : "lazy"}
              fetchPriority={preload ? "high" : "auto"}
              className={`absolute inset-0 h-full w-full transition-transform duration-500 ${
                inCarousel
                  ? "object-contain object-center"
                  : "object-cover object-top group-hover:scale-[1.02]"
              }`}
              onError={() => {
                const fallback = (movie.backdropUrl || "").trim();
                if (imageSrc && fallback && imageSrc !== fallback) {
                  setImageSrc(fallback);
                  return;
                }
                setImageSrc("");
              }}
            />
          </>
        ) : (
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgba(56,189,248,0.16),transparent_36%),linear-gradient(180deg,rgba(15,23,42,0.94)_0%,rgba(2,6,23,1)_100%)]" />
        )}

        <div className="absolute inset-0 bg-gradient-to-t from-slate-950/72 via-slate-950/12 to-transparent" />
        <div className="absolute inset-x-0 top-0 h-20 bg-gradient-to-b from-black/28 to-transparent" />

        {movie.ratingBadges.length > 0 && !inCarousel ? (
          <div className="absolute inset-x-0 bottom-0 border-t border-white/10 bg-[linear-gradient(180deg,rgba(2,6,23,0.94),rgba(2,6,23,0.985))] px-3 py-2 backdrop-blur-xl">
            <div className="flex flex-wrap items-center justify-center gap-2">
            {movie.ratingBadges.slice(0, 3).map((badge) => (
              <span
                key={`${movie.id}-${badge.key}`}
                className={`rounded-full border px-2.5 py-1 text-[10px] font-semibold shadow-[0_6px_16px_rgba(0,0,0,0.22)] ${badge.accentClassName}`}
              >
                {badge.label} {badge.value}
              </span>
            ))}
            </div>
          </div>
        ) : null}

        {showProgress ? (
          <div className={`absolute inset-x-3 ${movie.ratingBadges.length > 0 ? "bottom-14" : "bottom-3"} h-1.5 overflow-hidden rounded-full bg-white/10`}>
            <div
              className="h-full rounded-full bg-gradient-to-r from-cyan-400 via-sky-500 to-blue-500"
              style={{ width: `${clamp(movie.progress, 0, 100)}%` }}
            />
          </div>
        ) : null}

        {!inCarousel ? (
          <>
            <div className={`absolute inset-0 bg-gradient-to-t from-slate-950/92 via-slate-950/36 to-transparent transition-opacity duration-300 ${hovered || isFocused || isSelected ? "opacity-100" : "opacity-0"}`} />

            <div className={`absolute inset-x-3 bottom-4 transition-all duration-300 ${hovered || isFocused || isSelected ? "translate-y-0 opacity-100" : "translate-y-3 opacity-0"}`}>
              <div className="mb-3 line-clamp-3 text-xs leading-5 text-slate-200/90">{movie.description || movie.genres.join(" • ")}</div>
              <div className="flex flex-wrap gap-2">
                {onPlay ? (
                  <button
                    type="button"
                    className="flex items-center gap-1 rounded-full bg-gradient-to-r from-purple-600 to-blue-600 px-3 py-2 text-[11px] font-bold text-white shadow-[0_8px_20px_rgba(139,92,246,0.35)]"
                    onClick={(event) => {
                      event.stopPropagation();
                      onPlay(movie);
                    }}
                  >
                    <svg width="10" height="10" viewBox="0 0 16 16" fill="white"><path d="M3 2L13 8L3 14V2Z"/></svg>
                    Play
                  </button>
                ) : null}
                {onPlayTrailer ? (
                  <button
                    type="button"
                    className="rounded-full border border-white/15 bg-white/10 px-3 py-2 text-[11px] font-semibold text-white backdrop-blur-md"
                    onClick={(event) => {
                      event.stopPropagation();
                      onPlayTrailer(movie);
                    }}
                  >
                    Trailer
                  </button>
                ) : null}
                {!onPlay ? (
                  <button
                    type="button"
                    className="rounded-full bg-cyan-400 px-3 py-2 text-[11px] font-semibold text-slate-950 shadow-[0_8px_20px_rgba(34,211,238,0.35)]"
                    onClick={(event) => {
                      event.stopPropagation();
                      onOpen(movie);
                    }}
                  >
                    Open
                  </button>
                ) : null}
                <button
                  type="button"
                  className="rounded-full border border-white/15 bg-white/10 px-3 py-2 text-[11px] font-semibold text-white backdrop-blur-md"
                  onClick={(event) => {
                    event.stopPropagation();
                    onWatchlistToggle(movie);
                  }}
                >
                  {isWatchlisted ? "Saved" : "Watchlist"}
                </button>
              </div>
            </div>
          </>
        ) : null}
      </div>

      {!inCarousel ? (
        <div className="px-2 pb-1 pt-4">
          <div className="line-clamp-2 text-[15px] font-semibold leading-6 text-white">{movie.title}</div>
          <div className="mt-1 flex items-center gap-2 text-[11px] text-slate-400">
            <span>{movie.year || "TBA"}</span>
            <span>•</span>
            <span>{movie.runtime || movie.genres[0] || (movie.type === "tv" ? "Series" : "Movie")}</span>
          </div>
        </div>
      ) : null}

      {!inGrid && !inCarousel ? (
        <div
          className="pointer-events-none absolute left-4 right-4 top-full h-12 rounded-[50%] bg-cyan-300/10 blur-xl"
          style={{ transform: "translateY(-12px) scaleX(0.9)" }}
        />
      ) : null}
    </button>
  );
}

export default function AtlasApp() {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const carouselStageRef = useRef<HTMLDivElement | null>(null);
  const gridLoadTriggerRef = useRef<HTMLDivElement | null>(null);
  const lastBridgeLoadMoreRequestAtRef = useRef(0);
  const lastBridgeCatalogCountRef = useRef(0);
  const lastBridgeBusyRef = useRef(false);
  const carouselVelocityRef = useRef(0);
  const carouselAutoplaySpeedRef = useRef(0.03);
  const watchlistHydratedRef = useRef(false);
  const hiddenShelvesHydratedRef = useRef(false);
  const deletedShelvesHydratedRef = useRef(false);
  const shelfOrderHydratedRef = useRef(false);
  const carouselShelvesHydratedRef = useRef(false);
  const customShelvesHydratedRef = useRef(false);
  const pageScrollPositionsRef = useRef<Record<Page, number>>({ home: 0, grid: 0, info: 0, customize: 0 });
  const pendingScrollRestoreRef = useRef<{ page: Page; top: number } | null>(null);
  const [currentPage, setCurrentPage] = useState<Page>("home");
  const [returnPage, setReturnPage] = useState<Exclude<Page, "info">>("home");
  const [selectedMovie, setSelectedMovie] = useState<ShelfMovie | null>(null);
  const selectedMovieRef = useRef(selectedMovie);
  selectedMovieRef.current = selectedMovie;
  const [selectedShelfId, setSelectedShelfId] = useState<string>("");
  const [serversState, setServersState] = useState<AtlasServersState | null>(null);
  const [streamsState, setStreamsState] = useState<AtlasStreamsState | null>(null);
  const [seriesState, setSeriesState] = useState<SeriesState | null>(null);
  const [watchlist, setWatchlist] = useState<string[]>([]);
  const [hiddenShelfIds, setHiddenShelfIds] = useState<string[]>([]);
  const [deletedShelfIds, setDeletedShelfIds] = useState<string[]>([]);
  const [shelfOrderIds, setShelfOrderIds] = useState<string[]>([]);
  const [carouselShelfIds, setCarouselShelfIds] = useState<string[]>([]);
  const [customShelves, setCustomShelves] = useState<CustomShelfEntry[]>([]);
  const [customShelfQuery, setCustomShelfQuery] = useState("");
  const [customShelfLoading, setCustomShelfLoading] = useState(false);
  const [aiDetailResult, setAiDetailResult] = useState<AiDetailResult | null>(null);
  const [heroAiText, setHeroAiText] = useState("");
  const [armedMovieKey, setArmedMovieKey] = useState("");
  const [globalSearchQuery, setGlobalSearchQuery] = useState("");
  const [gridSearch, setGridSearch] = useState("");
  const [gridGenre, setGridGenre] = useState("All Genres");
  const [gridType, setGridType] = useState("all");
  const [minimumRating, setMinimumRating] = useState(0);
  const [visibleCount, setVisibleCount] = useState(60);
  const [stalledBridgeLoadRequests, setStalledBridgeLoadRequests] = useState(0);
  const [visibleCarouselCount, setVisibleCarouselCount] = useState(INITIAL_CAROUSEL_COUNT);
  const [cinemaMode, setCinemaMode] = useState(false);
  const [autoplayCarousel, setAutoplayCarousel] = useState(false);
  const [carouselAutoplaySpeed, setCarouselAutoplaySpeed] = useState(0.03);
  const [autoPlaySources, setAutoPlaySources] = useState(false);
  const [carouselType, setCarouselType] = useState<"all" | "movie" | "tv">("all");
  const [localLibraryMode, setLocalLibraryMode] = useState<LocalLibraryMode>("movies");
  const [localLibraryOverlayOpen, setLocalLibraryOverlayOpen] = useState(false);
  const [carouselAngle, setCarouselAngle] = useState(0);
  const [carouselVelocity, setCarouselVelocity] = useState(0);
  const [carouselSeedItems, setCarouselSeedItems] = useState<ShelfMovie[]>([]);
  const [featuredIndex, setFeaturedIndex] = useState(0);
  const [contentWidth, setContentWidth] = useState(0);
  const autoPlayedSourceKeyRef = useRef("");
  const aiTypingTimerRef = useRef<number | null>(null);
  const aiTypingTokenRef = useRef(0);

  function stopAiTyping() {
    aiTypingTokenRef.current += 1;
    if (aiTypingTimerRef.current != null) {
      window.clearTimeout(aiTypingTimerRef.current);
      aiTypingTimerRef.current = null;
    }
  }

  function animateAiTyping(fullText: string, onUpdate: (value: string) => void) {
    stopAiTyping();
    const text = String(fullText ?? "").trim();
    if (!text) {
      onUpdate("");
      return;
    }

    const token = aiTypingTokenRef.current;
    let index = 0;
    const charsPerTick = Math.max(3, Math.min(12, Math.ceil(text.length / 70)));

    const tick = () => {
      if (token !== aiTypingTokenRef.current) return;
      index = Math.min(text.length, index + charsPerTick);
      onUpdate(text.slice(0, index));
      if (index >= text.length) {
        aiTypingTimerRef.current = null;
        return;
      }
      aiTypingTimerRef.current = window.setTimeout(tick, 24);
    };

    onUpdate("");
    tick();
  }

  useEffect(() => () => stopAiTyping(), []);

  useEffect(() => {
    try {
      const cachedWatchlist = window.localStorage.getItem(WATCHLIST_STORAGE_KEY);
      if (cachedWatchlist) {
        const parsed = JSON.parse(cachedWatchlist);
        if (Array.isArray(parsed)) setWatchlist(parsed.map((value) => String(value)));
      }
    } catch {
    } finally {
      watchlistHydratedRef.current = true;
    }
  }, []);

  useEffect(() => {
    try {
      const cached = window.localStorage.getItem(AUTO_PLAY_STREAMS_STORAGE_KEY);
      if (cached == null) return;
      setAutoPlaySources(cached === "1");
    } catch {}
  }, []);

  useEffect(() => {
    try {
      window.localStorage.setItem(AUTO_PLAY_STREAMS_STORAGE_KEY, autoPlaySources ? "1" : "0");
    } catch {}
  }, [autoPlaySources]);

  useEffect(() => {
    if (!watchlistHydratedRef.current) return;
    try {
      window.localStorage.setItem(WATCHLIST_STORAGE_KEY, JSON.stringify(watchlist));
    } catch {
    }
  }, [watchlist]);

  useEffect(() => {
    try {
      const cachedHiddenShelves = window.localStorage.getItem(HIDDEN_SHELVES_STORAGE_KEY);
      if (cachedHiddenShelves) {
        const parsed = JSON.parse(cachedHiddenShelves);
        if (Array.isArray(parsed)) setHiddenShelfIds(parsed.map((value) => String(value)));
      }
    } catch {
    } finally {
      hiddenShelvesHydratedRef.current = true;
    }
  }, []);

  useEffect(() => {
    if (!hiddenShelvesHydratedRef.current) return;
    try {
      window.localStorage.setItem(HIDDEN_SHELVES_STORAGE_KEY, JSON.stringify(hiddenShelfIds));
    } catch {
    }
  }, [hiddenShelfIds]);

  useEffect(() => {
    try {
      setDeletedShelfIds(readDeletedShelfIds());
    } finally {
      deletedShelvesHydratedRef.current = true;
    }
  }, []);

  useEffect(() => {
    if (!deletedShelvesHydratedRef.current) return;
    writeDeletedShelfIds(deletedShelfIds);
  }, [deletedShelfIds]);

  useEffect(() => {
    setShelfOrderIds(readShelfOrder());
    shelfOrderHydratedRef.current = true;
  }, []);

  useEffect(() => {
    if (!shelfOrderHydratedRef.current) return;
    // Never overwrite the saved order with an empty array (can happen before
    // serversState arrives and the cleanup effect hasn't run yet).
    if (shelfOrderIds.length === 0) return;
    writeShelfOrder(shelfOrderIds);
  }, [shelfOrderIds]);

  const hostShelfOrderAppliedRef = useRef(false);

  useEffect(() => {
    // Only apply the host shelf order ONCE on initial load.
    // Subsequent serversState updates must not override user reordering.
    if (hostShelfOrderAppliedRef.current) return;
    const hostOrder = serversState?.clientShelfOrderIds ?? [];
    if (hostOrder.length === 0) return;

    hostShelfOrderAppliedRef.current = true;
    setShelfOrderIds((current) => {
      const deduped = dedupeIds(hostOrder);
      return sameStringArray(current, deduped) ? current : deduped;
    });
  }, [serversState?.clientShelfOrderIds]);

  useEffect(() => {
    setCarouselShelfIds(readCarouselShelfIds());
    carouselShelvesHydratedRef.current = true;
  }, []);

  useEffect(() => {
    if (!carouselShelvesHydratedRef.current) return;
    writeCarouselShelfIds(carouselShelfIds);
  }, [carouselShelfIds]);

  useEffect(() => {
    setCustomShelves(readCustomShelves());
    customShelvesHydratedRef.current = true;
  }, []);

  useEffect(() => {
    if (!customShelvesHydratedRef.current) return;
    writeCustomShelves(customShelves);
  }, [customShelves]);

  useEffect(() => {
    const root = containerRef.current;
    if (!root || typeof ResizeObserver === "undefined") return;

    const update = () => setContentWidth(root.clientWidth);
    update();

    const observer = new ResizeObserver(() => update());
    observer.observe(root);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const pending = pendingScrollRestoreRef.current;
    const targetTop = pending?.page === currentPage
      ? pending.top
      : currentPage === "home"
        ? pageScrollPositionsRef.current.home
        : 0;

    const frame = window.requestAnimationFrame(() => {
      const host = containerRef.current;
      writeScrollTop(host, targetTop);
      if (pending?.page === currentPage) {
        pendingScrollRestoreRef.current = null;
      }
    });

    return () => window.cancelAnimationFrame(frame);
  }, [currentPage]);

  useEffect(() => {
    const cached = readCachedShelves();
    if (cached) setServersState((current) => current ?? cached);

    // Clean up legacy AI shelf key (replaced by custom shelves v1).
    clearSavedAiShelf();

    if (!hasBridge()) return;

    const handler = (ev: any) => {
      const data = ev?.data;
      const type = data?.type;
      if (!type) return;
      if (type === "atlas:servers:state" || type === "servers.state") {
        const next = coerceServersState(data);
        if (!next) return;
        setServersState(next);
        writeCachedShelves(next);
      }
      if (type === "atlas:streams:state" || type === "servers.streams.state") {
        setStreamsState(data as AtlasStreamsState);
      }
      if (type === "servers.series.state") {
        setSeriesState(data as SeriesState);
      }
      if (type === "servers.ai.result") {
        const result = data as AtlasAiResultMessage;
        const requestId = String(result.payload?.id ?? "").trim();
        const content = String(result.payload?.content ?? "").trim();
        const recs = result.payload?.recommendations ?? [];
        const shelfTitle = String(result.payload?.shelfTitle ?? "").trim();

        if (requestId.startsWith("ai-shelf-")) {
          setCustomShelfLoading(false);
          if (recs.length > 0) {
            const shelfId = requestId;
            const entry: CustomShelfEntry = {
              id: shelfId,
              title: shelfTitle || requestId.replace("ai-shelf-", "").replace(/-/g, " "),
              query: requestId.replace("ai-shelf-", "").replace(/-\d+$/, "").replace(/-/g, " "),
              items: recs as IncomingServerItem[],
            };
            setCustomShelves((prev) => {
              const updated = prev.filter((s) => s.id !== shelfId);
              updated.push(entry);
              return updated;
            });
          }
        } else {
          const answerContent = content || "Atlas could not build a useful answer for this title yet.";
          const resultTitle = selectedMovieRef.current?.title ?? "Atlas AI";
          const mirrorToHero = requestId.includes("-overview") || requestId.includes("-next-watch");

          setAiDetailResult({
            title: resultTitle,
            content: "",
          });
          if (mirrorToHero) setHeroAiText("");

          animateAiTyping(answerContent, (typedText) => {
            setAiDetailResult({
              title: resultTitle,
              content: typedText,
            });
            if (mirrorToHero) setHeroAiText(typedText);
          });

          post({
            type: "servers.ai.speak",
            payload: {
              text: answerContent,
              title: resultTitle,
            },
          });
        }
      }
      if (type === "servers.openShelfTools") {
        setCinemaMode(false);
        setSelectedMovie(null);
        setCurrentPage("customize");
      }
    };

    try {
      (window as any).chrome.webview.addEventListener("message", handler);
    } catch {
    }

    post({ type: "servers.ready" });
    post({ type: "servers.getState" });

    return () => {
      try {
        (window as any).chrome.webview.removeEventListener("message", handler);
      } catch {
      }
    };
  }, []);

  useEffect(() => {
    if (!hasBridge()) return;

    const onError = (event: ErrorEvent) => {
      post({
        type: "servers.clientError",
        payload: {
          message: String(event.message ?? "Unhandled window error"),
          source: `${String(event.filename ?? "")}#${event.lineno ?? 0}:${event.colno ?? 0}`,
          stack: String((event.error as any)?.stack ?? ""),
        },
      });
    };

    const onUnhandledRejection = (event: PromiseRejectionEvent) => {
      const reason = event.reason;
      post({
        type: "servers.clientError",
        payload: {
          message: String(reason?.message ?? reason ?? "Unhandled promise rejection"),
          source: "promise",
          stack: String(reason?.stack ?? ""),
        },
      });
    };

    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onUnhandledRejection);
    return () => {
      window.removeEventListener("error", onError);
      window.removeEventListener("unhandledrejection", onUnhandledRejection);
    };
  }, []);

  useEffect(() => {
    if (!serversState?.localSelection) return;
    setLocalLibraryMode(serversState.localSelection);
  }, [serversState?.localSelection]);

  const shelves = useMemo(() => normalizeShelves(serversState), [serversState]);
  const activeAddonShelves = useMemo(
    () => shelves.filter((shelf) => !deletedShelfIds.includes(shelf.id)),
    [deletedShelfIds, shelves],
  );
  const deletedAddonShelves = useMemo(
    () => shelves.filter((shelf) => deletedShelfIds.includes(shelf.id)),
    [deletedShelfIds, shelves],
  );
  const customShelfObjects = useMemo<Shelf[]>(
    () =>
      customShelves
        .filter((entry) => entry.items.length > 0)
        .map((entry) => ({
          id: entry.id,
          title: entry.title,
          viewAll: true,
          items: entry.items.map((item) => mapServerItem(entry.id, item)),
        })),
    [customShelves],
  );
  const orderedAddonShelves = useMemo(
    () => orderShelves(activeAddonShelves, shelfOrderIds),
    [activeAddonShelves, shelfOrderIds],
  );
  const orderedAllShelves = useMemo(
    () => orderShelves([...activeAddonShelves, ...customShelfObjects], shelfOrderIds),
    [activeAddonShelves, customShelfObjects, shelfOrderIds],
  );
  const visibleShelves = useMemo(
    () => orderedAllShelves.filter((shelf) => !hiddenShelfIds.includes(shelf.id)),
    [hiddenShelfIds, orderedAllShelves],
  );
  const homeShelves = useMemo(() => visibleShelves, [visibleShelves]);
  const bridgeShelfOrderIds = useMemo(
    () => orderedAllShelves.filter((shelf) => shelf.persistOrderToBridge).map((shelf) => shelf.id),
    [orderedAllShelves],
  );
  const carouselShelves = useMemo(() => {
    if (carouselShelfIds.length === 0) return [];
    const selectedIds = new Set(carouselShelfIds);
    return visibleShelves.filter((shelf) => selectedIds.has(shelf.id));
  }, [carouselShelfIds, visibleShelves]);
  const latestAddonShelves = useMemo(
    () => orderedAddonShelves.filter((shelf) => isLatestShelf(shelf)),
    [orderedAddonShelves],
  );
  const featuredSourceShelves = useMemo(
    () => {
      const atlasLatestShelves = homeShelves.filter(
        (shelf) => isAtlasLatestMoviesShelf(shelf) || isAtlasLatestSeriesShelf(shelf),
      );
      if (atlasLatestShelves.length > 0) return atlasLatestShelves;

      const latestHomeShelves = homeShelves.filter((shelf) => isLatestShelf(shelf));
      if (latestHomeShelves.length > 0) return latestHomeShelves;

      if (latestAddonShelves.length > 0) return latestAddonShelves;

      const preferredShelves = orderedAddonShelves.filter(
        (shelf) => !isSyntheticShelfId(shelf.id) && !isPlaybackHistoryShelf(shelf),
      );

      if (preferredShelves.length > 0) return preferredShelves;
      return orderedAddonShelves.filter((shelf) => !isSyntheticShelfId(shelf.id));
    },
    [homeShelves, latestAddonShelves, orderedAddonShelves],
  );
  const hasBridgeCatalogItems = (serversState?.catalogItems?.length ?? 0) > 0;
  const isSyntheticGridShelf = currentPage === "grid" && isSyntheticShelfId(selectedShelfId);
  const isCustomGridShelf = currentPage === "grid" && isCustomShelfId(selectedShelfId);
  const isBridgeGrid = currentPage === "grid" && !isSyntheticGridShelf && !isCustomGridShelf && (serversState?.mode === "grid" || hasBridgeCatalogItems);
  const featuredCandidates = useMemo(() => {
    const lang = serversState?.preferredContentLanguage ?? "en";

    const dedupeAndSort = (items: ShelfMovie[], enforceLanguage: boolean) => {
      const seen = new Set<string>();

      return items
      .filter((item) => {
        if (enforceLanguage && !isPreferredLanguage(item, lang)) return false;
        const key = mediaKey(item);
        if (!key || seen.has(key)) return false;
        seen.add(key);
        return true;
      })
      .sort(compareByMetadataPriority);
    };

    const latestMovieItems = dedupeAndSort(
      featuredSourceShelves
        .filter((shelf) => isLatestMoviesShelf(shelf))
        .flatMap((shelf) => shelf.items),
      true,
    );
    const latestSeriesItems = dedupeAndSort(
      featuredSourceShelves
        .filter((shelf) => isLatestSeriesShelf(shelf))
        .flatMap((shelf) => shelf.items),
      true,
    );

    const fallbackMovieItems = latestMovieItems.length > 0
      ? latestMovieItems
      : dedupeAndSort(featuredSourceShelves.flatMap((shelf) => shelf.items).filter((item) => item.type === "movie"), true);
    const fallbackSeriesItems = latestSeriesItems.length > 0
      ? latestSeriesItems
      : dedupeAndSort(featuredSourceShelves.flatMap((shelf) => shelf.items).filter((item) => item.type === "tv"), true);

    const interleaved: ShelfMovie[] = [];
    const maxLength = Math.max(fallbackMovieItems.length, fallbackSeriesItems.length);
    for (let index = 0; index < maxLength; index += 1) {
      const movie = fallbackMovieItems[index];
      const series = fallbackSeriesItems[index];
      if (movie) interleaved.push(movie);
      if (series) interleaved.push(series);
    }

    if (interleaved.length > 0) {
      if (interleaved.length < 6) {
        const relaxed = dedupeAndSort(featuredSourceShelves.flatMap((shelf) => shelf.items), false).slice(0, 18);
        if (relaxed.length > interleaved.length) return relaxed;
      }
      return interleaved.slice(0, 18);
    }

    return dedupeAndSort(featuredSourceShelves.flatMap((shelf) => shelf.items), false).slice(0, 18);
  }, [featuredSourceShelves, serversState?.preferredContentLanguage]);
  const featuredCandidate = featuredCandidates[featuredIndex] ?? featuredSourceShelves[0]?.items[0] ?? null;
  const lastFeaturedRef = useRef<ShelfMovie | null>(null);
  if (featuredCandidate) lastFeaturedRef.current = featuredCandidate;
  const featuredMovie = useMemo(() => {
    const base = featuredCandidate ?? lastFeaturedRef.current;
    if (!base) return null;

    const preview = serversState?.preview;
    if (!preview) return base;

    const previewMovie = mapServerItem(base.bridgeShelfId || base.shelfId || "catalog", preview, base.type);
    if (!identityMatches(base, previewMovie)) {
      return base;
    }

    return {
      ...base,
      ...previewMovie,
      shelfId: base.shelfId,
      bridgeShelfId: base.bridgeShelfId,
    };
  }, [featuredCandidate, serversState?.preview]);

  useEffect(() => {
    // Don't clean up shelf state until real data has arrived from the host;
    // running this with empty shelves would wipe the saved order.
    if (shelves.length === 0) return;

    const validDeletedIds = new Set(shelves.map((shelf) => shelf.id));
    const validShelfIds = new Set(orderedAllShelves.map((shelf) => shelf.id));

    setDeletedShelfIds((current) => {
      const next = current.filter((id) => validDeletedIds.has(id));
      return sameStringArray(current, next) ? current : next;
    });

    setHiddenShelfIds((current) => {
      const next = current.filter((id) => validShelfIds.has(id));
      return sameStringArray(current, next) ? current : next;
    });

    setShelfOrderIds((current) => {
      // Preserve the user's existing order — only prune removed shelves and append new ones
      const validIds = new Set(orderedAllShelves.map((shelf) => shelf.id));
      const kept = current.filter((id) => validIds.has(id));
      const keptSet = new Set(kept);
      const newIds = orderedAllShelves.map((shelf) => shelf.id).filter((id) => !keptSet.has(id));
      const next = dedupeIds([...kept, ...newIds]);
      return sameStringArray(current, next) ? current : next;
    });

    setCarouselShelfIds((current) => {
      const next = current.filter((id) => validShelfIds.has(id));
      return sameStringArray(current, next) ? current : next;
    });
  }, [orderedAllShelves, shelves]);

  useEffect(() => {
    if (!hasBridge() || bridgeShelfOrderIds.length === 0) return;
    post({ type: "servers.saveShelfOrder", payload: { shelfIds: bridgeShelfOrderIds, clientShelfIds: shelfOrderIds } });
  }, [bridgeShelfOrderIds, shelfOrderIds]);

  useEffect(() => {
    setArmedMovieKey("");
  }, [cinemaMode, currentPage, selectedShelfId]);
  const globalSearchShelf = useMemo<Shelf | null>(() => {
    const query = globalSearchQuery.trim().toLowerCase();
    if (!query) return null;

    const allItems = dedupeMovies(visibleShelves.flatMap((shelf) => shelf.items));
    const matches = allItems.filter((item) => {
      const yearText = String(item.year ?? "").trim().toLowerCase();
      return (
        item.title.toLowerCase().includes(query) ||
        item.description.toLowerCase().includes(query) ||
        item.director.toLowerCase().includes(query) ||
        item.actors.some((actor) => actor.name.toLowerCase().includes(query)) ||
        item.genres.some((genre) => genre.toLowerCase().includes(query)) ||
        yearText.includes(query)
      );
    });

    return {
      id: GLOBAL_SEARCH_SHELF_ID,
      title: `Search Results for "${globalSearchQuery.trim()}"`,
      viewAll: true,
      items: matches.map((item) => ({ ...item, shelfId: GLOBAL_SEARCH_SHELF_ID, bridgeShelfId: item.bridgeShelfId || item.shelfId })),
    };
  }, [globalSearchQuery, visibleShelves]);
  const selectedShelf = useMemo(
    () => {
      if (selectedShelfId === GLOBAL_SEARCH_SHELF_ID && globalSearchShelf) return globalSearchShelf;
      return homeShelves.find((shelf) => shelf.id === selectedShelfId) ?? homeShelves[0] ?? globalSearchShelf ?? null;
    },
    [globalSearchShelf, homeShelves, selectedShelfId],
  );
  const activeGridShelf = useMemo(
    () => homeShelves.find((shelf) => shelf.id === (serversState?.activeShelfKey ?? "")) ?? selectedShelf,
    [homeShelves, selectedShelf, serversState?.activeShelfKey],
  );
  const catalogGridItems = useMemo(
    () => {
      const fallbackType = activeGridShelf
        ? normalizeGridTypeSelection(defaultGridTypeForShelf(activeGridShelf))
        : normalizeGridTypeSelection(serversState?.selectedType);

      return dedupeMovies(
        (serversState?.catalogItems ?? []).map((item) => mapServerItem(
          activeGridShelf?.id ?? "catalog",
          item,
          fallbackType === "all" ? undefined : fallbackType,
        )),
      );
    },
    [activeGridShelf, serversState?.catalogItems, serversState?.selectedType],
  );
  const normalizedServerSelectedType = useMemo(
    () => normalizeGridTypeSelection(serversState?.selectedType),
    [serversState?.selectedType],
  );
  const effectiveGridShelf = useMemo(
    () => (isBridgeGrid ? activeGridShelf ?? selectedShelf : selectedShelf),
    [activeGridShelf, isBridgeGrid, selectedShelf],
  );
  const preferredShelfGridType = useMemo(
    () => (effectiveGridShelf ? normalizeGridTypeSelection(defaultGridTypeForShelf(effectiveGridShelf)) : "all"),
    [effectiveGridShelf],
  );
  const bridgeGridSourceItems = useMemo(
    () => {
      // When catalog items arrive, merge them with the shelf items so the genre shelf
      // items stay at the top and catalog items extend below.  This prevents "View All"
      // on a genre shelf from immediately switching to unrelated random titles.
      const shelfItems = effectiveGridShelf?.items?.length
        ? effectiveGridShelf.items
        : (selectedShelf?.items ?? []);
      if (catalogGridItems.length > 0) {
        const seen = new Set(shelfItems.map((i) => mediaKey(i)).filter(Boolean));
        const extra = catalogGridItems.filter((i) => { const k = mediaKey(i); return k && !seen.has(k); });
        return [...shelfItems, ...extra];
      }
      return shelfItems;
    },
    [catalogGridItems, effectiveGridShelf, selectedShelf],
  );
  const activeStreamsState = useMemo(() => {
    if (!selectedMovie) return streamsState;
    if (!streamsState?.mediaId) return streamsState;
    // For series episodes, the stream mediaId is the episode id (e.g. tt123:1:5)
    // while selectedMovie has the series root id (e.g. tt123). Accept streams
    // if the root id is a prefix of the stream mediaId.
    const streamId = (streamsState.mediaId ?? "").toLowerCase();
    const rootId = (selectedMovie.metaId || selectedMovie.imdbId || selectedMovie.id || "").toLowerCase();
    if (rootId && streamId.startsWith(rootId)) return streamsState;
    return identityMatches(selectedMovie, { id: streamsState.mediaId }) ? streamsState : null;
  }, [selectedMovie, streamsState]);

  useEffect(() => {
    const preview = serversState?.preview;
    if (!preview || !selectedMovie) return;

    const previewMovie = mapServerItem(selectedMovie.bridgeShelfId || selectedMovie.shelfId || "catalog", preview);
    if (!previewMovie.id || !identityMatches(selectedMovie, previewMovie)) return;

    setSelectedMovie((current) => {
      if (!current || !identityMatches(current, previewMovie)) return current;
      return {
        ...current,
        ...previewMovie,
        shelfId: current.shelfId,
        bridgeShelfId: current.bridgeShelfId,
      };
    });
  }, [selectedMovie, serversState?.preview]);

  const streamSources = useMemo<StreamSource[]>(() => {
    const srcs = activeStreamsState?.sources ?? [];
    return srcs.map((s) => {
      if (Boolean((s as any).isInfoOnly) || (s as any).isPlayable === false) {
        return null;
      }

      const sourceId = String(s.sourceId ?? "").trim();
      if (!sourceId) {
        return null;
      }

      const provider = (s.providerName ?? "").trim();
      const name = (s.name ?? "").trim();
      const copyableLink = (s.urlOrPath ?? "").toString().trim();
      const quality = (s.quality ?? "").trim() || "HD";
      const display = provider && name ? `${provider} · ${name}` : provider || name || "Source";
      const meta = s.metadata ?? {};
      const fileSize = (s.sizeText ?? meta.fileSize ?? meta.size ?? "").toString().trim();
      const audioLanguage = (meta.audio ?? meta.audioLanguage ?? meta.language ?? "").toString().trim() || "Audio";
      const subtitlesRaw = meta.subtitles ?? meta.subs ?? [];
      const subtitles = Array.isArray(subtitlesRaw)
        ? subtitlesRaw.map((x: any) => String(x)).filter(Boolean)
        : typeof subtitlesRaw === "string"
          ? subtitlesRaw.split(/[|,]/).map((x: string) => x.trim()).filter(Boolean)
          : [];
      const seederText = (s.seedersText ?? meta.seedersText ?? "").toString();
      const parsedSeeders = Number.parseInt(seederText.replace(/[^0-9]/g, ""), 10);
      const seederCount =
        typeof meta.seeders === "number"
          ? meta.seeders
          : typeof meta.seederCount === "number"
            ? meta.seederCount
            : Number.isFinite(parsedSeeders)
              ? parsedSeeders
            : undefined;

      return {
        _sourceId: sourceId,
        sourceName: name || provider || "Source",
        providerName: provider,
        copyableLink,
        quality,
        fileSize,
        audioLanguage,
        subtitles,
        seederCount,
        isPlayable: (s as any).isPlayable !== false,
        metadata: meta,
      };
    }).filter((source): source is StreamSource => Boolean(source));
  }, [activeStreamsState]);

  // Autoplay is now manual-trigger only: the prominent Play button in the detail
  // view calls onPlay(firstSource) directly.  No automatic playback on source load.
  const streamInfoCards = useMemo<StreamInfoCard[]>(() => {
    const srcs = activeStreamsState?.sources ?? [];
    return srcs
      .filter((source: any) => Boolean(source?.isInfoOnly) || source?.isPlayable === false)
      .map((source: any) => ({
        id: String(source?.sourceId ?? ""),
        title: String(source?.name ?? "").trim(),
        providerName: String(source?.providerName ?? "").trim(),
        description: String(source?.metadata?.description ?? source?.quality ?? "").trim(),
        metadata: (source?.metadata ?? {}) as Record<string, string>,
      }))
      .filter((card) => card.title || card.description || Object.keys(card.metadata).length > 0);
  }, [activeStreamsState]);
  const effectiveSelectedMovieBadges = useMemo(() => selectedMovie?.ratingBadges ?? [], [selectedMovie?.ratingBadges]);
  const featuredRatingBadges = useMemo(() => defaultFeaturedRatingBadges(featuredMovie), [featuredMovie]);

  const allGenres = useMemo(() => {
    const genres = new Set<string>();
    for (const shelf of visibleShelves) {
      for (const item of shelf.items) {
        for (const genre of item.genres) {
          if (genre) genres.add(genre);
        }
      }
    }
    return ["All Genres", ...Array.from(genres).sort((a, b) => a.localeCompare(b))];
  }, [visibleShelves]);

  const filteredGridItems = useMemo(() => {
    const items = dedupeMovies(isBridgeGrid ? bridgeGridSourceItems : (selectedShelf?.items ?? []));
    const query = gridSearch.trim().toLowerCase();
    const filtered = items.filter((item) => {
      if (gridType !== "all" && item.type !== gridType) return false;
      if (gridGenre !== "All Genres" && !item.genres.includes(gridGenre)) return false;
      if (item.rating < minimumRating) return false;
      if (!query) return true;
      return (
        item.title.toLowerCase().includes(query) ||
        item.description.toLowerCase().includes(query) ||
        item.genres.some((genre) => genre.toLowerCase().includes(query))
      );
    });

    if (isLatestShelf(effectiveGridShelf)) {
      return [...filtered].sort((left, right) => {
        const leftYear = Number(left.year || 0);
        const rightYear = Number(right.year || 0);
        return rightYear - leftYear || right.rating - left.rating || left.title.localeCompare(right.title);
      });
    }

    return filtered;
  }, [bridgeGridSourceItems, effectiveGridShelf, gridGenre, gridSearch, gridType, isBridgeGrid, minimumRating, selectedShelf]);

  const visibleGridItems = useMemo(
    () => (isBridgeGrid ? filteredGridItems : filteredGridItems.slice(0, visibleCount)),
    [filteredGridItems, isBridgeGrid, visibleCount],
  );
  const bridgeLoadMoreStalled = stalledBridgeLoadRequests >= 2;
  const shouldShowBridgeLoadMore = currentPage === "grid" && isBridgeGrid && Boolean(serversState?.canLoadMore || serversState?.isBusy);
  const shouldShowLocalLoadMore = currentPage === "grid" && !isBridgeGrid && visibleCount < filteredGridItems.length;
  const shouldShowGridLoadMore = shouldShowBridgeLoadMore || shouldShowLocalLoadMore;

  const gridColumnCount = useMemo(() => {
    if (contentWidth <= 0) return 4;
    if (contentWidth < 720) return 2;
    if (contentWidth < 940) return 3;
    if (contentWidth < 1220) return 4;
    if (contentWidth < 1500) return 5;
    return 6;
  }, [contentWidth]);

  const carouselPoolItems = useMemo(() => {
    if (cinemaMode && carouselSeedItems.length > 0) {
      return carouselSeedItems;
    }

    const selectedSource = isBridgeGrid
      ? catalogGridItems
      : (effectiveGridShelf?.items?.length ? effectiveGridShelf.items : (selectedShelf?.items ?? []));
    const fallbackSource = visibleShelves.flatMap((shelf) => shelf.items);
    return dedupeMovies(selectedSource.length > 0 ? selectedSource : fallbackSource);
  }, [carouselSeedItems, catalogGridItems, cinemaMode, effectiveGridShelf, isBridgeGrid, selectedShelf, visibleShelves]);

  const carouselSourceItems = useMemo(() => {
    const exactTypeMatches = carouselPoolItems.filter((item) => carouselType === "all" || item.type === carouselType);
    if (exactTypeMatches.length > 0) return exactTypeMatches;

    const shelfFallback = (selectedShelf?.items ?? []).filter((item) => carouselType === "all" || item.type === carouselType);
    if (shelfFallback.length > 0) return dedupeMovies(shelfFallback);

    const crossShelfFallback = visibleShelves
      .flatMap((shelf) => shelf.items)
      .filter((item) => carouselType === "all" || item.type === carouselType);
    if (crossShelfFallback.length > 0) return dedupeMovies(crossShelfFallback);

    return carouselPoolItems;
  }, [carouselPoolItems, carouselType, selectedShelf, visibleShelves]);

  const carouselItems = useMemo(() => {
    const source = [...carouselSourceItems].sort((left, right) => {
      const leftYear = Number(left.year || 0);
      const rightYear = Number(right.year || 0);
      return rightYear - leftYear || right.popularity - left.popularity || right.rating - left.rating || left.title.localeCompare(right.title);
    });
    return source.slice(0, visibleCarouselCount);
  }, [carouselSourceItems, visibleCarouselCount]);
  const carouselStep = useMemo(
    () => (carouselItems.length > 0 ? 360 / carouselItems.length : 0),
    [carouselItems.length],
  );
  const carouselRadius = useMemo(
    () => Math.max(460, Math.min(760, Math.ceil((Math.max(carouselItems.length, 1) * 196) / (2 * Math.PI)))),
    [carouselItems.length],
  );

  const gridTypeOptions = useMemo(
    () => [
      { value: "all", label: "All Types" },
      { value: "movie", label: "Movies" },
      { value: "tv", label: "TV" },
    ],
    [],
  );

  const gridGenreOptions = useMemo(
    () => allGenres.map((genre) => ({ value: genre, label: genre })),
    [allGenres],
  );

  const serverFilterOptions = useMemo(() => {
    const options = (serversState?.serverOptions ?? []).filter(Boolean);
    const unique = Array.from(new Set([options.includes("All servers") ? [] : ["All servers"], options].flat()));
    return unique.map((value) => ({ value, label: formatServerOptionLabel(value) }));
  }, [serversState?.serverOptions]);

  const selectedServerValue = useMemo(() => {
    const current = String(serversState?.selectedServer ?? "").trim();
    if (current) return current;
    return serverFilterOptions[0]?.value ?? "All servers";
  }, [serverFilterOptions, serversState?.selectedServer]);
  const isLocalLibraryMode = serversState?.viewMode === "local";
  const localMovies = serversState?.localMovies ?? [];
  const localAlbums = serversState?.localAlbums ?? [];
  const localHeroArtwork = localMovies.find((movie) => movie.backdropUrl || movie.coverUrl)?.backdropUrl || localMovies.find((movie) => movie.coverUrl)?.coverUrl || localAlbums[0]?.coverUrl || "";
  const localLibraryStats = [
    { label: "Movies", value: String(localMovies.length).padStart(2, "0") },
    { label: "Albums", value: String(localAlbums.length).padStart(2, "0") },
    { label: "Ready", value: localMovies.length + localAlbums.length > 0 ? "Live" : "Empty" },
  ];

  useEffect(() => {
    if (!isLocalLibraryMode || currentPage !== "home") {
      setLocalLibraryOverlayOpen(false);
    }
  }, [currentPage, isLocalLibraryMode]);

  function openLocalLibraryOverlay(mode: LocalLibraryMode) {
    setLocalLibraryMode(mode);
    setLocalLibraryOverlayOpen(true);
    setCinemaMode(false);
  }

  const focusedCarouselIndex = useMemo(() => {
    if (carouselItems.length === 0) return 0;
    let bestIndex = 0;
    let bestDistance = Number.POSITIVE_INFINITY;
    for (let index = 0; index < carouselItems.length; index++) {
      const raw = ((index * carouselStep + carouselAngle + 540) % 360) - 180;
      const distance = Math.abs(raw);
      if (distance < bestDistance) {
        bestDistance = distance;
        bestIndex = index;
      }
    }
    return bestIndex;
  }, [carouselAngle, carouselItems.length, carouselStep]);
  const focusedCarouselMovie = carouselItems[focusedCarouselIndex] ?? null;
  const focusedCarouselOffset = useMemo(() => {
    if (carouselItems.length === 0 || carouselStep <= 0) return 0;
    const raw = ((focusedCarouselIndex * carouselStep + carouselAngle + 540) % 360) - 180;
    return raw / carouselStep;
  }, [carouselAngle, carouselItems.length, carouselStep, focusedCarouselIndex]);
  const renderedCarouselEntries = useMemo(() => {
    if (carouselItems.length === 0) return [] as Array<{ movie: ShelfMovie; index: number; signedDistance: number }>;

    if (carouselItems.length <= VISIBLE_CAROUSEL_RADIUS * 2 + 1) {
      return carouselItems.map((movie, index) => ({
        movie,
        index,
        signedDistance: (((index * carouselStep + carouselAngle + 540) % 360) - 180) / Math.max(carouselStep || 1, 1),
      }));
    }

    const entries: Array<{ movie: ShelfMovie; index: number; signedDistance: number }> = [];
    for (let offset = -VISIBLE_CAROUSEL_RADIUS; offset <= VISIBLE_CAROUSEL_RADIUS; offset += 1) {
      const index = (focusedCarouselIndex + offset + carouselItems.length) % carouselItems.length;
      entries.push({
        movie: carouselItems[index],
        index,
        signedDistance: offset + focusedCarouselOffset,
      });
    }

    return entries;
  }, [carouselAngle, carouselItems, carouselStep, focusedCarouselIndex, focusedCarouselOffset]);
  const focusedCarouselDetails = useMemo(() => {
    if (!focusedCarouselMovie) return null;
    const preview = serversState?.preview;
    if (!preview) return focusedCarouselMovie;

    const previewMovie = mapServerItem(
      focusedCarouselMovie.bridgeShelfId || focusedCarouselMovie.shelfId || "catalog",
      preview,
      focusedCarouselMovie.type,
    );

    if (!identityMatches(focusedCarouselMovie, previewMovie)) {
      return focusedCarouselMovie;
    }

    return {
      ...focusedCarouselMovie,
      ...previewMovie,
      shelfId: focusedCarouselMovie.shelfId,
      bridgeShelfId: focusedCarouselMovie.bridgeShelfId,
    };
  }, [focusedCarouselMovie, serversState?.preview]);
  const focusedCarouselSummary = useMemo(() => {
    const description = sanitizeMetadataText(focusedCarouselDetails?.description);
    if (description) return description;

    const directorSummary = sanitizeMetadataText(focusedCarouselDetails?.director);
    if (directorSummary) {
      const yearText = sanitizeMetadataText(focusedCarouselDetails?.year);
      const runtimeText = sanitizeMetadataText(focusedCarouselDetails?.runtime);
      return [directorSummary, yearText, runtimeText].filter(Boolean).join(" • ");
    }

    const castSummary = (focusedCarouselDetails?.actors ?? []).map((actor) => actor.name).filter(Boolean).slice(0, 4);
    if (castSummary.length > 0) return `Cast: ${castSummary.join(" • ")}`;

    const genres = (focusedCarouselDetails?.genres ?? []).filter(Boolean).slice(0, 4);
    if (genres.length > 0) return genres.join(" • ");

    const ratingLine = (focusedCarouselDetails?.ratingBadges ?? []).slice(0, 3).map((badge) => `${badge.label} ${badge.value}`).join(" • ");
    if (ratingLine) return ratingLine;

    return "Summary unavailable from the connected metadata providers for this title.";
  }, [focusedCarouselDetails]);
  const focusedCarouselCast = useMemo(
    () => (focusedCarouselDetails?.actors ?? []).map((actor) => actor.name).filter(Boolean).slice(0, 5),
    [focusedCarouselDetails],
  );
  const focusedCarouselActors = useMemo(
    () => (focusedCarouselDetails?.actors ?? []).filter((actor) => actor.name).slice(0, 5),
    [focusedCarouselDetails],
  );
  const focusedCarouselRatingLine = useMemo(() => {
    const badges = focusedCarouselDetails?.ratingBadges ?? [];
    if (badges.length > 0) return badges.slice(0, 3).map((badge) => `${badge.label} ${badge.value}`).join(" • ");

    const fallback: string[] = [];
    if ((focusedCarouselDetails?.rating ?? 0) > 0) fallback.push(`Rating ${focusedCarouselDetails!.rating.toFixed(1)}`);
    return fallback.join(" • ");
  }, [focusedCarouselDetails]);

  const carouselShelfOptions = useMemo(() => {
    const sourceShelves = carouselShelves.length > 0 ? carouselShelves : visibleShelves;
    return sourceShelves.map((shelf) => ({ value: shelf.id, label: shelf.title }));
  }, [carouselShelves, visibleShelves]);

  const selectedCarouselShelfId = useMemo(() => {
    if (selectedShelfId && carouselShelfOptions.some((option) => option.value === selectedShelfId)) {
      return selectedShelfId;
    }

    return carouselShelfOptions[0]?.value ?? "";
  }, [carouselShelfOptions, selectedShelfId]);
  const gridPreviewMovie = useMemo(() => {
    if (currentPage !== "grid") return null;

    const fallback = visibleGridItems[0] ?? filteredGridItems[0] ?? null;
    const preview = serversState?.preview;
    if (!preview) return fallback;

    const previewMovie = mapServerItem(
      fallback?.bridgeShelfId || effectiveGridShelf?.id || selectedShelf?.id || "catalog",
      preview,
      fallback?.type,
    );

    if (fallback && identityMatches(fallback, previewMovie)) {
      return {
        ...fallback,
        ...previewMovie,
        shelfId: fallback.shelfId,
        bridgeShelfId: fallback.bridgeShelfId,
      };
    }

    return fallback ?? previewMovie;
  }, [currentPage, effectiveGridShelf?.id, filteredGridItems, selectedShelf?.id, serversState?.preview, visibleGridItems]);

  const carouselTitle = useMemo(() => {
    if (currentPage === "grid") {
      if (gridType === "tv" && isLatestShelf(effectiveGridShelf)) return "Latest Series";
      if (gridType === "tv") return `${effectiveGridShelf?.title ?? selectedShelf?.title ?? "Catalog"} TV`;
      if (gridType === "movie" && isLatestShelf(effectiveGridShelf)) return "Latest Movies";
    }

    return effectiveGridShelf?.title ?? selectedShelf?.title ?? "Featured Carousel";
  }, [currentPage, effectiveGridShelf, gridType, selectedShelf]);

  function enterCarousel(shelfId?: string) {
    const sourceShelf = homeShelves.find((shelf) => shelf.id === shelfId) ?? selectedShelf;
    const nextShelfId = shelfId ?? sourceShelf?.id ?? selectedShelfId;
    if (nextShelfId) {
      setSelectedShelfId(nextShelfId);
    }

    const configuredCarouselSource = carouselShelves.length > 0
      ? carouselShelves.flatMap((shelf) => shelf.items)
      : featuredSourceShelves.flatMap((shelf) => shelf.items);
    const liveSource = currentPage === "grid"
      ? (isBridgeGrid
        ? (bridgeGridSourceItems.length > 0 ? bridgeGridSourceItems : (sourceShelf?.items ?? []))
        : (sourceShelf?.items ?? []))
      : (sourceShelf?.items?.length
        ? sourceShelf.items
        : configuredCarouselSource.length > 0
          ? configuredCarouselSource
          : visibleShelves.flatMap((shelf) => shelf.items));

    setCarouselSeedItems(dedupeMovies(liveSource));

    const nextType = currentPage === "grid"
      ? normalizeGridTypeSelection(gridType)
      : normalizeGridTypeSelection(defaultGridTypeForShelf(sourceShelf ?? null));

    setCarouselType(nextType === "all" ? "all" : nextType);
    setAutoplayCarousel(true);
    setCarouselAngle(0);
    setCarouselVelocity(Math.max(0.008, Math.min(0.04, carouselAutoplaySpeedRef.current)));
    setCinemaMode(true);
  }

  useEffect(() => {
    if (!cinemaMode) {
      setCarouselSeedItems([]);
    }
  }, [cinemaMode]);

  function focusCarouselStage() {
    window.requestAnimationFrame(() => {
      carouselStageRef.current?.focus();
    });
  }

  function nudgeCarousel(delta: number) {
    setCarouselVelocity((velocity) => Math.max(-0.085, Math.min(0.085, velocity + delta)));
  }

  function snapCarouselToIndex(index: number) {
    setCarouselAngle(-(index * carouselStep));
    setCarouselVelocity(0);
  }

  useEffect(() => {
    if (!cinemaMode || !focusedCarouselMovie || !hasBridge()) return;
    if (Math.abs(carouselVelocity) > 0.025) return;

    const metaId = focusedCarouselMovie.metaId || focusedCarouselMovie.id;
    if (!metaId) return;

    const timer = window.setTimeout(() => {
      post(
        currentPage === "grid" && isBridgeGrid
          ? { type: "servers.selectCatalogItem", payload: { metaId, ...buildBridgeItemPayload(focusedCarouselMovie) } }
          : { type: "servers.selectItem", payload: { key: focusedCarouselMovie.bridgeShelfId, metaId, ...buildBridgeItemPayload(focusedCarouselMovie) } },
      );
    }, 90);

    return () => window.clearTimeout(timer);
  }, [carouselVelocity, cinemaMode, currentPage, focusedCarouselMovie, isBridgeGrid]);

  useEffect(() => {
    if (typeof document === "undefined") return;
    const previousBodyOverflow = document.body.style.overflow;
    const previousDocOverflow = document.documentElement.style.overflow;

    if (cinemaMode) {
      document.body.style.overflow = "hidden";
      document.documentElement.style.overflow = "hidden";
    }

    return () => {
      document.body.style.overflow = previousBodyOverflow;
      document.documentElement.style.overflow = previousDocOverflow;
    };
  }, [cinemaMode]);

  useEffect(() => {
    setVisibleCount(60);
  }, [selectedShelfId, gridGenre, gridSearch, gridType, minimumRating]);

  useEffect(() => {
    setStalledBridgeLoadRequests(0);
    lastBridgeCatalogCountRef.current = 0;
    lastBridgeBusyRef.current = false;
  }, [currentPage, selectedShelfId, gridGenre, gridSearch, gridType, minimumRating, serversState?.selectedCatalogId, serversState?.selectedType]);

  const lastGridSyncKeyRef = useRef("");

  useEffect(() => {
    if (currentPage !== "grid") {
      lastGridSyncKeyRef.current = "";
      return;
    }

    const syncKey = [selectedShelfId, serversState?.activeShelfKey ?? "", serversState?.selectedCatalogId ?? ""].join("|");
    if (lastGridSyncKeyRef.current === syncKey) return;
    lastGridSyncKeyRef.current = syncKey;

    const shouldForceShelfType = preferredShelfGridType !== "all" && (
      isLatestShelf(effectiveGridShelf) ||
      (effectiveGridShelf?.id ?? "").includes("all-series") ||
      (effectiveGridShelf?.id ?? "").includes("all-movies") ||
      (effectiveGridShelf?.title ?? "").toLowerCase().includes("series") ||
      (effectiveGridShelf?.title ?? "").toLowerCase().includes("movie")
    );

    const nextType = shouldForceShelfType
      ? preferredShelfGridType
      : preferredShelfGridType !== "all"
        ? preferredShelfGridType
        : normalizedServerSelectedType !== "all"
          ? normalizedServerSelectedType
          : "all";

    setGridType(nextType);
  }, [currentPage, effectiveGridShelf, normalizedServerSelectedType, preferredShelfGridType, selectedShelfId, serversState?.activeShelfKey, serversState?.selectedCatalogId]);

  useEffect(() => {
    setVisibleCarouselCount(INITIAL_CAROUSEL_COUNT);
    setCarouselAngle(0);
    setCarouselVelocity(0);
  }, [carouselType, selectedShelfId, currentPage]);

  useEffect(() => {
    if (!cinemaMode) return;

    const targetCount = Math.min(carouselSourceItems.length, MAX_CAROUSEL_COUNT);
    if (visibleCarouselCount >= targetCount) return;
    if (focusedCarouselIndex < Math.max(0, visibleCarouselCount - 6)) return;

    setVisibleCarouselCount((current) => Math.min(targetCount, current + 18));
  }, [carouselSourceItems.length, cinemaMode, focusedCarouselIndex, visibleCarouselCount]);

  useEffect(() => {
    if (!cinemaMode || carouselItems.length > 0 || carouselType === "all") return;
    if (carouselPoolItems.length === 0) return;

    const hasMovies = carouselPoolItems.some((item) => item.type === "movie");
    const hasTv = carouselPoolItems.some((item) => item.type === "tv");
    if (hasMovies && hasTv) {
      setCarouselType("all");
      return;
    }

    if (hasTv) {
      setCarouselType("tv");
      return;
    }

    if (hasMovies) {
      setCarouselType("movie");
    }
  }, [carouselItems.length, carouselPoolItems, carouselType, cinemaMode]);

  useEffect(() => {
    carouselVelocityRef.current = carouselVelocity;
  }, [carouselVelocity]);

  useEffect(() => {
    carouselAutoplaySpeedRef.current = carouselAutoplaySpeed;
  }, [carouselAutoplaySpeed]);

  useEffect(() => {
    // Only reset when the candidate IDs actually change, not on every reference update
    setFeaturedIndex((current) => current >= featuredCandidates.length ? 0 : current);
  }, [featuredCandidates.length]);

  useEffect(() => {
    if (currentPage !== "home" || cinemaMode || featuredCandidates.length < 2) return;

    const timer = window.setInterval(() => {
      setFeaturedIndex((current) => (current + 1) % featuredCandidates.length);
    }, 30000);

    return () => window.clearInterval(timer);
  }, [cinemaMode, currentPage, featuredCandidates.length]);

  const heroAiMovieIdRef = useRef("");

  useEffect(() => {
    if (currentPage !== "home" || cinemaMode || !featuredCandidate || !hasBridge()) return;

    // Only clear AI text when the featured title actually changes to a different movie
    const candidateKey = featuredCandidate.metaId || featuredCandidate.id;
    if (heroAiMovieIdRef.current && heroAiMovieIdRef.current !== candidateKey) {
      setHeroAiText("");
      heroAiMovieIdRef.current = "";
    }

    const metaId = featuredCandidate.metaId || featuredCandidate.id;
    if (!metaId) return;

    const timer = window.setTimeout(() => {
      post({
        type: "servers.selectItem",
        payload: {
          key: featuredCandidate.bridgeShelfId || featuredCandidate.shelfId,
          metaId,
          imdbId: featuredCandidate.imdbId,
          title: featuredCandidate.title,
          ...buildBridgeItemPayload(featuredCandidate),
        },
      });
    }, 120);

    return () => window.clearTimeout(timer);
  }, [cinemaMode, currentPage, featuredCandidate]);

  useEffect(() => {
    if (currentPage !== "grid" || cinemaMode) return;
    const root = containerRef.current;
    const target = gridLoadTriggerRef.current;
    if (!root || !target) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        if (!entry?.isIntersecting) return;
        if (isBridgeGrid) {
          if (!serversState?.canLoadMore || bridgeLoadMoreStalled) return;
          requestBridgeLoadMore();
          return;
        }
        setVisibleCount((current) => Math.min(current + 36, filteredGridItems.length));
      },
      {
        root,
        rootMargin: "0px 0px 280px 0px",
        threshold: 0.2,
      },
    );

    observer.observe(target);
    return () => observer.disconnect();
  }, [bridgeLoadMoreStalled, catalogGridItems.length, cinemaMode, currentPage, filteredGridItems.length, isBridgeGrid, shouldShowGridLoadMore, visibleGridItems.length, serversState?.canLoadMore, serversState?.isBusy]);

  useEffect(() => {
    if (!isBridgeGrid) {
      lastBridgeBusyRef.current = false;
      return;
    }

    const isBusy = Boolean(serversState?.isBusy);
    const wasBusy = lastBridgeBusyRef.current;
    lastBridgeBusyRef.current = isBusy;

    if (!wasBusy || isBusy || lastBridgeCatalogCountRef.current <= 0) {
      return;
    }

    if (catalogGridItems.length > lastBridgeCatalogCountRef.current) {
      setStalledBridgeLoadRequests(0);
    } else {
      setStalledBridgeLoadRequests((current) => current + 1);
    }

    lastBridgeCatalogCountRef.current = 0;
  }, [catalogGridItems.length, isBridgeGrid, serversState?.isBusy]);

  useEffect(() => {
    if (!cinemaMode) return;

    let frame = 0;
    const tick = () => {
      let nextVelocity = carouselVelocityRef.current;
      if (autoplayCarousel) {
        const target = carouselAutoplaySpeedRef.current;
        // Keep velocity interpolation gentle to avoid visible stutter.
        const factor = Math.abs(nextVelocity) > Math.abs(target) * 1.2 ? 0.09 : 0.025;
        nextVelocity += (target - nextVelocity) * factor;
      } else {
        nextVelocity *= 0.82;
        if (Math.abs(nextVelocity) < 0.0035) nextVelocity = 0;
      }

      nextVelocity = Math.max(-0.085, Math.min(0.085, nextVelocity));

      carouselVelocityRef.current = nextVelocity;
      setCarouselVelocity(nextVelocity);
      setCarouselAngle((angle) => angle + nextVelocity);
      frame = window.requestAnimationFrame(tick);
    };

    frame = window.requestAnimationFrame(tick);
    return () => window.cancelAnimationFrame(frame);
  }, [autoplayCarousel, cinemaMode]);

  const status = (activeStreamsState?.statusText ?? serversState?.statusText ?? "").trim();

  function toggleWatchlist(movie: ShelfMovie) {
    setWatchlist((current) =>
      current.includes(movie.id)
        ? current.filter((entry) => entry !== movie.id)
        : [...current, movie.id],
    );
  }

  function openMovie(movie: ShelfMovie) {
    const canonicalMediaId = movie.metaId || movie.imdbId || movie.id;
    autoPlayedSourceKeyRef.current = "";
    pageScrollPositionsRef.current[currentPage] = readScrollTop(containerRef.current);
    pendingScrollRestoreRef.current = { page: "info", top: 0 };
    setAiDetailResult(null);
    setReturnPage(currentPage === "grid" || currentPage === "customize" ? currentPage : "home");
    setSelectedMovie(movie);
    setCurrentPage("info");
    setCinemaMode(false);
    if (movie.type === "tv") {
      setSeriesState({
        isOpen: true,
        isBusy: true,
        statusText: "Loading episodes...",
        rootTitle: movie.title,
        rootId: canonicalMediaId,
        rootType: "series",
        rootBackdrop: movie.backdropUrl,
        rootPoster: movie.posterUrl,
        seasons: [],
        selectedSeason: 0,
        episodes: [],
      });
      setStreamsState(null);
    } else {
      setSeriesState(null);
      setStreamsState({
        type: "atlas:streams:state",
        mediaId: canonicalMediaId,
        isBusy: true,
        statusText: "Finding sources...",
        sources: [],
      });
    }
    post({
      type: "servers.openDetail",
      payload: {
        key: movie.bridgeShelfId,
        metaId: canonicalMediaId,
        imdbId: movie.imdbId,
        title: movie.title,
        ...buildBridgeItemPayload(movie),
      },
    });
  }

  function selectEpisode(ep: SeriesEpisode) {
    setStreamsState({
      type: "atlas:streams:state",
      mediaId: ep.id || ep.metaId,
      isBusy: true,
      statusText: `Finding sources for S${ep.season}E${ep.episode}...`,
      sources: [],
    });
    post({
      type: "servers.selectEpisode",
      payload: { id: ep.id, season: ep.season, episode: ep.episode },
    });
  }

  function selectSeason(seasonNumber: number) {
    post({
      type: "servers.selectSeason",
      payload: { season: seasonNumber },
    });
  }

  function playTrailer(movie: ShelfMovie) {
    post({
      type: "servers.playTrailer",
      payload: {
        key: movie.bridgeShelfId,
        metaId: movie.metaId || movie.id,
        imdbId: movie.imdbId,
        title: movie.title,
        ...buildBridgeItemPayload(movie),
      },
    });
  }

  function askAtlasAi(movie: ShelfMovie, action: "overview" | "next-watch" = "overview") {
    if (!hasBridge()) return;
    stopAiTyping();
    const loadingText = action === "next-watch" ? "Atlas is building next-watch guidance..." : "Atlas is thinking...";
    setAiDetailResult({
      title: movie.title,
      content: loadingText,
    });
    heroAiMovieIdRef.current = movie.metaId || movie.id;
    setHeroAiText(loadingText);
    post({
      type: "servers.ai.query",
      payload: {
        id: `ai-${movie.id}-${action}`,
        text: action === "next-watch"
          ? `Recommend what to watch after ${movie.title} (${movie.year}).`
          : `Give me a short overview for ${movie.title} (${movie.year}).`,
      },
    });
  }

  function createCustomShelf(query: string) {
    if (!hasBridge() || !query.trim()) return;
    const slug = query.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
    const shelfId = `ai-shelf-${slug}-${Date.now()}`;
    setCustomShelfLoading(true);
    post({
      type: "servers.ai.query",
      payload: {
        id: shelfId,
        text: query.trim(),
        title: query.trim(),
      },
    });
  }

  function removeCustomShelf(shelfId: string) {
    setCustomShelves((prev) => prev.filter((s) => s.id !== shelfId));
  }

  function deleteAddonShelf(shelfId: string) {
    setDeletedShelfIds((current) => dedupeIds([...current, shelfId]));
    setHiddenShelfIds((current) => current.filter((entry) => entry !== shelfId));
    setCarouselShelfIds((current) => current.filter((entry) => entry !== shelfId));
    setShelfOrderIds((current) => current.filter((entry) => entry !== shelfId));
  }

  function restoreAddonShelf(shelfId: string) {
    setDeletedShelfIds((current) => current.filter((entry) => entry !== shelfId));
  }

  function toggleCarouselShelfSelection(shelfId: string) {
    setCarouselShelfIds((current) => (
      current.includes(shelfId)
        ? current.filter((entry) => entry !== shelfId)
        : [...current, shelfId]
    ));
  }

  function reorderShelvesByDrop(scope: "all" | "visible", draggedShelfId: string, targetShelfId: string) {
    if (!draggedShelfId || !targetShelfId || draggedShelfId === targetShelfId) return;

    setShelfOrderIds((current) => {
      const sourceShelves = scope === "visible" ? homeShelves : orderedAllShelves;
      const orderedIds = orderShelves(sourceShelves, current).map((shelf) => shelf.id);
      const draggedIndex = orderedIds.findIndex((id) => id === draggedShelfId);
      const targetIndex = orderedIds.findIndex((id) => id === targetShelfId);
      if (draggedIndex < 0 || targetIndex < 0) return current;

      const nextIds = [...orderedIds];
      const [draggedId] = nextIds.splice(draggedIndex, 1);
      nextIds.splice(targetIndex, 0, draggedId);

      const remaining = current.filter((id) => !nextIds.includes(id));
      return [...nextIds, ...remaining];
    });
  }

  function selectMovieCard(movie: ShelfMovie) {
    setArmedMovieKey(mediaKey(movie));
  }

  function openMovieFromShelf(movie: ShelfMovie) {
    const nextKey = mediaKey(movie);
    if (armedMovieKey !== nextKey) {
      setArmedMovieKey(nextKey);
      return;
    }

    openMovie(movie);
  }

  function moveCustomShelf(shelfId: string, direction: "up" | "down") {
    setCustomShelves((prev) => {
      const idx = prev.findIndex((s) => s.id === shelfId);
      if (idx < 0) return prev;

      const target = direction === "up" ? idx - 1 : idx + 1;
      if (target < 0 || target >= prev.length) return prev;

      const next = [...prev];
      [next[idx], next[target]] = [next[target], next[idx]];
      return next;
    });
  }

  function moveAddonShelf(shelfId: string, direction: "up" | "down") {
    setShelfOrderIds((current) => {
      const orderedIds = orderShelves(shelves, current).map((shelf) => shelf.id);
      const currentIndex = orderedIds.findIndex((id) => id === shelfId);
      if (currentIndex < 0) return current;

      const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
      if (targetIndex < 0 || targetIndex >= orderedIds.length) return current;

      const nextIds = [...orderedIds];
      [nextIds[currentIndex], nextIds[targetIndex]] = [nextIds[targetIndex], nextIds[currentIndex]];

      const remaining = current.filter((id) => !nextIds.includes(id));
      return [...nextIds, ...remaining];
    });
  }

  function moveVisibleShelf(shelfId: string, direction: "up" | "down") {
    setShelfOrderIds((current) => {
      const orderedIds = orderShelves(homeShelves, current).map((shelf) => shelf.id);
      const currentIndex = orderedIds.findIndex((id) => id === shelfId);
      if (currentIndex < 0) return current;

      const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
      if (targetIndex < 0 || targetIndex >= orderedIds.length) return current;

      const nextIds = [...orderedIds];
      [nextIds[currentIndex], nextIds[targetIndex]] = [nextIds[targetIndex], nextIds[currentIndex]];
      const remaining = current.filter((id) => !nextIds.includes(id));
      return [...nextIds, ...remaining];
    });
  }

  function refreshHomeShelves() {
    if (!hasBridge()) return;
    post({ type: "servers.refresh" });
  }

  function onPlay(source: StreamSource) {
    const sid = source?._sourceId;
    if (!sid) return;
    post({ type: "servers.playSource", payload: { sourceId: sid } });
  }

  function onCopySourceLink(source: StreamSource) {
    const link = source?.copyableLink?.trim();
    if (!link || !hasBridge()) return;
    post({ type: "servers.copyServerLink", payload: { url: link } });
  }

  function requestBridgeLoadMore() {
    if (!hasBridge() || serversState?.isBusy || !serversState?.canLoadMore || bridgeLoadMoreStalled) {
      return;
    }

    const now = Date.now();
    if (now - lastBridgeLoadMoreRequestAtRef.current < 900) {
      return;
    }

    lastBridgeLoadMoreRequestAtRef.current = now;
    lastBridgeCatalogCountRef.current = catalogGridItems.length;
    post({ type: "servers.loadMore" });
  }

  function updateGridType(nextType: "all" | "movie" | "tv") {
    setGridType(nextType);

    if (!isBridgeGrid || !hasBridge()) {
      return;
    }

    const bridgeType = toBridgeCatalogType(nextType);
    const currentBridgeType = String(serversState?.selectedType ?? "").trim().toLowerCase();
    if (!bridgeType || currentBridgeType === bridgeType) {
      return;
    }

    post({ type: "servers.setType", payload: { type: bridgeType } });
  }

  function updateSelectedServer(nextServer: string) {
    const requested = String(nextServer ?? "").trim();
    const normalized = requested || "All servers";
    if (!hasBridge()) {
      return;
    }

    if (normalized === String(serversState?.selectedServer ?? "").trim()) {
      return;
    }

    pageScrollPositionsRef.current[currentPage] = readScrollTop(containerRef.current);
    pendingScrollRestoreRef.current = { page: "home", top: 0 };
    setCurrentPage("home");
    setSelectedMovie(null);
    setCinemaMode(false);
    setAiDetailResult(null);

    // Optimistically update local state so the dropdown doesn't flicker back
    setServersState((current) => current ? {
      ...current,
      selectedServer: normalized,
      viewMode: normalized === "Local Library" ? "local" : "servers",
    } : current);
    post({ type: "servers.setServer", payload: { server: normalized } });
  }

  function playLocalMovie(movieId: string) {
    post({ type: "servers.local.playMovie", payload: { id: movieId } });
  }

  function playLocalAlbum(albumId: string) {
    post({ type: "servers.local.playAlbum", payload: { id: albumId } });
  }

  function toggleShelfVisibility(shelfId: string) {
    setHiddenShelfIds((current) => (
      current.includes(shelfId)
        ? current.filter((entry) => entry !== shelfId)
        : [...current, shelfId]
    ));
  }

  function showGrid(shelf: Shelf) {
    pageScrollPositionsRef.current.home = readScrollTop(containerRef.current);
    pendingScrollRestoreRef.current = { page: "grid", top: 0 };
    setSelectedShelfId(shelf.id);
    setReturnPage("grid");
    setGridSearch("");
    setGridGenre("All Genres");
    setGridType(defaultGridTypeForShelf(shelf));
    setMinimumRating(0);
    setVisibleCount(60);
    setCurrentPage("grid");
    setCinemaMode(false);
    if (!isSyntheticShelfId(shelf.id) && !isCustomShelfId(shelf.id)) {
      post({ type: "servers.seeAll", payload: { key: shelf.id } });
    }
  }

  function runGlobalSearch(query: string) {
    const trimmed = query.trim();
    if (!trimmed) return;
    pageScrollPositionsRef.current.home = readScrollTop(containerRef.current);
    pendingScrollRestoreRef.current = { page: "grid", top: 0 };
    setGlobalSearchQuery(trimmed);
    setSelectedShelfId(GLOBAL_SEARCH_SHELF_ID);
    setGridSearch("");
    setGridGenre("All Genres");
    setGridType("all");
    setMinimumRating(0);
    setVisibleCount(60);
    setCurrentPage("grid");
    setCinemaMode(false);
  }

  function backToHome() {
    pendingScrollRestoreRef.current = { page: "home", top: pageScrollPositionsRef.current.home ?? 0 };
    setAiDetailResult(null);
    setReturnPage("home");
    setCurrentPage("home");
    setSelectedMovie(null);
    if (hasBridge()) {
      post({ type: "servers.back" });
    }
  }

  function onBack() {
    pageScrollPositionsRef.current.info = readScrollTop(containerRef.current);
    setAiDetailResult(null);
    setSelectedMovie(null);
    setCinemaMode(false);

    if (returnPage === "grid" || returnPage === "customize") {
      pendingScrollRestoreRef.current = { page: returnPage, top: pageScrollPositionsRef.current[returnPage] ?? 0 };
      setCurrentPage(returnPage);
      return;
    }

    backToHome();
  }

  function handleHorizontalWheel(event: React.WheelEvent<HTMLDivElement>) {
    const container = event.currentTarget;
    const maxScrollLeft = Math.max(0, container.scrollWidth - container.clientWidth);
    if (maxScrollLeft <= 0) return;

    const horizontalDelta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
    if (Math.abs(horizontalDelta) < 1) return;

    event.preventDefault();
    event.stopPropagation();

    const nextScrollLeft = Math.max(0, Math.min(maxScrollLeft, container.scrollLeft + horizontalDelta));
    if (Math.abs(nextScrollLeft - container.scrollLeft) < 1) {
      return;
    }

    container.scrollTo({ left: nextScrollLeft, behavior: "auto" });
  }

  const heroTrailer = featuredMovie ? toEmbedUrl(featuredMovie.trailerUrl) : "";
  const homeShelvesContent = homeShelves.length === 0 ? (
    <div className="rounded-[28px] border border-white/10 bg-slate-950/45 px-8 py-14 backdrop-blur-xl">
      <h2 className="text-3xl font-semibold text-white">No shelves yet</h2>
      <p className="mt-3 max-w-2xl text-slate-400">
        Add addon servers with catalogs to populate shelves.
      </p>
    </div>
  ) : (
    homeShelves.map((shelf, shelfIndex) => (
      <section
        key={shelf.id}
        className="space-y-4"
        draggable
        onDragStart={(event) => {
          event.dataTransfer.effectAllowed = "move";
          event.dataTransfer.setData("text/plain", shelf.id);
        }}
        onDragOver={(event) => {
          event.preventDefault();
          event.dataTransfer.dropEffect = "move";
        }}
        onDrop={(event) => {
          event.preventDefault();
          const draggedShelfId = event.dataTransfer.getData("text/plain");
          reorderShelvesByDrop("visible", draggedShelfId, shelf.id);
        }}
      >
        <div className="flex items-center justify-between gap-4">
          <div>
            <h2 className="text-2xl font-semibold tracking-tight text-white">{shelf.title}</h2>
            <p className="mt-1 text-sm text-slate-400">{shelf.items.length} titles ready to browse</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full border border-white/10 bg-white/6 px-3 py-2 text-xs font-semibold uppercase tracking-[0.24em] text-slate-300">
              Drag Shelf
            </span>
            <button
              type="button"
              className="rounded-full border border-white/15 bg-white/8 px-3 py-2.5 text-sm font-semibold text-white backdrop-blur-md transition hover:bg-white/14 disabled:opacity-30"
              disabled={shelfIndex === 0}
              onClick={() => moveVisibleShelf(shelf.id, "up")}
            >
              ▲
            </button>
            <button
              type="button"
              className="rounded-full border border-white/15 bg-white/8 px-3 py-2.5 text-sm font-semibold text-white backdrop-blur-md transition hover:bg-white/14 disabled:opacity-30"
              disabled={shelfIndex === homeShelves.length - 1}
              onClick={() => moveVisibleShelf(shelf.id, "down")}
            >
              ▼
            </button>
            <button
              type="button"
              className="rounded-full border border-white/15 bg-white/8 px-4 py-2.5 text-sm font-semibold text-white backdrop-blur-md transition hover:bg-white/14"
              onClick={() => showGrid(shelf)}
            >
              View All
            </button>
          </div>
        </div>

        <div
          className="flex gap-3 overflow-x-auto px-1 pb-4 scrollbar-hide [scrollbar-width:none]"
          style={{ msOverflowStyle: "none", overscrollBehavior: "contain" }}
          onWheelCapture={handleHorizontalWheel}
        >
          {shelf.items.map((movie, movieIndex) => (
            <MediaPosterCard
              key={movie.id}
              movie={movie}
              preload={shelfIndex * 10 + movieIndex < 10}
              isSelected={armedMovieKey === mediaKey(movie)}
              isWatchlisted={watchlist.includes(movie.id)}
              onSelect={selectMovieCard}
              onOpen={openMovieFromShelf}
              onPlay={(m) => openMovie(m)}
              onPlayTrailer={(m) => playTrailer(m)}
              onWatchlistToggle={toggleWatchlist}
            />
          ))}
        </div>
      </section>
    ))
  );

  return (
    <div ref={containerRef} className={`min-h-[100dvh] w-full overflow-x-hidden ${cinemaMode ? "overflow-y-hidden" : "overflow-y-auto"} bg-[radial-gradient(circle_at_top,_rgba(14,165,233,0.18),_transparent_22%),linear-gradient(140deg,#010409_0%,#020817_42%,#04132a_100%)] text-white`}>
      <div className="pointer-events-none fixed inset-0 bg-[radial-gradient(circle_at_top_right,_rgba(34,211,238,0.12),_transparent_26%),radial-gradient(circle_at_bottom_left,_rgba(59,130,246,0.14),_transparent_20%)]" />
      <div className="fixed left-4 top-24 z-20 hidden lg:block">
        <div className="rounded-[24px] border border-white/10 bg-slate-950/70 p-2 shadow-[0_24px_60px_rgba(0,0,0,0.32)] backdrop-blur-xl">
          <button
            type="button"
            className={`flex items-center gap-3 rounded-[18px] px-4 py-3 text-sm font-semibold transition ${currentPage === "customize" ? "border border-cyan-300/25 bg-cyan-300/14 text-cyan-100" : "border border-transparent text-slate-300 hover:bg-white/8 hover:text-white"}`}
            onClick={() => {
              setCinemaMode(false);
              setSelectedMovie(null);
              setCurrentPage("customize");
            }}
          >
            <span className={`h-2.5 w-2.5 rounded-full ${currentPage === "customize" ? "bg-cyan-300 shadow-[0_0_12px_rgba(103,232,249,0.75)]" : "bg-slate-500"}`} />
            <span>Addon Manager</span>
          </button>
        </div>
      </div>

      {currentPage === "home" ? (
        <div className="relative z-10 pb-16">
          {isLocalLibraryMode ? (
            <>
              <section className="relative overflow-hidden border-b border-white/8 px-8 pb-12 pt-8">
                <div className="absolute inset-0 opacity-90">
                  {localHeroArtwork ? <img src={localHeroArtwork} alt="" className="h-full w-full object-cover opacity-50 blur-[1px] saturate-125" /> : null}
                  <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(1,4,9,0.18)_0%,rgba(1,4,9,0.7)_46%,rgba(1,4,9,0.98)_100%)]" />
                  <div className="absolute inset-0 bg-[radial-gradient(circle_at_left,_rgba(2,6,23,0.16),_rgba(2,6,23,0.92)_58%)]" />
                </div>

                <div className="relative flex items-start justify-between gap-6">
                  <div className="max-w-4xl">
                    <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-cyan-300/20 bg-cyan-300/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.32em] text-cyan-200 backdrop-blur-md">
                      Local Library
                    </div>
                    <h1 className="max-w-3xl text-5xl font-black tracking-tight text-white">Keep the page clean. Open the local carousel only when you want it.</h1>
                    <p className="mt-4 max-w-2xl text-base leading-7 text-slate-200/80">
                      Your local collection stays tucked away until you explicitly open Movies or Music. No permanent carousel, no dead space, no placeholder wall.
                    </p>

                    <div className="mt-8 flex flex-wrap gap-3">
                      <button
                        type="button"
                        className="rounded-full bg-cyan-300 px-5 py-3 text-sm font-semibold text-slate-950 shadow-[0_14px_32px_rgba(103,232,249,0.35)] disabled:cursor-not-allowed disabled:opacity-45"
                        onClick={() => openLocalLibraryOverlay("movies")}
                        disabled={localMovies.length === 0}
                      >
                        Open Movies Carousel
                      </button>
                      <button
                        type="button"
                        className="rounded-full border border-white/15 bg-white/10 px-5 py-3 text-sm font-semibold text-white backdrop-blur-md disabled:cursor-not-allowed disabled:opacity-45"
                        onClick={() => openLocalLibraryOverlay("music")}
                        disabled={localAlbums.length === 0}
                      >
                        Open Music Carousel
                      </button>
                    </div>
                  </div>

                  <div className="w-full max-w-sm space-y-4">
                    {serverFilterOptions.length > 0 ? (
                      <div className="rounded-[28px] border border-white/12 bg-slate-950/55 p-1.5 backdrop-blur-xl">
                        <div className="px-3 pb-1.5 pt-1 text-[10px] font-semibold uppercase tracking-[0.28em] text-cyan-200/72">Source</div>
                        <FilterSelect value={selectedServerValue} options={serverFilterOptions} onChange={updateSelectedServer} showCopyButton />
                      </div>
                    ) : null}

                    <div className="grid grid-cols-3 gap-3">
                      {localLibraryStats.map((stat) => (
                        <div key={stat.label} className="rounded-[24px] border border-white/10 bg-slate-950/50 p-4 backdrop-blur-xl">
                          <div className="text-[10px] font-semibold uppercase tracking-[0.24em] text-slate-400">{stat.label}</div>
                          <div className="mt-2 text-2xl font-bold text-white">{stat.value}</div>
                        </div>
                      ))}
                    </div>

                    <div className="rounded-[28px] border border-white/10 bg-slate-950/50 p-4 backdrop-blur-xl">
                      <div className="text-[10px] font-semibold uppercase tracking-[0.24em] text-slate-400">Quick Preview</div>
                      <div className="mt-4 grid grid-cols-3 gap-3">
                        {localMovies.slice(0, 3).map((movie) => (
                          <button
                            key={movie.id}
                            type="button"
                            className="group overflow-hidden rounded-[20px] border border-white/10 bg-black/20 text-left"
                            onClick={() => openLocalLibraryOverlay("movies")}
                          >
                            <img src={movie.coverUrl || movie.backdropUrl || localHeroArtwork} alt={movie.title} className="aspect-[3/4] w-full object-cover transition duration-300 group-hover:scale-[1.04]" />
                          </button>
                        ))}
                        {localMovies.length === 0 ? <div className="col-span-3 rounded-[20px] border border-dashed border-white/10 px-4 py-8 text-center text-sm text-slate-400">No local movies scanned yet.</div> : null}
                      </div>
                    </div>
                  </div>
                </div>
              </section>

              <LocalLibraryOverlay
                open={localLibraryOverlayOpen}
                onClose={() => setLocalLibraryOverlayOpen(false)}
                mode={localLibraryMode}
                onModeChange={setLocalLibraryMode}
                movies={localMovies}
                albums={localAlbums}
                onPlayMovie={playLocalMovie}
                onPlayAlbum={playLocalAlbum}
                topSlot={serverFilterOptions.length > 0 ? (
                  <div className="min-w-[13rem] rounded-[22px] border border-white/12 bg-slate-950/55 p-1.5 backdrop-blur-xl">
                    <div className="px-3 pb-1.5 pt-1 text-[10px] font-semibold uppercase tracking-[0.28em] text-cyan-200/72">Source</div>
                    <FilterSelect value={selectedServerValue} options={serverFilterOptions} onChange={updateSelectedServer} showCopyButton />
                  </div>
                ) : null}
              />
            </>
          ) : (
          <>
          <section className="relative overflow-hidden" style={{ minHeight: '65vh' }}>
            <div className="absolute inset-0">
              {heroTrailer ? (
                <iframe
                  src={heroTrailer}
                  title={featuredMovie?.title ?? "Trailer preview"}
                  className="h-full w-full scale-[1.35] object-cover blur-[1px] saturate-125"
                  allow="autoplay; encrypted-media"
                />
              ) : featuredMovie ? (
                <img src={featuredMovie.backdropUrl || featuredMovie.posterUrl} alt={featuredMovie.title} className="h-full w-full object-cover" />
              ) : null}
              <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(1,4,9,0.05)_0%,rgba(1,4,9,0.55)_50%,rgba(1,4,9,0.98)_100%)]" />
              <div className="absolute inset-0 bg-[radial-gradient(circle_at_left,_rgba(2,6,23,0.15),_rgba(2,6,23,0.75)_65%)]" />
            </div>

            <div className="relative flex h-full min-h-[65vh] flex-col justify-end px-12 pb-12 pt-24">
              {featuredMovie ? (
                <div className="max-w-3xl">
                  <div className="flex flex-wrap items-center gap-3 text-xs uppercase tracking-[0.28em] text-cyan-200/80 mb-2">
                    <span>Featured</span>
                    <span>{featuredMovie.type === "tv" ? "Series" : "Movie"}</span>
                    <span>{featuredMovie.year || new Date().getFullYear()}</span>
                  </div>

                  {featuredMovie.logoUrl && featuredMovie.logoUrl.startsWith("http") ? (
                    <div className="mb-4 inline-flex max-w-[22rem] items-center justify-start overflow-hidden">
                      <img
                        src={featuredMovie.logoUrl}
                        alt={`${featuredMovie.title} logo`}
                        className="max-h-[80px] w-auto object-contain drop-shadow-lg"
                        onError={(e) => {
                          (e.currentTarget as HTMLImageElement).style.display = "none";
                        }}
                      />
                    </div>
                  ) : null}

                  <h2 className="text-5xl font-black tracking-tight text-white drop-shadow-lg">{featuredMovie.title}</h2>

                  <div className="mt-3 flex flex-wrap items-center gap-3 text-sm text-slate-300">
                    {featuredRatingBadges.slice(0, 4).map((badge) => (
                      <span key={`featured-${badge.key}`} className={`rounded-full border px-3 py-1 text-xs font-semibold backdrop-blur-md ${badge.accentClassName}`}>
                        {badge.label} {badge.value}
                      </span>
                    ))}
                    {featuredMovie.runtime ? <span className="text-slate-400">{featuredMovie.runtime}</span> : null}
                    {featuredMovie.genres.slice(0, 3).map((genre, i) => (
                      <span key={`fg-${i}`} className="rounded-full border border-white/15 bg-white/8 px-3 py-1 text-xs text-white/80">{genre}</span>
                    ))}
                  </div>

                  <p className="mt-4 max-w-2xl text-base leading-7 text-slate-100/90 drop-shadow-md">
                    {featuredMovie.description || "Summary unavailable from the connected metadata providers for this title."}
                  </p>

                  <div className="mt-6 flex flex-wrap items-center gap-3">
                    <button
                      type="button"
                      className="flex items-center gap-2 rounded-full bg-gradient-to-r from-purple-600 to-blue-600 px-7 py-3.5 text-sm font-bold text-white shadow-[0_14px_32px_rgba(139,92,246,0.4)] hover:scale-105 transition-transform"
                      onClick={() => openMovie(featuredMovie)}
                    >
                      <svg width="16" height="16" viewBox="0 0 16 16" fill="white"><path d="M3 2L13 8L3 14V2Z"/></svg>
                      Play
                    </button>
                    <button
                      type="button"
                      className="flex items-center gap-2 rounded-full border border-white/20 bg-white/10 px-6 py-3.5 text-sm font-semibold text-white backdrop-blur-md hover:bg-white/18 transition"
                      onClick={() => playTrailer(featuredMovie)}
                    >
                      <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="white" strokeWidth="1.5"><rect x="2" y="3" width="12" height="10" rx="2"/><path d="M6.5 6.5L9.5 8L6.5 9.5V6.5Z" fill="white"/></svg>
                      Trailer
                    </button>
                    <button
                      type="button"
                      className="flex items-center gap-2 rounded-full border border-white/15 bg-white/8 px-5 py-3.5 text-sm font-semibold text-white/80 backdrop-blur-md hover:bg-white/14 transition"
                      onClick={() => toggleWatchlist(featuredMovie)}
                    >
                      {watchlist.includes(featuredMovie.id) ? "✓ In Watchlist" : "+ Watchlist"}
                    </button>
                    {featuredCandidates.length > 1 ? (
                      <button
                        type="button"
                        className="flex items-center gap-2 rounded-full border border-white/15 bg-white/8 px-5 py-3.5 text-sm font-semibold text-white/80 backdrop-blur-md hover:bg-white/14 transition"
                        onClick={() => setFeaturedIndex((current) => (current + 1) % featuredCandidates.length)}
                      >
                        <svg width="16" height="16" viewBox="0 0 16 16" fill="white"><path d="M8 2L11 7H5M8 14L5 9H11M13 8L8 11V5M3 8L8 5V11"/></svg>
                        Next
                      </button>
                    ) : null}
                    {heroAiText ? (
                      <button
                        type="button"
                        className="rounded-full border border-red-500/30 bg-red-500/15 px-4 py-3.5 text-sm font-semibold text-red-300 hover:bg-red-500/25 transition"
                        onClick={() => { post({ type: "servers.stopSpeech" }); setHeroAiText(""); }}
                      >
                        ■ Stop
                      </button>
                    ) : null}
                  </div>

                  {heroAiText ? (
                    <div className="mt-4 max-w-2xl rounded-2xl border border-cyan-300/15 bg-cyan-300/8 p-4 backdrop-blur-md">
                      <div className="text-xs uppercase tracking-[0.24em] text-cyan-200/75 mb-2">Atlas AI</div>
                      <div className="text-sm leading-7 text-slate-200 whitespace-pre-line">{heroAiText}</div>
                    </div>
                  ) : null}
                </div>
              ) : (
                <div className="max-w-3xl">
                  <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-cyan-300/20 bg-cyan-300/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.32em] text-cyan-200 backdrop-blur-md">
                    Premium Media Command Centre
                  </div>
                  <h1 className="max-w-3xl text-5xl font-black tracking-tight text-white">Shelf browsing, cinematic discovery, and immersive carousel navigation.</h1>
                  <p className="mt-4 max-w-2xl text-base leading-7 text-slate-200/80">
                    TMDB metadata, Trakt progress, and Atlas-compatible sources unified into a premium media homepage with live shelves, expandable grids, and cinema mode.
                  </p>
                </div>
              )}
            </div>
          </section>

          <div className="relative z-10 flex flex-wrap items-center gap-3 px-8 pt-6 pb-2">
            <form
              className="flex min-w-[18rem] flex-1 items-center gap-3 rounded-full border border-white/12 bg-slate-950/55 px-4 py-2.5 backdrop-blur-xl"
              onSubmit={(event) => {
                event.preventDefault();
                runGlobalSearch(globalSearchQuery);
              }}
            >
              <span className="text-cyan-200/80">⌕</span>
              <input
                type="text"
                value={globalSearchQuery}
                onChange={(event) => setGlobalSearchQuery(event.target.value)}
                placeholder="Search titles, genres, cast, or year..."
                className="min-w-0 flex-1 bg-transparent text-sm text-white placeholder-slate-500 outline-none"
              />
              <button
                type="submit"
                className="rounded-full border border-cyan-300/25 bg-cyan-300/12 px-4 py-1.5 text-xs font-semibold text-cyan-100 transition hover:bg-cyan-300/20"
              >
                Search
              </button>
            </form>
            {serverFilterOptions.length > 0 ? (
              <div className="min-w-[13rem] rounded-full border border-white/12 bg-slate-950/55 p-1.5 backdrop-blur-xl">
                <FilterSelect value={selectedServerValue} options={serverFilterOptions} onChange={updateSelectedServer} showCopyButton />
              </div>
            ) : null}
            {homeShelves.length > 0 ? (
              <button
                type="button"
                className="rounded-full border border-white/15 bg-white/8 px-4 py-2.5 text-sm font-semibold text-white backdrop-blur-md transition hover:bg-white/14"
                onClick={() => enterCarousel()}
              >
                Carousel
              </button>
            ) : null}
          </div>

          <div className="relative z-10 px-8 pt-4">
            {status ? <div className="mb-6 text-sm text-slate-400">{status}</div> : null}
            <div className="space-y-12">
              {homeShelvesContent}
            </div>
          </div>
          </>
          )}
        </div>
      ) : null}

      {currentPage === "customize" ? (
        <div className="relative z-10 px-8 pb-16 pt-8">
          <div className="mx-auto max-w-[1240px] space-y-6">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <div className="text-xs uppercase tracking-[0.28em] text-cyan-200/70">Addon Manager</div>
                <h2 className="mt-2 text-4xl font-bold text-white">Stremio Addon Manager</h2>
                <p className="mt-3 max-w-3xl text-slate-300">
                  Create custom shelves by genre, collection, or any search query. Powered by TMDB discovery and your metadata sources.
                </p>
              </div>
              <button
                type="button"
                className="rounded-full border border-white/15 bg-white/8 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-white/14"
                onClick={backToHome}
              >
                Back to Home
              </button>
            </div>

            <div className="rounded-[24px] border border-white/10 bg-slate-950/40 px-5 py-5 backdrop-blur-xl">
              <h3 className="text-xl font-semibold text-white">Create a Shelf</h3>
              <p className="mt-2 text-sm text-slate-400">
                Type a genre, collection, or description — e.g. "90s horror", "Marvel", "best sci-fi", "Korean thrillers".
              </p>
              <form
                className="mt-4 flex items-center gap-3"
                onSubmit={(e) => {
                  e.preventDefault();
                  if (customShelfQuery.trim()) {
                    createCustomShelf(customShelfQuery.trim());
                    setCustomShelfQuery("");
                  }
                }}
              >
                <input
                  type="text"
                  value={customShelfQuery}
                  onChange={(e) => setCustomShelfQuery(e.target.value)}
                  placeholder="e.g. 90s horror, Marvel, Korean thrillers..."
                  className="flex-1 rounded-xl border border-white/15 bg-white/8 px-4 py-3 text-sm text-white placeholder-slate-500 outline-none transition focus:border-cyan-300/40 focus:bg-white/10"
                />
                <button
                  type="submit"
                  disabled={customShelfLoading || !customShelfQuery.trim()}
                  className="rounded-full border border-cyan-300/25 bg-cyan-300/12 px-5 py-3 text-sm font-semibold text-cyan-100 transition hover:bg-cyan-300/18 disabled:opacity-40"
                >
                  {customShelfLoading ? "Building..." : "Create Shelf"}
                </button>
              </form>
            </div>

            {customShelves.length > 0 && (
              <div className="space-y-4">
                <h3 className="text-xl font-semibold text-white">Your Custom Shelves</h3>
                {customShelves.map((entry) => (
                  <div key={entry.id} className="rounded-[24px] border border-white/10 bg-slate-950/40 px-5 py-5 backdrop-blur-xl">
                    <div className="flex items-center justify-between gap-4">
                      <div>
                        <h4 className="text-lg font-semibold text-white">{entry.title}</h4>
                        <p className="mt-1 text-sm text-slate-400">{entry.items.length} titles</p>
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          className="rounded-full border border-white/15 bg-white/8 px-3 py-2 text-sm font-semibold text-white transition hover:bg-white/14 disabled:opacity-30"
                          disabled={customShelves.indexOf(entry) === 0}
                          onClick={() => moveCustomShelf(entry.id, "up")}
                        >
                          ▲
                        </button>
                        <button
                          type="button"
                          className="rounded-full border border-white/15 bg-white/8 px-3 py-2 text-sm font-semibold text-white transition hover:bg-white/14 disabled:opacity-30"
                          disabled={customShelves.indexOf(entry) === customShelves.length - 1}
                          onClick={() => moveCustomShelf(entry.id, "down")}
                        >
                          ▼
                        </button>
                        <button
                          type="button"
                          className="rounded-full border border-red-400/25 bg-red-400/10 px-4 py-2 text-sm font-semibold text-red-200 transition hover:bg-red-400/20"
                          onClick={() => removeCustomShelf(entry.id)}
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                    <div
                      className="mt-3 flex gap-2 overflow-x-auto pb-2 scrollbar-hide [scrollbar-width:none]"
                      style={{ msOverflowStyle: 'none', overscrollBehavior: 'contain' }}
                      onWheelCapture={handleHorizontalWheel}
                    >
                      {entry.items.slice(0, 15).map((item, i) => (
                        <div key={item.id ?? i} className="flex-shrink-0">
                          <img
                            src={String(item.thumbnail ?? item.coverUrl ?? item.poster ?? "")}
                            alt={String(item.title ?? "")}
                            className="h-[160px] w-[107px] rounded-lg object-cover"
                            loading="lazy"
                          />
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {shelves.length > 0 && (
              <div className="rounded-[24px] border border-white/10 bg-slate-950/40 px-5 py-5 backdrop-blur-xl">
                <h3 className="text-xl font-semibold text-white">Manage Home Shelves</h3>
                <p className="mt-2 text-sm text-slate-400">
                  Drag shelves into place, remove stale custom shelves, and choose which shelves feed the carousel.
                </p>
                <div className="mt-4 space-y-2">
                  {orderedAllShelves.map((shelf) => {
                    const hidden = hiddenShelfIds.includes(shelf.id);
                    const shelfIndex = orderedAllShelves.findIndex((entry) => entry.id === shelf.id);
                    const inCarousel = carouselShelfIds.includes(shelf.id);
                    const isCustomShelf = isCustomShelfId(shelf.id);
                    const canDeleteShelf = !isCustomShelf && !isSyntheticShelfId(shelf.id);
                    return (
                      <div
                        key={shelf.id}
                        className={`flex items-center justify-between gap-3 rounded-xl border px-4 py-3 ${inCarousel ? "border-cyan-300/20 bg-cyan-300/6" : "border-white/8 bg-white/4"}`}
                        draggable
                        onDragStart={(event) => {
                          event.dataTransfer.effectAllowed = "move";
                          event.dataTransfer.setData("text/plain", shelf.id);
                        }}
                        onDragOver={(event) => {
                          event.preventDefault();
                          event.dataTransfer.dropEffect = "move";
                        }}
                        onDrop={(event) => {
                          event.preventDefault();
                          const draggedShelfId = event.dataTransfer.getData("text/plain");
                          reorderShelvesByDrop("all", draggedShelfId, shelf.id);
                        }}
                      >
                        <div className="min-w-0">
                          <span className={`text-sm font-medium ${hidden ? "text-slate-500 line-through" : "text-white"}`}>{shelf.title}</span>
                          <span className="ml-2 text-xs text-slate-500">{shelf.items.length} titles</span>
                          <span className="ml-2 text-xs text-slate-500">{isCustomShelf ? "Custom" : "Server"}</span>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className="rounded-full border border-white/10 bg-white/6 px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.22em] text-slate-300">
                            Drag
                          </span>
                          <button
                            type="button"
                            className="rounded-full border border-white/15 bg-white/8 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-white/14 disabled:opacity-30"
                            disabled={shelfIndex <= 0}
                            onClick={() => moveAddonShelf(shelf.id, "up")}
                          >
                            ▲
                          </button>
                          <button
                            type="button"
                            className="rounded-full border border-white/15 bg-white/8 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-white/14 disabled:opacity-30"
                            disabled={shelfIndex >= orderedAddonShelves.length - 1}
                            onClick={() => moveAddonShelf(shelf.id, "down")}
                          >
                            ▼
                          </button>
                          <button
                            type="button"
                            className={`rounded-full px-4 py-1.5 text-xs font-semibold transition ${hidden ? "border border-cyan-300/25 bg-cyan-300/10 text-cyan-100 hover:bg-cyan-300/18" : "border border-white/15 bg-white/8 text-white hover:bg-white/14"}`}
                            onClick={() => toggleShelfVisibility(shelf.id)}
                          >
                            {hidden ? "Show" : "Hide"}
                          </button>
                          <button
                            type="button"
                            className={`rounded-full px-4 py-1.5 text-xs font-semibold transition ${inCarousel ? "border border-cyan-300/25 bg-cyan-300/12 text-cyan-100 hover:bg-cyan-300/18" : "border border-white/15 bg-white/8 text-white hover:bg-white/14"}`}
                            onClick={() => toggleCarouselShelfSelection(shelf.id)}
                          >
                            {inCarousel ? "Carousel On" : "Carousel Off"}
                          </button>
                          {isCustomShelf ? (
                            <button
                              type="button"
                              className="rounded-full border border-red-400/25 bg-red-400/10 px-4 py-1.5 text-xs font-semibold text-red-200 transition hover:bg-red-400/20"
                              onClick={() => removeCustomShelf(shelf.id)}
                            >
                              Delete
                            </button>
                          ) : null}
                          {canDeleteShelf ? (
                            <button
                              type="button"
                              className="rounded-full border border-red-400/25 bg-red-400/10 px-4 py-1.5 text-xs font-semibold text-red-200 transition hover:bg-red-400/20"
                              onClick={() => deleteAddonShelf(shelf.id)}
                            >
                              Delete
                            </button>
                          ) : null}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {deletedAddonShelves.length > 0 ? (
              <div className="rounded-[24px] border border-white/10 bg-slate-950/40 px-5 py-5 backdrop-blur-xl">
                <h3 className="text-xl font-semibold text-white">Deleted Addon Shelves</h3>
                <p className="mt-2 text-sm text-slate-400">
                  Restore shelves you deleted from the server homepage.
                </p>
                <div className="mt-4 space-y-2">
                  {deletedAddonShelves.map((shelf) => (
                    <div key={`deleted-${shelf.id}`} className="flex items-center justify-between gap-3 rounded-xl border border-white/8 bg-white/4 px-4 py-3">
                      <div className="min-w-0">
                        <span className="text-sm font-medium text-slate-300">{shelf.title}</span>
                        <span className="ml-2 text-xs text-slate-500">{shelf.items.length} titles</span>
                      </div>
                      <button
                        type="button"
                        className="rounded-full border border-cyan-300/25 bg-cyan-300/10 px-4 py-1.5 text-xs font-semibold text-cyan-100 transition hover:bg-cyan-300/18"
                        onClick={() => restoreAddonShelf(shelf.id)}
                      >
                        Restore
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            <div className="rounded-[24px] border border-white/10 bg-slate-950/40 px-5 py-5 backdrop-blur-xl">
              <h3 className="text-xl font-semibold text-white">Reload Homepage Shelves</h3>
              <p className="mt-2 text-sm text-slate-400">Refresh addon catalog shelves and regenerate the homepage using live server metadata.</p>
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <button
                  type="button"
                  className="rounded-full border border-cyan-300/25 bg-cyan-300/12 px-4 py-2.5 text-sm font-semibold text-cyan-100 transition hover:bg-cyan-300/18"
                  onClick={refreshHomeShelves}
                >
                  Reload Shelves
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : null}

      {currentPage === "grid" && selectedShelf ? (
        <>
        <div className="relative z-10 px-8 pb-16 pt-3">
          <div className="sticky top-2 z-40 -mx-8 mb-5 bg-[linear-gradient(180deg,rgba(2,6,23,0.99)_0%,rgba(2,6,23,0.92)_72%,rgba(2,6,23,0)_100%)] px-8 pb-4 pt-3">
            <div className="rounded-[22px] border border-cyan-300/12 bg-[linear-gradient(180deg,rgba(2,6,23,0.98)_0%,rgba(2,6,23,0.9)_100%)] p-1.5 shadow-[0_20px_44px_rgba(2,6,23,0.42)] backdrop-blur-xl">
              <div className="grid gap-3 overflow-visible rounded-[20px] bg-slate-950/82 p-2.5 md:grid-cols-[auto_auto_minmax(220px,1.15fr)_1fr_1fr_1fr]">
                <button
                  type="button"
                  className="rounded-xl border border-white/10 bg-white/6 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-white/10"
                  onClick={backToHome}
                >
                  Back to Home
                </button>
                <button
                  type="button"
                  className="rounded-xl border border-cyan-300/20 bg-cyan-300/10 px-4 py-2.5 text-sm font-semibold text-cyan-100 transition hover:bg-cyan-300/16"
                  onClick={() => enterCarousel(selectedShelf?.id ?? effectiveGridShelf?.id ?? "")}
                >
                  3D Carousel
                </button>
                <FilterSelect value={selectedServerValue} options={serverFilterOptions} onChange={updateSelectedServer} showCopyButton />
                <form
                  className="flex items-center gap-3 rounded-xl border border-white/10 bg-white/6 px-4 py-2.5"
                  onSubmit={(event) => {
                    event.preventDefault();
                    runGlobalSearch(globalSearchQuery);
                  }}
                >
                  <span className="text-cyan-200/80">⌕</span>
                  <input
                    type="text"
                    value={globalSearchQuery}
                    onChange={(event) => setGlobalSearchQuery(event.target.value)}
                    placeholder="Search titles..."
                    className="min-w-[180px] flex-1 bg-transparent text-sm text-white placeholder-slate-500 outline-none"
                  />
                </form>
                <FilterSelect value={gridGenre} options={gridGenreOptions} onChange={setGridGenre} />
                <FilterSelect value={gridType} options={gridTypeOptions} onChange={(value) => updateGridType(value as "all" | "movie" | "tv")} />
                <label className="flex items-center gap-3 rounded-xl border border-white/10 bg-white/6 px-4 py-2.5 text-sm text-white">
                  <span>Min rating</span>
                  <input type="range" min={0} max={10} step={0.5} value={minimumRating} onChange={(event) => setMinimumRating(Number(event.target.value))} className="w-full accent-cyan-300" />
                  <span>{minimumRating.toFixed(1)}</span>
                </label>
              </div>
            </div>
          </div>

          <div className="mb-8 flex flex-wrap items-center justify-between gap-4">
            <div>
              <h2 className="text-4xl font-bold text-white">{activeGridShelf?.title ?? selectedShelf.title}</h2>
              <p className="mt-2 text-slate-400">{filteredGridItems.length} real titles loaded for this view.</p>
            </div>
          </div>

          {gridPreviewMovie ? (
            <div className="mb-8 overflow-hidden rounded-[28px] border border-white/10 bg-slate-950/45 backdrop-blur-xl">
              <div className="grid gap-6 px-6 py-6 md:grid-cols-[220px_1fr]">
                <div className="overflow-hidden rounded-[22px] border border-white/10 bg-slate-900/70">
                  {gridPreviewMovie.posterUrl || gridPreviewMovie.backdropUrl ? (
                    <img
                      src={gridPreviewMovie.posterUrl || gridPreviewMovie.backdropUrl}
                      alt={gridPreviewMovie.title}
                      className="h-full min-h-[300px] w-full object-cover"
                    />
                  ) : (
                    <div className="flex min-h-[300px] items-center justify-center bg-[radial-gradient(circle_at_top,_rgba(34,211,238,0.18),_transparent_28%),linear-gradient(180deg,rgba(15,23,42,0.95)_0%,rgba(2,6,23,1)_100%)] px-6 text-center text-sm text-slate-400">
                      No poster available yet
                    </div>
                  )}
                </div>

                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-3 text-xs uppercase tracking-[0.26em] text-cyan-200/75">
                    <span>{gridPreviewMovie.type === "tv" ? "TV" : "Movie"}</span>
                    {gridPreviewMovie.year ? <span>{gridPreviewMovie.year}</span> : null}
                    {gridPreviewMovie.runtime ? <span>{gridPreviewMovie.runtime}</span> : null}
                  </div>
                  <h3 className="mt-3 text-3xl font-bold text-white">{gridPreviewMovie.title}</h3>
                  <div className="mt-4 flex flex-wrap gap-2">
                    {gridPreviewMovie.ratingBadges.length > 0 ? gridPreviewMovie.ratingBadges.map((badge) => (
                      <span key={`grid-preview-${badge.key}`} className={`rounded-full border px-3 py-1 text-xs font-semibold ${badge.accentClassName}`}>
                        {badge.label} {badge.value}
                      </span>
                    )) : null}
                    {gridPreviewMovie.genres.slice(0, 4).map((genre) => (
                      <span key={`grid-preview-genre-${genre}`} className="rounded-full border border-white/10 bg-white/6 px-3 py-1 text-xs text-slate-200">
                        {genre}
                      </span>
                    ))}
                  </div>
                  <p className="mt-5 max-w-4xl text-sm leading-7 text-slate-200/84">
                    {gridPreviewMovie.description
                      || [
                        gridPreviewMovie.director ? `Directed by ${gridPreviewMovie.director}` : "",
                        (gridPreviewMovie.actors ?? []).slice(0, 3).map((actor) => actor.name).filter(Boolean).join(" • "),
                      ].filter(Boolean).join(" • ")
                      || gridPreviewMovie.genres.join(" • ")
                      || "Summary unavailable from provider metadata."}
                  </p>
                  <div className="mt-5 flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      className="rounded-full bg-gradient-to-r from-cyan-500 to-blue-500 px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_24px_rgba(6,182,212,0.35)] transition hover:scale-[1.02]"
                      onClick={() => openMovie(gridPreviewMovie)}
                    >
                      Play
                    </button>
                    <button
                      type="button"
                      className="rounded-full border border-white/15 bg-white/8 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-white/14"
                      onClick={() => playTrailer(gridPreviewMovie)}
                    >
                      Trailer
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ) : null}

          {filteredGridItems.length === 0 ? (
            <div className="rounded-[28px] border border-white/10 bg-slate-950/45 px-8 py-12 text-center backdrop-blur-xl">
              <h3 className="text-2xl font-semibold text-white">No titles match this filter</h3>
              <p className="mt-3 text-slate-400">Try switching back to All Types or choosing a different shelf.</p>
            </div>
          ) : (
            <div className="grid gap-x-5 gap-y-8" style={{ gridTemplateColumns: `repeat(${gridColumnCount}, minmax(0, 1fr))` }}>
              {visibleGridItems.map((movie, index) => (
                <MediaPosterCard
                  key={movie.id}
                  movie={movie}
                  preload={index < 10}
                  inGrid
                  isSelected={armedMovieKey === mediaKey(movie)}
                  isWatchlisted={watchlist.includes(movie.id)}
                  onSelect={selectMovieCard}
                  onOpen={openMovieFromShelf}
                  onPlay={(m) => openMovie(m)}
                  onPlayTrailer={(m) => playTrailer(m)}
                  onWatchlistToggle={toggleWatchlist}
                />
              ))}
            </div>
          )}

          {shouldShowGridLoadMore ? (
            <div className="mt-8 flex flex-col items-center justify-center gap-3">
              <div ref={gridLoadTriggerRef} className="h-10 w-full" />
              <button
                type="button"
                className="rounded-full border border-white/15 bg-white/8 px-5 py-3 text-sm font-semibold text-white backdrop-blur-md transition hover:bg-white/14 disabled:cursor-not-allowed disabled:opacity-50"
                disabled={isBridgeGrid ? Boolean(serversState?.isBusy) || bridgeLoadMoreStalled || !serversState?.canLoadMore : visibleCount >= filteredGridItems.length}
                onClick={() => {
                  if (isBridgeGrid) {
                    requestBridgeLoadMore();
                    return;
                  }

                  setVisibleCount((current) => Math.min(current + 36, filteredGridItems.length));
                }}
              >
                {isBridgeGrid
                  ? (serversState?.isBusy ? "Loading more titles..." : bridgeLoadMoreStalled ? "Server repeated the last page" : "Load More Titles")
                  : "Load More Titles"}
              </button>
              <div className="text-sm text-slate-400">
                {isBridgeGrid
                  ? (serversState?.isBusy
                    ? "Fetching the next server page..."
                    : bridgeLoadMoreStalled
                      ? "Atlas stopped auto-paging because the addon repeated the same page. Change shelf/type or refresh to continue."
                    : serversState?.canLoadMore
                      ? "More titles will load automatically near the bottom, or use the button now."
                      : "Reached the end. The footer and auto-scroll still use the same load-more path as the top button.")
                  : "More titles will load automatically near the bottom, or use the button now."}
              </div>
            </div>
          ) : null}
        </div>
        </>
      ) : null}

      {currentPage === "info" && selectedMovie ? (
        <MovieInfoPage
          key={selectedMovie.id}
          backdropUrl={selectedMovie.backdropUrl || selectedMovie.posterUrl}
          posterUrl={selectedMovie.posterUrl}
          title={selectedMovie.title}
          year={selectedMovie.year}
          runtime={selectedMovie.runtime}
          rating={selectedMovie.rating}
          aiRating={selectedMovie.aiRating}
          ratingBadges={effectiveSelectedMovieBadges}
          infoCards={streamInfoCards}
          aiInsight={aiDetailResult?.title === selectedMovie.title ? aiDetailResult.content : ""}
          genres={selectedMovie.genres}
          description={selectedMovie.description}
          director={selectedMovie.director}
          actors={selectedMovie.actors}
          trailerUrl={selectedMovie.trailerUrl}
          streamSources={streamSources as any}
          autoPlaySources={autoPlaySources}
          onToggleAutoPlaySources={() => {
            autoPlayedSourceKeyRef.current = "";
            setAutoPlaySources((current) => !current);
          }}
          streamsStatusText={(activeStreamsState?.statusText ?? "").trim()}
          isLoadingSources={Boolean(activeStreamsState?.isBusy)}
          isSeries={selectedMovie.type === "tv"}
          seriesSeasons={seriesState?.seasons ?? []}
          seriesEpisodes={seriesState?.episodes ?? []}
          seriesSelectedSeason={seriesState?.selectedSeason ?? 0}
          isSeriesBusy={Boolean(seriesState?.isBusy)}
          seriesStatusText={seriesState?.statusText ?? ""}
          onBack={onBack}
          onPlay={onPlay}
          onPlayFirst={() => {
            if (streamSources.length > 0) onPlay(streamSources[0] as any);
          }}
          onCopySourceLink={onCopySourceLink}
          onPlayTrailer={() => playTrailer(selectedMovie)}
          onAskAi={() => askAtlasAi(selectedMovie, "overview")}
          onSelectEpisode={selectEpisode}
          onSelectSeason={selectSeason}
        />
      ) : null}

      {cinemaMode ? (
        <div
          className="fixed inset-0 z-50 overflow-hidden bg-[radial-gradient(circle_at_center,_rgba(15,23,42,0.7),_rgba(2,6,23,0.96)_58%,_rgba(1,4,9,1)_100%)] backdrop-blur-md"
        >
          <div className="absolute inset-0 opacity-45">
            <img src={(focusedCarouselDetails?.backdropUrl || focusedCarouselDetails?.posterUrl) ?? ""} alt="" className="h-full w-full object-cover" />
          </div>
          <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(2,6,23,0.2)_0%,rgba(2,6,23,0.7)_45%,rgba(2,6,23,0.98)_100%)]" />

          <div className="relative z-10 mx-auto flex h-full w-full max-w-[1540px] flex-col px-6 pb-[220px] pt-1">
            <div className="relative z-[90] flex flex-wrap items-start justify-between gap-2 border-b border-white/10 pb-1">
              <div className="min-w-0">
                <div className="text-[10px] uppercase tracking-[0.3em] text-cyan-200/70">Carousel</div>
                <h2 className="mt-1 text-2xl font-bold text-white">{carouselTitle}</h2>
                <p className="mt-1 text-xs text-slate-300">Use the mouse wheel or arrow keys to move through the carousel. Enter opens the focused title. Escape closes.</p>
              </div>
              <div className="flex flex-wrap items-center justify-end gap-2 text-xs">
                {carouselShelfOptions.length > 0 ? (
                  <div className="min-w-[12rem] max-w-[15rem] rounded-[18px] border border-white/10 bg-slate-950/58 p-1 backdrop-blur-xl">
                    <FilterSelect
                      value={selectedCarouselShelfId}
                      options={carouselShelfOptions}
                      onChange={(value) => {
                        setSelectedShelfId(value);
                        setCarouselAngle(0);
                        setCarouselVelocity(0);
                      }}
                    />
                  </div>
                ) : null}
                <div className="min-w-[9rem] max-w-[11rem] rounded-[18px] border border-white/10 bg-slate-950/58 p-1 backdrop-blur-xl">
                  <FilterSelect
                    value={carouselType}
                    options={[
                      { value: "movie", label: "Movies" },
                      { value: "tv", label: "TV Shows" },
                      { value: "all", label: "All" },
                    ]}
                    onChange={(value) => {
                      setCarouselType(value as "all" | "movie" | "tv");
                      setCarouselAngle(0);
                      setCarouselVelocity(0);
                    }}
                  />
                </div>
                <button
                  type="button"
                  className={`rounded-full border px-3 py-1.5 text-xs font-semibold backdrop-blur-md transition ${autoplayCarousel ? "border-cyan-300/30 bg-cyan-300/12 text-cyan-100" : "border-white/15 bg-white/8 text-white hover:bg-white/14"}`}
                  onClick={() => {
                    setAutoplayCarousel((current) => {
                      return !current;
                    });
                  }}
                >
                  Auto {autoplayCarousel ? "On" : "Off"}
                </button>
                <label className="flex items-center gap-2 text-[11px] text-slate-300">
                  <span>Speed {carouselAutoplaySpeed.toFixed(2)}</span>
                  <input
                    type="range"
                    min={0.01}
                    max={0.06}
                    step={0.005}
                    value={carouselAutoplaySpeed}
                    onChange={(event) => setCarouselAutoplaySpeed(Number(event.target.value))}
                    className="w-20 accent-cyan-300"
                    aria-label="Carousel speed"
                  />
                </label>
                <button
                  type="button"
                  aria-label="Exit Carousel"
                  className="rounded-full border border-white/15 bg-slate-950/72 px-3 py-1.5 text-xs font-semibold text-white shadow-[0_12px_30px_rgba(0,0,0,0.28)] backdrop-blur-xl transition hover:bg-slate-900/84"
                  onClick={() => setCinemaMode(false)}
                >
                  Close
                </button>
              </div>
            </div>
            <div
              ref={carouselStageRef}
              tabIndex={0}
              className="relative z-10 mt-0 flex-1 overflow-visible px-1 pt-0 outline-none"
              onWheel={(event) => {
                if (carouselItems.length < 2) {
                  return;
                }

                event.preventDefault();
                const sourceDelta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
                const inject = Math.max(-0.038, Math.min(0.038, -sourceDelta * 0.00035));
                nudgeCarousel(inject);
              }}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  setCinemaMode(false);
                  return;
                }

                if (event.key === "ArrowRight") {
                  event.preventDefault();
                  nudgeCarousel(-0.05);
                  return;
                }

                if (event.key === "ArrowLeft") {
                  event.preventDefault();
                  nudgeCarousel(0.05);
                  return;
                }

                if (event.key === "Enter") {
                  const focused = carouselItems[focusedCarouselIndex];
                  if (focused) {
                    event.preventDefault();
                    openMovie(focused);
                  }
                }
              }}
            >
              <div className="pointer-events-none absolute inset-x-24 top-2 h-20 rounded-full bg-cyan-300/8 blur-3xl" />
              <div className="relative mx-auto h-[340px] max-w-[1440px] overflow-visible" style={{ perspective: "2200px" }}>
                <div className="pointer-events-none absolute inset-x-24 bottom-8 h-16 rounded-full bg-cyan-300/9 blur-3xl" />
                <div className="pointer-events-none absolute inset-x-20 bottom-6 h-px bg-gradient-to-r from-transparent via-cyan-200/45 to-transparent" />
                <div className="absolute inset-0">
                  {renderedCarouselEntries.map(({ movie, index, signedDistance }) => {
                    const clampedDistance = Math.max(-3.4, Math.min(3.4, signedDistance));
                    const distanceAbs = Math.abs(clampedDistance);
                    const direction = clampedDistance === 0 ? 0 : clampedDistance > 0 ? 1 : -1;
                    const laneDistance = Math.min(3, distanceAbs);
                    const focusRatio = Math.max(0, 1 - distanceAbs / 4.8);
                    const isFocused = index === focusedCarouselIndex;
                    const translateXBase = laneDistance <= 1
                      ? laneDistance * 258
                      : laneDistance <= 2
                        ? 258 + (laneDistance - 1) * 212
                        : 470 + (laneDistance - 2) * 152;
                    const translateX = direction * translateXBase;
                    const translateY = isFocused ? -44 : laneDistance <= 1 ? -10 : laneDistance <= 2 ? 18 : 46;
                    const translateZ = isFocused ? 128 : laneDistance <= 1 ? 12 : laneDistance <= 2 ? -112 : -228;
                    const rotateY = direction === 0 ? 0 : -direction * (laneDistance <= 1 ? 20 : laneDistance <= 2 ? 26 : 31);
                    const scale = laneDistance < 0.001
                      ? 1
                      : laneDistance <= 1
                        ? 1 - laneDistance * 0.15
                        : laneDistance <= 2
                          ? 0.85 - (laneDistance - 1) * 0.2
                          : Math.max(0.55, 0.65 - (laneDistance - 2) * 0.1);
                    const opacity = distanceAbs > 3.15 ? 0 : Math.max(0.14, 1 - laneDistance * 0.2);
                    const zIndex = Math.round(focusRatio * 1000);
                    const blur = laneDistance <= 1.05 ? 0 : Math.min(2.8, (laneDistance - 1) * 1.5);

                    return (
                      <div
                        key={movie.id}
                        className="absolute left-1/2 top-1/2 will-change-transform"
                        style={{
                          transformStyle: "preserve-3d",
                          marginLeft: "-114px",
                          marginTop: "-171px",
                          transform: `translate3d(${translateX}px, ${translateY}px, ${translateZ}px) rotateY(${rotateY}deg) scale(${scale})`,
                          opacity,
                          zIndex,
                          filter: `brightness(${0.76 + focusRatio * 0.28}) saturate(${0.9 + focusRatio * 0.24}) blur(${blur}px)`,
                          backfaceVisibility: "hidden",
                          contain: "layout style paint",
                          pointerEvents: opacity > 0.2 ? "auto" : "none",
                        }}
                      >
                        <div className="relative">
                          <MediaPosterCard
                            movie={movie}
                            inCarousel
                            isFocused={isFocused}
                            isWatchlisted={watchlist.includes(movie.id)}
                            onActivate={() => {
                              focusCarouselStage();

                              if (isFocused) {
                                openMovie(movie);
                                return;
                              }

                              snapCarouselToIndex(index);
                            }}
                            onOpen={openMovie}
                            onWatchlistToggle={toggleWatchlist}
                          />
                          <div className="pointer-events-none absolute left-6 right-6 top-full h-12 origin-top scale-y-[-1] overflow-hidden rounded-[20px] opacity-18 blur-[2px]">
                            <img src={movie.posterUrl || movie.backdropUrl} alt="" className="h-full w-full object-contain object-center" />
                            <div className="absolute inset-0 bg-gradient-to-b from-slate-950/15 to-slate-950/95" />
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
                {carouselItems.length === 0 ? (
                  <div className="absolute inset-x-0 top-1/2 mx-auto w-fit -translate-y-1/2 rounded-[24px] border border-white/10 bg-slate-950/55 px-6 py-10 text-center backdrop-blur-xl">
                    <div className="text-lg font-semibold text-white">No titles available for this filter yet</div>
                    <div className="mt-2 text-sm text-slate-400">Switch Movies or All to keep browsing while the host loads TV metadata.</div>
                  </div>
                ) : null}
              </div>
              <div className="-mt-2 flex items-center justify-between gap-3 px-2 text-[11px] text-slate-400">
                <button
                  type="button"
                  className="rounded-full border border-white/10 bg-black/28 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-white/10"
                  onClick={() => {
                    focusCarouselStage();
                    nudgeCarousel(0.05);
                  }}
                >
                  Prev
                </button>
                <div className="text-center uppercase tracking-[0.24em] text-slate-400">
                  {focusedCarouselIndex + 1} / {carouselItems.length}
                </div>
                <button
                  type="button"
                  className="rounded-full border border-white/10 bg-black/28 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-white/10"
                  onClick={() => {
                    focusCarouselStage();
                    nudgeCarousel(-0.05);
                  }}
                >
                  Next
                </button>
              </div>
            </div>

            <div className="absolute inset-x-6 bottom-4 z-20 rounded-[18px] border border-white/10 bg-[linear-gradient(180deg,rgba(2,6,23,0.92),rgba(7,12,24,0.82))] px-4 py-3 shadow-[0_18px_50px_rgba(0,0,0,0.34)] backdrop-blur-xl">
              <div className="flex items-start justify-between gap-4">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <h3 className="truncate text-[1.3rem] font-bold text-white">{focusedCarouselDetails?.title}</h3>
                    <span className="text-xs text-slate-300/85">
                      {[
                        focusedCarouselDetails?.type ? (focusedCarouselDetails.type === "tv" ? "TV" : "Movie") : "",
                        focusedCarouselDetails?.year,
                        focusedCarouselDetails?.runtime,
                        ...(focusedCarouselDetails?.genres ?? []).slice(0, 2),
                      ].filter(Boolean).join(" • ")}
                    </span>
                  </div>
                  <p className="mt-1 line-clamp-2 text-sm leading-6 text-slate-200/88">{focusedCarouselSummary}</p>
                  <div className="mt-1 text-[11px] text-slate-400">
                    {[
                      focusedCarouselDetails?.director ? `Director: ${focusedCarouselDetails.director}` : "",
                      focusedCarouselDetails?.rating ? `Rating ${focusedCarouselDetails.rating.toFixed(1)}` : "",
                      focusedCarouselRatingLine,
                    ].filter(Boolean).join(" • ")}
                  </div>
                </div>
                <div className="shrink-0">
                  <div className="flex flex-wrap items-center justify-end gap-2">
                    <button
                      type="button"
                      className="rounded-full bg-gradient-to-r from-cyan-500 to-blue-500 px-4 py-2 text-xs font-semibold text-white shadow-[0_10px_20px_rgba(6,182,212,0.3)] transition hover:scale-[1.02]"
                      onClick={() => focusedCarouselDetails && openMovie(focusedCarouselDetails)}
                    >
                      Play
                    </button>
                    <button
                      type="button"
                      className="rounded-full border border-white/15 bg-white/8 px-4 py-2 text-xs font-semibold text-white transition hover:bg-white/14"
                      onClick={() => focusedCarouselDetails && playTrailer(focusedCarouselDetails)}
                    >
                      Trailer
                    </button>
                    {(focusedCarouselDetails?.ratingBadges ?? []).slice(0, 1).map((badge) => (
                      <span
                        key={`carousel-detail-badge-${badge.key}`}
                        className={`rounded-full border px-3 py-1 text-xs font-semibold ${badge.accentClassName}`}
                      >
                        {badge.label} {badge.value}
                      </span>
                    ))}
                  </div>
                </div>
              </div>
            </div>
        </div>
        </div>
      ) : null}
    </div>
  );
}

