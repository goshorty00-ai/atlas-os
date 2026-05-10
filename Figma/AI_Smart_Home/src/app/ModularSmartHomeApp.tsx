import { motion } from 'motion/react';
import { useEffect, useMemo, useState } from 'react';
import {
  Bell,
  Bot,
  Camera,
  ChevronRight,
  Cpu,
  DoorOpen,
  Gauge,
  Grid3x3,
  Home,
  MessageSquareText,
  PlusCircle,
  QrCode,
  RefreshCcw,
  Shield,
  Sparkles,
  Workflow,
} from 'lucide-react';
import { createAutomation, deleteAutomation, openExternalUrl, refreshState, runAutomation, runSmartHomeCommand, toggleAutomation } from './bridge';
import { AIAssistant } from './components/AIAssistant';
import { CameraDeck } from './components/CameraDeck';
import { CommandStudio } from './components/CommandStudio';
import { DeviceInventory } from './components/DeviceInventory';
import { GreetingStudio } from './components/GreetingStudio';
import { ProviderConnections } from './components/ProviderConnections';
import { SmartHomeSettingsPanel } from './components/SmartHomeSettingsPanel';
import {
  buildIntegrationStates,
  buildRecommendations,
  buildRoomGroups,
  buildSetupMethods,
  flattenDevices,
  formatRelativeTime,
  getAccessDevices,
  getClimateTelemetry,
  getSecurityDevices,
  groupAlerts,
  looksLikeCamera,
  looksLikeClimate,
  looksLikeLight,
  looksLikeMedia,
  summarizeAutomations,
  type LiveDevice,
  type RoomGroup,
} from './smartHomeModel';
import type { SmartHomeSnapshot } from './types';

type PageId =
  | 'overview'
  | 'devices'
  | 'device-setup'
  | 'rooms'
  | 'cameras'
  | 'security'
  | 'ai-scenes'
  | 'automations'
  | 'custom-commands'
  | 'alerts'
  | 'climate-energy'
  | 'access'
  | 'ai-assistant';

const PAGES: Array<{ id: PageId; label: string; description: string; icon: typeof Home }> = [
  { id: 'overview', label: 'Overview', description: 'Live smart home status, alerts, cameras, and recommendations.', icon: Home },
  { id: 'devices', label: 'Devices', description: 'Connected devices, live capability controls, and provider health.', icon: Cpu },
  { id: 'device-setup', label: 'Device Setup', description: 'Real provider onboarding, QR pairing, and setup method availability.', icon: PlusCircle },
  { id: 'rooms', label: 'Rooms', description: 'Device groups inferred from your live device inventory.', icon: Grid3x3 },
  { id: 'cameras', label: 'Cameras', description: 'Live camera feeds, health state, and camera-related alerts.', icon: Camera },
  { id: 'security', label: 'Security', description: 'Atlas security posture, incidents, and security-capable devices.', icon: Shield },
  { id: 'ai-scenes', label: 'AI Scenes', description: 'Scene-like phrases and live recommendations built from real devices.', icon: Sparkles },
  { id: 'automations', label: 'Automations', description: 'Persisted Atlas automations with real trigger and action state.', icon: Workflow },
  { id: 'custom-commands', label: 'Custom Commands', description: 'Saved command phrases and custom greetings already stored in Atlas.', icon: MessageSquareText },
  { id: 'alerts', label: 'Alerts', description: 'Ledger activity, provider failures, and offline device warnings.', icon: Bell },
  { id: 'climate-energy', label: 'Climate & Energy', description: 'Live climate and energy telemetry when providers expose it.', icon: Gauge },
  { id: 'access', label: 'Access', description: 'Locks, garage, and gate controls when access hardware is connected.', icon: DoorOpen },
  { id: 'ai-assistant', label: 'AI Assistant', description: 'Existing Smart Home command agent and troubleshooting surface.', icon: Bot },
];

interface ModularSmartHomeAppProps {
  snapshot: SmartHomeSnapshot | null;
  bridgeError: string;
  bridgeNotice: string;
  bridgeEventId: number;
}

