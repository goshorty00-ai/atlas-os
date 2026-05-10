import { useState } from 'react';
import {
  Mic2, Volume, VolumeX, Music, Users, Trophy,
  Plus, X, Sparkles, ChevronUp, ChevronDown
} from 'lucide-react';

interface KaraokeSong {
  id: string;
  title: string;
  artist: string;
  key: string;
  duration: string;
}

const queue: KaraokeSong[] = [
  { id: '1', title: 'Bohemian Rhapsody', artist: 'Queen', key: 'Bb', duration: '5:55' },
  { id: '2', title: 'Don\'t Stop Believin\'', artist: 'Journey', key: 'E', duration: '4:11' },
  { id: '3', title: 'Sweet Child O\' Mine', artist: 'Guns N\' Roses', key: 'D', duration: '5:56' },
];

export function KaraokePanel() {
  const [vocalRemoval, setVocalRemoval] = useState(true);
  const [keyChange, setKeyChange] = useState(0);
  const [micEnabled, setMicEnabled] = useState(true);

  return (
    <div
      className="relative bg-gradient-to-br from-slate-900/90 to-slate-800/90 backdrop-blur-xl rounded-2xl border border-pink-500/20 p-6 space-y-5"
      style={{
        backdropFilter: 'blur(20px)',
        boxShadow: '0 0 40px rgba(236, 72, 153, 0.1)',
      }}
    >
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-xl bg-gradient-to-r from-pink-500 to-purple-500">
            <Mic2 size={20} className="text-white" />
          </div>
          <div>
            <h3 className="text-slate-100">AI Karaoke</h3>
            <p className="text-xs text-slate-400">Party Mode Active</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button className="p-2 rounded-lg bg-gradient-to-r from-pink-500/20 to-purple-500/20 text-pink-400 hover:shadow-lg hover:shadow-pink-500/20 transition-all">
            <Sparkles size={18} />
          </button>
          <button className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
            <Trophy size={18} />
          </button>
        </div>
      </div>

      {/* Controls */}
      <div className="grid grid-cols-3 gap-3">
        {/* Vocal Removal */}
        <button
          onClick={() => setVocalRemoval(!vocalRemoval)}
          className={`p-3 rounded-xl border transition-all ${
            vocalRemoval
              ? 'bg-gradient-to-r from-pink-500/20 to-purple-500/20 border-pink-500/50 text-pink-200'
              : 'bg-slate-800/50 border-slate-700 text-slate-400 hover:text-slate-200'
          }`}
        >
          <div className="flex flex-col items-center gap-1">
            {vocalRemoval ? <VolumeX size={20} /> : <Volume size={20} />}
            <span className="text-xs">Vocals {vocalRemoval ? 'Off' : 'On'}</span>
          </div>
        </button>

        {/* Microphone */}
        <button
          onClick={() => setMicEnabled(!micEnabled)}
          className={`p-3 rounded-xl border transition-all ${
            micEnabled
              ? 'bg-gradient-to-r from-green-500/20 to-emerald-500/20 border-green-500/50 text-green-200'
              : 'bg-slate-800/50 border-slate-700 text-slate-400 hover:text-slate-200'
          }`}
        >
          <div className="flex flex-col items-center gap-1">
            <Mic2 size={20} className={micEnabled ? 'animate-pulse' : ''} />
            <span className="text-xs">Mic {micEnabled ? 'On' : 'Off'}</span>
          </div>
        </button>

        {/* Duet Mode */}
        <button className="p-3 rounded-xl bg-slate-800/50 border border-slate-700 text-slate-400 hover:text-slate-200 hover:bg-slate-700/50 transition-all">
          <div className="flex flex-col items-center gap-1">
            <Users size={20} />
            <span className="text-xs">Duet</span>
          </div>
        </button>
      </div>

      {/* Key Change */}
      <div>
        <label className="text-sm text-slate-300 mb-2 block">Key Change</label>
        <div className="flex items-center gap-3">
          <button
            onClick={() => setKeyChange(Math.max(-12, keyChange - 1))}
            className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
          >
            <ChevronDown size={18} />
          </button>
          <div className="flex-1 text-center">
            <div className="text-lg text-cyan-400">
              {keyChange > 0 ? `+${keyChange}` : keyChange}
            </div>
            <div className="text-xs text-slate-500">semitones</div>
          </div>
          <button
            onClick={() => setKeyChange(Math.min(12, keyChange + 1))}
            className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
          >
            <ChevronUp size={18} />
          </button>
        </div>
      </div>

      {/* Queue */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <h4 className="text-sm text-slate-300">Queue ({queue.length})</h4>
          <button className="flex items-center gap-1 px-3 py-1.5 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 text-white text-sm hover:shadow-lg hover:shadow-pink-500/30 transition-all">
            <Plus size={14} />
            <span>Add Song</span>
          </button>
        </div>
        <div className="space-y-2 max-h-48 overflow-y-auto">
          {queue.map((song, index) => (
            <div
              key={song.id}
              className="flex items-center gap-3 p-3 rounded-xl bg-slate-950/50 border border-slate-700/50 hover:border-pink-500/30 transition-all group"
            >
              <div className="text-slate-500 text-sm w-6">{index + 1}</div>
              <Music size={16} className="text-pink-400" />
              <div className="flex-1 min-w-0">
                <h5 className="text-slate-200 text-sm truncate">{song.title}</h5>
                <p className="text-xs text-slate-400 truncate">{song.artist}</p>
              </div>
              <div className="flex items-center gap-2 text-xs text-slate-500">
                <span className="px-2 py-1 rounded bg-slate-800">Key: {song.key}</span>
                <span>{song.duration}</span>
              </div>
              <button className="p-1.5 rounded-lg text-slate-500 hover:text-red-400 hover:bg-slate-800 transition-all opacity-0 group-hover:opacity-100">
                <X size={16} />
              </button>
            </div>
          ))}
        </div>
      </div>

      {/* AI Actions */}
      <div className="flex gap-2">
        <button className="flex-1 flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl bg-gradient-to-r from-pink-500/20 to-purple-500/20 border border-pink-500/30 text-pink-200 hover:shadow-lg hover:shadow-pink-500/20 transition-all">
          <Sparkles size={16} />
          <span className="text-sm">Generate Queue</span>
        </button>
      </div>
    </div>
  );
}
