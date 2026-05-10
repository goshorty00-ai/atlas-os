import { motion } from 'motion/react';
import { MessageSquareQuote, Mic, Plus, Sparkles, Trash2 } from 'lucide-react';
import { FormEvent, useEffect, useRef, useState } from 'react';
import { addSpeechEntry, addSpeechRule, generateSpeechPreset, removeSpeechEntry, removeSpeechRule, requestSpeechState, subscribeSpeech } from './speechBridge';

type SpeechBucket = 'startupGreetings' | 'chatGreetings' | 'quickResponses';

type SpeechStudioState = {
  startupGreetings: string[];
  chatGreetings: string[];
  quickResponses: string[];
  customRules: Array<{ id: string; phrase: string; responseText: string }>;
  chaosIntensity: number;
  allowProfanity: boolean;
  startupPreview: string;
  chatPreview: string;
  responsePreview: string;
};

function BucketCard({
  title,
  subtitle,
  bucket,
  items,
  preview,
  accent,
}: {
  title: string;
  subtitle: string;
  bucket: SpeechBucket;
  items: string[];
  preview: string;
  accent: string;
}) {
  const [input, setInput] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [micNote, setMicNote] = useState('');
  const micTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleMicClick = () => {
    // IsMicWired("SpeechStudio") = false — mic is not wired; never auto-saves or overwrites presets.
    setMicNote('Mic not wired');
    if (micTimer.current) clearTimeout(micTimer.current);
    micTimer.current = setTimeout(() => setMicNote(''), 2400);
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const value = input.trim();
    if (!value) {
      return;
    }

    addSpeechEntry(bucket, value);
    setInput('');
  };

  return (
    <section
      className="rounded-[30px] p-6 backdrop-blur-xl"
      style={{
        background: 'linear-gradient(180deg, rgba(8, 18, 30, 0.92), rgba(6, 10, 18, 0.98))',
        border: `1px solid ${accent}`,
        boxShadow: '0 30px 80px rgba(0,0,0,0.28)',
      }}
    >
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-[24px] font-semibold text-slate-100">{title}</h2>
          <p className="mt-2 text-sm text-slate-400">{subtitle}</p>
        </div>
        <div
          className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.18em]"
          style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)', color: '#C7EAFE' }}
        >
          {items.length} saved
        </div>
      </div>

      <div
        className="mt-5 rounded-[24px] p-4 text-sm leading-7"
        style={{ background: 'rgba(0, 212, 255, 0.06)', border: '1px solid rgba(0, 212, 255, 0.14)', color: '#E7F8FF' }}
      >
        {preview}
      </div>

      <div className="mt-5 flex flex-wrap gap-3 min-h-[132px] content-start">
        {items.length === 0 && (
          <div className="rounded-2xl px-4 py-3 text-sm"
               style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.07)', color: '#91A5B8' }}>
            Nothing saved yet. Add lines that actually sound like you.
          </div>
        )}

        {items.map((item) => {
          const isSelected = selected === item;
          return (
            <button
              key={item}
              type="button"
              onClick={() => setSelected(isSelected ? null : item)}
              className="text-left rounded-2xl px-4 py-3 text-sm transition-colors"
              style={{
                background: isSelected ? 'rgba(0, 212, 255, 0.16)' : 'rgba(255,255,255,0.03)',
                border: isSelected ? '1px solid rgba(0, 212, 255, 0.42)' : '1px solid rgba(255,255,255,0.08)',
                color: isSelected ? '#F0FBFF' : '#CBD8E6',
                maxWidth: '100%',
              }}
            >
              {item}
            </button>
          );
        })}
      </div>

      <form className="mt-6 flex flex-col gap-3" onSubmit={onSubmit}>
        <div className="flex items-center gap-2">
          <input
            value={input}
            onChange={(event) => setInput(event.target.value)}
            placeholder={`Type a ${title.toLowerCase()} line and press Enter or click Add`}
            className="flex-1 rounded-[22px] px-4 py-3 outline-none"
            style={{ background: 'rgba(4, 11, 20, 0.94)', border: '1px solid rgba(71, 85, 105, 0.6)', color: '#F8FAFC' }}
          />
          <button
            type="button"
            onClick={handleMicClick}
            title="Input mic"
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full"
            style={{ background: 'rgba(4, 11, 20, 0.94)', border: '1px solid rgba(71, 85, 105, 0.6)', color: '#94A3B8' }}
          >
            <Mic className="h-4 w-4" />
          </button>
        </div>
        {micNote && (
          <p className="text-xs font-mono" style={{ color: '#64748B' }}>{micNote}</p>
        )}

        <div className="flex items-center gap-3">
          <button
            type="submit"
            className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium"
            style={{ background: 'linear-gradient(135deg, #0891B2, #155E75)', color: '#F8FAFC', border: '1px solid rgba(125, 211, 252, 0.3)' }}
          >
            <Plus className="w-4 h-4" />
            Add line
          </button>

          <button
            type="button"
            disabled={!selected}
            onClick={() => {
              if (!selected) {
                return;
              }

              removeSpeechEntry(bucket, selected);
              setSelected(null);
            }}
            className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium disabled:opacity-40"
            style={{ background: 'rgba(127, 29, 29, 0.28)', color: '#FECACA', border: '1px solid rgba(248, 113, 113, 0.28)' }}
          >
            <Trash2 className="w-4 h-4" />
            Remove selected
          </button>
        </div>
      </form>
    </section>
  );
}