export function ModularSmartHomeApp({ snapshot, bridgeError, bridgeNotice, bridgeEventId }: ModularSmartHomeAppProps) {
  const [activePage, setActivePage] = useHashPage();
  const devices = useMemo(() => flattenDevices(snapshot), [snapshot]);
  const rooms = useMemo(() => buildRoomGroups(snapshot), [snapshot]);
  const setupMethods = useMemo(() => buildSetupMethods(snapshot), [snapshot]);
  const integrations = useMemo(() => buildIntegrationStates(snapshot), [snapshot]);
  const alerts = useMemo(() => groupAlerts(snapshot?.alerts ?? []), [snapshot]);
  const recommendations = useMemo(() => buildRecommendations(snapshot), [snapshot]);
  const automationSummary = useMemo(() => summarizeAutomations(snapshot?.automations ?? []), [snapshot]);
  const activePageMeta = PAGES.find((page) => page.id === activePage) ?? PAGES[0];

  return (
    <div className="min-h-screen bg-[#050912] text-slate-50">
      <div
        className="fixed inset-0 pointer-events-none opacity-80"
        style={{
          background: 'radial-gradient(circle at top left, rgba(10,174,255,0.18), transparent 36%), radial-gradient(circle at bottom right, rgba(27,81,196,0.22), transparent 28%), linear-gradient(180deg, rgba(255,255,255,0.02), transparent 40%)',
        }}
      />

      <aside className="fixed inset-y-0 left-0 z-20 w-[290px] border-r border-cyan-500/10 bg-[rgba(5,10,18,0.92)] backdrop-blur-2xl">
        <div className="flex h-full flex-col px-5 py-6">
          <div className="mb-6 rounded-[28px] border border-cyan-400/15 bg-[rgba(255,255,255,0.02)] p-5 shadow-[0_20px_60px_rgba(0,0,0,0.25)]">
            <div className="mb-3 flex items-center justify-between gap-3">
              <div>
                <p className="text-[11px] uppercase tracking-[0.32em] text-cyan-300/58">Atlas Smart Home</p>
                <h1 className="mt-2 text-2xl font-semibold text-cyan-50">Modular Control</h1>
              </div>
              <div className="rounded-2xl border border-cyan-400/20 bg-cyan-400/10 p-3 text-cyan-200">
                <Home className="h-5 w-5" />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <SidebarMetric label="Devices" value={String(snapshot?.totalDevices ?? 0)} />
              <SidebarMetric label="Online" value={String(snapshot?.onlineDevices ?? 0)} />
              <SidebarMetric label="Alerts" value={String(alerts.filter((alert) => !alert.isResolved).length)} />
              <SidebarMetric label="Autos" value={String(automationSummary.enabled)} />
            </div>

            <div className="mt-4 rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-3 py-2 text-xs text-cyan-100/72">
              {snapshot ? `Last sync ${new Date(snapshot.generatedAtUtc).toLocaleTimeString()}` : 'Waiting for live Smart Home snapshot'}
            </div>
          </div>

          <nav className="flex-1 space-y-2 overflow-y-auto pr-1">
            {PAGES.map((page) => {
              const Icon = page.icon;
              const isActive = page.id === activePage;

              return (
                <button
                  key={page.id}
                  type="button"
                  onClick={() => setActivePage(page.id)}
                  className="w-full rounded-[24px] border px-4 py-3 text-left transition-all"
                  style={{
                    background: isActive ? 'linear-gradient(135deg, rgba(17,117,187,0.22), rgba(8,27,46,0.75))' : 'rgba(255,255,255,0.02)',
                    borderColor: isActive ? 'rgba(92,215,255,0.28)' : 'rgba(0,212,255,0.08)',
                    boxShadow: isActive ? '0 0 24px rgba(0,212,255,0.12)' : 'none',
                  }}
                >
                  <div className="flex items-start gap-3">
                    <div className="mt-0.5 rounded-2xl border border-cyan-400/15 bg-cyan-400/10 p-2 text-cyan-200">
                      <Icon className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-3">
                        <span className="text-sm font-medium text-cyan-50">{page.label}</span>
                        <ChevronRight className="h-4 w-4 text-cyan-200/56" />
                      </div>
                      <p className="mt-1 text-xs leading-5 text-cyan-100/58">{page.description}</p>
                    </div>
                  </div>
                </button>
              );
            })}
          </nav>

          <div className="mt-5 rounded-[24px] border border-cyan-400/12 bg-[rgba(255,255,255,0.02)] p-4 text-sm text-cyan-100/70">
            <div className="mb-2 flex items-center gap-2 text-cyan-200">
              <QrCode className="h-4 w-4" />
              Setup status
            </div>
            <p>{snapshot?.companionPairing.availabilityMessage ?? 'Companion pairing is not available yet.'}</p>
          </div>
        </div>
      </aside>

      <main className="relative z-10 pl-[290px]">
        <div className="mx-auto max-w-[1680px] px-8 py-8">
          <header className="mb-8 rounded-[32px] border border-cyan-400/12 bg-[rgba(5,10,18,0.76)] p-6 shadow-[0_24px_70px_rgba(0,0,0,0.32)] backdrop-blur-xl">
            <div className="flex flex-wrap items-start justify-between gap-6">
              <div>
                <p className="text-[11px] uppercase tracking-[0.34em] text-cyan-300/55">Smart Home Section</p>
                <h2 className="mt-3 text-4xl font-semibold text-cyan-50">{activePageMeta.label}</h2>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-cyan-100/64">{activePageMeta.description}</p>
              </div>

              <div className="grid min-w-[320px] grid-cols-3 gap-3">
                <HeaderPill label="Bridge" value={snapshot ? 'Connected' : 'Waiting'} />
                <HeaderPill label="Security" value={snapshot?.security.mode || 'Unknown'} />
                <HeaderPill label="Companion" value={snapshot?.companionPairing.isAvailable ? 'Ready' : 'Offline'} />
              </div>
            </div>
          </header>

          {bridgeError && <Banner tone="error" text={bridgeError} />}
          {bridgeNotice && <Banner tone="notice" text={bridgeNotice} />}

          <PageRenderer
            activePage={activePage}
            snapshot={snapshot}
            devices={devices}
            rooms={rooms}
            alerts={alerts}
            setupMethods={setupMethods}
            integrations={integrations}
            recommendations={recommendations}
            bridgeError={bridgeError}
            bridgeNotice={bridgeNotice}
            bridgeEventId={bridgeEventId}
          />
        </div>
      </main>
    </div>
  );
}

function PageRenderer({
  activePage,
  snapshot,
  devices,
  rooms,
  alerts,
  setupMethods,
  integrations,
  recommendations,
  bridgeError,
  bridgeNotice,
  bridgeEventId,
}: {
  activePage: PageId;
  snapshot: SmartHomeSnapshot | null;
  devices: LiveDevice[];
  rooms: RoomGroup[];
  alerts: SmartHomeSnapshot['alerts'];
  setupMethods: ReturnType<typeof buildSetupMethods>;
  integrations: ReturnType<typeof buildIntegrationStates>;
  recommendations: ReturnType<typeof buildRecommendations>;
  bridgeError: string;
  bridgeNotice: string;
  bridgeEventId: number;
}) {
  switch (activePage) {
    case 'overview':
      return <OverviewPage snapshot={snapshot} devices={devices} alerts={alerts} recommendations={recommendations} />;
    case 'devices':
      return <DevicesPage snapshot={snapshot} devices={devices} />;
    case 'device-setup':
      return <DeviceSetupPage snapshot={snapshot} setupMethods={setupMethods} integrations={integrations} />;
    case 'rooms':
      return <RoomsPage rooms={rooms} />;
    case 'cameras':
      return <CamerasPage snapshot={snapshot} devices={devices} alerts={alerts} />;
    case 'security':
      return <SecurityPage snapshot={snapshot} devices={devices} alerts={alerts} />;
    case 'ai-scenes':
      return <AIScenesPage snapshot={snapshot} devices={devices} recommendations={recommendations} />;
    case 'automations':
      return <AutomationsPage snapshot={snapshot} />;
    case 'custom-commands':
      return <CustomCommandsPage snapshot={snapshot} />;
    case 'alerts':
      return <AlertsPage alerts={alerts} />;
    case 'climate-energy':
      return <ClimateEnergyPage devices={devices} />;
    case 'access':
      return <AccessPage devices={devices} />;
    case 'ai-assistant':
      return <AssistantPage snapshot={snapshot} bridgeNotice={bridgeNotice} bridgeError={bridgeError} bridgeEventId={bridgeEventId} />;
    default:
      return null;
  }
}

