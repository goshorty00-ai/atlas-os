import { motion } from 'motion/react';
import { Bot, Mic, Send, Sparkles, Volume2, Wand2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { askAtlas, runSmartHomeCommand } from '../bridge';
import type { SmartHomeSnapshot } from '../types';

const SMART_HOME_SPEECH_WIRED = false;
const SMART_HOME_MIC_WIRED = false;

interface AIAssistantProps {
  snapshot: SmartHomeSnapshot | null;
  latestNotice: string;
  latestError: string;
  bridgeEventId: number;
}

function buildSuggestions(snapshot: SmartHomeSnapshot | null) {
  if (!snapshot) {
    return [] as string[];
  }

  const greetingSuggestions = snapshot.customGreetings.slice(0, 2).map((greeting) => greeting.phrase);
  const suggestions = snapshot.customCommands.slice(0, 3).map((command) => command.phrase);
  suggestions.unshift(...snapshot.customScenes.slice(0, 3).map((scene) => scene.phrase || scene.name));
  suggestions.unshift(...greetingSuggestions);
  const devices = snapshot.providers.flatMap((provider) => provider.devices.map((device) => ({ provider, device })));

  for (const entry of devices) {
    if (entry.device.capabilities.some((capability) => capability.instance === 'powerSwitch')) {
      suggestions.push(`turn ${entry.device.name} on`);
    }
    if (entry.device.capabilities.some((capability) => capability.instance === 'volume')) {
      suggestions.push(`set ${entry.device.name} volume to 18`);
    }
    if (entry.device.capabilities.some((capability) => capability.instance.toLowerCase().includes('brightness'))) {
      suggestions.push(`set ${entry.device.name} brightness to 40`);
    }

    if (suggestions.length >= 6) {
      break;
    }
  }

  return suggestions.slice(0, 6);
}

export function AIAssistant({ snapshot, latestNotice, latestError, bridgeEventId }: AIAssistantProps) {
  const [input, setInput] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [statusMessage, setStatusMessage] = useState('Ask Atlas for real AI guidance, or run a direct Smart Home command locally when you want immediate device control.');
  const [voiceNote, setVoiceNote] = useState('');

  useEffect(() => {
    if (latestError) {
      setIsSubmitting(false);
      setStatusMessage(latestError);
    } else if (latestNotice) {
      setIsSubmitting(false);
      setStatusMessage(latestNotice);
    }
  }, [bridgeEventId, latestError, latestNotice]);

  useEffect(() => {
    if (!voiceNote) {
      return;
    }

    const timeout = window.setTimeout(() => setVoiceNote(''), 2400);
    return () => window.clearTimeout(timeout);
  }, [voiceNote]);

  const suggestions = useMemo(() => buildSuggestions(snapshot), [snapshot]);

  const submitAskAtlas = (event: React.FormEvent) => {
    event.preventDefault();
    if (!input.trim()) {
      return;
    }

    const commandText = input.trim();
    setIsSubmitting(true);
    setStatusMessage(`Atlas is using the main AI stack for: ${commandText}`);
    askAtlas(commandText);
    setInput('');
  };

  const submitLocalCommand = () => {
    if (!input.trim()) {
      return;
    }

    const commandText = input.trim();
    setIsSubmitting(true);
    setStatusMessage(`Atlas is matching devices and custom phrases for: ${commandText}`);
    runSmartHomeCommand(commandText);
    setInput('');
  };

  const handleMicClick = () => {
    if (!SMART_HOME_MIC_WIRED) {
      setVoiceNote('Mic not wired');
      return;
    }
  };

  const handleSpeechClick = () => {
    if (!SMART_HOME_SPEECH_WIRED) {
      return;
    }
  };

  return (
    <div className="grid grid-cols-1 xl:grid-cols-[1.5fr_0.9fr] gap-5 mt-8">
      <motion.section
        initial={{ opacity: 0, y: 18 }}
        animate={{ opacity: 1, y: 0 }}
        className="rounded-[28px] p-6"
        style={{
          background: 'linear-gradient(145deg, rgba(9, 18, 30, 0.96), rgba(5, 10, 18, 0.86))',
          border: '1px solid rgba(0, 212, 255, 0.18)',
          boxShadow: '0 24px 60px rgba(0, 0, 0, 0.28), inset 0 1px 0 rgba(255,255,255,0.03), 0 0 48px rgba(0, 212, 255, 0.12)',
        }}
      >
        <div className="flex items-center justify-between gap-4 mb-6">
          <div>
            <div className="flex items-center gap-3 mb-2">
              <div className="w-11 h-11 rounded-2xl flex items-center justify-center"
                   style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.24), rgba(0,102,255,0.18))', border: '1px solid rgba(0, 212, 255, 0.24)' }}>
                <Bot className="w-5 h-5 text-cyan-200" />
              </div>
              <div>
                <p className="text-xs uppercase tracking-[0.28em] text-cyan-400/56">Pinned Agent</p>
                <h2 className="text-3xl font-semibold text-cyan-100">Smart Home Command Agent</h2>
              </div>
            </div>
            <p className="text-sm leading-6 text-cyan-100/68 max-w-2xl">
              This panel now runs device commands through Atlas native Smart Home execution, so the same interpreter can power your saved voice phrases, typed commands, and the live command deck below.
            </p>
          </div>

          <div className="grid grid-cols-3 gap-3 min-w-[280px]">
            <MetricCard label="Providers" value={String(snapshot?.configuredProviders ?? 0)} icon={Sparkles} />
            <MetricCard label="Devices" value={String(snapshot?.totalDevices ?? 0)} icon={Wand2} />
            <MetricCard label="Scenes" value={String(snapshot?.customScenes.length ?? 0)} icon={Mic} />
          </div>
        </div>

        <div className="rounded-3xl p-5 mb-5"
             style={{ background: latestError ? 'rgba(255,120,120,0.08)' : 'rgba(0,212,255,0.06)', border: latestError ? '1px solid rgba(255,120,120,0.22)' : '1px solid rgba(0,212,255,0.14)' }}>
          <p className="text-sm text-cyan-100/82 leading-6">{statusMessage}</p>
          {isSubmitting && (
            <div className="mt-3 flex items-center gap-2 text-[11px] uppercase tracking-[0.18em] text-cyan-300/78">
              <div className="w-2 h-2 rounded-full bg-cyan-300 animate-pulse" />
              Processing now
            </div>
          )}
        </div>

        <form onSubmit={submitAskAtlas} className="relative">
          <input
            value={input}
            onChange={(event) => setInput(event.target.value)}
            placeholder="Ask for help, or type a direct command like: set living room tv volume to 18"
            className="w-full px-5 py-4 pr-36 rounded-2xl text-sm bg-transparent outline-none"
            style={{ border: '1px solid rgba(0, 212, 255, 0.24)', color: '#D9FAFF', boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.02)' }}
          />
          {SMART_HOME_SPEECH_WIRED && (
            <button
              type="button"
              onClick={handleSpeechClick}
              className="absolute right-[108px] top-1/2 -translate-y-1/2 h-10 w-10 rounded-xl text-sm flex items-center justify-center"
              style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0, 212, 255, 0.18)', color: '#D8F9FF' }}
              title="Speech output"
              aria-label="Speech output"
            >
              <Volume2 className="w-4 h-4" />
            </button>
          )}
          <button
            type="button"
            onClick={handleMicClick}
            className="absolute right-[60px] top-1/2 -translate-y-1/2 h-10 w-10 rounded-xl text-sm flex items-center justify-center"
            style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0, 212, 255, 0.18)', color: '#D8F9FF' }}
            title="Mic"
            aria-label="Mic"
          >
            <Mic className="w-4 h-4" />
          </button>
          <button
            type="submit"
            disabled={isSubmitting}
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-2.5 rounded-xl text-sm flex items-center gap-2"
            style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.18), rgba(0,102,255,0.18))', border: '1px solid rgba(0, 212, 255, 0.26)', color: '#D8F9FF', opacity: isSubmitting ? 0.7 : 1 }}
          >
            <Send className="w-4 h-4" />
            {isSubmitting ? 'Thinking' : 'Ask Atlas'}
          </button>
        </form>

        {voiceNote ? (
          <div className="mt-2 text-[10px] text-amber-200">{voiceNote}</div>
        ) : null}

        <div className="mt-4 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={isSubmitting}
            onClick={submitLocalCommand}
            className="px-4 py-2.5 rounded-xl text-sm"
            style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(0, 212, 255, 0.18)', color: '#D8F9FF', opacity: isSubmitting ? 0.7 : 1 }}
          >
            Run Local Command
          </button>
          <div className="text-xs text-cyan-100/56 self-center">
            `Ask Atlas` uses the AI provider. `Run Local Command` stays inside the native Smart Home interpreter.
          </div>
        </div>

        <div className="mt-5 flex flex-wrap gap-2">
          {suggestions.map((suggestion) => (
            <button
              key={suggestion}
              type="button"
              onClick={() => setInput(suggestion)}
              className="px-3 py-2 rounded-full text-xs"
              style={{ background: 'rgba(0, 212, 255, 0.06)', border: '1px solid rgba(0, 212, 255, 0.14)', color: '#B8F7FF' }}
            >
              {suggestion}
            </button>
          ))}
        </div>

      </motion.section>

      <section className="rounded-[28px] p-6"
               style={{ background: 'rgba(5, 10, 18, 0.84)', border: '1px solid rgba(0,212,255,0.14)', boxShadow: '0 18px 40px rgba(0,0,0,0.22)' }}>
        <div className="flex items-center gap-3 mb-5">
          <div className="w-10 h-10 rounded-2xl flex items-center justify-center"
               style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)' }}>
            <Volume2 className="w-4 h-4 text-cyan-200" />
          </div>
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Live Rules</p>
            <p className="text-lg text-cyan-100">Voice + Agent Readiness</p>
          </div>
        </div>

        <div className="space-y-3 text-sm text-cyan-100/70">
          <InfoRow label="Voice commands" value={snapshot?.agentSettings.voiceCommandsEnabled ? 'Enabled' : 'Disabled'} />
          <InfoRow label="Sidebar device rail" value={snapshot?.agentSettings.showDeviceShortcutsInSidebar ? 'Enabled' : 'Hidden'} />
          <InfoRow label="TV volume step" value={`${snapshot?.agentSettings.defaultVolumeStep ?? 5}%`} />
          <InfoRow label="Saved phrases" value={String(snapshot?.customCommands.length ?? 0)} />
          <InfoRow label="Saved scenes" value={String(snapshot?.customScenes.length ?? 0)} />
        </div>
      </section>
    </div>
  );
}

function MetricCard({ label, value, icon: Icon }: { label: string; value: string; icon: typeof Bot }) {
  return (
    <div className="rounded-2xl px-4 py-3"
         style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(0, 212, 255, 0.12)' }}>
      <div className="flex items-center justify-between gap-2 mb-2 text-cyan-300/72">
        <span className="text-[11px] uppercase tracking-[0.18em]">{label}</span>
        <Icon className="w-3.5 h-3.5" />
      </div>
      <p className="text-2xl text-cyan-100 font-semibold">{value}</p>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-2xl px-4 py-3"
         style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
      <span>{label}</span>
      <span className="text-cyan-200 font-medium">{value}</span>
    </div>
  );
}
