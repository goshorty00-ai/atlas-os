import { AICommandBar } from '../components/ai-command-bar';
import { Tv, Play, RotateCcw, Calendar, TrendingUp, Bell, Sparkles, CheckCircle, XCircle, Clock } from 'lucide-react';
import { FeaturedPanel } from '../components/featured-panel';

const featuredTV = {
  id: 'severance-s3',
  title: 'Severance · Season 3',
  backdropUrl: 'https://images.unsplash.com/photo-1497124401559-3e75ec2ed794?w=1600&q=80',
  rating: 9.4,
  runtime: '10 episodes · 55m each',
  release: 'Premieres Sep 2026',
  genres: ['Thriller', 'Mystery', 'Sci-Fi'],
  source: 'Apple TV+ · 4K Atmos',
  description: 'Mark and the macrodata team push past the severed floor as Lumon\'s deeper conspiracy fractures into daylight. The teaser already broke streaming records.',
  type: 'TV' as const,
  primaryAction: 'Add Reminder' as const,
};

interface EpisodeItem {
  show: string;
  season: number;
  episode: number;
  title: string;
  progress: number;
  runtime: string;
}

const continueWatching: EpisodeItem[] = [
  { show: 'Severance', season: 2, episode: 3, title: 'The Gantry', progress: 45, runtime: '48min' },
  { show: 'The Last of Us', season: 2, episode: 1, title: 'When We Are in Need', progress: 12, runtime: '58min' },
  { show: 'Shogun', season: 1, episode: 7, title: 'A Nest of Vipers', progress: 78, runtime: '62min' },
  { show: 'Fallout', season: 1, episode: 5, title: 'The Head', progress: 90, runtime: '52min' },
];

const upcomingEpisodes = [
  { show: 'House of the Dragon', season: 3, episode: 1, airDate: 'May 15, 2026', status: 'upcoming' },
  { show: 'Andor', season: 2, episode: 1, airDate: 'Jun 8, 2026', status: 'upcoming' },
  { show: 'The Mandalorian', season: 4, episode: 1, airDate: 'Jul 2, 2026', status: 'upcoming' },
];

const showStatus = [
  { show: 'Severance', status: 'renewed', season: 3, note: 'Confirmed for Season 3' },
  { show: 'Stranger Things', status: 'final', season: 5, note: 'Final season announced' },
  { show: 'Mindhunter', status: 'cancelled', season: 2, note: 'Cancelled after S2' },
  { show: 'Dark Matter', status: 'renewed', season: 2, note: 'Renewed for Season 2' },
];

export function TVPage() {
  return (
    <div className="space-y-8">
      <FeaturedPanel item={featuredTV} />
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-purple-500 to-violet-500">
          <Tv size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">TV Command Centre</h1>
          <p className="text-slate-400">89 shows • 42 in progress</p>
        </div>
      </div>

      {/* AI Command Bar */}
      <AICommandBar />

      {/* Continue Watching */}
      <div>
        <h3 className="text-slate-200 mb-4">Continue Watching</h3>
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {continueWatching.map((ep, index) => (
            <div
              key={index}
              className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-5 hover:border-purple-500/30 transition-all group"
            >
              <div className="flex gap-4">
                <div className="w-28 h-40 rounded-lg bg-gradient-to-br from-purple-600 to-violet-600 shrink-0 flex items-center justify-center">
                  <Tv size={32} className="text-white/30" />
                </div>
                <div className="flex-1">
                  <h4 className="text-slate-100 mb-1">{ep.show}</h4>
                  <p className="text-sm text-slate-400 mb-2">S{ep.season}E{ep.episode} • {ep.title}</p>
                  <div className="space-y-3">
                    <div>
                      <div className="flex items-center justify-between text-xs text-slate-400 mb-1.5">
                        <span>{ep.runtime} remaining</span>
                        <span>{ep.progress}%</span>
                      </div>
                      <div className="h-1.5 bg-slate-950/50 rounded-full overflow-hidden">
                        <div className="h-full bg-gradient-to-r from-purple-500 to-violet-500" style={{ width: `${ep.progress}%` }} />
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <button className="flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg bg-gradient-to-r from-purple-500 to-violet-500 text-white hover:shadow-lg hover:shadow-purple-500/30 transition-all">
                        <Play size={14} />
                        <span className="text-sm">Resume</span>
                      </button>
                      <button className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                        <RotateCcw size={14} />
                      </button>
                      <button className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                        <Sparkles size={14} />
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Episode Tracker */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Upcoming Episodes */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-cyan-500/20 p-6">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 rounded-lg bg-cyan-500/20">
              <Calendar size={20} className="text-cyan-300" />
            </div>
            <div>
              <h3 className="text-slate-100">Upcoming Episodes</h3>
              <p className="text-xs text-slate-400">New episodes this week</p>
            </div>
          </div>
          <div className="space-y-2">
            {upcomingEpisodes.map((ep, index) => (
              <div key={index} className="flex items-center justify-between p-3 rounded-lg bg-slate-950/50 border border-slate-700/30 hover:border-cyan-500/30 transition-all group">
                <div>
                  <h4 className="text-slate-200 text-sm">{ep.show}</h4>
                  <p className="text-xs text-slate-400">S{ep.season}E{ep.episode} • {ep.airDate}</p>
                </div>
                <button className="p-1.5 rounded-lg bg-cyan-500/20 text-cyan-300 hover:bg-cyan-500/30 transition-all opacity-0 group-hover:opacity-100">
                  <Bell size={14} />
                </button>
              </div>
            ))}
          </div>
        </div>

        {/* Show Status */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 rounded-lg bg-purple-500/20">
              <TrendingUp size={20} className="text-purple-300" />
            </div>
            <div>
              <h3 className="text-slate-100">Show Status</h3>
              <p className="text-xs text-slate-400">Renewals & cancellations</p>
            </div>
          </div>
          <div className="space-y-2">
            {showStatus.map((show, index) => (
              <div key={index} className="flex items-center justify-between p-3 rounded-lg bg-slate-950/50 border border-slate-700/30">
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h4 className="text-slate-200 text-sm">{show.show}</h4>
                    {show.status === 'renewed' && <CheckCircle size={14} className="text-green-400" />}
                    {show.status === 'cancelled' && <XCircle size={14} className="text-red-400" />}
                    {show.status === 'final' && <Clock size={14} className="text-amber-400" />}
                  </div>
                  <p className="text-xs text-slate-400">{show.note}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Smart TV Tools */}
      <div>
        <h3 className="text-slate-200 mb-4">Smart TV Tools</h3>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            { label: 'AI Episode Recap', icon: Sparkles, color: 'from-cyan-500 to-blue-500' },
            { label: 'Skip Filler Episodes', icon: RotateCcw, color: 'from-purple-500 to-violet-500' },
            { label: 'Find Best Episode Order', icon: TrendingUp, color: 'from-pink-500 to-rose-500' },
            { label: 'Auto-Build Binge Shelf', icon: Play, color: 'from-green-500 to-emerald-500' },
          ].map((tool) => {
            const Icon = tool.icon;
            return (
              <button
                key={tool.label}
                className="relative bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-5 hover:border-purple-500/30 transition-all group text-left"
              >
                <div className={`w-12 h-12 rounded-xl bg-gradient-to-r ${tool.color} mb-3 flex items-center justify-center`}>
                  <Icon size={24} className="text-white" />
                </div>
                <h4 className="text-slate-100 text-sm group-hover:text-purple-300 transition-colors">{tool.label}</h4>
              </button>
            );
          })}
        </div>
      </div>

      <div className="h-8" />
    </div>
  );
}
