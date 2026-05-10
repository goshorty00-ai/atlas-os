import { Toolbar } from './components/dj/Toolbar';
import { Waveforms } from './components/dj/Waveforms';
import { Deck } from './components/dj/Deck';
import { CdjDeck } from './components/dj/CdjDeck';
import { Mixer } from './components/dj/Mixer';
import { Browser } from './components/dj/Browser';
import { AiDjPanel } from './components/dj/AiDjPanel';
import { useDjState } from './dj/bridge';
import {
  selectBlendInsights,
  selectBrowserSources,
  selectVisibleBrowserTracks,
} from './dj/selectors';
import type { DeckLabel, DjDeckState } from './dj/types';

const djBackdropUrl = 'https://atlas-ui-dj-assets/Logos/robot_dj_backdrop_2560x960.png';

function PerformanceBackdrop({ children, className, style }: { children: React.ReactNode; className: string; style?: React.CSSProperties }) {
  return (
    <main className="relative min-h-0 overflow-hidden bg-[#0c1016]">
      <div
        className="pointer-events-none absolute inset-0 opacity-55"
        style={{
          backgroundImage: [
            'linear-gradient(180deg, rgba(8,10,14,0.16) 0%, rgba(8,10,14,0.04) 28%, rgba(8,10,14,0.08) 68%, rgba(8,10,14,0.18) 100%)',
            'radial-gradient(circle at center, rgba(8,10,14,0.01) 0%, rgba(8,10,14,0.10) 55%, rgba(8,10,14,0.22) 100%)',
            `url("${djBackdropUrl}")`,
          ].join(','),
          backgroundPosition: 'center, center, center',
          backgroundRepeat: 'no-repeat, no-repeat, no-repeat',
          backgroundSize: 'cover, cover, cover',
        }}
      />
      <div className="pointer-events-none absolute inset-y-0 left-1/2 z-[1] w-[200px] -translate-x-1/2 bg-gradient-to-r from-transparent via-[#10141c]/45 to-transparent" />
      <div className="relative z-10 h-full min-h-0">
        <div className={className} style={style}>
          {children}
        </div>
      </div>
    </main>
  );
}

export default function App() {
  const { state, actions } = useDjState();
  const browserTracks = selectVisibleBrowserTracks(state);
  const browserSources = selectBrowserSources(state);
  const blendInsights = selectBlendInsights(state);
  const mode = state.consoleMode;

  /* ── Performance area depends on console mode ─────────────── */
  let performanceArea: React.ReactNode;

  if (mode === 'four-deck') {
    performanceArea = (
      <PerformanceBackdrop className="grid h-full min-h-0 grid-cols-[minmax(0,1fr)_150px_minmax(0,1fr)] grid-rows-2 overflow-hidden">
          <Deck deck={state.decks.A} side="left" actions={actions} compact />
          <div className="row-span-2">
            <Mixer decks={state.decks} mixer={state.mixer} actions={actions} isRecording={state.isRecording} />
          </div>
          <Deck deck={state.decks.B} side="right" actions={actions} compact />
          <Deck deck={state.decks.C} side="left" actions={actions} compact />
          <Deck deck={state.decks.D} side="right" actions={actions} compact />
      </PerformanceBackdrop>
    );
  } else if (mode === 'cdj') {
    performanceArea = (
      <PerformanceBackdrop className="grid h-full min-h-0 grid-cols-[minmax(0,1fr)_200px_minmax(0,1fr)] overflow-hidden">
          <CdjDeck deck={state.decks.A} side="left" actions={actions} />
          <Mixer decks={state.decks} mixer={state.mixer} actions={actions} isRecording={state.isRecording} />
          <CdjDeck deck={state.decks.B} side="right" actions={actions} />
      </PerformanceBackdrop>
    );
  } else {
    performanceArea = (
      <PerformanceBackdrop
        className="grid h-full min-h-0 overflow-hidden"
        style={{
          gridTemplateColumns:
              'minmax(330px,1fr) 190px minmax(330px,1fr)',
        }}
      >
          <Deck deck={state.decks.A} side="left" actions={actions} />
          <Mixer decks={state.decks} mixer={state.mixer} actions={actions} isRecording={state.isRecording} />
          <Deck deck={state.decks.B} side="right" actions={actions} />
      </PerformanceBackdrop>
    );
  }

  /* ── Waveform rows depend on mode ─────────────────────────── */
  const waveformRows = mode === 'four-deck'
    ? '46px 56px minmax(0,1fr) 180px'
    : '46px 70px minmax(312px,1.35fr) minmax(240px,0.95fr)';

  return (
    <div
      className="grid h-full min-h-0 overflow-hidden bg-[#08090d] text-zinc-50"
      style={{ gridTemplateRows: waveformRows }}
    >
      {/* Row 1 – toolbar with track info */}
      <Toolbar
        decks={state.decks}
        mixer={state.mixer}
        online={state.online}
        insights={blendInsights}
        consoleMode={state.consoleMode}
        onLoadFile={actions.loadFile}
        onSetConsoleMode={actions.setConsoleMode}
      />

      {/* Row 2 – waveforms */}
      {mode === 'four-deck' ? (
        <div className="grid grid-cols-2 grid-rows-2 min-h-0 overflow-hidden">
          <Waveforms decks={{ A: state.decks.A, B: state.decks.B }} onSeek={actions.seek} mini />
          <Waveforms decks={{ A: state.decks.C, B: state.decks.D }} onSeek={actions.seek} mini deckLabels={['C', 'D']} />
        </div>
      ) : (
        <Waveforms decks={{ A: state.decks.A, B: state.decks.B }} onSeek={actions.seek} />
      )}

      {/* Row 3 – performance */}
      {performanceArea}

      {/* Row 4 – browser */}
      <Browser
        browser={state.browser}
        decks={state.decks}
        tracks={browserTracks}
        sources={browserSources}
        samples={state.samples}
        actions={actions}
        onAddFolder={actions.addFolder}
        onAddFiles={actions.addFiles}
        onAddSamplesFolder={actions.addSamplesFolder}
        onPlaySample={actions.playSample}
        consoleMode={state.consoleMode}
      />

      {/* AI DJ overlay */}
      <AiDjPanel decks={state.decks} library={state.library} autoMix={state.autoMix} actions={actions} />
    </div>
  );
}