function OverviewPage({ snapshot, devices, alerts, recommendations }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; alerts: SmartHomeSnapshot['alerts']; recommendations: ReturnType<typeof buildRecommendations> }) {
  const cameras = devices.filter(looksLikeCamera);
  const climateDevices = devices.filter(looksLikeClimate);
  const accessDevices = getAccessDevices(devices);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Home mode" value={snapshot ? 'Not exposed' : '--'} detail="No dedicated home-mode backend is currently published into Smart Home." />
        <StatCard title="Security status" value={snapshot?.security.mode || '--'} detail={`${snapshot?.security.criticalAlertCount ?? 0} critical or high alerts`} />
        <StatCard title="Active cameras" value={String(cameras.length)} detail={`${snapshot?.security.activeCameraCount ?? 0} camera devices in the current snapshot`} />
        <StatCard title="Automations running" value={String(snapshot?.automations.filter((automation) => automation.isEnabled).length ?? 0)} detail="Persisted Atlas automations enabled right now" />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.1fr_0.9fr]">
        <Card title="Live overview" subtitle="Real status derived from the current Smart Home snapshot.">
          <InfoRow label="Security posture" value={snapshot?.security.mode || 'Unknown'} />
          <InfoRow label="Recent alerts" value={String(alerts.filter((alert) => !alert.isResolved).length)} />
          <InfoRow label="Climate telemetry" value={climateDevices.length > 0 ? `${climateDevices.length} live device${climateDevices.length === 1 ? '' : 's'}` : 'Not available'} />
          <InfoRow label="Access devices" value={accessDevices.length > 0 ? `${accessDevices.length} connected` : 'None discovered'} />
          <InfoRow label="Companion pairing" value={snapshot?.companionPairing.isAvailable ? 'QR ready' : 'Unavailable'} />
        </Card>

        <Card title="Recent alerts" subtitle="Newest ledger and device health items.">
          <div className="space-y-3">
            {alerts.slice(0, 5).map((alert) => (
              <AlertRow key={alert.id} alert={alert} />
            ))}
            {alerts.length === 0 && <EmptyState text="No recent smart home alerts have been published yet." />}
          </div>
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1.1fr_0.9fr]">
        <Card title="Camera and access summary" subtitle="Live camera inventory and exposed access-control hardware.">
          <div className="space-y-3">
            {cameras.slice(0, 6).map((camera) => (
              <DeviceRow key={`${camera.providerId}:${camera.deviceId}`} device={camera} />
            ))}
            {cameras.length === 0 && <EmptyState text="No live camera devices are currently available in the snapshot." />}
          </div>
        </Card>

        <Card title="Atlas recommendations" subtitle="Recommendations generated from real Smart Home state.">
          <div className="space-y-3">
            {recommendations.map((recommendation) => (
              <div key={recommendation.title} className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 p-4">
                <p className="text-sm font-medium text-cyan-50">{recommendation.title}</p>
                <p className="mt-2 text-sm leading-6 text-cyan-100/64">{recommendation.detail}</p>
              </div>
            ))}
            {recommendations.length === 0 && <EmptyState text="Atlas does not have any state-based recommendations right now." />}
          </div>
        </Card>
      </div>
    </div>
  );
}

function DevicesPage({ snapshot, devices }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[] }) {
  const counts = {
    lighting: devices.filter(looksLikeLight).length,
    cameras: devices.filter(looksLikeCamera).length,
    media: devices.filter(looksLikeMedia).length,
    offline: devices.filter((device) => device.isOnline === false).length,
  };

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Lighting" value={String(counts.lighting)} detail="Live devices classified from provider snapshots" />
        <StatCard title="Cameras" value={String(counts.cameras)} detail="Camera and doorbell devices available now" />
        <StatCard title="Media" value={String(counts.media)} detail="TV and media endpoints exposed by providers" />
        <StatCard title="Offline" value={String(counts.offline)} detail="Devices currently reported offline or cached" />
      </div>

      <DeviceInventory providers={snapshot?.providers ?? []} agentSettings={snapshot?.agentSettings ?? null} />
    </div>
  );
}

