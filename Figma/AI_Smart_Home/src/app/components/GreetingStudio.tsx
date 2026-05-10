import { useEffect, useState } from 'react';
import { MessageSquareHeart, Save, Sparkles, Trash2, WandSparkles } from 'lucide-react';
import { deleteCustomGreeting, generateGreetingPreset, runSmartHomeCommand, saveCustomGreeting } from '../bridge';
import type { SmartHomeCustomGreetingDraft, SmartHomeSavedGreeting, SmartHomeSnapshot } from '../types';

interface GreetingStudioProps {
  snapshot: SmartHomeSnapshot | null;
}

const emptyDraft: SmartHomeCustomGreetingDraft = {
  phrase: '',
  responseText: '',
  enabled: true,
};

export function GreetingStudio({ snapshot }: GreetingStudioProps) {
  const [draft, setDraft] = useState<SmartHomeCustomGreetingDraft>(emptyDraft);

  useEffect(() => {
    if (!snapshot?.customGreetings.length && draft.id) {
      setDraft(emptyDraft);
    }
  }, [draft.id, snapshot?.customGreetings.length]);

  const save = () => {
    if (!draft.phrase.trim() || !draft.responseText?.trim()) {
      return;
    }

    saveCustomGreeting({
      ...draft,
      phrase: draft.phrase.trim(),
      responseText: draft.responseText.trim(),
    });
    setDraft(emptyDraft);
  };

  const editGreeting = (greeting: SmartHomeSavedGreeting) => {
    setDraft({
      id: greeting.id,
      enabled: greeting.enabled,
      phrase: greeting.phrase,
      responseText: greeting.responseText,
    });
  };

  return (
    <>
    <div className="mt-8 grid grid-cols-1 xl:grid-cols-[0.95fr_1.05fr] gap-5">
      <section className="rounded-[28px] p-6"
               style={{ background: 'rgba(5, 10, 18, 0.82)', border: '1px solid rgba(0,212,255,0.16)', boxShadow: '0 20px 48px rgba(0,0,0,0.24)' }}>
        <div className="flex items-center gap-3 mb-5">
          <div className="w-11 h-11 rounded-2xl flex items-center justify-center"
               style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)' }}>
            <MessageSquareHeart className="w-5 h-5 text-cyan-200" />
          </div>
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Custom Greetings</p>
            <h3 className="text-2xl text-cyan-100 font-semibold">Teach Atlas How To Say Hello</h3>
          </div>
        </div>

        <div className="space-y-4">
          <Field label="Greeting phrase">
            <input
              value={draft.phrase}
              onChange={(event) => setDraft((current) => ({ ...current, phrase: event.target.value }))}
              placeholder="good morning atlas"
              className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm"
              style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
            />
          </Field>

          <Field label="Response Atlas should say">
            <textarea
              value={draft.responseText ?? ''}
              onChange={(event) => setDraft((current) => ({ ...current, responseText: event.target.value }))}
              placeholder="Good morning. Everything in the house is online and ready."
              rows={5}
              className="w-full px-4 py-3 rounded-2xl bg-transparent outline-none text-sm resize-none"
              style={{ border: '1px solid rgba(0,212,255,0.22)', color: '#D8F9FF' }}
            />
          </Field>

          <div className="flex flex-wrap gap-3 pt-2">
            <button
              type="button"
              onClick={save}
              className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
              style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.18), rgba(0,102,255,0.18))', border: '1px solid rgba(0,212,255,0.28)', color: '#D8F9FF' }}
            >
              <Save className="w-4 h-4" />
              Save Greeting
            </button>

            <button
              type="button"
              onClick={() => draft.phrase.trim() && runSmartHomeCommand(draft.phrase.trim())}
              className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
              style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.18)', color: '#D8F9FF' }}
            >
              <WandSparkles className="w-4 h-4" />
              Test Greeting
            </button>
          </div>
        </div>
      </section>

      <section className="rounded-[28px] p-6"
               style={{ background: 'rgba(5, 10, 18, 0.72)', border: '1px solid rgba(0,212,255,0.14)', boxShadow: '0 18px 40px rgba(0,0,0,0.2)' }}>
        <div className="flex items-center justify-between gap-3 mb-5">
          <div>
            <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Greeting Library</p>
            <h3 className="text-2xl text-cyan-100 font-semibold">Saved Openers</h3>
          </div>
          <div className="px-3 py-2 rounded-full text-xs text-cyan-200"
               style={{ background: 'rgba(0,212,255,0.06)', border: '1px solid rgba(0,212,255,0.14)' }}>
            {snapshot?.customGreetings.length ?? 0} saved
          </div>
        </div>

        <div className="space-y-3 max-h-[560px] overflow-y-auto pr-1">
          {(snapshot?.customGreetings ?? []).map((greeting) => (
            <div key={greeting.id} className="rounded-3xl px-4 py-4"
                 style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
              <div className="flex items-start justify-between gap-3 mb-2">
                <div>
                  <p className="text-base text-cyan-100 font-medium">"{greeting.phrase}"</p>
                  <p className="text-sm text-cyan-100/72 mt-2">{greeting.responseText}</p>
                </div>
                <div className="px-2 py-1 rounded-full text-[10px] uppercase tracking-[0.18em]"
                     style={{ background: greeting.enabled ? 'rgba(124,255,178,0.08)' : 'rgba(255,185,112,0.08)', border: greeting.enabled ? '1px solid rgba(124,255,178,0.22)' : '1px solid rgba(255,185,112,0.18)', color: greeting.enabled ? '#BFFFD5' : '#FFCAA0' }}>
                  {greeting.enabled ? 'Live' : 'Paused'}
                </div>
              </div>

              <div className="flex flex-wrap gap-2 mt-4">
                <button
                  type="button"
                  onClick={() => runSmartHomeCommand(greeting.phrase)}
                  className="px-3 py-2 rounded-2xl text-xs"
                  style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)', color: '#D8F9FF' }}
                >
                  Run Now
                </button>
                <button
                  type="button"
                  onClick={() => editGreeting(greeting)}
                  className="px-3 py-2 rounded-2xl text-xs"
                  style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)', color: '#D8F9FF' }}
                >
                  Edit
                </button>
                <button
                  type="button"
                  onClick={() => deleteCustomGreeting(greeting.id)}
                  className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                  style={{ background: 'rgba(255,92,92,0.08)', border: '1px solid rgba(255,92,92,0.18)', color: '#FFD3D3' }}
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  Delete
                </button>
              </div>
            </div>
          ))}

          {(snapshot?.customGreetings.length ?? 0) === 0 && (
            <div className="rounded-3xl px-4 py-5 text-sm text-cyan-100/68"
                 style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.1)' }}>
              No saved greetings yet. Add one on the left and Atlas will answer it instantly from the Smart Home agent.
            </div>
          )}
        </div>
      </section>
    </div>

    <div className="mt-5 rounded-[28px] p-6"
         style={{ background: 'rgba(5, 10, 18, 0.82)', border: '1px solid rgba(0,212,255,0.16)', boxShadow: '0 20px 48px rgba(0,0,0,0.24)' }}>
      <div className="flex items-center gap-3 mb-5">
        <div className="w-11 h-11 rounded-2xl flex items-center justify-center"
             style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)' }}>
          <Sparkles className="w-5 h-5 text-cyan-200" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Greeting Packs</p>
          <h3 className="text-2xl text-cyan-100 font-semibold">Generate Greetings &amp; Responses</h3>
        </div>
      </div>

      <p className="text-sm text-slate-400 mb-5">
        Instantly populate your greeting library with a curated set of phrases and custom responses. Choose a tone that fits your style.
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="rounded-[22px] p-5"
             style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.14)' }}>
          <h4 className="text-lg text-cyan-100 font-semibold mb-2">Professional Pack</h4>
          <p className="text-xs text-slate-400 mb-4">
            Clean, formal greetings and responses. Atlas sounds polished, composed, and ready for business.
          </p>
          <ul className="text-xs text-slate-500 space-y-1 mb-4">
            <li>&bull; &quot;Good morning&quot; &rarr; formal, ready-to-work reply</li>
            <li>&bull; &quot;Thank you&quot; &rarr; polite acknowledgement</li>
            <li>&bull; &quot;Status report&quot; &rarr; concise system summary</li>
          </ul>
          <button
            type="button"
            onClick={() => generateGreetingPreset('pro')}
            className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
            style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.18), rgba(0,102,255,0.18))', border: '1px solid rgba(0,212,255,0.28)', color: '#D8F9FF' }}
          >
            <Sparkles className="w-4 h-4" />
            Generate Professional Pack
          </button>
        </div>

        <div className="rounded-[22px] p-5"
             style={{ background: 'rgba(56,189,248,0.04)', border: '1px solid rgba(56,189,248,0.14)' }}>
          <h4 className="text-lg text-cyan-100 font-semibold mb-2">Unfiltered Pack</h4>
          <p className="text-xs text-slate-400 mb-4">
            Casual, direct greetings and responses. Atlas sounds like it has personality and doesn&apos;t hold back.
          </p>
          <ul className="text-xs text-slate-500 space-y-1 mb-4">
            <li>&bull; &quot;Good morning&quot; &rarr; casual, no-nonsense response</li>
            <li>&bull; &quot;Thank you&quot; &rarr; straight-talking dismissal</li>
            <li>&bull; &quot;What&apos;s the damage&quot; &rarr; direct status check</li>
          </ul>
          <button
            type="button"
            onClick={() => generateGreetingPreset('unfiltered')}
            className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
            style={{ background: 'linear-gradient(135deg, rgba(56,189,248,0.14), rgba(15,23,42,0.5))', border: '1px solid rgba(56,189,248,0.22)', color: '#D8F9FF' }}
          >
            <Sparkles className="w-4 h-4" />
            Generate Unfiltered Pack
          </button>
        </div>
      </div>
    </div>
    </>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <p className="text-xs uppercase tracking-[0.16em] text-cyan-400/58 mb-2">{label}</p>
      {children}
    </label>
  );
}