import { useState } from 'react';
import { StreamSourceCard } from './StreamSourceCard';

interface Actor {
  name: string;
  character: string;
  imageUrl: string;
}

interface StreamSource {
  sourceName: string;
  providerName?: string;
  copyableLink?: string;
  quality: string;
  fileSize: string;
  audioLanguage: string;
  subtitles: string[];
  seederCount?: number;
}

interface RatingBadge {
  key: string;
  label: string;
  value: string;
  accentClassName: string;
}

interface InfoCard {
  id: string;
  title: string;
  providerName: string;
  description: string;
  metadata?: Record<string, string>;
}

interface SeriesEpisode {
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
}

interface SeriesSeason {
  seasonNumber: number;
  label: string;
}

interface MovieInfoPageProps {
  backdropUrl: string;
  posterUrl: string;
  title: string;
  year: string;
  runtime: string;
  rating: number;
  aiRating: number;
  ratingBadges: RatingBadge[];
  genres: string[];
  description: string;
  director: string;
  actors: Actor[];
  trailerUrl?: string;
  streamSources: StreamSource[];
  autoPlaySources?: boolean;
  infoCards?: InfoCard[];
  aiInsight?: string;
  streamsStatusText?: string;
  isLoadingSources?: boolean;
  isSeries?: boolean;
  seriesSeasons?: SeriesSeason[];
  seriesEpisodes?: SeriesEpisode[];
  seriesSelectedSeason?: number;
  isSeriesBusy?: boolean;
  seriesStatusText?: string;
  onBack: () => void;
  onPlay: (source: StreamSource) => void;
  onPlayFirst?: () => void;
  onCopySourceLink?: (source: StreamSource) => void;
  onToggleAutoPlaySources?: () => void;
  onPlayTrailer: () => void;
  onAskAi?: () => void;
  onSelectEpisode?: (episode: SeriesEpisode) => void;
  onSelectSeason?: (seasonNumber: number) => void;
}

