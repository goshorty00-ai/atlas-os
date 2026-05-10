import { startTransition, useEffect, useReducer } from 'react';
import type { Dispatch } from 'react';
import { djReducer, initialState } from './state';
import type { DeckLabel, DjActions, DjConsoleMode, DjState } from './types';

const DJ_STATE_STORAGE_KEY = 'atlas.dj.persistedState.v1';

interface WebViewMessageEvent {
  data: unknown;
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string) => void;
        addEventListener: (type: 'message', listener: (event: WebViewMessageEvent) => void) => void;
        removeEventListener: (type: 'message', listener: (event: WebViewMessageEvent) => void) => void;
      };
    };
  }
}

function postMessage(message: Record<string, unknown>) {
  window.chrome?.webview?.postMessage(JSON.stringify(message));
}

function hasWebViewBridge() {
  return Boolean(window.chrome?.webview);
}

function send(dispatch: Dispatch<Parameters<typeof djReducer>[1]>, message: Record<string, unknown>, optimistic?: Parameters<typeof djReducer>[1]) {
  if (optimistic) {
    dispatch(optimistic);
  }

  postMessage(message);
}

function tryParseJson(payload: unknown) {
  if (typeof payload !== 'string') {
    return payload;
  }

  try {
    return JSON.parse(payload);
  } catch {
    return null;
  }
}

function hydratePersistedState(): DjState {
  try {
    const raw = window.localStorage.getItem(DJ_STATE_STORAGE_KEY);
    if (!raw) {
      return initialState;
    }

    const parsed = JSON.parse(raw) as Partial<DjState>;
    return {
      ...initialState,
      ...parsed,
      online: false,
      lastUpdatedUtc: '',
      decks: {
        A: { ...initialState.decks.A, ...(parsed.decks?.A ?? {}) },
        B: { ...initialState.decks.B, ...(parsed.decks?.B ?? {}) },
        C: { ...initialState.decks.C, ...(parsed.decks?.C ?? {}) },
        D: { ...initialState.decks.D, ...(parsed.decks?.D ?? {}) },
      },
      mixer: { ...initialState.mixer, ...(parsed.mixer ?? {}) },
      browser: { ...initialState.browser, ...(parsed.browser ?? {}) },
      controllers: initialState.controllers,
      autoMix: initialState.autoMix,
    };
  } catch {
    return initialState;
  }
}

function createPersistedSnapshot(state: DjState) {
  return {
    decks: state.decks,
    mixer: state.mixer,
    browser: state.browser,
    library: state.library,
    samples: state.samples,
    consoleMode: state.consoleMode,
    isRecording: state.isRecording,
    recordingPath: state.recordingPath,
    autoMix: state.autoMix,
  };
}

