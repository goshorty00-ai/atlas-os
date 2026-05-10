import { StreamSourceCard } from './StreamSourceCard';

interface StreamSource {
  sourceName: string;
  quality: string;
  fileSize: string;
  audioLanguage: string;
  subtitles: string[];
  seederCount?: number;
}

interface RightPanelProps {
  title: string;
  sources: StreamSource[];
  onPlay: (source: StreamSource) => void;
}

export function RightPanel({ title, sources, onPlay }: RightPanelProps) {
  return (
    <div className="h-full bg-slate-950/50 backdrop-blur-md border-l border-white/5 p-6 overflow-auto">
      <div className="mb-6">
        <div className="flex items-center gap-2 mb-2">
          <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
          <span className="text-green-400 text-sm font-medium">AI Stream Selection</span>
        </div>
        <h2 className="text-white text-xl font-semibold">{title}</h2>
      </div>

      <div className="space-y-3">
        {sources.map((source, index) => (
          <StreamSourceCard
            key={index}
            sourceName={source.sourceName}
            quality={source.quality}
            fileSize={source.fileSize}
            audioLanguage={source.audioLanguage}
            subtitles={source.subtitles}
            seederCount={source.seederCount}
            onPlay={() => onPlay(source)}
          />
        ))}
      </div>

      <div className="mt-6 p-4 bg-purple-500/10 backdrop-blur-sm rounded-lg border border-purple-500/20">
        <div className="flex items-start gap-3">
          <span className="text-2xl">🤖</span>
          <div>
            <h3 className="text-purple-300 font-semibold mb-1">AI Recommendation</h3>
            <p className="text-gray-300 text-sm">
              Based on your network speed and device capabilities, we recommend the{' '}
              <span className="text-purple-400 font-semibold">{sources[0]?.quality}</span> source for the
              best viewing experience.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
