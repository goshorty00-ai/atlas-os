import { AICommandBar } from '../components/ai-command-bar';
import { Gamepad2, Clock, Star, Zap, Joystick } from 'lucide-react';
import { FeaturedPanel } from '../components/featured-panel';

const featuredGame = {
  title: 'GTA VI',
  backdropUrl: 'https://images.unsplash.com/photo-1538481199705-c710c4e965fc?w=1600&q=80',
  rating: 9.7,
  runtime: 'Open world · 80h+ campaign',
  release: 'Q1 2027',
  genres: ['Action', 'Open World', 'Crime'],
  source: 'PS5 · Xbox Series X · PC',
  description: 'Return to a reimagined Vice City. The latest gameplay reveal lit up the internet — pre-order tracking is at record-breaking levels.',
  type: 'Game' as const,
  primaryAction: 'Add Reminder' as const,
};

interface GameItem {
  id: string;
  title: string;
  platform: string;
  rating: string;
  hours: string;
  status: 'installed' | 'cloud' | 'not-installed';
  lastPlayed?: string;
  aiReason?: string;
}

const recentlyPlayed: GameItem[] = [
  { id: '1', title: 'Cyberpunk 2077', platform: 'PC', rating: '8.5', hours: '142h', status: 'installed', lastPlayed: '2 hours ago' },
  { id: '2', title: 'Baldur\'s Gate 3', platform: 'PC', rating: '9.2', hours: '89h', status: 'installed', lastPlayed: 'Yesterday' },
  { id: '3', title: 'Starfield', platform: 'Xbox', rating: '7.8', hours: '67h', status: 'cloud', lastPlayed: '3 days ago' },
  { id: '4', title: 'Alan Wake 2', platform: 'PC', rating: '8.9', hours: '23h', status: 'installed', lastPlayed: '5 days ago' },
];

const recommended: GameItem[] = [
  { id: '5', title: 'Hades II', platform: 'PC', rating: '9.0', hours: '0h', status: 'not-installed', aiReason: 'Perfect for quick sessions' },
  { id: '6', title: 'Lies of P', platform: 'PC', rating: '8.1', hours: '0h', status: 'cloud', aiReason: 'You love soulslikes' },
  { id: '7', title: 'Hi-Fi Rush', platform: 'Xbox', rating: '8.7', hours: '0h', status: 'cloud', aiReason: 'Unique rhythm action' },
  { id: '8', title: 'Cocoon', platform: 'PC', rating: '8.3', hours: '0h', status: 'not-installed', aiReason: 'Stunning puzzle game' },
];

function GameCard({ game }: { game: GameItem }) {
  return (
    <div className="relative group cursor-pointer shrink-0" style={{ width: '200px' }}>
      <div className="relative rounded-xl overflow-hidden bg-slate-800 aspect-[16/9]">
        <div className="absolute inset-0 bg-gradient-to-br from-slate-700 to-slate-900 flex items-center justify-center">
          <Joystick size={48} className="text-slate-600" />
        </div>

        {/* Status badge */}
        <div className="absolute top-2 right-2 px-2 py-1 rounded-lg text-xs backdrop-blur-sm"
          style={{
            background: game.status === 'installed'
              ? 'rgba(34, 197, 94, 0.3)'
              : game.status === 'cloud'
              ? 'rgba(6, 182, 212, 0.3)'
              : 'rgba(100, 116, 139, 0.3)',
            color: game.status === 'installed'
              ? '#86efac'
              : game.status === 'cloud'
              ? '#67e8f9'
              : '#cbd5e1'
          }}>
          {game.status === 'installed' ? 'Installed' : game.status === 'cloud' ? 'Cloud' : 'Not Installed'}
        </div>

        {/* Hover overlay */}
        <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/80 to-transparent opacity-0 group-hover:opacity-100 transition-all duration-300">
          <div className="absolute bottom-0 left-0 right-0 p-3">
            <button className="w-full p-2 rounded-lg bg-gradient-to-r from-cyan-500 to-purple-500 text-white hover:shadow-lg hover:shadow-cyan-500/30 transition-all">
              {game.status === 'installed' ? 'Play' : game.status === 'cloud' ? 'Stream' : 'Install'}
            </button>
          </div>
        </div>
      </div>

      <div className="mt-2 space-y-1">
        <h4 className="text-slate-200 text-sm truncate">{game.title}</h4>
        <div className="flex items-center gap-2 text-xs">
          <span className="px-1.5 py-0.5 rounded bg-slate-800 text-slate-400">{game.platform}</span>
          <span className="px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400">{game.rating}</span>
          {game.hours && <span className="text-slate-500">{game.hours}</span>}
        </div>
        {game.lastPlayed && (
          <p className="text-xs text-slate-500">{game.lastPlayed}</p>
        )}
        {game.aiReason && (
          <p className="text-xs text-cyan-400 truncate">{game.aiReason}</p>
        )}
      </div>
    </div>
  );
}

function GameShelf({ title, items, count }: { title: string; items: GameItem[]; count: string }) {
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="text-slate-200">{title}</h3>
          <span className="px-2 py-1 rounded-lg bg-slate-800 text-slate-400 text-xs">{count}</span>
        </div>
        <button className="text-xs text-cyan-400 hover:text-cyan-300 transition-colors">
          View All
        </button>
      </div>
      <div className="flex gap-4 overflow-x-auto pb-2 scrollbar-hide">
        {items.map((game) => (
          <GameCard key={game.id} game={game} />
        ))}
      </div>
    </div>
  );
}

export function GamesPage() {
  return (
    <div className="space-y-8">
      <FeaturedPanel item={featuredGame} />
      {/* Header */}
      <div className="flex items-center gap-4">
        <div className="p-3 rounded-xl bg-gradient-to-r from-cyan-500 to-purple-500">
          <Gamepad2 size={28} className="text-white" />
        </div>
        <div>
          <h1 className="text-slate-100 text-3xl">Games</h1>
          <p className="text-slate-400">47 games in your library</p>
        </div>
      </div>

      {/* AI Command Bar */}
      <AICommandBar />

      {/* Stats Cards */}
      <div className="grid grid-cols-4 gap-4">
        {[
          { icon: Clock, label: 'Recently Played', value: '12', color: 'from-cyan-500 to-blue-500' },
          { icon: Star, label: 'Installed', value: '28', color: 'from-green-500 to-emerald-500' },
          { icon: Zap, label: 'Cloud Ready', value: '19', color: 'from-purple-500 to-violet-500' },
          { icon: Gamepad2, label: 'Total Hours', value: '842', color: 'from-amber-500 to-orange-500' },
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

      {/* Game Shelves */}
      <GameShelf title="Recently Played" items={recentlyPlayed} count="12 games" />
      <GameShelf title="AI Recommended" items={recommended} count="8 picks" />

      <div className="h-8" />
    </div>
  );
}
