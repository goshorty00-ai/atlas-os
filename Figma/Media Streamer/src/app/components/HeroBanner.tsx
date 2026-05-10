interface HeroBannerProps {
  backdropUrl: string;
  title: string;
  year: string;
  runtime: string;
  rating: number;
  genres: string[];
  description: string;
  onPlay: () => void;
  onTrailer: () => void;
  onAddToLibrary: () => void;
  onLike: () => void;
}

export function HeroBanner({
  backdropUrl,
  title,
  year,
  runtime,
  rating,
  genres,
  description,
  onPlay,
  onTrailer,
  onAddToLibrary,
  onLike,
}: HeroBannerProps) {
  return (
    <div className="relative w-full rounded-2xl overflow-hidden mb-12" style={{ aspectRatio: '16/9' }}>
      <img src={backdropUrl} alt={title} className="w-full h-full object-cover" />

      <div className="absolute inset-0 bg-gradient-to-r from-black/90 via-black/60 to-transparent" />
      <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-transparent to-transparent" />

      <div className="absolute inset-0 flex flex-col justify-end p-12">
        <h1 className="text-6xl font-bold text-white mb-4">{title}</h1>

        <div className="flex items-center gap-4 mb-4">
          <div className="flex items-center gap-2 bg-yellow-500/20 backdrop-blur-sm px-3 py-1.5 rounded-lg border border-yellow-500/30">
            <span className="text-yellow-400 text-lg">★</span>
            <span className="text-yellow-400 font-semibold">{rating.toFixed(1)}</span>
            <span className="text-yellow-400/70 text-sm">IMDb</span>
          </div>
          <span className="text-gray-300">{year}</span>
          <span className="text-gray-300">•</span>
          <span className="text-gray-300">{runtime}</span>
        </div>

        <div className="flex gap-2 mb-4">
          {genres.map((genre, index) => (
            <span
              key={index}
              className="px-3 py-1 bg-white/10 backdrop-blur-sm rounded-full text-sm text-gray-200 border border-white/20"
            >
              {genre}
            </span>
          ))}
        </div>

        <p className="text-gray-300 text-lg max-w-2xl mb-6 line-clamp-3">{description}</p>

        <div className="flex gap-4">
          <button
            onClick={onPlay}
            className="px-8 py-4 bg-gradient-to-r from-purple-500 to-blue-500 rounded-lg text-white font-semibold text-lg flex items-center gap-2 hover:scale-105 transition-transform duration-200 shadow-[0_0_30px_rgba(139,92,246,0.5)]"
          >
            <svg width="20" height="20" viewBox="0 0 20 20" fill="white">
              <path d="M4 3L16 10L4 17V3Z" />
            </svg>
            Play
          </button>
          <button
            onClick={onTrailer}
            className="px-6 py-4 bg-white/10 backdrop-blur-md rounded-lg text-white font-semibold border border-white/20 hover:bg-white/20 transition-all duration-200"
          >
            Trailer
          </button>
          <button
            onClick={onAddToLibrary}
            className="px-6 py-4 bg-white/10 backdrop-blur-md rounded-lg text-white font-semibold border border-white/20 hover:bg-white/20 transition-all duration-200"
          >
            Add to Library
          </button>
          <button
            onClick={onLike}
            className="px-4 py-4 bg-white/10 backdrop-blur-md rounded-lg text-white border border-white/20 hover:bg-white/20 transition-all duration-200"
          >
            ♥
          </button>
        </div>
      </div>
    </div>
  );
}
