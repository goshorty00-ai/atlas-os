// WebView2 message bridge for Atlas Smart Home

declare global {
  interface Window {
    chrome?: {
      webview?: {
        // WebView2 accepts both string and object - passing object avoids double-serialization
        postMessage: (msg: string | object) => void;
        addEventListener: (event: string, handler: (e: any) => void) => void;
        removeEventListener: (event: string, handler: (e: any) => void) => void;
      };
    };
  }
}

export function postToHost(type: string, payload?: unknown) {
  try {
    // Pass as object so WebView2 serializes it as JSON (not a quoted string)
    window.chrome?.webview?.postMessage({ type, payload: payload ?? null });
  } catch {
    // Not running inside WebView2
  }
}

export function onHostMessage(handler: (type: string, payload: unknown) => void): () => void {
  const listener = (e: any) => {
    try {
      // PostWebMessageAsJson delivers e.data as an already-parsed object
      // PostWebMessageAsString delivers e.data as a string
      const raw = e?.data ?? e;
      let data: any;
      if (typeof raw === 'string') {
        data = JSON.parse(raw);
      } else if (raw && typeof raw === 'object') {
        data = raw;
      } else {
        return;
      }
      if (data?.type) handler(data.type, data.payload ?? null);
    } catch {
      // ignore malformed
    }
  };
  window.chrome?.webview?.addEventListener('message', listener);
  return () => window.chrome?.webview?.removeEventListener('message', listener);
}

export function requestState() {
  postToHost('smart-home.getState');
}
