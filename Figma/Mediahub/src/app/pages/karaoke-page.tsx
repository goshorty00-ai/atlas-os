import { useState } from 'react';
import {
  Mic2, Play, Pause, RotateCcw, Volume, VolumeX, Music,
  Users, Trophy, Plus, X, ChevronUp, ChevronDown, Sparkles, Settings
} from 'lucide-react';

interface KaraokeSong {
  id: string;
  title: string;
  artist: string;
  key: string;
  duration: string;
  singer?: string;
}

const queue: KaraokeSong[] = [
  { id: '1', title: 'Bohemian Rhapsody', artist: 'Queen', key: 'Bb', duration: '5:55', singer: 'Alex' },
  { id: '2', title: 'Don\'t Stop Believin\'', artist: 'Journey', key: 'E', duration: '4:11', singer: 'Jordan' },
  { id: '3', title: 'Sweet Child O\' Mine', artist: 'Guns N\' Roses', key: 'D', duration: '5:56' },
  { id: '4', title: 'Livin\' on a Prayer', artist: 'Bon Jovi', key: 'Em', duration: '4:09' },
];

const currentLyrics = [
  "Is this the real life?",
  "Is this just fantasy?",
  "Caught in a landslide",
  "No escape from reality",
];

export function KaraokePage() {
  const [isPlaying, setIsPlaying] = useState(true);
  const [vocalRemoval, setVocalRemoval] = useState(true);
  const [keyChange, setKeyChange] = useState(0);
  const [tempo, setTempo] = useState(0);
  const [micEnabled, setMicEnabled] = useState(true);
  const [duetMode, setDuetMode] = useState(false);
  const [scoreMode, setScoreMode] = useState(true);
  const [currentLine, setCurrentLine] = useState(1);
  const [score, setScore] = useState(8742);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <div className="p-3 rounded-xl bg-gradient-to-r from-pink-500 to-purple-500">
            <Mic2 size={28} className="text-white" />
          </div>
          <div>
            <h1 className="text-slate-100 text-3xl">AI Karaoke</h1>
            <p className="text-slate-400">Party Mode • 4 songs in queue</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-gradient-to-r from-pink-500 to-purple-500 text-white hover:shadow-lg hover:shadow-pink-500/30 transition-all">
            <Sparkles size={18} />
            <span>Generate Queue</span>
          </button>
        </div>
      </div>

      {/* Karaoke Stage */}
      <div className="relative bg-gradient-to-br from-slate-950 via-purple-950/30 to-slate-950 rounded-2xl border border-pink-500/20 p-8 overflow-hidden"
        style={{ minHeight: '400px' }}>
        {/* Animated Background */}
        <div className="absolute inset-0 opacity-20">
          <div className="absolute inset-0 bg-gradient-to-br from-pink-500 via-purple-500 to-cyan-500 blur-3xl animate-pulse" />
        </div>

        {/* Current Song Info */}
        <div className="relative z-10 text-center mb-8">
          <h2 className="text-slate-100 text-3xl mb-2">Bohemian Rhapsody</h2>
          <p className="text-slate-400 text-lg">Queen</p>
        </div>

        {/* Lyrics Display */}
        <div className="relative z-10 space-y-6 max-w-3xl mx-auto">
          {currentLyrics.map((line, index) => (
            <div
              key={index}
              className={`text-center text-2xl transition-all duration-300 ${
                index === currentLine
                  ? 'text-transparent bg-clip-text bg-gradient-to-r from-cyan-400 via-pink-400 to-purple-400 scale-110'
                  : index === currentLine + 1
                  ? 'text-slate-400'
                  : 'text-slate-600'
              }`}
            >
              {line}
            </div>
          ))}
        </div>

        {/* Progress & Score */}
        <div className="relative z-10 mt-12 max-w-3xl mx-auto space-y-4">
          {/* Progress Bar */}
          <div className="h-2 bg-slate-900/50 rounded-full overflow-hidden">
            <div className="h-full bg-gradient-to-r from-pink-500 to-purple-500" style={{ width: '35%' }} />
          </div>

          {/* Meters */}
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <Mic2 size={16} className="text-pink-400" />
                <div className="flex gap-1">
                  {[...Array(8)].map((_, i) => (
                    <div
                      key={i}
                      className="w-1.5 h-8 rounded-full"
                      style={{
                        background: i < 6 ? 'linear-gradient(to top, rgb(236, 72, 153), rgb(168, 85, 247))' : 'rgb(51, 65, 85)',
                      }}
                    />
                  ))}
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Volume size={16} className="text-cyan-400" />
                <div className="flex gap-1">
                  {[...Array(8)].map((_, i) => (
                    <div
                      key={i}
                      className="w-1.5 h-8 rounded-full"
                      style={{
                        background: i < 5 ? 'linear-gradient(to top, rgb(6, 182, 212), rgb(59, 130, 246))' : 'rgb(51, 65, 85)',
                      }}
                    />
                  ))}
                </div>
              </div>
            </div>
            {scoreMode && (
              <div className="flex items-center gap-2">
                <Trophy size={20} className="text-amber-400" />
                <span className="text-2xl text-amber-300">{score.toLocaleString()}</span>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Controls */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Playback Controls */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <h3 className="text-slate-100 mb-4">Playback</h3>
          <div className="space-y-4">
            <div className="flex items-center justify-center gap-4">
              <button className="p-3 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <RotateCcw size={20} />
              </button>
              <button
                onClick={() => setIsPlaying(!isPlaying)}
                className="p-4 rounded-xl bg-gradient-to-r from-pink-500 to-purple-500 text-white hover:shadow-lg hover:shadow-pink-500/30 transition-all"
              >
                {isPlaying ? <Pause size={24} /> : <Play size={24} />}
              </button>
              <button className="p-3 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all">
                <Settings size={20} />
              </button>
            </div>

            <div className="grid grid-cols-3 gap-2">
              <button
                onClick={() => setVocalRemoval(!vocalRemoval)}
                className={`p-3 rounded-lg border transition-all ${
                  vocalRemoval
                    ? 'bg-pink-500/20 border-pink-500/50 text-pink-200'
                    : 'bg-slate-800/50 border-slate-700 text-slate-400'
                }`}
              >
                <div className="flex flex-col items-center gap-1">
                  <VolumeX size={18} />
                  <span className="text-xs">Vocals Off</span>
                </div>
              </button>

              <button
                onClick={() => setMicEnabled(!micEnabled)}
                className={`p-3 rounded-lg border transition-all ${
                  micEnabled
                    ? 'bg-green-500/20 border-green-500/50 text-green-200'
                    : 'bg-slate-800/50 border-slate-700 text-slate-400'
                }`}
              >
                <div className="flex flex-col items-center gap-1">
                  <Mic2 size={18} className={micEnabled ? 'animate-pulse' : ''} />
                  <span className="text-xs">Mic</span>
                </div>
              </button>

              <button
                onClick={() => setDuetMode(!duetMode)}
                className={`p-3 rounded-lg border transition-all ${
                  duetMode
                    ? 'bg-purple-500/20 border-purple-500/50 text-purple-200'
                    : 'bg-slate-800/50 border-slate-700 text-slate-400'
                }`}
              >
                <div className="flex flex-col items-center gap-1">
                  <Users size={18} />
                  <span className="text-xs">Duet</span>
                </div>
              </button>
            </div>
          </div>
        </div>

        {/* Key & Tempo */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <h3 className="text-slate-100 mb-4">Key & Tempo</h3>
          <div className="space-y-4">
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
                  <div className="text-xl text-cyan-400">
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

            <div>
              <label className="text-sm text-slate-300 mb-2 block">Tempo</label>
              <div className="flex items-center gap-3">
                <button
                  onClick={() => setTempo(Math.max(-20, tempo - 5))}
                  className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
                >
                  <ChevronDown size={18} />
                </button>
                <div className="flex-1 text-center">
                  <div className="text-xl text-purple-400">
                    {tempo > 0 ? `+${tempo}` : tempo}%
                  </div>
                  <div className="text-xs text-slate-500">speed</div>
                </div>
                <button
                  onClick={() => setTempo(Math.min(20, tempo + 5))}
                  className="p-2 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-slate-700 transition-all"
                >
                  <ChevronUp size={18} />
                </button>
              </div>
            </div>
          </div>
        </div>

        {/* AI Features */}
        <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
          <h3 className="text-slate-100 mb-4">AI Features</h3>
          <div className="space-y-2">
            {[
              'Pick Songs for My Voice',
              'AI Vocal Coach',
              'Auto-Detect Pitch',
              'Generate Party Queue',
            ].map((feature) => (
              <button
                key={feature}
                className="w-full p-3 rounded-lg bg-slate-800/50 text-slate-300 hover:text-white hover:bg-gradient-to-r hover:from-pink-500/20 hover:to-purple-500/20 hover:border-pink-500/30 border border-transparent transition-all text-left text-sm"
              >
                {feature}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Queue */}
      <div className="bg-gradient-to-br from-slate-900/80 to-slate-800/80 backdrop-blur-xl rounded-xl border border-slate-700/50 p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-slate-100">Party Queue ({queue.length})</h3>
          <button className="flex items-center gap-2 px-4 py-2 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 text-white hover:shadow-lg hover:shadow-pink-500/30 transition-all">
            <Plus size={16} />
            <span>Add Song</span>
          </button>
        </div>
        <div className="space-y-2">
          {queue.map((song, index) => (
            <div
              key={song.id}
              className={`flex items-center gap-4 p-4 rounded-xl border transition-all group ${
                index === 0
                  ? 'bg-gradient-to-r from-pink-500/20 to-purple-500/20 border-pink-500/30'
                  : 'bg-slate-950/50 border-slate-700/30 hover:border-pink-500/20'
              }`}
            >
              <div className="text-slate-500 w-8">{index + 1}</div>
              <Music size={18} className={index === 0 ? 'text-pink-400' : 'text-slate-400'} />
              <div className="flex-1 min-w-0">
                <h4 className={`text-sm truncate ${index === 0 ? 'text-pink-100' : 'text-slate-200'}`}>
                  {song.title}
                </h4>
                <p className="text-xs text-slate-400 truncate">{song.artist}</p>
              </div>
              <div className="flex items-center gap-3 text-xs">
                {song.singer && (
                  <span className="px-2 py-1 rounded bg-purple-500/20 text-purple-200">{song.singer}</span>
                )}
                <span className="px-2 py-1 rounded bg-slate-800 text-slate-400">Key: {song.key}</span>
                <span className="text-slate-500">{song.duration}</span>
              </div>
              <button className="p-1.5 rounded-lg text-slate-500 hover:text-red-400 hover:bg-slate-800 transition-all opacity-0 group-hover:opacity-100">
                <X size={16} />
              </button>
            </div>
          ))}
        </div>
      </div>

      <div className="h-8" />
    </div>
  );
}
