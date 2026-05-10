export type AtlasMessage<T = unknown> = {
  type: string;
  payload?: T;
};

type AtlasListener = (msg: AtlasMessage) => void;

function hasWebViewBridge(): boolean {
  // @ts-expect-error webview2 injects chrome.webview
  return typeof window !== 'undefined' && !!window.chrome?.webview;
}

export function postToHost<T = unknown>(type: string, payload?: T): void {
  if (!hasWebViewBridge()) return;
  const msg: AtlasMessage = { type, payload };
  // @ts-expect-error webview2 injects chrome.webview
  window.chrome.webview.postMessage(msg);
}

export function onHostMessage(handler: AtlasListener): () => void {
  if (!hasWebViewBridge()) return () => {};
  const wrapped = (ev: any) => {
    const data = ev?.data;
    if (!data || typeof data.type !== 'string') return;
    handler(data as AtlasMessage);
  };

  // @ts-expect-error webview2 injects chrome.webview
  window.chrome.webview.addEventListener('message', wrapped);
  return () => {
    try {
      // @ts-expect-error webview2 injects chrome.webview
      window.chrome.webview.removeEventListener('message', wrapped);
    } catch {
    }
  };
}

