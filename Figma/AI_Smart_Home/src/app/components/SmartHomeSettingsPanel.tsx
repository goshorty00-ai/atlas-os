import { useEffect, useState } from 'react';
import { Settings2, Volume2 } from 'lucide-react';
import { saveSmartHomeAgentSettings } from '../bridge';
import type { SmartHomeAgentSettings, SmartHomeSnapshot } from '../types';

interface SmartHomeSettingsPanelProps {
  snapshot: SmartHomeSnapshot | null;
}

export function SmartHomeSettingsPanel({ snapshot }: SmartHomeSettingsPanelProps) {
  const [settings, setSettings] = useState<SmartHomeAgentSettings>({
    voiceCommandsEnabled: true,
    answerDoorEnabled: true,
    showDeviceShortcutsInSidebar: true,
    defaultVolumeStep: 5,
  });

  useEffect(() => {
    if (snapshot?.agentSettings) {
      setSettings(snapshot.agentSettings);
    }
  }, [snapshot]);

  return (
    <div className="mt-8 rounded-[28px] p-6"
         style={{ background: 'rgba(5, 10, 18, 0.78)', border: '1px solid rgba(0,212,255,0.14)', boxShadow: '0 18px 40px rgba(0,0,0,0.2)' }}>
      <div className="flex items-center gap-3 mb-6">
        <div className="w-11 h-11 rounded-2xl flex items-center justify-center"
             style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)' }}>
          <Settings2 className="w-5 h-5 text-cyan-200" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-[0.22em] text-cyan-400/56">Agent Settings</p>
          <h3 className="text-2xl text-cyan-100 font-semibold">Smart Home Control Preferences</h3>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-[1fr_auto] gap-6 items-end">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <ToggleCard
            title="Voice command routing"
            description="Allow Atlas voice and typed chat to execute Smart Home device phrases through the shared interpreter."
            checked={settings.voiceCommandsEnabled}
            onToggle={() => setSettings((current) => ({ ...current, voiceCommandsEnabled: !current.voiceCommandsEnabled }))}
          />
          <ToggleCard
            title="Answer door command"
            description="Enable or disable Atlas opening the doorbell camera when you say answer the door."
            checked={settings.answerDoorEnabled}
            onToggle={() => setSettings((current) => ({ ...current, answerDoorEnabled: !current.answerDoorEnabled }))}
          />
          <ToggleCard
            title="Sidebar device shortcuts"
            description="Show live device icons in the sidebar instead of a plain generic rail."
            checked={settings.showDeviceShortcutsInSidebar}
            onToggle={() => setSettings((current) => ({ ...current, showDeviceShortcutsInSidebar: !current.showDeviceShortcutsInSidebar }))}
          />
          <div className="rounded-3xl p-4 md:col-span-2"
               style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
            <div className="flex items-center gap-3 mb-3 text-cyan-100">
              <Volume2 className="w-4 h-4" />
              <span className="text-sm font-medium">TV volume step</span>
            </div>
            <div className="grid grid-cols-[1fr_auto] gap-4 items-center">
              <input
                type="range"
                min={1}
                max={25}
                value={settings.defaultVolumeStep}
                onChange={(event) => setSettings((current) => ({ ...current, defaultVolumeStep: Number(event.target.value) }))}
                className="w-full accent-cyan-400"
              />
              <div className="w-16 text-center px-3 py-2 rounded-2xl text-cyan-200"
                   style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)' }}>
                {settings.defaultVolumeStep}%
              </div>
            </div>
          </div>
        </div>

        <button
          type="button"
          onClick={() => saveSmartHomeAgentSettings(settings)}
          className="px-5 py-3 rounded-2xl text-sm"
          style={{ background: 'linear-gradient(135deg, rgba(0,212,255,0.18), rgba(0,102,255,0.18))', border: '1px solid rgba(0,212,255,0.26)', color: '#D8F9FF' }}>
          Save Agent Settings
        </button>
      </div>
    </div>
  );
}

function ToggleCard({ title, description, checked, onToggle }: { title: string; description: string; checked: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className="text-left rounded-3xl p-4"
      style={{ background: checked ? 'rgba(0,212,255,0.08)' : 'rgba(255,255,255,0.03)', border: checked ? '1px solid rgba(0,212,255,0.2)' : '1px solid rgba(255,255,255,0.08)' }}>
      <div className="flex items-center justify-between gap-3 mb-2">
        <span className="text-sm text-cyan-100 font-medium">{title}</span>
        <span className="px-2 py-1 rounded-full text-[10px] uppercase tracking-[0.18em]"
              style={{ background: checked ? 'rgba(124,255,178,0.08)' : 'rgba(255,185,112,0.08)', color: checked ? '#C7FFD9' : '#FFD3A7' }}>
          {checked ? 'On' : 'Off'}
        </span>
      </div>
      <p className="text-sm leading-6 text-cyan-100/64">{description}</p>
    </button>
  );
}