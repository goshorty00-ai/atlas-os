import { useState, useRef, useCallback, useEffect } from 'react';
import { Outlet } from 'react-router';
import { Sidebar } from '../components/sidebar';

function postBridge(msg: object) {
  try { (window as any).chrome?.webview?.postMessage(msg); } catch { /* no bridge */ }
}

export function RootLayout() {
  const [immersive, setImmersive] = useState(false);
  const [showArrow, setShowArrow] = useState(false);
  const arrowTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Listen for WPF → React header-collapse notifications
  useEffect(() => {
    function onBridgeMessage(e: Event) {
      try {
        const raw = (e as MessageEvent).data;
        const msg = typeof raw === 'string' ? JSON.parse(raw) : raw;
        if (msg?.type === 'mediahub.chromeCollapsed') {
          const collapsed = !!msg?.payload?.collapsed;
          console.log('[MediaHubImmersive] React received chromeCollapsed=' + collapsed);
          setImmersive(collapsed);
          if (!collapsed) setShowArrow(false);
          // Send confirmation back so C# log confirms React processed the message
          postBridge({ type: 'servers.clientError', payload: { message: `[MediaHubImmersive] setImmersive=${collapsed}`, source: 'root-layout.tsx' } });
        }
      } catch (err) {
        console.error('[MediaHubImmersive] onBridgeMessage error', err);
      }
    }
    try {
      (window as any).chrome?.webview?.addEventListener('message', onBridgeMessage);
    } catch { /* no WebView2 in dev */ }
    // Request current collapsed state from WPF (handles the case where header was
    // already collapsed before React finished loading and the initial message was lost).
    postBridge({ type: 'mediahub.requestChromeState' });
    return () => {
      try {
        (window as any).chrome?.webview?.removeEventListener('message', onBridgeMessage);
      } catch { /* ignore */ }
    };
  }, []);

  const exitImmersive = useCallback(() => {
    setImmersive(false);
    setShowArrow(false);
    if (arrowTimerRef.current) clearTimeout(arrowTimerRef.current);
    // Ask WPF to restore the Atlas header
    postBridge({ type: 'mediahub.immersive.set', payload: { enabled: false } });
  }, []);

  const handleRevealMove = useCallback(() => {
    setShowArrow(true);
    if (arrowTimerRef.current) clearTimeout(arrowTimerRef.current);
    arrowTimerRef.current = setTimeout(() => setShowArrow(false), 2000);
  }, []);

  return (
    <div className="size-full flex bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950 relative">
      <Sidebar immersive={immersive} />

      {/* Reveal zone — 24 px transparent strip at left edge, only active in immersive mode */}
      {immersive && (
        <div
          className="fixed left-0 top-0 h-full z-50"
          style={{ width: '24px' }}
          onMouseMove={handleRevealMove}
          onMouseEnter={handleRevealMove}
        >
          {showArrow && (
            <button
              onClick={exitImmersive}
              className="absolute left-0 top-1/2 -translate-y-1/2 flex items-center justify-center cursor-pointer"
              style={{
                width: '22px',
                height: '64px',
                background: 'rgba(6,182,212,0.15)',
                border: '1px solid rgba(6,182,212,0.4)',
                borderLeft: 'none',
                borderRadius: '0 8px 8px 0',
              }}
              title="Restore header"
            >
              <svg width="10" height="14" viewBox="0 0 10 14" fill="none">
                <path d="M2 2L8 7L2 12" stroke="#67e8f9" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              </svg>
            </button>
          )}
        </div>
      )}

      <div id="atlas-main-scroll" className="flex-1 overflow-y-auto">
        <div className="max-w-[1800px] mx-auto px-4 py-6">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
