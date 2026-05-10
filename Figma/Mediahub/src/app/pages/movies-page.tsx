import { AICommandBar } from '../components/ai-command-bar';
import { CarouselToggleShelf } from '../components/carousel-toggle-shelf';
import { CarouselItem } from '../components/carousel-3d';
import { Film, TrendingUp, Clock, Star, Calendar } from 'lucide-react';
import { FeaturedPanel } from '../components/featured-panel';

const featuredMovie = {
  id: 'avatar-3',
  title: 'Avatar: Fire and Ash',
  backdropUrl: 'https://images.unsplash.com/photo-1446776811953-b23d57bd21aa?w=1600&q=80',
  rating: 8.8,
  runtime: '3h 12m',
  release: 'Dec 19, 2026',
  genres: ['Sci-Fi', 'Adventure', 'Action'],
  source: 'Disney+ · 4K Dolby Vision',
  description: 'Jake and Neytiri lead a new clan into the volcanic Ash People territory. The next chapter of Pandora arrives in cinematic IMAX scale.',
  type: 'Movie' as const,
  primaryAction: 'Play' as const,
};

const latestMovies: CarouselItem[] = [
  { id: '1', title: 'Oppenheimer', type: 'Movie', releaseDate: '2023', platform: 'Cinema', heatScore: 83, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '2', title: 'The Creator', type: 'Movie', releaseDate: '2023', platform: 'Hulu', heatScore: 78, aiVerdict: 'Worth Watching', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '3', title: 'Poor Things', type: 'Movie', releaseDate: '2023', platform: 'Cinema', heatScore: 79, aiVerdict: 'Worth Watching', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '4', title: 'Civil War', type: 'Movie', releaseDate: '2024', platform: 'Cinema', heatScore: 72, aiVerdict: 'Worth Watching', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '5', title: 'The Zone of Interest', type: 'Movie', releaseDate: '2023', platform: 'Prime', heatScore: 74, aiVerdict: 'Worth Watching', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '6', title: 'Dune: Part Two', type: 'Movie', releaseDate: '2024', platform: 'Cinema', heatScore: 89, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
];

const topRated: CarouselItem[] = [
  { id: '7', title: 'The Shawshank Redemption', type: 'Movie', releaseDate: '1994', platform: 'Netflix', heatScore: 93, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '8', title: 'The Godfather', type: 'Movie', releaseDate: '1972', platform: 'Paramount+', heatScore: 92, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '9', title: 'The Dark Knight', type: 'Movie', releaseDate: '2008', platform: 'HBO Max', heatScore: 90, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '10', title: 'Pulp Fiction', type: 'Movie', releaseDate: '1994', platform: 'Netflix', heatScore: 89, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '11', title: 'Schindler\'s List', type: 'Movie', releaseDate: '1993', platform: 'Netflix', heatScore: 89, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '12', title: 'The Lord of the Rings', type: 'Movie', releaseDate: '2001', platform: 'HBO Max', heatScore: 88, aiVerdict: 'Must Watch', verdictColor: 'bg-green-500/30 text-green-200' },
];

const sciFiCollection: CarouselItem[] = [
  { id: '13', title: 'Blade Runner 2049', type: 'Movie', releaseDate: '2017', platform: 'Prime', heatScore: 80, aiVerdict: 'Stunning visuals', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '14', title: 'Interstellar', type: 'Movie', releaseDate: '2014', platform: 'Paramount+', heatScore: 87, aiVerdict: 'Epic storytelling', verdictColor: 'bg-green-500/30 text-green-200' },
  { id: '15', title: 'Arrival', type: 'Movie', releaseDate: '2016', platform: 'Prime', heatScore: 79, aiVerdict: 'Mind-bending', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '16', title: 'Ex Machina', type: 'Movie', releaseDate: '2014', platform: 'Netflix', heatScore: 77, aiVerdict: 'Thought-provoking', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '17', title: 'Annihilation', type: 'Movie', releaseDate: '2018', platform: 'Paramount+', heatScore: 68, aiVerdict: 'Visually striking', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
  { id: '18', title: 'Her', type: 'Movie', releaseDate: '2013', platform: 'HBO Max', heatScore: 80, aiVerdict: 'Emotionally rich', verdictColor: 'bg-cyan-500/30 text-cyan-200' },
];

export function MoviesPage() {
  return (
    <div className="space-y-8">
      <FeaturedPanel item={featuredMovie} />
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500">
          <Film size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">Movies</h1>
          <p className="text-slate-400">248 movies in your library</p>
        </div>
      </div>

      {/* AI Command Bar */}
      <AICommandBar />

      {/* Stats Cards */}
      <div className="grid grid-cols-4 gap-4">
        {[
          { icon: TrendingUp, label: 'Trending', value: '42', color: 'from-cyan-500 to-blue-500' },
          { icon: Clock, label: 'Recently Added', value: '12', color: 'from-purple-500 to-violet-500' },
          { icon: Star, label: 'Top Rated', value: '89', color: 'from-amber-500 to-orange-500' },
          { icon: Calendar, label: 'This Month', value: '8', color: 'from-green-500 to-emerald-500' },
        ].map((stat) => {
          const Icon = stat.icon;
          return (
            <div
              key={stat.label}
              className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-5 hover:border-cyan-500/30 transition-all group"
            >
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-slate-400 text-sm">{stat.label}</p>
                  <p className="text-slate-100 text-2xl mt-1">{stat.value}</p>
                </div>
                <div className={`p-3 rounded-xl bg-gradient-to-r ${stat.color} opacity-80`}>
                  <Icon size={24} className="text-white" />
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Movie Shelves with Carousel Toggle */}
      <CarouselToggleShelf title="Latest Movies" items={latestMovies} itemCount="248 total" />
      <CarouselToggleShelf title="Top Rated" items={topRated} itemCount="89 movies" />
      <CarouselToggleShelf title="Sci-Fi Collection" items={sciFiCollection} itemCount="24 movies" showAIBadge />

      <div className="h-8" />
    </div>
  );
}