export function MovieInfoPage({
  backdropUrl,
  posterUrl,
  title,
  year,
  runtime,
  rating,
  aiRating,
  ratingBadges,
  genres,
  description,
  director,
  actors,
  streamSources,
  autoPlaySources,
  infoCards = [],
  aiInsight,
  streamsStatusText,
  isLoadingSources,
  isSeries,
  seriesSeasons = [],
  seriesEpisodes = [],
  seriesSelectedSeason = 0,
  isSeriesBusy,
  seriesStatusText,
  onBack,
  onPlay,
  onPlayFirst,
  onCopySourceLink,
  onToggleAutoPlaySources,
  onPlayTrailer,
  onAskAi,
  onSelectEpisode,
  onSelectSeason,
}: MovieInfoPageProps) {
  const [selectedEpisodeId, setSelectedEpisodeId] = useState<string | null>(null);

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950">
      <button
        onClick={onBack}
        className="absolute top-6 left-6 z-10 px-4 py-2 bg-black/50 backdrop-blur-md rounded-lg text-white hover:bg-black/70 transition-all duration-200 flex items-center gap-2"
      >
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M12 4L6 10L12 16" stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        Back
      </button>

      <div className="relative w-full" style={{ height: '60vh' }}>
        {backdropUrl ? (
          <img src={backdropUrl} alt={title} className="w-full h-full object-cover" onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }} />
        ) : null}
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/60 to-transparent" />
      </div>

      <div className="relative -mt-40 px-12 pb-12">
        <div className="flex gap-8">
          <div className="flex-shrink-0">
            {posterUrl ? (
              <img
                src={posterUrl}
                alt={title}
                className="w-64 rounded-xl shadow-2xl"
                style={{ aspectRatio: '2/3' }}
                onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
              />
            ) : null}
          </div>

          <div className="flex-1 min-w-0 overflow-hidden">
            <h1 className="text-5xl font-bold text-white mb-4 break-words">{title}</h1>

            <div className="flex items-center gap-6 mb-4">
              <span className="text-gray-300 text-lg">{year}</span>
              <span className="text-gray-400">•</span>
              <span className="text-gray-300 text-lg">{runtime}</span>
            </div>

            {ratingBadges.length > 0 ? (
              <div className="mb-4 flex flex-wrap gap-2">
                {ratingBadges.map((badge) => (
                  <span
                    key={badge.key}
                    className={`rounded-full border px-3 py-1 text-xs font-semibold backdrop-blur-md ${badge.accentClassName}`}
                  >
                    {badge.label} {badge.value}
                  </span>
                ))}
              </div>
            ) : null}

            <div className="flex gap-2 mb-6">
              {genres.map((genre, index) => (
                <span
                  key={index}
                  className="px-4 py-1.5 bg-white/10 backdrop-blur-sm rounded-full text-sm text-gray-200 border border-white/20"
                >
                  {genre}
                </span>
              ))}
            </div>

            <p className="text-gray-300 text-lg mb-6 leading-relaxed">{description}</p>

            <div className="flex items-center gap-3 mb-8">
              <button
                type="button"
                onClick={() => {
                  if (streamSources.length > 0) {
                    onPlay(streamSources[0]);
                  } else if (onPlayFirst) {
                    onPlayFirst();
                  }
                }}
                disabled={isLoadingSources && streamSources.length === 0}
                className="flex items-center gap-2.5 rounded-full bg-gradient-to-r from-purple-600 to-blue-500 px-7 py-3 text-base font-bold text-white shadow-lg shadow-purple-500/30 transition hover:scale-105 hover:shadow-purple-500/50 disabled:opacity-50 disabled:hover:scale-100"
              >
                <svg width="18" height="18" viewBox="0 0 16 16" fill="white" xmlns="http://www.w3.org/2000/svg">
                  <path d="M3 2L13 8L3 14V2Z" />
                </svg>
                {isLoadingSources && streamSources.length === 0 ? 'Loading…' : 'Play'}
              </button>
              <button
                type="button"
                onClick={onPlayTrailer}
                className="flex items-center gap-2 rounded-full border border-white/20 bg-white/10 px-6 py-3 text-base font-semibold text-white backdrop-blur-md transition hover:bg-white/20"
              >
                <svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
                  <rect x="1" y="3" width="14" height="10" rx="2" stroke="white" strokeWidth="1.5" />
                  <path d="M6.5 6L10 8L6.5 10V6Z" fill="white" />
                </svg>
                Trailer
              </button>
            </div>

            <div className="mb-8 rounded-2xl border border-cyan-300/15 bg-cyan-300/8 p-5 backdrop-blur-md">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <div className="text-xs uppercase tracking-[0.24em] text-cyan-200/75">Atlas AI</div>
                  <div className="mt-2 text-sm leading-7 text-slate-200">
                    {aiInsight || "Ask Atlas to summarize this title using your connected TMDb-backed metadata source."}
                  </div>
                </div>
                {onAskAi ? (
                  <button
                    type="button"
                    onClick={onAskAi}
                    className="rounded-full border border-cyan-300/25 bg-cyan-300/12 px-4 py-2 text-sm font-semibold text-cyan-100 transition hover:bg-cyan-300/20"
                  >
                    Ask Atlas
                  </button>
                ) : null}
              </div>
            </div>

            <div className="mb-8">
              <p className="text-gray-400 mb-1">Director</p>
              <p className="text-white text-lg font-medium">{director}</p>
            </div>

            <div className="mb-8">
              <h3 className="text-white text-xl font-semibold mb-4">Cast</h3>
              <div className="flex gap-4 overflow-x-auto pb-4 scrollbar-hide">
                {actors.filter((a) => a.name).map((actor, index) => (
                  <div key={index} className="flex-shrink-0 text-center" style={{ width: '120px' }}>
                    {actor.imageUrl ? (
                      <img
                        src={actor.imageUrl}
                        alt={actor.name}
                        className="w-full h-32 object-cover rounded-lg mb-2"
                        onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                      />
                    ) : null}
                    <p className="text-white text-sm font-medium">{actor.name}</p>
                    <p className="text-gray-400 text-xs">{actor.character}</p>
                  </div>
                ))}
              </div>
            </div>

            {isSeries ? (
              <div className="mb-8">
                <h3 className="text-white text-xl font-semibold mb-4">
                  {isSeriesBusy ? "Loading Episodes..." : `Seasons & Episodes`}
                </h3>

                {seriesSeasons.length > 1 ? (
                  <div className="relative inline-flex mb-4">
                    <select
                      value={seriesSelectedSeason}
                      onChange={(e) => onSelectSeason?.(Number(e.target.value))}
                      className="appearance-none rounded-full border border-white/15 bg-white/8 px-5 py-2.5 pr-10 text-sm font-semibold text-white backdrop-blur-md cursor-pointer hover:bg-white/14 transition focus:outline-none focus:border-cyan-300/40"
                    >
                      {seriesSeasons.map((s) => (
                        <option key={s.seasonNumber} value={s.seasonNumber} className="bg-slate-900 text-white">
                          {s.label}
                        </option>
                      ))}
                    </select>
                    <div className="pointer-events-none absolute inset-y-0 right-3 flex items-center">
                      <svg width="12" height="12" viewBox="0 0 12 12" fill="none"><path d="M3 4.5L6 7.5L9 4.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" className="text-slate-300" /></svg>
                    </div>
                  </div>
                ) : null}

                {seriesStatusText && !isSeriesBusy ? (
                  <div className="mb-3 text-xs text-slate-400">{seriesStatusText}</div>
                ) : null}

                {isSeriesBusy ? (
                  <div className="rounded-2xl border border-white/10 bg-white/5 p-5 text-sm text-slate-300 backdrop-blur-md">
                    <div className="flex items-center gap-3">
                      <div className="w-2 h-2 bg-cyan-400 rounded-full animate-pulse" />
                      <span>{seriesStatusText || "Loading episodes from addon servers..."}</span>
                    </div>
                  </div>
                ) : seriesEpisodes.length === 0 ? (
                  <div className="rounded-2xl border border-white/10 bg-white/5 p-5 text-sm text-slate-300 backdrop-blur-md">
                    No episodes found for this series.
                  </div>
                ) : (
                  <div className="grid grid-cols-1 gap-2 max-w-4xl max-h-[480px] overflow-y-auto pr-1">
                    {seriesEpisodes.map((ep) => (
                      <button
                        key={`${ep.season}-${ep.episode}-${ep.id}`}
                        type="button"
                        className={`w-full text-left rounded-2xl border p-3 backdrop-blur-md transition hover:bg-white/10 ${
                          selectedEpisodeId === ep.id
                            ? "border-cyan-300/40 bg-cyan-300/10"
                            : "border-white/10 bg-white/5"
                        }`}
                        onClick={() => {
                          setSelectedEpisodeId(ep.id);
                          onSelectEpisode?.(ep);
                        }}
                      >
                        <div className="flex items-start gap-3">
                          {ep.thumbnail ? (
                            <img
                              src={ep.thumbnail}
                              alt={ep.title}
                              className="w-28 h-16 rounded-lg object-cover flex-shrink-0"
                              onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                            />
                          ) : null}
                          <div className="min-w-0 flex-1">
                            <div className="flex items-center gap-2">
                              <span className="text-xs font-semibold text-cyan-200/80">
                                S{ep.season} E{ep.episode}
                              </span>
                              {ep.runtime ? <span className="text-xs text-slate-500">{ep.runtime}</span> : null}
                              {ep.released ? <span className="text-xs text-slate-500">{ep.released}</span> : null}
                            </div>
                            <div className="mt-1 text-sm font-medium text-white truncate">{ep.title}</div>
                            {ep.overview ? (
                              <div className="mt-1 text-xs text-slate-400 line-clamp-2">{ep.overview}</div>
                            ) : null}
                          </div>
                        </div>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ) : null}

            {infoCards.length > 0 ? (
              <div className="mb-8">
                <h3 className="mb-4 text-xl font-semibold text-white">Addon Details</h3>
                <div className="grid grid-cols-1 gap-3 max-w-4xl">
                  {infoCards.map((card) => (
                    <div key={card.id} className="rounded-2xl border border-white/10 bg-white/5 p-4 backdrop-blur-md">
                      <div className="flex flex-wrap items-center gap-2 text-sm text-slate-300">
                        <span className="font-semibold text-white">{card.title || 'Details'}</span>
                        {card.providerName ? <span className="text-slate-500">•</span> : null}
                        {card.providerName ? <span>{card.providerName}</span> : null}
                      </div>
                      {card.description ? <div className="mt-2 text-sm leading-6 text-slate-300">{card.description}</div> : null}
                      {card.metadata && Object.keys(card.metadata).length > 0 ? (
                        <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-400">
                          {Object.entries(card.metadata)
                            .filter(([key, value]) => key !== 'description' && key !== 'title' && key !== 'name' && String(value ?? '').trim())
                            .slice(0, 12)
                            .map(([key, value]) => (
                              <span key={`${card.id}-${key}`} className="rounded-full border border-white/10 bg-slate-900/60 px-3 py-1">
                                {key}: {String(value)}
                              </span>
                            ))}
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            <div>
              <div className="flex items-center gap-3 mb-4">
                <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
                <h3 className="text-white text-xl font-semibold">AI Stream Selection</h3>
                {onToggleAutoPlaySources ? (
                  <button
                    type="button"
                    onClick={onToggleAutoPlaySources}
                    className={`rounded-full border px-3 py-1.5 text-xs font-semibold backdrop-blur-md transition ${autoPlaySources ? 'border-cyan-300/30 bg-cyan-300/12 text-cyan-100' : 'border-white/15 bg-white/8 text-white hover:bg-white/14'}`}
                  >
                    Auto Play {autoPlaySources ? 'On' : 'Off'}
                  </button>
                ) : null}
              </div>

              <div className="grid grid-cols-1 gap-3 max-w-3xl mb-6">
                {streamSources.map((source, index) => (
                  <StreamSourceCard
                    key={index}
                    sourceName={source.sourceName}
                    providerName={source.providerName ?? ''}
                    quality={source.quality}
                    fileSize={source.fileSize}
                    audioLanguage={source.audioLanguage}
                    subtitles={source.subtitles}
                    seederCount={source.seederCount}
                    copyableLink={source.copyableLink}
                    onCopyLink={() => onCopySourceLink?.(source)}
                    onPlay={() => onPlay(source)}
                  />
                ))}
                {streamSources.length === 0 ? (
                  <div className="rounded-2xl border border-white/10 bg-white/5 p-5 text-sm text-slate-300 backdrop-blur-md">
                    <div className="font-semibold text-white">{isLoadingSources ? 'Loading sources…' : 'No sources available yet'}</div>
                    {streamsStatusText ? <div className="mt-2 text-slate-400">{streamsStatusText}</div> : null}
                  </div>
                ) : null}
              </div>


            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