function DeviceSetupPage({
  snapshot,
  setupMethods,
  integrations,
}: {
  snapshot: SmartHomeSnapshot | null;
  setupMethods: ReturnType<typeof buildSetupMethods>;
  integrations: ReturnType<typeof buildIntegrationStates>;
}) {
  const [tab, setTab] = useState<'connect' | 'methods' | 'pairing'>('connect');
  const [scanning, setScanning] = useState(false);
  const pairing = snapshot?.companionPairing;
  const allDevices = flattenDevices(snapshot);
  const integrationById = new Map(integrations.map((i) => [i.providerId, i]));

  const handleScan = () => {
    setScanning(true);
    refreshState();
    setTimeout(() => { refreshState(); setScanning(false); }, 4000);
  };

  const scrollTo = (id: string) => {
    setTab('connect');
    setTimeout(() => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 80);
  };

  const ecosystems = [
    { id: 'philips_hue', title: 'Philips Hue', icon: '💡', devices: 'Bulbs, lamps, lightstrips, gradient lights, bridge rooms & zones', how: 'Enter bridge IP, press physical Hue button, link and save.', count: integrationById.get('philips_hue')?.deviceCount ?? 0, anchor: 'provider-philips_hue' },
    { id: 'govee', title: 'Govee', icon: '🎨', devices: 'LED strips, bars, lamps, panels, scene-capable cloud devices', how: 'Paste Govee API key, save, and refresh to discover devices.', count: integrationById.get('govee')?.deviceCount ?? 0, anchor: 'provider-govee' },
    { id: 'ring', title: 'Ring', icon: '🔔', devices: 'Doorbells, indoor/outdoor cameras, floodlight cams, alarm devices', how: 'Sign in with Ring email/password inside Atlas, complete 2FA.', count: integrationById.get('ring')?.deviceCount ?? 0, anchor: 'provider-ring' },
    { id: 'lg_webos', title: 'LG webOS', icon: '📺', devices: 'LG TVs, webOS displays, media playback & power control', how: 'Scan LAN or enter TV IP, confirm on TV, save client key.', count: integrationById.get('lg_webos')?.deviceCount ?? 0, anchor: 'provider-lg_webos' },
  ];

  return (
    <div className="space-y-6">
      {/* Action bar */}
      <div className="flex flex-wrap items-center gap-3">
        <button type="button" disabled={scanning} onClick={handleScan}
          className="px-5 py-3 rounded-2xl text-sm font-semibold flex items-center gap-2"
          style={{ background: scanning ? 'rgba(0,212,255,0.15)' : 'linear-gradient(135deg, rgba(0,212,255,0.22), rgba(0,160,210,0.34))', border: '1px solid rgba(0,212,255,0.38)', color: '#D8F9FF' }}>
          <RefreshCcw className={`w-4 h-4${scanning ? ' animate-spin' : ''}`} />
          {scanning ? 'Scanning...' : 'Scan & Refresh All Devices'}
        </button>
        <button type="button" onClick={() => openExternalUrl('ms-settings:bluetooth', 'Bluetooth')}
          className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
          style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.12)', color: '#D8F9FF' }}>
          Pair Bluetooth
        </button>
        <button type="button" onClick={() => openExternalUrl('ms-settings:network-wifi', 'Wi-Fi')}
          className="px-4 py-3 rounded-2xl text-sm flex items-center gap-2"
          style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.12)', color: '#D8F9FF' }}>
          Connect Wi-Fi
        </button>
        <div className="ml-auto text-xs text-cyan-200/50">{allDevices.length} device{allDevices.length === 1 ? '' : 's'} discovered</div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 rounded-2xl p-1" style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
        {([['connect', 'Connect Devices'], ['methods', 'Setup Methods'], ['pairing', 'Pairing & QR']] as const).map(([key, label]) => (
          <button key={key} type="button" onClick={() => setTab(key)}
            className="flex-1 px-4 py-2.5 rounded-xl text-sm font-medium transition-colors"
            style={tab === key ? { background: 'rgba(0,212,255,0.14)', color: '#D8F9FF', border: '1px solid rgba(0,212,255,0.22)' } : { color: 'rgba(216,249,255,0.5)', border: '1px solid transparent' }}>
            {label}
          </button>
        ))}
      </div>

      {/* Tab: Connect Devices */}
      {tab === 'connect' && (
        <div className="space-y-6">
          {/* Ecosystem cards */}
          <Card title="Connect an ecosystem" subtitle="Choose a brand to add devices to Atlas. Each ecosystem card shows how to connect and how many devices are already live.">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {ecosystems.map((eco) => (
                <div key={eco.id} onClick={() => scrollTo(eco.anchor)}
                  className="rounded-2xl p-5 cursor-pointer hover:border-cyan-400/40 transition-all"
                  style={{ background: 'rgba(0,212,255,0.04)', border: `1px solid ${eco.count > 0 ? 'rgba(124,255,178,0.24)' : 'rgba(0,212,255,0.16)'}` }}>
                  <div className="flex items-start justify-between gap-3 mb-3">
                    <div className="flex items-center gap-3">
                      <span className="text-2xl">{eco.icon}</span>
                      <div>
                        <p className="text-base font-semibold text-cyan-50">{eco.title}</p>
                        <p className="text-xs text-cyan-200/50 mt-0.5">{eco.devices}</p>
                      </div>
                    </div>
                    {eco.count > 0 ? (
                      <span className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-wider" style={{ color: '#7CFFB2', border: '1px solid rgba(124,255,178,0.28)', background: 'rgba(124,255,178,0.08)' }}>
                        {eco.count} live
                      </span>
                    ) : (
                      <span className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-wider" style={{ color: '#FFB970', border: '1px solid rgba(255,185,112,0.28)', background: 'rgba(255,185,112,0.08)' }}>
                        not connected
                      </span>
                    )}
                  </div>
                  <p className="text-sm text-cyan-100/64 leading-6">{eco.how}</p>
                  <p className="mt-3 text-xs text-cyan-300/70 font-medium">Click to set up →</p>
                </div>
              ))}
            </div>
          </Card>

          {/* Also show other protocols */}
          <Card title="Other connection methods" subtitle="Additional ways to connect devices to Atlas.">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
              <div onClick={() => openExternalUrl('ms-settings:bluetooth', 'Bluetooth')} className="rounded-2xl p-4 cursor-pointer hover:border-cyan-400/30 transition-colors" style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
                <p className="text-sm font-medium text-cyan-50 mb-1">Bluetooth</p>
                <p className="text-xs text-cyan-100/52">Open Windows Bluetooth to pair speakers, sensors, or controllers.</p>
              </div>
              <div onClick={() => openExternalUrl('ms-settings:network-wifi', 'Wi-Fi')} className="rounded-2xl p-4 cursor-pointer hover:border-cyan-400/30 transition-colors" style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
                <p className="text-sm font-medium text-cyan-50 mb-1">Wi-Fi</p>
                <p className="text-xs text-cyan-100/52">Open Windows Wi-Fi for devices that connect over your local network.</p>
              </div>
              <div onClick={() => setTab('pairing')} className="rounded-2xl p-4 cursor-pointer hover:border-cyan-400/30 transition-colors" style={{ background: 'rgba(0,212,255,0.04)', border: '1px solid rgba(0,212,255,0.12)' }}>
                <p className="text-sm font-medium text-cyan-50 mb-1">Companion QR</p>
                <p className="text-xs text-cyan-100/52">{pairing?.qrCodeDataUrl ? 'Live QR is ready — scan it from your phone.' : 'Check QR availability for phone pairing.'}</p>
              </div>
            </div>
          </Card>

          {/* Provider connections – the actual forms for connecting */}
          <ProviderConnections providers={snapshot?.providers ?? []} />

          {/* Live devices */}
          <Card title="Discovered devices" subtitle={`${allDevices.length} device${allDevices.length === 1 ? '' : 's'} from all connected providers.`}>
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
              {allDevices.slice(0, 12).map((device) => (
                <DeviceRow key={`${device.providerId}:${device.deviceId}`} device={device} />
              ))}
              {allDevices.length === 0 && <EmptyState text="No devices discovered yet. Connect an ecosystem above to start." />}
              {allDevices.length > 12 && <div className="col-span-full text-xs text-cyan-100/50 text-center">+{allDevices.length - 12} more devices</div>}
            </div>
          </Card>
        </div>
      )}

      {/* Tab: Setup Methods */}
      {tab === 'methods' && (
        <div className="space-y-6">
          <Card title="Setup availability" subtitle="Real setup paths currently exposed by the runtime or marked unavailable when missing.">
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              {setupMethods.map((method) => (
                <div key={method.id}
                  className={`rounded-2xl border border-cyan-400/10 bg-cyan-400/5 p-4${method.action ? ' cursor-pointer hover:border-cyan-400/30 transition-colors' : ''}`}
                  onClick={method.action ? () => openExternalUrl(method.action!, method.label) : undefined}>
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-sm font-medium text-cyan-50">{method.label}</p>
                    <StatusDot tone={method.status === 'available' ? 'good' : method.status === 'limited' ? 'warn' : 'neutral'} />
                  </div>
                  <p className="mt-2 text-sm leading-6 text-cyan-100/64">{method.detail}</p>
                  {method.action && method.status === 'available' && (
                    <p className="mt-2 text-xs text-cyan-300/70 font-medium">Click to open →</p>
                  )}
                </div>
              ))}
            </div>
          </Card>
          <Card title="Provider integrations" subtitle="Real integration state derived from current provider descriptors and errors.">
            <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              {integrations.map((integration) => (
                <div key={integration.providerId} className="rounded-[26px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-5">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className="text-lg font-semibold text-cyan-50">{integration.label}</p>
                      <p className="mt-1 text-xs uppercase tracking-[0.2em] text-cyan-200/48">{integration.status}</p>
                    </div>
                    <span className="rounded-full border border-cyan-400/14 bg-cyan-400/5 px-3 py-1 text-xs text-cyan-100/72">
                      {integration.deviceCount} device{integration.deviceCount === 1 ? '' : 's'}
                    </span>
                  </div>
                  <p className="mt-3 text-sm leading-6 text-cyan-100/64">{integration.detail}</p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    {integration.categories.map((c) => <Pill key={c} text={c} />)}
                    {integration.methods.map((m) => <Pill key={m} text={m} />)}
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}

      {/* Tab: Pairing & QR */}
      {tab === 'pairing' && (
        <div className="space-y-6">
          <Card title="Companion QR pairing" subtitle="Live companion pairing data from Atlas companion transport.">
            {pairing?.qrCodeDataUrl ? (
              <div className="space-y-4">
                <div className="rounded-[28px] border border-cyan-400/12 bg-slate-100/95 p-5">
                  <img src={pairing.qrCodeDataUrl} alt="Atlas companion pairing QR" className="mx-auto h-56 w-56 rounded-2xl" />
                </div>
                <InfoRow label="Availability" value={pairing.availabilityMessage} />
                <InfoRow label="Base URL" value={pairing.baseUrl || 'Not published'} />
                <InfoRow label="Payload format" value={pairing.payloadFormat || 'Unknown'} />
              </div>
            ) : (
              <EmptyState text={pairing?.availabilityMessage || 'Companion pairing QR is not available right now.'} />
            )}
          </Card>
        </div>
      )}

      <SmartHomeSettingsPanel snapshot={snapshot} />
    </div>
  );
}

function RoomsPage({ rooms }: { rooms: RoomGroup[] }) {
  return (
    <div className="space-y-6">
      <Card title="Room groups" subtitle="Room mapping inferred from real device names because the current backend does not publish explicit room assignments.">
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          {rooms.map((room) => (
            <div key={room.name} className="rounded-[28px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-5">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <p className="text-lg font-semibold text-cyan-50">{room.name}</p>
                  <p className="mt-1 text-xs uppercase tracking-[0.18em] text-cyan-200/48">{room.inferred ? 'Inferred from live device names' : 'No explicit room mapping'}</p>
                </div>
                <span className="rounded-full border border-cyan-400/14 bg-cyan-400/5 px-3 py-1 text-xs text-cyan-100/72">
                  {room.devices.length} device{room.devices.length === 1 ? '' : 's'}
                </span>
              </div>

              <div className="mt-4 grid grid-cols-3 gap-3">
                <MiniStat label="Offline" value={String(room.offlineCount)} />
                <MiniStat label="Cameras" value={String(room.cameraCount)} />
                <MiniStat label="Controls" value={String(room.controllableCount)} />
              </div>

              <div className="mt-4 space-y-3">
                {room.devices.slice(0, 5).map((device) => (
                  <DeviceRow key={`${device.providerId}:${device.deviceId}`} device={device} compact />
                ))}
                {room.devices.length > 5 && (
                  <p className="text-xs text-cyan-100/52">+{room.devices.length - 5} more device{room.devices.length - 5 === 1 ? '' : 's'} in this inferred room group.</p>
                )}
              </div>
            </div>
          ))}
          {rooms.length === 0 && <EmptyState text="Rooms will appear as soon as Atlas has live devices to group." />}
        </div>
      </Card>
    </div>
  );
}

function CamerasPage({ snapshot, devices, alerts }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; alerts: SmartHomeSnapshot['alerts'] }) {
  const cameras = devices.filter(looksLikeCamera);
  const cameraAlerts = alerts.filter((alert) => alert.category === 'camera' || alert.category === 'security');

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <StatCard title="Visible feeds" value={String(cameras.length)} detail="Live camera-capable devices in the current snapshot" />
        <StatCard title="Security events" value={String(cameraAlerts.length)} detail="Recent camera and security alerts from Atlas" />
        <StatCard title="Managed live view" value={snapshot?.providers.some((provider) => provider.providerId === 'ring') ? 'Available' : 'Unavailable'} detail="Ring managed live view support depends on live Ring auth" />
      </div>

      <CameraDeck providers={snapshot?.providers ?? []} />
    </div>
  );
}

