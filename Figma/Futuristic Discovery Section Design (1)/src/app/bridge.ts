// Bridge for communicating with C# host
declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string) => void;
        addEventListener: (event: string, handler: (e: any) => void) => void;
      };
    };
  }
}

type MessageHandler = (type: string, payload: any) => void;

const messageHandlers: MessageHandler[] = [];

// Listen for messages from C# host
if (window.chrome?.webview) {
  console.log('[Bridge] WebView2 detected, setting up message listener');
  window.chrome.webview.addEventListener('message', (event) => {
    try {
      console.log('[Bridge] Received message from host:', event.data);
      const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
      const { type, payload } = data;
      console.log('[Bridge] Parsed message:', { type, payload });
      messageHandlers.forEach(handler => handler(type, payload));
    } catch (error) {
      console.error('[Bridge] Failed to parse message:', error);
    }
  });
} else {
  console.warn('[Bridge] WebView2 not available - running in browser mode');
}

export function postToHost(type: string, payload?: any) {
  const message = JSON.stringify({ type, payload });
  console.log('[Bridge] Posting to host:', { type, payload });
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage(message);
    console.log('[Bridge] Message sent successfully');
  } else {
    console.warn('[Bridge] WebView2 not available, message not sent:', { type, payload });
  }
}

export function onHostMessage(handler: MessageHandler) {
  messageHandlers.push(handler);
  return () => {
    const index = messageHandlers.indexOf(handler);
    if (index > -1) messageHandlers.splice(index, 1);
  };
}

// Request initial data
export function requestDiscoveryData() {
  console.log('[Bridge] Requesting discovery data...');
  postToHost('discovery.getData');
}

// Request search
export function searchMedia(query: string, filters?: { type?: string; genre?: string }) {
  postToHost('discovery.search', { query, filters });
}

// Request media details
export function getMediaDetails(id: string, type: 'movie' | 'tv' | 'music' | 'game') {
  postToHost('discovery.getDetails', { id, type });
}
