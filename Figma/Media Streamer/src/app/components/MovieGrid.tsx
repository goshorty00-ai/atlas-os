import { PosterCard } from './PosterCard';

interface Movie {
  id: number;
  title: string;
  posterUrl: string;
  rating?: number;
  progress?: number;
}

interface MovieGridProps {
  movies: Movie[];
  onMovieClick: (movie: Movie) => void;
}

export function MovieGrid({ movies, onMovieClick }: MovieGridProps) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-7 gap-6 p-8">
      {movies.map((movie) => (
        <PosterCard
          key={movie.id}
          title={movie.title}
          posterUrl={movie.posterUrl}
          rating={movie.rating}
          progress={movie.progress}
          onClick={() => onMovieClick(movie)}
        />
      ))}
    </div>
  );
}
