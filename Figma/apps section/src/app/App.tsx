import { useState } from 'react';
import { StreamingLibrary } from './components/streaming-library';

export default function App() {
  return (
    <div className="min-h-screen bg-[#0a0a0f]">
      <StreamingLibrary />
    </div>
  );
}