export function useDjState() {
  const [state, dispatch] = useReducer(djReducer, initialState, hydratePersistedState);

  const getLibraryTrack = (path: string) => state.library.find((track) => track.path === path);

  useEffect(() => {
    if (hasWebViewBridge()) {
      return;
    }

    const hasActivePlayback = state.decks.A.isPlaying || state.decks.B.isPlaying || state.decks.C.isPlaying || state.decks.D.isPlaying;
    if (!hasActivePlayback) {
      return;
    }

    const timer = window.setInterval(() => {
      dispatch({ type: 'tick', deltaMs: 120 });
    }, 120);

    return () => {
      window.clearInterval(timer);
    };
  }, [state.decks.A.isPlaying, state.decks.B.isPlaying, state.decks.C.isPlaying, state.decks.D.isPlaying]);

  useEffect(() => {
    const webview = window.chrome?.webview;
    if (!webview) {
      return;
    }

    const onMessage = (event: WebViewMessageEvent) => {
      const payload = tryParseJson(event.data);
      if (!payload || typeof payload !== 'object') {
        return;
      }

      if ((payload as { type?: string }).type === 'dj.state') {
        startTransition(() => {
          dispatch({ type: 'server-state', state: (payload as { state?: DjState }).state });
        });
      }
    };

    webview.addEventListener('message', onMessage);
    postMessage({ type: 'dj.requestState' });

    return () => {
      webview.removeEventListener('message', onMessage);
    };
  }, []);

  useEffect(() => {
    try {
      window.localStorage.setItem(DJ_STATE_STORAGE_KEY, JSON.stringify(createPersistedSnapshot(state)));
    } catch {
      /* ignore persistence failures */
    }
  }, [state]);

  const actions: DjActions = {
    loadFile: (deck) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.loadFile', deck });
        return;
      }

      const fallbackTrack = state.browser.loadHistory
        .map((path) => state.library.find((track) => track.path === path))
        .find(Boolean) ?? state.library[0];

      if (!fallbackTrack) {
        return;
      }

      dispatch({ type: 'register-track-load', path: fallbackTrack.path });
      dispatch({ type: 'load-track', deck, track: fallbackTrack });
    },
    loadTrack: (deck, path) => {
      const track = getLibraryTrack(path);

      dispatch({ type: 'register-track-load', path });

      if (track) {
        dispatch({ type: 'load-track', deck, track });
      }

      postMessage({ type: 'dj.deck.loadTrack', deck, path });
    },
    play: (deck) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.play', deck });
        return;
      }

      dispatch({ type: 'play', deck });
    },
    pause: (deck) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.pause', deck });
        return;
      }

      dispatch({ type: 'pause', deck });
    },
    cue: (deck) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.cue', deck });
        return;
      }

      dispatch({ type: 'cue', deck });
    },
    sync: (deck) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.sync', deck });
        return;
      }

      dispatch({ type: 'sync', deck });
    },
    bend: (deck, delta) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.bend', deck, delta });
        return;
      }

      dispatch({ type: 'bend', deck, delta });
    },
    seek: (deck, value) => {
      if (hasWebViewBridge()) {
        postMessage({ type: 'dj.deck.seek', deck, value });
        return;
      }

      dispatch({ type: 'seek', deck, value });
    },
    setCue: (deck, value) => send(dispatch, { type: 'dj.deck.setCue', deck, value }, { type: 'set-cue', deck, value }),
    setTempo: (deck, value) => send(dispatch, { type: 'dj.deck.setTempo', deck, value }, { type: 'set-tempo', deck, value }),
    setPitchRange: (deck, value) => send(dispatch, { type: 'dj.deck.setPitchRange', deck, value }, { type: 'set-pitch-range', deck, value }),
    setVolume: (deck, value) => send(dispatch, { type: 'dj.deck.setVolume', deck, value }, { type: 'patch-deck', deck, patch: { volume: value } }),
    setGain: (deck, value) => send(dispatch, { type: 'dj.deck.setGain', deck, value }, { type: 'patch-deck', deck, patch: { gain: value } }),
    setEq: (deck, band, value) => {
      const patch = band === 'high' ? { eqHigh: value } : band === 'mid' ? { eqMid: value } : { eqLow: value };
      send(dispatch, { type: 'dj.deck.setEq', deck, band, value }, { type: 'patch-deck', deck, patch });
    },
    setFilter: (deck, value) => send(dispatch, { type: 'dj.deck.setFilter', deck, value }, { type: 'patch-deck', deck, patch: { filter: value } }),
    toggleCueMonitor: (deck) => send(dispatch, { type: 'dj.deck.toggleCueMonitor', deck }, { type: 'toggle-cue-monitor', deck }),
    loopIn: (deck) => send(dispatch, { type: 'dj.deck.loopIn', deck }, { type: 'loop-in', deck }),
    loopOut: (deck) => send(dispatch, { type: 'dj.deck.loopOut', deck }, { type: 'loop-out', deck }),
    loopClear: (deck) => send(dispatch, { type: 'dj.deck.loopClear', deck }, { type: 'loop-clear', deck }),
    hotCue: (deck, cueIndex) => send(dispatch, { type: 'dj.deck.hotCue', deck, cueIndex }, { type: 'hot-cue', deck, cueIndex }),
    setLoopSize: (deck, value) => send(dispatch, { type: 'dj.deck.setLoopSize', deck, value }, { type: 'set-loop-size', deck, value }),
    setWaveformVisibility: (deck, visible) => dispatch({ type: 'set-waveform-visibility', deck, visible }),
    setCrossfader: (value) => send(dispatch, { type: 'dj.mixer.setCrossfader', value }, { type: 'patch-mixer', patch: { crossfader: value } }),
    setCrossfaderCurve: (value) => send(dispatch, { type: 'dj.mixer.setCrossfaderCurve', value }, { type: 'patch-mixer', patch: { crossfaderCurve: value } }),
    setMasterVolume: (value) => send(dispatch, { type: 'dj.mixer.setMasterVolume', value }, { type: 'patch-mixer', patch: { masterVolume: value } }),
    setCueMix: (value) => send(dispatch, { type: 'dj.mixer.setCueMix', value }, { type: 'patch-mixer', patch: { cueMix: value } }),
    setHeadphoneVolume: (value) => send(dispatch, { type: 'dj.mixer.setHeadphoneVolume', value }, { type: 'patch-mixer', patch: { headphoneVolume: value } }),
    setMicVolume: (value) => send(dispatch, { type: 'dj.mixer.setMicVolume', value }, { type: 'patch-mixer', patch: { micVolume: value } }),
    setEffectMode: (value) => send(dispatch, { type: 'dj.mixer.setEffectMode', value }, { type: 'patch-mixer', patch: { effectMode: value } }),
    setBrowserSearch: (value) => dispatch({ type: 'set-browser-search', value }),
    setBrowserSection: (value) => dispatch({ type: 'set-browser-section', value }),
    setBrowserSource: (value) => dispatch({ type: 'set-browser-source', value }),
    cycleBrowserFilter: () => dispatch({ type: 'cycle-browser-filter' }),
    cycleBrowserSort: () => dispatch({ type: 'cycle-browser-sort' }),
    toggleBrowserColumns: () => dispatch({ type: 'toggle-browser-columns' }),
    addFolder: () => postMessage({ type: 'dj.browser.addFolder' }),
    addFiles: () => postMessage({ type: 'dj.browser.addFiles' }),
    setConsoleMode: (mode: DjConsoleMode) => dispatch({ type: 'set-console-mode', mode }),
    startRecording: () => postMessage({ type: 'dj.record.start' }),
    stopRecording: () => postMessage({ type: 'dj.record.stop' }),
    startAutoMix: (sourceDeck, targetDeck, transitionBeats = 16) => postMessage({ type: 'dj.automix.start', sourceDeck, targetDeck, transitionBeats }),
    stopAutoMix: () => postMessage({ type: 'dj.automix.stop' }),
    playSample: (path) => postMessage({ type: 'dj.samples.play', path }),
    addSamplesFolder: () => postMessage({ type: 'dj.samples.addFolder' }),
  };

  /* ── Keyboard shortcuts ─────────────────────────────────────── */
  useEffect(() => {
    const XFADE_STEP = 5;

    const onKeyDown = (e: KeyboardEvent) => {
      // Ignore when typing in inputs
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;

      switch (e.key.toLowerCase()) {
        /* ── Crossfader ── */
        case 'z': // nudge crossfader left (toward A)
          actions.setCrossfader(Math.max(0, state.mixer.crossfader - XFADE_STEP));
          e.preventDefault();
          break;
        case 'x': // nudge crossfader right (toward B)
          actions.setCrossfader(Math.min(100, state.mixer.crossfader + XFADE_STEP));
          e.preventDefault();
          break;
        case 'c': // snap crossfader to centre
          actions.setCrossfader(50);
          e.preventDefault();
          break;

        /* ── Deck A ── */
        case 'q': // play/pause A
          e.preventDefault();
          state.decks.A.isPlaying ? actions.pause('A') : actions.play('A');
          break;
        case 'w': // cue A
          e.preventDefault();
          actions.cue('A');
          break;

        /* ── Deck B ── */
        case 'p': // play/pause B
          e.preventDefault();
          state.decks.B.isPlaying ? actions.pause('B') : actions.play('B');
          break;
        case 'o': // cue B
          e.preventDefault();
          actions.cue('B');
          break;

        /* ── Volume ── */
        case 'arrowup':
          if (e.shiftKey)
            actions.setVolume('B', Math.min(100, state.decks.B.volume + 5));
          else
            actions.setVolume('A', Math.min(100, state.decks.A.volume + 5));
          e.preventDefault();
          break;
        case 'arrowdown':
          if (e.shiftKey)
            actions.setVolume('B', Math.max(0, state.decks.B.volume - 5));
          else
            actions.setVolume('A', Math.max(0, state.decks.A.volume - 5));
          e.preventDefault();
          break;
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [state.mixer.crossfader, state.decks.A.isPlaying, state.decks.B.isPlaying, state.decks.A.volume, state.decks.B.volume, actions]);

  return {
    state,
    actions,
  };
}

export const useDjBridge = useDjState;

export type {
  DeckLabel,
  DjActions,
  DjBrowserFilterMode,
  DjBrowserSection,
  DjBrowserSortMode,
  DjBrowserState,
  DjConsoleMode,
  DjCrossfaderCurve,
  DjCuePoint,
  DjDeckState,
  DjLibraryTrack,
  DjLoopSize,
  DjMixerState,
  DjState,
  DjTrack,
} from './types';