function SecurityPage({ snapshot, devices, alerts }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; alerts: SmartHomeSnapshot['alerts'] }) {
  const securityDevices = getSecurityDevices(devices);
  const securityAlerts = alerts.filter((alert) => alert.category === 'security' || alert.category === 'integration');

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Atlas security mode" value={snapshot?.security.mode || '--'} detail="Current `SecurityAgent` posture, not alarm arming state" />
        <StatCard title="Threat level" value={String(snapshot?.security.threatLevel ?? 0)} detail="Live `SecurityAgent` threat level" />
        <StatCard title="Critical alerts" value={String(snapshot?.security.criticalAlertCount ?? 0)} detail="High and critical alerts in the current smart home feed" />
        <StatCard title="Siren state" value={snapshot?.security.sirenActive ? 'Active' : 'Idle'} detail="Derived from live siren-capable device state" />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_0.95fr]">
        <Card title="Security incidents" subtitle="Ledger and provider incidents surfaced into Smart Home.">
          <div className="space-y-3">
            {securityAlerts.map((alert) => (
              <AlertRow key={alert.id} alert={alert} />
            ))}
            {securityAlerts.length === 0 && <EmptyState text="No security incidents are currently visible in the smart home feed." />}
          </div>
        </Card>

        <Card title="Security-capable devices" subtitle="Live devices classified as cameras, alarms, sensors, or entry hardware.">
          <div className="space-y-3">
            {securityDevices.map((device) => (
              <DeviceRow key={`${device.providerId}:${device.deviceId}`} device={device} />
            ))}
            {securityDevices.length === 0 && <EmptyState text="No security-capable devices are currently exposed by the active providers." />}
          </div>
        </Card>
      </div>
    </div>
  );
}