function RuleCard({ rules }: { rules: Array<{ id: string; phrase: string; responseText: string }> }) {
  const [phrase, setPhrase] = useState('');
  const [responseText, setResponseText] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const nextPhrase = phrase.trim();
    const nextResponse = responseText.trim();
    if (!nextPhrase || !nextResponse) {
      return;
    }

    addSpeechRule(nextPhrase, nextResponse);
    setPhrase('');
    setResponseText('');
  };

  return (
    <section
      className="rounded-[30px] p-6 backdrop-blur-xl"
      style={{
        background: 'linear-gradient(180deg, rgba(8, 18, 30, 0.92), rgba(6, 10, 18, 0.98))',
        border: '1px solid rgba(125, 211, 252, 0.22)',
        boxShadow: '0 30px 80px rgba(0,0,0,0.28)',
      }}
    >
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-[24px] font-semibold text-slate-100">Code Phrases</h2>
          <p className="mt-2 text-sm text-slate-400">Add exact phrases Atlas should answer with a fixed response before it falls back to AI.</p>
        </div>
        <div className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.18em]"
             style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)', color: '#C7EAFE' }}>
          {rules.length} rules
        </div>
      </div>

      <div className="mt-5 space-y-3 max-h-[280px] overflow-y-auto pr-1">
        {rules.length === 0 ? (
          <div className="rounded-2xl px-4 py-3 text-sm"
               style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.07)', color: '#91A5B8' }}>
            No fixed phrase rules yet. Add your own command phrase and the exact reply Atlas should give.
          </div>
        ) : null}

        {rules.map((rule) => {
          const isSelected = selectedId === rule.id;
          return (
            <button
              key={rule.id}
              type="button"
              onClick={() => setSelectedId(isSelected ? null : rule.id)}
              className="w-full text-left rounded-2xl px-4 py-4"
              style={{
                background: isSelected ? 'rgba(0, 212, 255, 0.12)' : 'rgba(255,255,255,0.03)',
                border: isSelected ? '1px solid rgba(0, 212, 255, 0.38)' : '1px solid rgba(255,255,255,0.08)',
                color: '#E5F6FF',
              }}
            >
              <div className="text-xs uppercase tracking-[0.18em] text-cyan-300/70">Phrase</div>
              <div className="mt-1 text-sm text-slate-100">{rule.phrase}</div>
              <div className="mt-3 text-xs uppercase tracking-[0.18em] text-cyan-300/70">Response</div>
              <div className="mt-1 text-sm text-slate-300">{rule.responseText}</div>
            </button>
          );
        })}
      </div>

      <form className="mt-6 grid grid-cols-1 xl:grid-cols-[1fr,1.2fr,auto] gap-3 items-start" onSubmit={onSubmit}>
        <input
          value={phrase}
          onChange={(event) => setPhrase(event.target.value)}
          placeholder="Code phrase"
          className="w-full rounded-[22px] px-4 py-3 outline-none"
          style={{ background: 'rgba(4, 11, 20, 0.94)', border: '1px solid rgba(71, 85, 105, 0.6)', color: '#F8FAFC' }}
        />
        <input
          value={responseText}
          onChange={(event) => setResponseText(event.target.value)}
          placeholder="Exact response Atlas should say"
          className="w-full rounded-[22px] px-4 py-3 outline-none"
          style={{ background: 'rgba(4, 11, 20, 0.94)', border: '1px solid rgba(71, 85, 105, 0.6)', color: '#F8FAFC' }}
        />
        <div className="flex gap-3">
          <button
            type="submit"
            className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium"
            style={{ background: 'linear-gradient(135deg, #0891B2, #155E75)', color: '#F8FAFC', border: '1px solid rgba(125, 211, 252, 0.3)' }}
          >
            <Plus className="w-4 h-4" />
            Save rule
          </button>
          <button
            type="button"
            disabled={!selectedId}
            onClick={() => {
              const selectedRule = rules.find((rule) => rule.id === selectedId);
              if (!selectedRule) {
                return;
              }

              removeSpeechRule(selectedRule.id, selectedRule.phrase);
              setSelectedId(null);
            }}
            className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium disabled:opacity-40"
            style={{ background: 'rgba(127, 29, 29, 0.28)', color: '#FECACA', border: '1px solid rgba(248, 113, 113, 0.28)' }}
          >
            <Trash2 className="w-4 h-4" />
            Remove rule
          </button>
        </div>
      </form>
    </section>
  );
}

