type Envelope = {
  type: string;
  payload: unknown;
};

type MessageHandler = (message: Envelope) => void;

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string | object) => void;
        addEventListener: (event: 'message', listener: (event: MessageEvent<Envelope>) => void) => void;
        removeEventListener: (event: 'message', listener: (event: MessageEvent<Envelope>) => void) => void;
      };
    };
  }
}

function getWebView() {
  return window.chrome?.webview;
}

function post(type: string, payload: unknown = {}) {
  const webView = getWebView();
  if (!webView) {
    return;
  }

  webView.postMessage({ type, payload });
}

export function subscribeSpeech(handler: MessageHandler) {
  const webView = getWebView();
  if (!webView) {
    return () => {};
  }

  const listener = (event: MessageEvent<Envelope>) => {
    handler(event.data);
  };

  webView.addEventListener('message', listener);
  return () => webView.removeEventListener('message', listener);
}

export function requestSpeechState() {
  post('speech.getState');
}

export function addSpeechEntry(bucket: 'startupGreetings' | 'chatGreetings' | 'quickResponses', text: string) {
  post('speech.addEntry', { bucket, text });
}

export function removeSpeechEntry(bucket: 'startupGreetings' | 'chatGreetings' | 'quickResponses', text: string) {
  post('speech.removeEntry', { bucket, text });
}

export function addSpeechRule(phrase: string, responseText: string) {
  post('speech.addRule', { phrase, responseText });
}

export function removeSpeechRule(id: string, phrase: string) {
  post('speech.removeRule', { id, phrase });
}

export function generateSpeechPreset(preset: 'pro' | 'unfiltered') {
  post('speech.generatePreset', { preset });
}