function AIScenesPage({ snapshot, devices, recommendations }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; recommendations: ReturnType<typeof buildRecommendations> }) {
  const lightCount = devices.filter(looksLikeLight).length;
  const sceneCards = [
    {
      id: 'all-lights-on',
      title: 'All lights on',
      description: 'Runs the existing built-in light-scene phrase through the native Smart Home interpreter.',
      enabled: lightCount > 0,
      phrase: 'all lights on',
    },
    {
      id: 'goodnight',
      title: 'Goodnight',
      description: 'Runs the built-in all-lights-off phrase that already exists in Smart Home command handling.',
      enabled: lightCount > 0,
      phrase: 'goodnight',
    },
  ];

  return (
    <div className="space-y-6">
      <Card title="Scene execution" subtitle="Scene-like actions that are already backed by the native Smart Home interpreter.">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {sceneCards.map((scene) => (
            <div key={scene.id} className="rounded-[28px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-5">
              <p className="text-lg font-semibold text-cyan-50">{scene.title}</p>
              <p className="mt-2 text-sm leading-6 text-cyan-100/64">{scene.description}</p>
              <button
                type="button"
                onClick={() => runSmartHomeCommand(scene.phrase)}
                disabled={!scene.enabled}
                className="mt-4 rounded-2xl border border-cyan-400/18 bg-cyan-400/10 px-4 py-2 text-sm text-cyan-100 disabled:opacity-50"
              >
                Run Scene
              </button>
            </div>
          ))}
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_0.95fr]">
        <Card title="Saved command phrases" subtitle="Existing custom phrases that can act like personal scenes.">
          <div className="space-y-3">
            {snapshot?.customCommands.map((command) => (
              <div key={command.id} className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 p-4">
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm font-medium text-cyan-50">{command.phrase}</p>
                  <button
                    type="button"
                    onClick={() => runSmartHomeCommand(command.phrase)}
                    className="rounded-full border border-cyan-400/18 bg-cyan-400/10 px-3 py-1 text-xs text-cyan-100"
                  >
                    Run
                  </button>
                </div>
                <p className="mt-2 text-sm text-cyan-100/64">{command.responseText || `${command.providerId} · ${command.capabilityInstance}`}</p>
              </div>
            ))}
            {(snapshot?.customCommands.length ?? 0) === 0 && <EmptyState text="No saved command phrases exist yet. Create one in Custom Commands to use it like a scene." />}
          </div>
        </Card>

        <Card title="Recommendations" subtitle="State-based recommendations because no dedicated AI scene backend is currently exposed here.">
          <div className="space-y-3">
            {recommendations.map((recommendation) => (
              <div key={recommendation.title} className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 p-4">
                <p className="text-sm font-medium text-cyan-50">{recommendation.title}</p>
                <p className="mt-2 text-sm leading-6 text-cyan-100/64">{recommendation.detail}</p>
              </div>
            ))}
            {recommendations.length === 0 && <EmptyState text="Atlas does not currently have scene recommendations to surface from live state." />}
          </div>
        </Card>
      </div>
    </div>
  );
}