export function SpeechStudioApp() {
  const [state, setState] = useState<SpeechStudioState | null>(null);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    const unsubscribe = subscribeSpeech((message) => {
      if (message.type === 'speech.state') {
        setState(message.payload as SpeechStudioState);
        setError('');
      }

      if (message.type === 'speech.result') {
        const payload = message.payload as { message?: string };
        setNotice(payload.message ?? 'Speech Studio updated');
        setError('');
      }

      if (message.type === 'speech.error') {
        const payload = message.payload as { message?: string };
        setError(payload.message ?? 'Speech Studio error');
      }
    });

    requestSpeechState();
    return unsubscribe;
  }, []);

  return (
    <div className="min-h-screen w-full relative overflow-hidden" style={{ background: '#050A12' }}>
      <div className="absolute inset-0 pointer-events-none overflow-hidden">
        <div className="absolute inset-0 opacity-[0.09]"
             style={{ backgroundImage: 'linear-gradient(rgba(0,212,255,0.08) 1px, transparent 1px), linear-gradient(90deg, rgba(0,212,255,0.08) 1px, transparent 1px)', backgroundSize: '80px 80px' }} />
        <motion.div className="absolute -top-24 left-[12%] w-[420px] h-[420px] rounded-full blur-3xl"
                    style={{ background: 'radial-gradient(circle, rgba(0,212,255,0.18) 0%, transparent 70%)' }}
                    animate={{ scale: [1, 1.15, 1], opacity: [0.4, 0.65, 0.4] }}
                    transition={{ duration: 9, repeat: Infinity }} />
        <motion.div className="absolute bottom-[-120px] right-[10%] w-[460px] h-[460px] rounded-full blur-3xl"
                    style={{ background: 'radial-gradient(circle, rgba(56,189,248,0.16) 0%, transparent 70%)' }}
                    animate={{ scale: [1.1, 0.95, 1.1], opacity: [0.45, 0.28, 0.45] }}
                    transition={{ duration: 10, repeat: Infinity }} />
      </div>

      <div className="relative mx-auto max-w-7xl px-8 py-10">
        <div className="flex items-start justify-between gap-6 flex-wrap">
          <div>
            <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.22em]"
                 style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.18)', color: '#7DD3FC' }}>
              <Sparkles className="w-3.5 h-3.5" />
              Atlas Speech Studio
            </div>
            <h1 className="mt-5 text-5xl font-semibold leading-tight text-slate-50">One place to tune how Atlas talks.</h1>
            <p className="mt-4 max-w-3xl text-[17px] leading-8 text-slate-300">
              Startup greetings, in-chat greetings, and quick responses live here together so you can shape Atlas without bouncing between half-finished sections.
            </p>
          </div>

          <div className="rounded-[28px] px-5 py-4 min-w-[260px]"
               style={{ background: 'rgba(9, 17, 28, 0.88)', border: '1px solid rgba(0,212,255,0.14)' }}>
            <div className="text-[11px] uppercase tracking-[0.2em] text-cyan-300/72">Current tone</div>
            <div className="mt-3 flex items-center gap-3 text-slate-100">
              <Sparkles className="w-5 h-5 text-cyan-300" />
              <span>Unfiltered {state?.chaosIntensity ?? 0}/5</span>
            </div>
            <div className="mt-2 text-sm text-slate-400">
              {state?.allowProfanity ? 'Profanity is allowed in current settings.' : 'Profanity is blocked in current settings.'}
            </div>
          </div>
        </div>

        <div className="mt-8 grid grid-cols-1 xl:grid-cols-3 gap-5">
          <BucketCard
            title="Startup greetings"
            subtitle="What Atlas says when a section opens or the shell wakes up."
            bucket="startupGreetings"
            items={state?.startupGreetings ?? []}
            preview={state?.startupPreview ?? 'Atlas is up. Let\'s get on with it.'}
            accent="rgba(34, 211, 238, 0.22)"
          />
          <BucketCard
            title="Chat greetings"
            subtitle="The opening tone inside active conversation views."
            bucket="chatGreetings"
            items={state?.chatGreetings ?? []}
            preview={state?.chatPreview ?? 'What do you need?'}
            accent="rgba(56, 189, 248, 0.22)"
          />
          <BucketCard
            title="Quick responses"
            subtitle="Short replies Atlas can pull from when it should land fast and clean."
            bucket="quickResponses"
            items={state?.quickResponses ?? []}
            preview={state?.responsePreview ?? 'Handled.'}
            accent="rgba(14, 165, 233, 0.22)"
          />
        </div>

        <div className="mt-6 grid grid-cols-1 xl:grid-cols-[1.1fr,0.9fr] gap-5">
          <RuleCard rules={state?.customRules ?? []} />

          <section className="rounded-[30px] p-6"
                   style={{ background: 'rgba(8, 16, 28, 0.92)', border: '1px solid rgba(255,255,255,0.08)' }}>
            <div className="flex items-center gap-3 text-slate-100">
              <MessageSquareQuote className="w-5 h-5 text-cyan-300" />
              <h2 className="text-xl font-semibold">What good entries look like</h2>
            </div>
            <div className="mt-4 grid grid-cols-1 md:grid-cols-3 gap-3 text-sm">
              {[
                'Short enough to sound spoken, not written.',
                'Specific enough to feel deliberate, not placeholder filler.',
                'In your tone, not in generic assistant-speak.'
              ].map((item) => (
                <div key={item} className="rounded-2xl px-4 py-4" style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.07)', color: '#CBD5E1' }}>
                  {item}
                </div>
              ))}
            </div>

            <div className="mt-6 flex flex-wrap gap-3">
              <button
                type="button"
                onClick={() => generateSpeechPreset('pro')}
                className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium"
                style={{ background: 'linear-gradient(135deg, rgba(8,145,178,0.22), rgba(21,94,117,0.3))', color: '#EAFBFF', border: '1px solid rgba(125, 211, 252, 0.22)' }}
              >
                <Sparkles className="w-4 h-4" />
                Generate Pro Pack
              </button>
              <button
                type="button"
                onClick={() => generateSpeechPreset('unfiltered')}
                className="inline-flex items-center gap-2 rounded-2xl px-4 py-3 text-sm font-medium"
                style={{ background: 'linear-gradient(135deg, rgba(15,23,42,0.8), rgba(8,145,178,0.22))', color: '#EAFBFF', border: '1px solid rgba(56, 189, 248, 0.22)' }}
              >
                <MessageSquareQuote className="w-4 h-4" />
                Generate Unfiltered Pack
              </button>
            </div>
          </section>

          <section className="rounded-[30px] p-6"
                   style={{ background: 'rgba(8, 16, 28, 0.92)', border: '1px solid rgba(255,255,255,0.08)' }}>
            <div className="text-[11px] uppercase tracking-[0.2em] text-cyan-300/72">Status</div>
            <div className="mt-3 text-sm text-slate-300 min-h-[24px]">{error || notice || 'Speech Studio synced with Atlas settings.'}</div>
          </section>
        </div>
      </div>
    </div>
  );
}