import { useEffect, useState } from 'react';
import { DesignSmartHomeApp } from './DesignSmartHomeApp';
import { SpeechStudioApp } from './SpeechStudioApp';
import { requestState, subscribe } from './bridge';
import type { SmartHomeSnapshot } from './types';

export default function App() {
  const mode = new URLSearchParams(window.location.search).get('mode');
  if (mode === 'speech') {
    return <SpeechStudioApp />;
  }

  const [snapshot, setSnapshot] = useState<SmartHomeSnapshot | null>(null);
  const [bridgeError, setBridgeError] = useState('');
  const [bridgeNotice, setBridgeNotice] = useState('');
  const [bridgeEventId, setBridgeEventId] = useState(0);

  useEffect(() => {
    const unsubscribe = subscribe((message) => {
      if (message.type === 'smart-home.state') {
        setSnapshot(message.payload as SmartHomeSnapshot);
        setBridgeError('');
      }

      if (message.type === 'smart-home.actionResult') {
        const payload = message.payload as { message?: string };
        setBridgeNotice(payload.message ?? 'Smart Home action completed');
        setBridgeError('');
        setBridgeEventId((current) => current + 1);
      }

      if (message.type === 'smart-home.error') {
        const payload = message.payload as { message?: string };
        setBridgeError(payload.message ?? 'Unknown Smart Home error');
        setBridgeNotice('');
        setBridgeEventId((current) => current + 1);
      }
    });

    requestState();
    return unsubscribe;
  }, []);

  return (
    <DesignSmartHomeApp
      snapshot={snapshot}
      bridgeError={bridgeError}
      bridgeNotice={bridgeNotice}
      bridgeEventId={bridgeEventId}
    />
  );
}