function AutomationsPage({ snapshot }: { snapshot: SmartHomeSnapshot | null }) {
  const [trigger, setTrigger] = useState('');
  const [actionsText, setActionsText] = useState('');
  const [schedule, setSchedule] = useState('');
  const automations = snapshot?.automations ?? [];
  const summary = summarizeAutomations(automations);

  const submit = () => {
    const actions = actionsText
      .split(/\r?\n/)
      .map((action) => action.trim())
      .filter(Boolean);

    if (!trigger.trim() || actions.length === 0) {
      return;
    }

    createAutomation({
      trigger: trigger.trim(),
      actions,
      schedule: schedule.trim() || undefined,
    });

    setTrigger('');
    setActionsText('');
    setSchedule('');
  };

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <StatCard title="Total automations" value={String(summary.total)} detail="Persisted in the Atlas automation store" />
        <StatCard title="Enabled" value={String(summary.enabled)} detail="Automations currently able to run" />
        <StatCard title="Scheduled" value={String(summary.scheduled)} detail="Automations with a stored schedule" />
        <StatCard title="Triggered" value={String(summary.recentlyTriggered)} detail="Automations that have run at least once" />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[0.86fr_1.14fr]">
        <Card title="Create automation" subtitle="Writes directly into the existing `SmartAutomation` backend.">
          <div className="space-y-4">
            <label className="block">
              <span className="mb-2 block text-xs uppercase tracking-[0.18em] text-cyan-200/52">Trigger phrase</span>
              <input
                value={trigger}
                onChange={(event) => setTrigger(event.target.value)}
                placeholder="movie time"
                className="w-full rounded-2xl border border-cyan-400/18 bg-[rgba(255,255,255,0.03)] px-4 py-3 text-sm text-cyan-50 outline-none"
              />
            </label>

            <label className="block">
              <span className="mb-2 block text-xs uppercase tracking-[0.18em] text-cyan-200/52">Actions, one per line</span>
              <textarea
                value={actionsText}
                onChange={(event) => setActionsText(event.target.value)}
                placeholder={"turn living room lights off\nset lg tv volume to 12"}
                rows={6}
                className="w-full rounded-2xl border border-cyan-400/18 bg-[rgba(255,255,255,0.03)] px-4 py-3 text-sm text-cyan-50 outline-none"
              />
            </label>

            <label className="block">
              <span className="mb-2 block text-xs uppercase tracking-[0.18em] text-cyan-200/52">Optional schedule</span>
              <input
                value={schedule}
                onChange={(event) => setSchedule(event.target.value)}
                placeholder="daily at 9 pm"
                className="w-full rounded-2xl border border-cyan-400/18 bg-[rgba(255,255,255,0.03)] px-4 py-3 text-sm text-cyan-50 outline-none"
              />
            </label>

            <button
              type="button"
              onClick={submit}
              className="rounded-2xl border border-cyan-400/18 bg-cyan-400/10 px-4 py-3 text-sm text-cyan-50"
            >
              Save automation
            </button>
          </div>
        </Card>

        <Card title="Saved automations" subtitle="Real automation records loaded from the existing Atlas automation store.">
          <div className="space-y-3">
            {automations.map((automation) => (
              <div key={automation.id} className="rounded-[28px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-5">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div>
                    <p className="text-lg font-semibold text-cyan-50">{automation.trigger}</p>
                    <p className="mt-1 text-xs uppercase tracking-[0.18em] text-cyan-200/48">{automation.schedule || 'Manual trigger'}</p>
                  </div>
                  <span className="rounded-full border border-cyan-400/14 bg-cyan-400/5 px-3 py-1 text-xs text-cyan-100/72">
                    {automation.isEnabled ? 'Enabled' : 'Disabled'}
                  </span>
                </div>

                <div className="mt-4 space-y-2">
                  {automation.actions.map((action) => (
                    <div key={action} className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-4 py-3 text-sm text-cyan-100/72">
                      {action}
                    </div>
                  ))}
                </div>

                <div className="mt-4 grid grid-cols-3 gap-3">
                  <MiniStat label="Runs" value={String(automation.triggerCount)} />
                  <MiniStat label="Last run" value={formatRelativeTime(automation.lastTriggeredUtc)} />
                  <MiniStat label="Created" value={formatRelativeTime(automation.createdAtUtc)} />
                </div>

                <div className="mt-4 flex flex-wrap gap-3">
                  <button type="button" onClick={() => runAutomation(automation.id)} className="rounded-full border border-cyan-400/18 bg-cyan-400/10 px-4 py-2 text-sm text-cyan-50">Run now</button>
                  <button type="button" onClick={() => toggleAutomation(automation.id)} className="rounded-full border border-cyan-400/12 bg-[rgba(255,255,255,0.03)] px-4 py-2 text-sm text-cyan-50">{automation.isEnabled ? 'Disable' : 'Enable'}</button>
                  <button type="button" onClick={() => deleteAutomation(automation.id)} className="rounded-full border border-red-400/16 bg-red-500/10 px-4 py-2 text-sm text-red-100">Delete</button>
                </div>
              </div>
            ))}
            {automations.length === 0 && <EmptyState text="No automations are saved yet. Create one here to write directly into Atlas automation storage." />}
          </div>
        </Card>
      </div>
    </div>
  );
}

function CustomCommandsPage({ snapshot }: { snapshot: SmartHomeSnapshot | null }) {
  return (
    <div className="space-y-6">
      <CommandStudio snapshot={snapshot} />
      <GreetingStudio snapshot={snapshot} />
    </div>
  );
}

function AlertsPage({ alerts }: { alerts: SmartHomeSnapshot['alerts'] }) {
  return (
    <Card title="Alert feed" subtitle="Live alert feed assembled from Atlas Ledger events, provider errors, and offline device warnings.">
      <div className="space-y-3">
        {alerts.map((alert) => (
          <AlertRow key={alert.id} alert={alert} />
        ))}
        {alerts.length === 0 && <EmptyState text="No alerts are currently visible in the smart home feed." />}
      </div>
    </Card>
  );
}

function ClimateEnergyPage({ devices }: { devices: LiveDevice[] }) {
  const climateDevices = getClimateTelemetry(devices);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <StatCard title="Climate devices" value={String(climateDevices.length)} detail="Devices whose names or capabilities expose climate-related telemetry" />
        <StatCard title="Energy telemetry" value={String(climateDevices.filter((device) => device.capabilities.some((capability) => capability.instance.toLowerCase().includes('energy') || capability.instance.toLowerCase().includes('power'))).length)} detail="Devices publishing power or energy style capabilities" />
        <StatCard title="Leak detection" value={String(climateDevices.filter((device) => device.capabilities.some((capability) => capability.instance.toLowerCase().includes('leak'))).length)} detail="Devices exposing leak-style capability names" />
      </div>

      <Card title="Climate and energy devices" subtitle="Only live telemetry already exposed by providers is shown here.">
        <div className="space-y-3">
          {climateDevices.map((device) => (
            <DeviceRow key={`${device.providerId}:${device.deviceId}`} device={device} />
          ))}
          {climateDevices.length === 0 && (
            <EmptyState text="The current providers do not expose thermostat, humidity, air quality, leak, or energy telemetry into Smart Home yet." />
          )}
        </div>
      </Card>
    </div>
  );
}

function AccessPage({ devices }: { devices: LiveDevice[] }) {
  const accessDevices = getAccessDevices(devices);

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <StatCard title="Locks and entry" value={String(accessDevices.length)} detail="Devices whose names or types look like access-control hardware" />
        <StatCard title="Remote actions" value={String(accessDevices.filter((device) => device.capabilities.length > 0).length)} detail="Access devices with exposed capabilities" />
        <StatCard title="Guest access" value="Not exposed" detail="No guest access or code-management backend is published into Smart Home yet" />
      </div>

      <Card title="Access devices" subtitle="Only access hardware already exposed by the provider layer will appear here.">
        <div className="space-y-3">
          {accessDevices.map((device) => (
            <DeviceRow key={`${device.providerId}:${device.deviceId}`} device={device} />
          ))}
          {accessDevices.length === 0 && <EmptyState text="No lock, garage, or gate devices are currently connected through the active Smart Home providers." />}
        </div>
      </Card>
    </div>
  );
}

function AssistantPage({ snapshot, bridgeNotice, bridgeError, bridgeEventId }: { snapshot: SmartHomeSnapshot | null; bridgeNotice: string; bridgeError: string; bridgeEventId: number }) {
  return <AIAssistant snapshot={snapshot} latestNotice={bridgeNotice} latestError={bridgeError} bridgeEventId={bridgeEventId} />;
}

function useHashPage(): [PageId, (page: PageId) => void] {
  const [page, setPage] = useState<PageId>(readPageFromHash());

  useEffect(() => {
    const onHashChange = () => setPage(readPageFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const setHashPage = (nextPage: PageId) => {
    window.location.hash = nextPage;
    setPage(nextPage);
  };

  return [page, setHashPage];
}

function readPageFromHash(): PageId {
  const hash = window.location.hash.replace(/^#/, '').trim() as PageId;
  return PAGES.some((page) => page.id === hash) ? hash : 'overview';
}

function Card({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <section className="rounded-[32px] border border-cyan-400/12 bg-[rgba(5,10,18,0.76)] p-6 shadow-[0_22px_60px_rgba(0,0,0,0.28)] backdrop-blur-xl">
      <div className="mb-5">
        <p className="text-xl font-semibold text-cyan-50">{title}</p>
        <p className="mt-2 text-sm leading-6 text-cyan-100/62">{subtitle}</p>
      </div>
      {children}
    </section>
  );
}

function StatCard({ title, value, detail }: { title: string; value: string; detail: string }) {
  return (
    <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} className="rounded-[28px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-5 shadow-[0_18px_42px_rgba(0,0,0,0.22)]">
      <p className="text-xs uppercase tracking-[0.22em] text-cyan-200/50">{title}</p>
      <p className="mt-3 text-3xl font-semibold text-cyan-50">{value}</p>
      <p className="mt-2 text-sm leading-6 text-cyan-100/58">{detail}</p>
    </motion.div>
  );
}

function DeviceRow({ device, compact = false }: { device: LiveDevice; compact?: boolean }) {
  return (
    <div className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-4 py-3">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className={`${compact ? 'text-sm' : 'text-base'} font-medium text-cyan-50`}>{device.name}</p>
          <p className="mt-1 text-xs text-cyan-100/54">{device.providerName} · {device.deviceType || device.sku || 'Unknown type'}</p>
        </div>
        <span className="rounded-full border border-cyan-400/12 bg-[rgba(255,255,255,0.03)] px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-cyan-100/72">
          {device.isOnline === false ? 'Offline' : 'Online'}
        </span>
      </div>
    </div>
  );
}

function AlertRow({ alert }: { alert: SmartHomeSnapshot['alerts'][number] }) {
  return (
    <div className="rounded-[24px] border border-cyan-400/10 bg-[rgba(255,255,255,0.02)] p-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="rounded-full border border-cyan-400/12 bg-cyan-400/5 px-2 py-1 text-[10px] uppercase tracking-[0.18em] text-cyan-100/70">{alert.category}</span>
            <span className="rounded-full border border-cyan-400/12 bg-[rgba(255,255,255,0.03)] px-2 py-1 text-[10px] uppercase tracking-[0.18em] text-cyan-100/70">{alert.severity}</span>
          </div>
          <p className="mt-3 text-base font-medium text-cyan-50">{alert.title}</p>
          <p className="mt-2 text-sm leading-6 text-cyan-100/62">{alert.detail || 'No extra detail was attached to this alert.'}</p>
        </div>
        <div className="text-right text-xs text-cyan-100/48">
          <div>{formatRelativeTime(alert.timestampUtc)}</div>
          <div className="mt-1">{alert.source}</div>
        </div>
      </div>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-4 py-3">
      <span className="text-sm text-cyan-100/68">{label}</span>
      <span className="text-sm font-medium text-cyan-50">{value}</span>
    </div>
  );
}

function MiniStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-3 py-3 text-center">
      <div className="text-[11px] uppercase tracking-[0.18em] text-cyan-200/50">{label}</div>
      <div className="mt-2 text-sm font-medium text-cyan-50">{value}</div>
    </div>
  );
}

function Pill({ text }: { text: string }) {
  return <span className="rounded-full border border-cyan-400/12 bg-cyan-400/5 px-3 py-1 text-xs text-cyan-100/72">{text}</span>;
}

function EmptyState({ text }: { text: string }) {
  return <div className="rounded-2xl border border-dashed border-cyan-400/12 bg-cyan-400/5 px-4 py-5 text-sm leading-6 text-cyan-100/58">{text}</div>;
}

function Banner({ tone, text }: { tone: 'error' | 'notice'; text: string }) {
  return (
    <div
      className="mb-6 rounded-[24px] border px-4 py-3 text-sm"
      style={{
        background: tone === 'error' ? 'rgba(255,107,107,0.08)' : 'rgba(124,255,178,0.08)',
        borderColor: tone === 'error' ? 'rgba(255,107,107,0.18)' : 'rgba(124,255,178,0.18)',
        color: tone === 'error' ? '#FFD0D0' : '#D3FFE3',
      }}
    >
      {text}
    </div>
  );
}

function SidebarMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-3 py-3">
      <div className="text-[11px] uppercase tracking-[0.2em] text-cyan-200/50">{label}</div>
      <div className="mt-2 text-lg font-semibold text-cyan-50">{value}</div>
    </div>
  );
}

function HeaderPill({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-cyan-400/10 bg-cyan-400/5 px-4 py-3">
      <div className="text-[11px] uppercase tracking-[0.18em] text-cyan-200/50">{label}</div>
      <div className="mt-2 text-sm font-medium text-cyan-50">{value}</div>
    </div>
  );
}

function StatusDot({ tone }: { tone: 'good' | 'warn' | 'neutral' }) {
  const color = tone === 'good' ? '#7CFFB2' : tone === 'warn' ? '#FFB970' : '#9FB9D9';
  return <div className="h-2.5 w-2.5 rounded-full" style={{ background: color, boxShadow: `0 0 10px ${color}` }} />;
}