import { useEffect, useMemo, useState } from 'react';
import {
  Activity,
  Bell,
  Bot,
  Camera,
  Clock,
  Cpu,
  DoorOpen,
  Gauge,
  Grid3x3,
  Home,
  Lightbulb,
  Lock,
  MessageSquareText,
  Plus,
  PlusCircle,
  RefreshCcw,
  Shield,
  Sparkles,
  Thermometer,
  TrendingUp,
  Wifi,
  Workflow,
  Zap,
} from 'lucide-react';
import {
  askAtlas,
  createAutomation,
  deleteAutomation,
  discoverNetwork,
  executeDeviceAction,
  openExternalUrl,
  refreshState,
  runAutomation,
  runSmartHomeCommand,
  toggleAutomation,
} from './bridge';
import { AIAssistant } from './components/AIAssistant';
import { CameraDeck } from './components/CameraDeck';
import { CommandStudio } from './components/CommandStudio';
import { DeviceInventory } from './components/DeviceInventory';
import { ProviderConnections } from './components/ProviderConnections';
import { SceneStudio } from './components/SceneStudio';
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
  looksLikeCamera,
  looksLikeClimate,
  looksLikeLight,
  looksLikeMedia,
  groupAlerts,
  summarizeAutomations,
  type LiveDevice,
  type RoomGroup,
} from './smartHomeModel';
import type { SmartHomeAutomationDraft, SmartHomeCapability, SmartHomeSnapshot } from './types';

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

const navigation: Array<{ id: PageId; name: string; path: string; icon: typeof Home }> = [
  { id: 'overview', name: 'Overview', path: '/', icon: Home },
  { id: 'devices', name: 'Devices', path: '/devices', icon: Cpu },
  { id: 'device-setup', name: 'Device Setup', path: '/device-setup', icon: PlusCircle },
  { id: 'rooms', name: 'Rooms', path: '/rooms', icon: Grid3x3 },
  { id: 'cameras', name: 'Cameras', path: '/cameras', icon: Camera },
  { id: 'security', name: 'Security', path: '/security', icon: Shield },
  { id: 'ai-scenes', name: 'AI Scenes', path: '/ai-scenes', icon: Sparkles },
  { id: 'automations', name: 'Automations', path: '/automations', icon: Workflow },
  { id: 'custom-commands', name: 'Custom Commands', path: '/custom-commands', icon: MessageSquareText },
  { id: 'alerts', name: 'Alerts', path: '/alerts', icon: Bell },
  { id: 'climate-energy', name: 'Climate & Energy', path: '/climate-energy', icon: Gauge },
  { id: 'access', name: 'Access', path: '/access', icon: DoorOpen },
  { id: 'ai-assistant', name: 'AI Assistant', path: '/ai-assistant', icon: Bot },
];

interface DesignSmartHomeAppProps {
  snapshot: SmartHomeSnapshot | null;
  bridgeError: string;
  bridgeNotice: string;
  bridgeEventId: number;
}

export function DesignSmartHomeApp({ snapshot, bridgeError, bridgeNotice, bridgeEventId }: DesignSmartHomeAppProps) {
  const [activePage, setActivePage] = useHashPage();
  const devices = useMemo(() => flattenDevices(snapshot), [snapshot]);
  const rooms = useMemo(() => buildRoomGroups(snapshot), [snapshot]);
  const alerts = useMemo(() => groupAlerts(snapshot?.alerts ?? []), [snapshot]);
  const setupMethods = useMemo(() => buildSetupMethods(snapshot), [snapshot]);
  const integrations = useMemo(() => buildIntegrationStates(snapshot), [snapshot]);
  const recommendations = useMemo(() => buildRecommendations(snapshot), [snapshot]);
  const automationSummary = useMemo(() => summarizeAutomations(snapshot?.automations ?? []), [snapshot]);

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black text-gray-100">
      <div className="flex min-h-screen bg-black text-gray-100">
        <aside className="w-64 bg-gradient-to-b from-gray-950 via-gray-900 to-black border-r border-gray-800/50 flex flex-col">
          <div className="p-6">
            <div className="text-2xl font-bold bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text text-transparent">
              SMARTCORE
            </div>
            <div className="text-xs text-gray-500 mt-1">Atlas Command Center</div>
          </div>

          <nav className="flex-1 px-3 py-2 overflow-y-auto">
            {navigation.map((item) => {
              const Icon = item.icon;
              const isActive = item.id === activePage;

              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => setActivePage(item.id)}
                  className={[
                    'w-full flex items-center gap-3 px-3 py-2.5 rounded-lg mb-1 transition-all text-left',
                    isActive
                      ? 'bg-cyan-500/10 text-cyan-400 border border-cyan-500/30'
                      : 'text-gray-400 hover:bg-gray-800/50 hover:text-gray-200 border border-transparent',
                  ].join(' ')}
                >
                  <Icon className="w-5 h-5 flex-shrink-0" />
                  <span className="text-sm font-medium">{item.name}</span>
                </button>
              );
            })}
          </nav>

          <div className="p-4 border-t border-gray-800/50 space-y-3">
            <div>
              <div className="text-xs text-gray-500">System Status</div>
              <div className="flex items-center gap-2 mt-2">
                <div className={`w-2 h-2 rounded-full ${snapshot ? 'bg-green-500 animate-pulse' : 'bg-yellow-500 animate-pulse'}`} />
                <span className="text-xs text-gray-400">{snapshot ? 'Live Smart Home Connected' : 'Waiting for Snapshot'}</span>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs">
              <SidebarStat label="Devices" value={String(snapshot?.totalDevices ?? 0)} />
              <SidebarStat label="Alerts" value={String(alerts.filter((alert) => !alert.isResolved).length)} />
              <SidebarStat label="Online" value={String(snapshot?.onlineDevices ?? 0)} />
              <SidebarStat label="Autos" value={String(automationSummary.enabled)} />
            </div>
          </div>
        </aside>

        <main className="flex-1 overflow-auto">
          {(bridgeError || bridgeNotice) && (
            <div className="px-8 pt-6">
              {bridgeError ? <Banner tone="error" text={bridgeError} /> : null}
              {!bridgeError && bridgeNotice ? <Banner tone="notice" text={bridgeNotice} /> : null}
            </div>
          )}

          <PageRouter
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
        </main>
      </div>
    </div>
  );
}

function PageRouter({
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
      return <DeviceSetupPage snapshot={snapshot} devices={devices} setupMethods={setupMethods} integrations={integrations} />;
    case 'rooms':
      return <RoomsPage rooms={rooms} />;
    case 'cameras':
      return <CamerasPage snapshot={snapshot} devices={devices} />;
    case 'security':
      return <SecurityPage snapshot={snapshot} devices={devices} alerts={alerts} />;
    case 'ai-scenes':
      return <AIScenesPage snapshot={snapshot} devices={devices} recommendations={recommendations} />;
    case 'automations':
      return <AutomationsPage snapshot={snapshot} recommendations={recommendations} />;
    case 'custom-commands':
      return <CustomCommandsPage snapshot={snapshot} />;
    case 'alerts':
      return <AlertsPage alerts={alerts} />;
    case 'climate-energy':
      return <ClimateEnergyPage devices={devices} />;
    case 'access':
      return <AccessPage devices={devices} />;
    case 'ai-assistant':
      return <AIAssistantPage snapshot={snapshot} bridgeNotice={bridgeNotice} bridgeError={bridgeError} bridgeEventId={bridgeEventId} />;
    default:
      return null;
  }
}

function OverviewPage({
  snapshot,
  devices,
  alerts,
  recommendations,
}: {
  snapshot: SmartHomeSnapshot | null;
  devices: LiveDevice[];
  alerts: SmartHomeSnapshot['alerts'];
  recommendations: ReturnType<typeof buildRecommendations>;
}) {
  const cameras = devices.filter(looksLikeCamera);
  const climateDevices = devices.filter(looksLikeClimate);
  const activeAlerts = alerts.filter((alert) => !alert.isResolved);
  const criticalAlerts = activeAlerts.filter((alert) => alert.severity === 'critical' || alert.severity === 'high');
  const recentEvents = activeAlerts.slice(0, 4);

  const quickStats = [
    { label: 'Active Devices', value: String(snapshot?.onlineDevices ?? 0), icon: Activity, color: 'text-cyan-400' },
    { label: 'Energy Usage', value: climateDevices.length > 0 ? `${climateDevices.length} live` : 'N/A', icon: Zap, color: 'text-yellow-400' },
    { label: 'Temperature', value: climateDevices.length > 0 ? 'Live' : 'N/A', icon: Thermometer, color: 'text-orange-400' },
    { label: 'Security Status', value: snapshot?.security.mode || 'Unknown', icon: Shield, color: 'text-green-400' },
  ];

  return (
    <PageShell title="Command Center" subtitle="Your smart home at a glance">
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {quickStats.map((stat) => {
          const Icon = stat.icon;
          return (
            <div key={stat.label} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all">
              <div className="flex items-center justify-between mb-2">
                <Icon className={`w-6 h-6 ${stat.color}`} />
                <TrendingUp className="w-4 h-4 text-green-400" />
              </div>
              <div className="text-3xl font-bold text-gray-100 mb-1">{stat.value}</div>
              <div className="text-sm text-gray-500">{stat.label}</div>
            </div>
          );
        })}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <InfoPanel title="Home Mode" accent="text-cyan-400">
          <div className="space-y-3">
            <ModeButton active label="Current State" value="Not Exposed" />
            <p className="text-sm text-gray-400">Atlas does not yet publish a dedicated home mode backend into Smart Home.</p>
          </div>
        </InfoPanel>

        <InfoPanel title="Security" accent="text-cyan-400">
          <div className="mb-4">
            <div className="inline-flex items-center gap-2 rounded-full bg-green-500/20 text-green-400 border border-green-500/30 px-3 py-1 text-xs">
              <Shield className="w-3 h-3" />
              {snapshot?.security.mode || 'Unknown'}
            </div>
          </div>
          <div className="space-y-2 text-sm">
            <PairRow label="Critical Alerts" value={String(criticalAlerts.length)} />
            <PairRow label="Active Sensors" value={String(getSecurityDevices(devices).length)} />
            <PairRow label="Last Activity" value={recentEvents[0] ? formatRelativeTime(recentEvents[0].timestampUtc) : 'No recent alerts'} />
          </div>
        </InfoPanel>

        <InfoPanel title="Climate" accent="text-cyan-400">
          <div className="text-center mb-4">
            <div className="text-5xl font-bold text-gray-100">{climateDevices.length > 0 ? 'Live' : '--'}</div>
            <div className="text-sm text-gray-500 mt-1">{climateDevices.length > 0 ? `${climateDevices.length} climate devices` : 'No climate telemetry published'}</div>
          </div>
          <div className="space-y-2 text-sm">
            <PairRow label="Humidity" value="Live if exposed" />
            <PairRow label="Air Quality" value={climateDevices.length > 0 ? 'Available on some devices' : 'Unavailable'} />
          </div>
        </InfoPanel>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
        <InfoPanel title="Recent Events" accent="text-cyan-400" icon={<Clock className="w-5 h-5 text-gray-500" />}>
          <div className="space-y-3">
            {recentEvents.map((event) => (
              <div key={event.id} className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                <div>
                  <div className="text-gray-200">{event.title}</div>
                  <div className="text-sm text-gray-500">{formatRelativeTime(event.timestampUtc)}</div>
                </div>
                <SeverityBadge severity={event.severity} />
              </div>
            ))}
            {recentEvents.length === 0 ? <EmptyState text="No recent Smart Home events have been published yet." /> : null}
          </div>
        </InfoPanel>

        <InfoPanel title="Active Cameras" accent="text-cyan-400" icon={<Camera className="w-5 h-5 text-gray-500" />}>
          <div className="space-y-3">
            {cameras.slice(0, 3).map((camera) => (
              <div key={camera.deviceId} className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                <div>
                  <div className="text-gray-200">{camera.name}</div>
                  <div className="inline-flex items-center gap-2 text-xs text-green-400 mt-1">
                    <div className={`w-2 h-2 rounded-full ${camera.isOnline === false ? 'bg-yellow-500' : 'bg-green-500 animate-pulse'}`} />
                    {camera.isOnline === false ? 'offline / cached' : 'active'}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => window.location.hash = '#/cameras'}
                  className="px-3 py-2 text-sm rounded-md border border-gray-700 hover:border-cyan-500/40"
                >
                  Open Camera Centre
                </button>
              </div>
            ))}
            {cameras.length === 0 ? <EmptyState text="No camera devices are available in the live snapshot." /> : null}
          </div>
        </InfoPanel>
      </div>

      <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6 mt-6">
        <h2 className="text-xl font-semibold text-purple-400 mb-4">AI Recommendations</h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {recommendations.map((recommendation) => (
            <div key={recommendation.title} className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">{recommendation.title}</div>
              <div className="text-sm text-gray-400">{recommendation.detail}</div>
            </div>
          ))}
          {recommendations.length === 0 ? <EmptyState text="Atlas does not have any live Smart Home recommendations right now." /> : null}
        </div>
      </div>
    </PageShell>
  );
}

function DevicesPage({ snapshot, devices }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[] }) {
  const [pendingKey, setPendingKey] = useState<string | null>(null);

  const executeBooleanToggle = (device: LiveDevice, capability: SmartHomeCapability, nextValue: boolean) => {
    const actionKey = `${device.deviceId}:${capability.type}:${capability.instance}`;
    setPendingKey(actionKey);
    executeDeviceAction({
      providerId: device.providerId,
      deviceId: device.deviceId,
      sku: device.sku,
      capabilityType: capability.type,
      capabilityInstance: capability.instance,
      value: nextValue,
    });
    window.setTimeout(() => {
      refreshState();
      setPendingKey(null);
    }, 900);
  };

  return (
    <PageShell title="Connected Devices" subtitle={`${devices.length} devices connected`}>
      <div className="flex items-center justify-end mb-8">
        <button
          type="button"
          onClick={() => window.location.hash = '#/device-setup'}
          className="px-4 py-2 rounded-md bg-cyan-600 hover:bg-cyan-700 text-white"
        >
          Add Device
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
        {devices.map((device) => {
          const Icon = getDeviceIcon(device);
          const toggleCapability = findPrimaryBooleanCapability(device);
          const isOn = toggleCapability ? Boolean(toggleCapability.stateValue) : device.isOnline !== false;
          const actionKey = toggleCapability ? `${device.deviceId}:${toggleCapability.type}:${toggleCapability.instance}` : null;

          return (
            <div key={`${device.providerId}:${device.deviceId}`} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all">
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-center gap-3">
                  <div className={`p-2 rounded-lg ${isOn ? 'bg-cyan-500/20' : 'bg-gray-800/50'}`}>
                    <Icon className={`w-6 h-6 ${isOn ? 'text-cyan-400' : 'text-gray-500'}`} />
                  </div>
                  <div>
                    <div className="font-semibold text-gray-200">{device.name}</div>
                    <div className="text-xs text-gray-500">{device.deviceType || device.providerName}</div>
                  </div>
                </div>
                {toggleCapability ? (
                  <button
                    type="button"
                    disabled={pendingKey === actionKey}
                    onClick={() => executeBooleanToggle(device, toggleCapability, !Boolean(toggleCapability.stateValue))}
                    className={[
                      'px-3 py-1.5 rounded-full text-xs border',
                      isOn ? 'bg-green-500/20 text-green-400 border-green-500/30' : 'bg-gray-700/20 text-gray-400 border-gray-700/30',
                    ].join(' ')}
                  >
                    {pendingKey === actionKey ? 'Updating' : isOn ? 'On' : 'Off'}
                  </button>
                ) : (
                  <div className={`px-3 py-1.5 rounded-full text-xs border ${device.isOnline === false ? 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30' : 'bg-green-500/20 text-green-400 border-green-500/30'}`}>
                    {device.isOnline === false ? 'Offline' : 'Online'}
                  </div>
                )}
              </div>

              <div className="space-y-2 mb-4">
                <PairRow label="Provider" value={device.providerName} />
                <PairRow label="Capabilities" value={String(device.capabilities.length)} />
                <PairRow label="Primary State" value={describePrimaryState(device)} />
              </div>

              <div className="flex gap-2 mt-4">
                <button
                  type="button"
                  onClick={() => askAtlas(buildDeviceAskPrompt(device), device.providerId, device.deviceId)}
                  className="flex-1 px-3 py-2 text-sm rounded-md border border-gray-700 hover:border-cyan-500/40"
                >
                  Ask Atlas
                </button>
                <button
                  type="button"
                  onClick={() => openAutomationDraftForDevice(device)}
                  className="flex-1 px-3 py-2 text-sm rounded-md border border-gray-700 hover:border-cyan-500/40"
                >
                  Automate
                </button>
              </div>
            </div>
          );
        })}
      </div>

      <div className="mt-8 bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
        <h2 className="text-xl font-semibold text-cyan-400 mb-2">Advanced Live Controls</h2>
        <p className="text-sm text-gray-400 mb-4">These are the existing native Atlas control surfaces, kept in place for provider-specific capability control.</p>
        <DeviceInventory providers={snapshot?.providers ?? []} agentSettings={snapshot?.agentSettings ?? null} />
      </div>
    </PageShell>
  );
}

function DeviceSetupPage({
  snapshot,
  devices,
  setupMethods,
  integrations,
}: {
  snapshot: SmartHomeSnapshot | null;
  devices: LiveDevice[];
  setupMethods: ReturnType<typeof buildSetupMethods>;
  integrations: ReturnType<typeof buildIntegrationStates>;
}) {
  const [tab, setTab] = useState<'catalog' | 'ecosystems' | 'pairing'>('ecosystems');
  const [isNetworkScanRunning, setIsNetworkScanRunning] = useState(false);
  const [activeProviderId, setActiveProviderId] = useState<string | null>(null);
  const pairing = snapshot?.companionPairing;
  const networkDiscovery = snapshot?.networkDiscovery;
  const liveIntegrations = integrations.filter((integration) => integration.configured && !integration.error);
  const integrationById = new Map(integrations.map((integration) => [integration.providerId, integration]));
  const lightCount = devices.filter(looksLikeLight).length;
  const cameraCount = devices.filter(looksLikeCamera).length;
  const climateCount = devices.filter(looksLikeClimate).length;
  const mediaCount = devices.filter(looksLikeMedia).length;
  const accessCount = getAccessDevices(devices).length;

  const openSetupTarget = (nextTab: 'catalog' | 'ecosystems' | 'pairing', anchorId?: string) => {
    setTab(nextTab);
    const targetId = anchorId || (nextTab === 'ecosystems'
      ? 'provider-connections'
      : nextTab === 'pairing'
        ? 'pairing-panel'
        : 'setup-catalog');

    let attempts = 0;
    const scrollToTarget = () => {
      const target = document.getElementById(targetId);
      if (target) {
        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        return;
      }

      if (attempts < 8) {
        attempts += 1;
        window.setTimeout(scrollToTarget, 120);
      }
    };

    window.setTimeout(scrollToTarget, 80);
  };

  const openProviderConnector = (providerId: string) => {
    setActiveProviderId(providerId);
    openSetupTarget('ecosystems', `provider-${providerId}`);
  };

  const ecosystemCatalog = [
    {
      id: 'philips_hue',
      title: 'Philips Hue',
      devices: 'Hue bulbs, lamps, lightstrips, gradient lights, bridge-managed rooms and zones',
      how: 'Enter the bridge IP or discovery JSON, press the physical Hue bridge button, then link and save the application key.',
      liveCount: integrationById.get('philips_hue')?.deviceCount ?? 0,
      status: integrationById.get('philips_hue')?.deviceCount ? 'live' : integrationById.get('philips_hue') ? 'ready' : 'limited',
      actionLabel: 'Open Hue connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-philips_hue',
    },
    {
      id: 'govee',
      title: 'Govee',
      devices: 'LED strips, bars, lamps, panels, scene-capable cloud devices, ambient lights',
      how: 'Paste a Govee developer API key, save it, then refresh providers to pull live devices and any scene-capable endpoints.',
      liveCount: integrationById.get('govee')?.deviceCount ?? 0,
      status: integrationById.get('govee')?.deviceCount ? 'live' : integrationById.get('govee') ? 'ready' : 'limited',
      actionLabel: 'Open Govee connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-govee',
    },
    {
      id: 'ring',
      title: 'Ring',
      devices: 'Doorbells, indoor and outdoor cameras, floodlight cams, alarm devices and live view endpoints',
      how: 'Sign in inside Atlas with your Ring account, complete 2FA if prompted, then refresh to load cameras and other Ring devices.',
      liveCount: integrationById.get('ring')?.deviceCount ?? 0,
      status: integrationById.get('ring')?.deviceCount ? 'live' : integrationById.get('ring') ? 'ready' : 'limited',
      actionLabel: 'Open Ring sign-in',
      tab: 'ecosystems' as const,
      anchorId: 'provider-ring',
    },
    {
      id: 'lg_webos',
      title: 'LG webOS',
      devices: 'Local LG TVs, webOS displays, media playback and power control targets',
      how: 'Scan the LAN for a TV or enter the TV host manually, confirm the prompt on the TV, then save the client key.',
      liveCount: integrationById.get('lg_webos')?.deviceCount ?? 0,
      status: integrationById.get('lg_webos')?.deviceCount ? 'live' : integrationById.get('lg_webos') ? 'ready' : 'limited',
      actionLabel: 'Open LG pairing',
      tab: 'ecosystems' as const,
      anchorId: 'provider-lg_webos',
    },
    {
      id: 'smartthings',
      title: 'SmartThings',
      devices: 'Samsung SmartThings hubs, switches, dimmers, locks, sensors, and linked ecosystems exposed through the SmartThings API',
      how: 'Paste a SmartThings personal access token and optionally a location id, then save to load devices from your SmartThings account.',
      liveCount: integrationById.get('smartthings')?.deviceCount ?? 0,
      status: integrationById.get('smartthings')?.deviceCount ? 'live' : integrationById.get('smartthings') ? 'ready' : 'limited',
      actionLabel: 'Open SmartThings connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-smartthings',
    },
    {
      id: 'home_assistant',
      title: 'Home Assistant',
      devices: 'Lights, switches, locks, media players, cameras, covers, climate entities, and any other supported Home Assistant domains',
      how: 'Enter the Home Assistant base URL and long-lived token, then save to load supported entities into Atlas.',
      liveCount: integrationById.get('home_assistant')?.deviceCount ?? 0,
      status: integrationById.get('home_assistant')?.deviceCount ? 'live' : integrationById.get('home_assistant') ? 'ready' : 'limited',
      actionLabel: 'Open Home Assistant connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-home_assistant',
    },
    {
      id: 'tapo_kasa',
      title: 'TP-Link Kasa / Tapo',
      devices: 'Compatible local TP-Link plugs, switches, bulbs, and other Kasa-style endpoints reachable on your LAN',
      how: 'Enter a local device host to try the Kasa-compatible local protocol. This path is strongest for compatible Kasa hardware today.',
      liveCount: integrationById.get('tapo_kasa')?.deviceCount ?? 0,
      status: integrationById.get('tapo_kasa')?.deviceCount ? 'live' : integrationById.get('tapo_kasa') ? 'ready' : 'limited',
      actionLabel: 'Open TP-Link connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-tapo_kasa',
    },
    {
      id: 'onvif_rtsp',
      title: 'ONVIF / RTSP',
      devices: 'Manual camera endpoints, NVR feeds, RTSP sources, and camera web interfaces for devices Atlas cannot onboard through a cloud provider',
      how: 'Enter a camera or NVR host and optionally a direct RTSP URL to surface the endpoint in Atlas as a camera source.',
      liveCount: integrationById.get('onvif_rtsp')?.deviceCount ?? 0,
      status: integrationById.get('onvif_rtsp')?.deviceCount ? 'live' : integrationById.get('onvif_rtsp') ? 'ready' : 'limited',
      actionLabel: 'Open ONVIF / RTSP connector',
      tab: 'ecosystems' as const,
      anchorId: 'provider-onvif_rtsp',
    },
    {
      id: 'companion',
      title: 'Atlas Companion',
      devices: 'Phone pairing, remote Atlas control, companion transport handoff and mobile setup entry point',
      how: pairing?.qrCodeDataUrl
        ? 'Scan the live QR from your phone while both devices are on the same reachable LAN.'
        : 'Refresh pairing status and verify Atlas is publishing a phone-reachable LAN address instead of localhost only.',
      liveCount: pairing?.qrCodeDataUrl ? 1 : 0,
      status: pairing?.qrCodeDataUrl ? 'live' : pairing?.isAvailable ? 'ready' : 'limited',
      actionLabel: 'Open QR pairing',
      tab: 'pairing' as const,
      anchorId: 'pairing-panel',
    },
  ];

  const deviceFamilies = [
    {
      id: 'lights',
      title: 'Lights & Scene Devices',
      count: lightCount,
      detail: 'Hue and Govee cover bulbs, strips, lamps, bars, panels, and most color-scene lighting Atlas can control today.',
      route: 'Use the Philips Hue or Govee connectors in Ecosystems & Brands.',
      actionLabel: 'Open lighting connectors',
      tab: 'ecosystems' as const,
      anchorId: 'provider-philips_hue',
    },
    {
      id: 'cameras',
      title: 'Cameras, Doorbells & Security',
      count: cameraCount,
      detail: 'Ring is the current path for smart cameras, doorbells, and live security video in Atlas.',
      route: 'Use the Ring sign-in flow in Ecosystems & Brands.',
      actionLabel: 'Open Ring setup',
      tab: 'ecosystems' as const,
      anchorId: 'provider-ring',
    },
    {
      id: 'media',
      title: 'TVs & Media Screens',
      count: mediaCount,
      detail: 'LG webOS TVs can be discovered locally and paired directly from Atlas.',
      route: 'Use LAN discovery or manual host pairing in the LG connector.',
      actionLabel: 'Open TV pairing',
      tab: 'ecosystems' as const,
      anchorId: 'provider-lg_webos',
    },
    {
      id: 'climate',
      title: 'Climate, Sensors & Energy',
      count: climateCount,
      detail: 'Atlas can surface climate and energy telemetry when those devices come through a supported provider, but there is no direct thermostat onboarding path yet.',
      route: 'Bring them in through a supported ecosystem; direct Matter/Zigbee/Z-Wave climate onboarding is not exposed yet.',
      actionLabel: 'Review setup methods',
      tab: 'catalog' as const,
      anchorId: 'setup-methods',
    },
    {
      id: 'access',
      title: 'Locks, Doors & Entry',
      count: accessCount,
      detail: 'Access devices can appear in Atlas if a supported provider exposes them, but Atlas does not yet provide a direct lock commissioning flow.',
      route: 'Use the provider that owns the lock or door device, then refresh Atlas.',
      actionLabel: 'Review supported paths',
      tab: 'catalog' as const,
      anchorId: 'setup-methods',
    },
  ];

  const onboardingCards = [
    {
      id: 'philips_hue',
      title: 'Philips Hue',
      detail: 'Bridge linking and local IP setup are already wired into Atlas.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'philips_hue')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'ring',
      title: 'Ring',
      detail: 'Sign in inside Atlas and keep cameras, doorbells, and live view in one place.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'ring')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'govee',
      title: 'Govee',
      detail: 'Cloud API onboarding for lights, strips, and scene-capable devices.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'govee')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'lg_webos',
      title: 'LG webOS',
      detail: 'LAN discovery and on-device pairing for local TVs.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'lg_webos')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'smartthings',
      title: 'SmartThings',
      detail: 'Cloud-connected Samsung SmartThings devices and linked ecosystems.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'smartthings')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'home_assistant',
      title: 'Home Assistant',
      detail: 'Bring supported Home Assistant entities into Atlas with a base URL and token.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'home_assistant')?.deviceCount ? 'live' : 'ready',
    },
    {
      id: 'tapo_kasa',
      title: 'TP-Link Kasa / Tapo',
      detail: 'Local TP-Link connector for compatible Kasa-style devices.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'tapo_kasa')?.deviceCount ? 'live' : 'limited',
    },
    {
      id: 'onvif_rtsp',
      title: 'ONVIF / RTSP',
      detail: 'Manual camera endpoint path for RTSP and ONVIF-adjacent sources.',
      targetTab: 'ecosystems' as const,
      status: integrations.find((integration) => integration.providerId === 'onvif_rtsp')?.deviceCount ? 'live' : 'limited',
    },
    {
      id: 'companion',
      title: 'Companion QR',
      detail: pairing?.qrCodeDataUrl
        ? 'A live pairing QR is available now for the Atlas companion transport.'
        : 'Use the pairing tab to check QR availability and copy the live payload.',
      targetTab: 'pairing' as const,
      status: pairing?.qrCodeDataUrl ? 'live' : pairing?.isAvailable ? 'ready' : 'limited',
    },
    {
      id: 'ai-guide',
      title: 'AI setup help',
      detail: 'Atlas can guide the next integration step based on your live providers and pairing state.',
      targetTab: 'catalog' as const,
      status: 'ready',
    },
  ];

  useEffect(() => {
    refreshState();
  }, []);

  const supportedNowCatalog = [
    { id: 'ring', title: 'Ring', category: 'Cameras & doorbells', status: 'supported now', how: 'Sign in inside Atlas using the Ring connector. Cameras then appear in Cameras and Security.' },
    { id: 'philips_hue', title: 'Philips Hue', category: 'Lights', status: 'supported now', how: 'Enter bridge IP, press the bridge button, then link and save the application key.' },
    { id: 'govee', title: 'Govee', category: 'Lights', status: 'supported now', how: 'Paste a Govee developer API key, save, then refresh providers.' },
    { id: 'lg_webos', title: 'LG webOS', category: 'TVs & media', status: 'supported now', how: 'Use LAN discovery or enter the TV host, then confirm the pairing prompt on the TV.' },
    { id: 'smartthings', title: 'SmartThings', category: 'Hub / multi-device', status: 'supported now', how: 'Paste a SmartThings personal access token and optionally a location id.' },
    { id: 'home_assistant', title: 'Home Assistant', category: 'Hub / multi-device', status: 'supported now', how: 'Enter your Home Assistant base URL and long-lived token.' },
    { id: 'tapo_kasa', title: 'TP-Link Kasa / Tapo', category: 'Local devices', status: 'limited now', how: 'Enter a compatible local TP-Link device host. This path currently targets Kasa-compatible local devices first.' },
    { id: 'onvif_rtsp', title: 'ONVIF / RTSP', category: 'Manual cameras', status: 'limited now', how: 'Enter a camera host and optionally a direct RTSP URL to surface a manual camera endpoint.' },
    { id: 'companion', title: 'Atlas Companion', category: 'Phone pairing', status: 'supported now', how: 'Open the QR pairing tab and scan the live Atlas QR from your phone.' },
  ];

  const ecosystemReferenceCatalog = [
    { title: 'Apple HomeKit', category: 'Hub / multi-device', status: 'not wired yet', how: 'Would need HomeKit pairing and secure accessory protocol support.' },
    { title: 'Google Home / Nest', category: 'Cameras, thermostats, speakers', status: 'not wired yet', how: 'Would need Google smart home auth and per-device API support.' },
    { title: 'Amazon Alexa', category: 'Speakers / routines / hub', status: 'not wired yet', how: 'Would need Alexa skill or local integration path.' },
    { title: 'Nanoleaf', category: 'Lights / panels', status: 'not wired yet', how: 'Would need local API discovery and auth token flow.' },
    { title: 'LIFX', category: 'Lights', status: 'not wired yet', how: 'Would need LAN discovery and cloud fallback support.' },
    { title: 'Shelly', category: 'Relays / power / sensors', status: 'not wired yet', how: 'Would need HTTP or CoAP discovery plus per-device control mapping.' },
    { title: 'Sonos', category: 'Speakers', status: 'not wired yet', how: 'Would need UPnP discovery and playback control wiring.' },
    { title: 'Roku', category: 'TV / streaming', status: 'not wired yet', how: 'Would need ECP discovery on the local network.' },
    { title: 'Android TV / Google TV', category: 'TV / streaming', status: 'not wired yet', how: 'Would need Android TV remote pairing and app control support.' },
    { title: 'Apple TV', category: 'TV / streaming', status: 'not wired yet', how: 'Would need MRP pairing and playback transport support.' },
    { title: 'Arlo', category: 'Cameras', status: 'not wired yet', how: 'Would need Arlo auth plus camera session handling.' },
    { title: 'Blink', category: 'Cameras', status: 'not wired yet', how: 'Would need Blink cloud auth and camera/device mapping.' },
    { title: 'Eufy', category: 'Cameras / doorbells', status: 'not wired yet', how: 'Would need Eufy account auth and local/cloud transport support.' },
    { title: 'Reolink', category: 'Cameras / NVR', status: 'not wired yet', how: 'Would need deeper ONVIF/RTSP/NVR discovery, stream negotiation, and vendor-specific login support.' },
    { title: 'UniFi Protect', category: 'Cameras / door access', status: 'not wired yet', how: 'Would need controller auth and Protect API support.' },
    { title: 'August / Yale / Aqara', category: 'Locks / access', status: 'not wired yet', how: 'Would need lock-provider auth and secure state/control mapping.' },
    { title: 'Ecobee / Tado / Honeywell', category: 'Climate', status: 'not wired yet', how: 'Would need thermostat cloud auth and climate entity support.' },
  ];

  const runNetworkScan = () => {
    setIsNetworkScanRunning(true);
    discoverNetwork();
    window.setTimeout(() => {
      refreshState();
      setIsNetworkScanRunning(false);
    }, 5000);
  };

  return (
    <PageShell
      title="Device Setup Hub"
      subtitle="Master onboarding center for all smart home devices, ecosystems, and protocols"
    >
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 mb-6">
        {onboardingCards.map((card) => (
          <button
            key={card.id}
            type="button"
            onClick={() => setTab(card.targetTab)}
            className="text-left bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all"
          >
            <div className="flex items-center justify-between gap-3 mb-3">
              <div className="text-lg font-semibold text-gray-100">{card.title}</div>
              <SeverityBadge
                severity={card.status === 'live' ? 'info' : card.status === 'limited' ? 'medium' : 'low'}
                text={card.status === 'live' ? 'live' : card.status === 'limited' ? 'check' : 'ready'}
              />
            </div>
            <div className="text-sm text-gray-400">{card.detail}</div>
          </button>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-3 mb-6">
        <button
          type="button"
          onClick={() => refreshState()}
          className="px-4 py-2 rounded-md bg-cyan-600 hover:bg-cyan-700 text-white"
        >
          Refresh Setup State
        </button>
        <button
          type="button"
          onClick={runNetworkScan}
          className="px-4 py-2 rounded-md border border-cyan-500/40 hover:border-cyan-400/60 text-gray-100"
        >
          {isNetworkScanRunning ? 'Scanning Network...' : 'Scan Network For Devices'}
        </button>
        <button
          type="button"
          onClick={() => window.location.hash = '#/ai-assistant'}
          className="px-4 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Open AI Integration Help
        </button>
        <div className="text-sm text-gray-400">
          {liveIntegrations.length > 0
            ? `${liveIntegrations.length} provider${liveIntegrations.length === 1 ? '' : 's'} already configured.`
            : 'No live providers yet. Start with Ecosystems & Brands or Pairing & QR.'}
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3 mb-6">
        <button
          type="button"
          onClick={() => openProviderConnector('ring')}
          className="text-left px-4 py-3 rounded-lg border border-cyan-500/30 bg-cyan-500/10 hover:border-cyan-400/60 text-gray-100"
        >
          Connect Ring
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('philips_hue')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Connect Hue
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('govee')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Connect Govee
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('lg_webos')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Pair LG TV
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('smartthings')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Connect SmartThings
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('home_assistant')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Connect Home Assistant
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('tapo_kasa')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Connect TP-Link
        </button>
        <button
          type="button"
          onClick={() => openProviderConnector('onvif_rtsp')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Add RTSP Camera
        </button>
        <button
          type="button"
          onClick={() => openSetupTarget('pairing', 'pairing-panel')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Open Phone QR Pairing
        </button>
        <button
          type="button"
          onClick={() => openExternalUrl('ms-settings:bluetooth', 'Bluetooth')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Open Bluetooth Pairing
        </button>
        <button
          type="button"
          onClick={() => openExternalUrl('ms-settings:network-wifi', 'Wi-Fi')}
          className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
        >
          Open Wi-Fi Settings
        </button>
      </div>

      <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-cyan-500/30 rounded-xl p-6 mb-6">
        <div className="flex items-center justify-between gap-4 mb-4">
          <div>
            <h2 className="text-xl font-semibold text-cyan-400">Network Discovery</h2>
            <p className="text-sm text-gray-400 mt-1">Scan your current LAN for devices Atlas can see right now, then use the connectors below for supported ecosystems.</p>
          </div>
          <button
            type="button"
            onClick={runNetworkScan}
            className="px-4 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-gray-100"
          >
            {isNetworkScanRunning ? 'Scanning...' : 'Run LAN Scan'}
          </button>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3 mb-4 text-sm">
          <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-3 text-gray-300">IP: {networkDiscovery?.localIp || 'Unknown'}</div>
          <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-3 text-gray-300">Gateway: {networkDiscovery?.gateway || 'Unknown'}</div>
          <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-3 text-gray-300">DNS: {networkDiscovery?.dnsServer || 'Unknown'}</div>
          <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-3 text-gray-300">Found: {networkDiscovery?.devices.length ?? 0} devices</div>
        </div>
        <div className="text-xs text-gray-500 mb-4">
          {networkDiscovery?.lastScanUtc ? `Last scan ${new Date(networkDiscovery.lastScanUtc).toLocaleTimeString()}` : 'No network scan has been run yet in Smart Home.'}
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {(networkDiscovery?.devices ?? []).slice(0, 18).map((device) => (
            <div key={`${device.ipAddress}-${device.macAddress}`} className="rounded-lg border border-gray-800 bg-gray-900/50 p-4">
              <div className="flex items-center justify-between gap-3 mb-2">
                <div className="font-medium text-gray-200">{device.hostname || device.ipAddress}</div>
                <SeverityBadge severity={device.isOnline ? 'info' : 'medium'} text={device.deviceType || 'unknown'} />
              </div>
              <div className="text-sm text-gray-400">{device.ipAddress}</div>
              <div className="text-xs text-gray-500 mt-1">{device.portServices || (device.openPorts.length > 0 ? `Open ports: ${device.openPorts.join(', ')}` : 'No common ports detected')}</div>
            </div>
          ))}
          {(networkDiscovery?.devices.length ?? 0) === 0 ? <EmptyState text="Run a LAN scan to discover reachable devices on your network." /> : null}
        </div>
      </div>

      <div id="provider-connections" className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-cyan-500/30 rounded-xl p-6 mb-6">
        <div className="flex items-center justify-between gap-3 mb-4">
          <div>
            <h2 className="text-xl font-semibold text-cyan-400">Connect Devices Inside Atlas</h2>
            <p className="text-sm text-gray-400 mt-1">These are the actual connection forms. Use them here instead of hunting through other pages.</p>
          </div>
          <button
            type="button"
            onClick={() => refreshState()}
            className="px-4 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-gray-100"
          >
            Refresh Providers
          </button>
        </div>
        <ProviderConnections providers={snapshot?.providers ?? []} activeProviderId={activeProviderId} />
      </div>

      <div className="inline-flex rounded-lg bg-gray-900/50 border border-gray-800 mb-6 overflow-hidden">
        {[
          { id: 'catalog', label: 'Setup Methods' },
          { id: 'ecosystems', label: 'Ecosystems & Brands' },
          { id: 'pairing', label: 'Pairing & QR' },
        ].map((item) => (
          <button
            key={item.id}
            type="button"
            onClick={() => setTab(item.id as typeof tab)}
            className={`px-4 py-2 text-sm ${tab === item.id ? 'bg-cyan-500/10 text-cyan-400' : 'text-gray-400 hover:text-gray-200'}`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {tab === 'catalog' ? (
        <div className="grid grid-cols-1 lg:grid-cols-[1.05fr_0.95fr] gap-6">
          <div className="space-y-6">
            <InfoPanel title="Supported Device Families" accent="text-cyan-400">
              <div id="setup-catalog" className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {deviceFamilies.map((family) => (
                  <div key={family.id} className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
                    <div className="flex items-center justify-between gap-3 mb-2">
                      <div>
                        <div className="font-medium text-gray-200">{family.title}</div>
                        <div className="text-xs text-gray-500 mt-1">{family.count} live device{family.count === 1 ? '' : 's'} detected</div>
                      </div>
                      <SeverityBadge severity={family.count > 0 ? 'info' : 'medium'} text={family.count > 0 ? 'live' : 'route'} />
                    </div>
                    <div className="text-sm text-gray-400 mb-3">{family.detail}</div>
                    <div className="text-xs text-cyan-300/80 mb-3">{family.route}</div>
                    <button
                      type="button"
                      onClick={() => openSetupTarget(family.tab, family.anchorId)}
                      className="px-3 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-sm text-gray-100"
                    >
                      {family.actionLabel}
                    </button>
                  </div>
                ))}
              </div>
            </InfoPanel>

            <InfoPanel title="Supported Ecosystems And What They Add" accent="text-cyan-400">
              <div className="grid grid-cols-1 gap-3">
                {ecosystemCatalog.map((ecosystem) => (
                  <div key={ecosystem.id} className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
                    <div className="flex items-center justify-between gap-3 mb-2">
                      <div>
                        <div className="font-medium text-gray-200">{ecosystem.title}</div>
                        <div className="text-xs text-gray-500 mt-1">{ecosystem.devices}</div>
                      </div>
                      <SeverityBadge severity={ecosystem.status === 'live' ? 'info' : ecosystem.status === 'limited' ? 'medium' : 'low'} text={ecosystem.status === 'live' ? 'live' : ecosystem.status === 'limited' ? 'check' : 'ready'} />
                    </div>
                    <div className="text-sm text-gray-400 mb-2">{ecosystem.how}</div>
                    <div className="text-xs text-cyan-300/80 mb-3">
                      {ecosystem.liveCount > 0 ? `${ecosystem.liveCount} device${ecosystem.liveCount === 1 ? '' : 's'} already loaded in Atlas.` : 'No live devices loaded from this path yet.'}
                    </div>
                    <button
                      type="button"
                      onClick={() => openSetupTarget(ecosystem.tab, ecosystem.anchorId)}
                      className="px-3 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-sm text-gray-100"
                    >
                      {ecosystem.actionLabel}
                    </button>
                  </div>
                ))}
              </div>
            </InfoPanel>

            <InfoPanel title="Supported Right Now In Atlas" accent="text-cyan-400">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {supportedNowCatalog.map((entry) => (
                  <div key={entry.id} className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
                    <div className="flex items-center justify-between gap-3 mb-2">
                      <div className="font-medium text-gray-200">{entry.title}</div>
                      <SeverityBadge severity="info" text={entry.status} />
                    </div>
                    <div className="text-xs text-gray-500 mb-2">{entry.category}</div>
                    <div className="text-sm text-gray-400">{entry.how}</div>
                  </div>
                ))}
              </div>
            </InfoPanel>

            <InfoPanel title="Wider Smart Home Ecosystem Reference" accent="text-cyan-400">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {ecosystemReferenceCatalog.map((entry) => (
                  <div key={entry.title} className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
                    <div className="flex items-center justify-between gap-3 mb-2">
                      <div className="font-medium text-gray-200">{entry.title}</div>
                      <SeverityBadge severity="medium" text={entry.status} />
                    </div>
                    <div className="text-xs text-gray-500 mb-2">{entry.category}</div>
                    <div className="text-sm text-gray-400">{entry.how}</div>
                  </div>
                ))}
              </div>
            </InfoPanel>

            <InfoPanel title="Available Setup Methods" accent="text-cyan-400">
              <div id="setup-methods" className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {setupMethods.map((method) => (
                  <button
                    key={method.id}
                    type="button"
                    onClick={method.action ? () => openExternalUrl(method.action, method.label) : undefined}
                    className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60 text-left disabled:cursor-default"
                    disabled={!method.action}
                  >
                    <div className="flex items-center justify-between gap-3 mb-2">
                      <div className="font-medium text-gray-200">{method.label}</div>
                      <SeverityBadge severity={method.status === 'available' ? 'info' : method.status === 'limited' ? 'medium' : 'low'} text={method.status} />
                    </div>
                    <div className="text-sm text-gray-400">{method.detail}</div>
                  </button>
                ))}
              </div>
            </InfoPanel>

            <InfoPanel title="Live Device Snapshot" accent="text-cyan-400">
              <div className="space-y-3">
                {devices.slice(0, 6).map((device) => (
                  <div key={`${device.providerId}:${device.deviceId}`} className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                    <div>
                      <div className="text-gray-200">{device.name}</div>
                      <div className="text-xs text-gray-500">{device.providerName}</div>
                    </div>
                    <div className="text-xs text-gray-400">{device.isOnline === false ? 'offline' : 'online'}</div>
                  </div>
                ))}
                {devices.length === 0 ? <EmptyState text="No devices are visible yet. Configure a provider connection to start onboarding." /> : null}
              </div>
            </InfoPanel>
          </div>

          <InfoPanel title="AI Integration Guide" accent="text-cyan-400">
            <div className="space-y-4">
              <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
                <div className="text-sm font-medium text-gray-200 mb-2">Next recommended step</div>
                <div className="text-sm text-gray-400">
                  {pairing?.qrCodeDataUrl
                    ? 'QR pairing is already live. Scan it from Pairing & QR, then return here and refresh the setup state.'
                    : liveIntegrations.length > 0
                      ? 'Your providers are partially configured. Open Ecosystems & Brands to finish sign-in, linking, or device refresh for each brand.'
                      : 'Start in Ecosystems & Brands to add Hue, Ring, Govee, or LG webOS, then use Pairing & QR if you want the Atlas companion path.'}
                </div>
              </div>

              <div className="grid grid-cols-1 gap-3">
                <button
                  type="button"
                  onClick={() => setTab('ecosystems')}
                  className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
                >
                  Configure providers and refresh devices
                </button>
                <button
                  type="button"
                  onClick={() => setTab('pairing')}
                  className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
                >
                  Open QR pairing and payload tools
                </button>
                <button
                  type="button"
                  onClick={() => window.location.hash = '#/ai-assistant'}
                  className="text-left px-4 py-3 rounded-lg border border-gray-700 hover:border-cyan-500/40 text-gray-100"
                >
                  Ask Atlas AI what to connect next
                </button>
              </div>

              <div className="rounded-lg border border-dashed border-gray-800 p-4 text-sm text-gray-400">
                QR pairing only appears when the companion transport publishes a phone-reachable LAN address. If this panel is blank, Atlas may still be bound to localhost only.
              </div>
            </div>
          </InfoPanel>
        </div>
      ) : null}

      {tab === 'ecosystems' ? (
        <div className="space-y-6">
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {ecosystemCatalog.map((ecosystem) => (
              <div key={ecosystem.id} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all">
                <div className="flex items-center justify-between gap-3 mb-3">
                  <div className="text-lg font-semibold text-gray-100">{ecosystem.title}</div>
                  <SeverityBadge severity={ecosystem.status === 'live' ? 'info' : ecosystem.status === 'limited' ? 'medium' : 'low'} text={ecosystem.status === 'live' ? 'live' : ecosystem.status === 'limited' ? 'check' : 'ready'} />
                </div>
                <div className="text-sm text-gray-400 mb-3">{ecosystem.devices}</div>
                <div className="text-sm text-cyan-200/80 mb-4">{ecosystem.how}</div>
                <button
                  type="button"
                  onClick={() => openSetupTarget(ecosystem.tab, ecosystem.anchorId)}
                  className="px-3 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-sm text-gray-100"
                >
                  {ecosystem.actionLabel}
                </button>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {integrations.map((integration) => (
              <div key={integration.providerId} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all">
                <div className="flex items-center justify-between gap-3 mb-3">
                  <div>
                    <div className="font-semibold text-gray-200">{integration.label}</div>
                    <div className="text-sm text-gray-500">{integration.deviceCount} devices</div>
                  </div>
                  <div className={`px-3 py-1.5 rounded-full text-xs border ${integration.configured ? 'bg-green-500/20 text-green-400 border-green-500/30' : 'bg-gray-700/20 text-gray-400 border-gray-700/30'}`}>
                    {integration.configured ? 'connected' : 'not connected'}
                  </div>
                </div>
                <div className="text-sm text-gray-400 mb-3">{integration.detail || integration.status}</div>
                <div className="flex flex-wrap gap-2">
                  {integration.methods.map((method) => (
                    <span key={method} className="px-2 py-1 rounded-full text-xs bg-gray-900/60 border border-gray-800 text-gray-300">{method}</span>
                  ))}
                </div>
              </div>
            ))}
          </div>

        </div>
      ) : null}

      {tab === 'pairing' ? (
        <div className="grid grid-cols-1 xl:grid-cols-[0.8fr_1.2fr] gap-6">
          <InfoPanel title="Companion QR Pairing" accent="text-cyan-400">
            <div id="pairing-panel" className="space-y-0">
            {pairing?.qrCodeDataUrl ? (
              <div className="space-y-4">
                <div className="rounded-xl bg-white p-4 inline-block">
                  <img src={pairing.qrCodeDataUrl} alt="Companion pairing QR" className="w-64 h-64" />
                </div>
                <div className="text-sm text-gray-400">{pairing.availabilityMessage}</div>
                <button
                  type="button"
                  onClick={() => refreshState()}
                  className="px-4 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-gray-100"
                >
                  Refresh QR Status
                </button>
              </div>
            ) : (
              <div className="space-y-4">
                <EmptyState text={pairing?.availabilityMessage || 'QR pairing is not available in the current runtime state.'} />
                <div className="text-sm text-gray-400">
                  Atlas can only render the QR when the companion service has a live LAN base URL that a phone can reach.
                </div>
                <button
                  type="button"
                  onClick={() => refreshState()}
                  className="px-4 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-gray-100"
                >
                  Retry Pairing Refresh
                </button>
              </div>
            )}
            </div>
          </InfoPanel>

          <InfoPanel title="Pairing Payload" accent="text-cyan-400">
            <div className="space-y-2 text-sm">
              <PairRow label="Display Name" value={pairing?.displayName || 'Unavailable'} />
              <PairRow label="Host" value={pairing?.host || 'Unavailable'} />
              <PairRow label="Base URL" value={pairing?.baseUrl || 'Unavailable'} />
              <PairRow label="Protocol" value={pairing?.protocol || 'Unavailable'} />
              <PairRow label="Payload Format" value={pairing?.payloadFormat || 'Unavailable'} />
            </div>
            {pairing?.payload ? (
              <textarea
                readOnly
                value={pairing.payload}
                rows={10}
                className="mt-4 w-full rounded-lg bg-gray-950 border border-gray-800 px-4 py-3 text-xs text-gray-300"
              />
            ) : null}
          </InfoPanel>
        </div>
      ) : null}
    </PageShell>
  );
}

function RoomsPage({ rooms }: { rooms: RoomGroup[] }) {
  return (
    <PageShell title="Rooms" subtitle="Live device groups inferred from your current device inventory">
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {rooms.map((room) => (
          <div key={room.name} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5">
            <div className="flex items-center justify-between gap-3 mb-3">
              <div>
                <div className="text-lg font-semibold text-gray-200">{room.name}</div>
                <div className="text-sm text-gray-500">{room.inferred ? 'inferred from device names' : 'unassigned grouping'}</div>
              </div>
              <div className="px-3 py-1 rounded-full text-xs bg-cyan-500/10 text-cyan-400 border border-cyan-500/30">{room.devices.length} devices</div>
            </div>
            <div className="space-y-2 text-sm">
              <PairRow label="Offline" value={String(room.offlineCount)} />
              <PairRow label="Cameras" value={String(room.cameraCount)} />
              <PairRow label="Controllable" value={String(room.controllableCount)} />
            </div>
          </div>
        ))}
        {rooms.length === 0 ? <EmptyState text="No room groups can be inferred until Smart Home devices are available." /> : null}
      </div>
    </PageShell>
  );
}

function CamerasPage({ snapshot, devices }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[] }) {
  const cameras = devices.filter(looksLikeCamera);

  return (
    <PageShell title="Cameras" subtitle={`${cameras.length} live camera devices detected`}>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
        <SummaryCard label="Cameras" value={String(cameras.length)} />
        <SummaryCard label="Active" value={String(cameras.filter((camera) => camera.isOnline !== false).length)} />
        <SummaryCard label="Provider Errors" value={String((snapshot?.providers ?? []).filter((provider) => provider.error).length)} />
      </div>

      <CameraDeck providers={snapshot?.providers ?? []} />
    </PageShell>
  );
}

function SecurityPage({ snapshot, devices, alerts }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; alerts: SmartHomeSnapshot['alerts'] }) {
  const securityDevices = getSecurityDevices(devices);
  const securityAlerts = alerts.filter((alert) => alert.category.toLowerCase().includes('security') || alert.severity === 'critical' || alert.severity === 'high');

  return (
    <PageShell title="Security" subtitle="Live posture, incidents, and security-capable devices">
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <SummaryCard label="Mode" value={snapshot?.security.mode || 'Unknown'} />
        <SummaryCard label="Threat Level" value={String(snapshot?.security.threatLevel ?? 0)} />
        <SummaryCard label="Critical Alerts" value={String(snapshot?.security.criticalAlertCount ?? 0)} />
        <SummaryCard label="Cameras" value={String(snapshot?.security.activeCameraCount ?? 0)} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <InfoPanel title="Security Devices" accent="text-cyan-400">
          <div className="space-y-3">
            {securityDevices.slice(0, 8).map((device) => (
              <div key={`${device.providerId}:${device.deviceId}`} className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                <div>
                  <div className="text-gray-200">{device.name}</div>
                  <div className="text-xs text-gray-500">{device.providerName}</div>
                </div>
                <div className={`text-xs ${device.isOnline === false ? 'text-yellow-400' : 'text-green-400'}`}>{device.isOnline === false ? 'offline' : 'online'}</div>
              </div>
            ))}
            {securityDevices.length === 0 ? <EmptyState text="No security-capable devices were detected in the current snapshot." /> : null}
          </div>
        </InfoPanel>

        <InfoPanel title="Recent Security Alerts" accent="text-cyan-400">
          <div className="space-y-3">
            {securityAlerts.slice(0, 8).map((alert) => (
              <div key={alert.id} className="p-3 bg-gray-900/50 rounded-lg border border-gray-800/60">
                <div className="flex items-center justify-between gap-3">
                  <div className="font-medium text-gray-200">{alert.title}</div>
                  <SeverityBadge severity={alert.severity} />
                </div>
                <div className="text-sm text-gray-400 mt-2">{alert.detail}</div>
              </div>
            ))}
            {securityAlerts.length === 0 ? <EmptyState text="No security alerts are active right now." /> : null}
          </div>
        </InfoPanel>
      </div>
    </PageShell>
  );
}

function AIScenesPage({ snapshot, devices, recommendations }: { snapshot: SmartHomeSnapshot | null; devices: LiveDevice[]; recommendations: ReturnType<typeof buildRecommendations> }) {
  const sceneSuggestions = buildSceneSuggestions(devices, snapshot);
  const savedCommands = snapshot?.customCommands ?? [];

  const runSceneSuggestion = (scene: ReturnType<typeof buildSceneSuggestions>[number]) => {
    if (scene.action.type === 'navigate') {
      window.location.hash = scene.action.page;
      return;
    }

    if (scene.action.type === 'device-action') {
      executeDeviceAction(scene.action.request);
      window.setTimeout(() => refreshState(), 1200);
      return;
    }

    runSmartHomeCommand(scene.phrase);
  };

  return (
    <PageShell title="AI Scenes" subtitle="Scene-like phrases and recommendations built from your live devices">
      <SceneStudio snapshot={snapshot} />

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4 mb-8">
        {sceneSuggestions.map((scene) => (
          <div key={scene.title} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5">
            <div className="text-lg font-semibold text-gray-200 mb-2">{scene.title}</div>
            <div className="text-sm text-gray-400 mb-4">{scene.detail}</div>
            <button
              type="button"
              onClick={() => runSceneSuggestion(scene)}
              className="px-4 py-2 rounded-md bg-purple-600 hover:bg-purple-700 text-white"
            >
              {scene.action.label}
            </button>
          </div>
        ))}
      </div>

      <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 mb-8">
        <div className="flex items-center justify-between gap-4 mb-4">
          <div>
            <h2 className="text-xl font-semibold text-cyan-400 mb-1">Saved Custom Commands</h2>
            <div className="text-sm text-gray-400">Bring back your phrase library here, not just a single saved-phrase teaser.</div>
          </div>
          <button
            type="button"
            onClick={() => window.location.hash = '#/custom-commands'}
            className="px-4 py-2 rounded-md border border-cyan-500/30 text-cyan-200 hover:border-cyan-400/50"
          >
            Open Custom Commands
          </button>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {savedCommands.slice(0, 6).map((command) => (
            <div key={command.id} className="p-4 bg-gray-900/50 rounded-lg border border-gray-800/60">
              <div className="font-medium text-gray-200 mb-2">"{command.phrase}"</div>
              <div className="text-sm text-gray-400 mb-3">{command.targetLabel || command.deviceId || command.providerId}</div>
              {command.responseText ? <div className="text-xs text-cyan-200/80 mb-3">Response: {command.responseText}</div> : null}
              <button
                type="button"
                onClick={() => runSmartHomeCommand(command.phrase)}
                className="px-3 py-2 rounded-md bg-cyan-600 hover:bg-cyan-700 text-white text-sm"
              >
                Run Command
              </button>
            </div>
          ))}
          {savedCommands.length === 0 ? <EmptyState text="No saved custom commands yet. Create on/off, color, scene, and grouped device phrases in Custom Commands." /> : null}
        </div>
      </div>

      <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
        <h2 className="text-xl font-semibold text-purple-400 mb-4">Live Recommendations</h2>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {recommendations.map((recommendation) => (
            <div key={recommendation.title} className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">{recommendation.title}</div>
              <div className="text-sm text-gray-400">{recommendation.detail}</div>
            </div>
          ))}
          {recommendations.length === 0 ? <EmptyState text="No AI scene recommendations are available yet." /> : null}
        </div>
      </div>
    </PageShell>
  );
}

function AutomationsPage({ snapshot, recommendations }: { snapshot: SmartHomeSnapshot | null; recommendations: ReturnType<typeof buildRecommendations> }) {
  const [draft, setDraft] = useState<SmartHomeAutomationDraft>({ trigger: '', actions: [], schedule: '' });
  const automations = snapshot?.automations ?? [];
  const summary = summarizeAutomations(automations);

  useEffect(() => {
    try {
      const rawDraft = window.sessionStorage.getItem(PENDING_AUTOMATION_DRAFT_KEY);
      if (!rawDraft) {
        return;
      }

      const parsedDraft = JSON.parse(rawDraft) as SmartHomeAutomationDraft;
      if (parsedDraft && (parsedDraft.trigger?.trim() || parsedDraft.actions?.length)) {
        setDraft({
          trigger: parsedDraft.trigger ?? '',
          actions: Array.isArray(parsedDraft.actions) ? parsedDraft.actions : [],
          schedule: parsedDraft.schedule ?? '',
        });
      }

      window.sessionStorage.removeItem(PENDING_AUTOMATION_DRAFT_KEY);
    } catch {
      window.sessionStorage.removeItem(PENDING_AUTOMATION_DRAFT_KEY);
    }
  }, []);

  const saveAutomation = () => {
    const actions = draft.actions.filter((action) => action.trim());
    if (!draft.trigger.trim() || actions.length === 0) {
      return;
    }

    createAutomation({
      trigger: draft.trigger.trim(),
      actions,
      schedule: draft.schedule?.trim() || '',
    });
    setDraft({ trigger: '', actions: [], schedule: '' });
  };

  const suggestionCards = buildAutomationSuggestions(recommendations);

  return (
    <PageShell title="Automations" subtitle={`${automations.length} automations configured`}>
      <div className="flex items-center justify-end mb-8">
        <button type="button" onClick={saveAutomation} className="bg-cyan-600 hover:bg-cyan-700 px-4 py-2 rounded-md text-white inline-flex items-center gap-2">
          <Plus className="w-4 h-4" />
          New Automation
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <SummaryCard label="Enabled" value={String(summary.enabled)} />
        <SummaryCard label="Scheduled" value={String(summary.scheduled)} />
        <SummaryCard label="Triggered" value={String(summary.recentlyTriggered)} />
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-[0.95fr_1.05fr] gap-6 mb-8">
        <InfoPanel title="Create Automation" accent="text-cyan-400">
          <div className="space-y-4">
            <Field label="Trigger">
              <input
                value={draft.trigger}
                onChange={(event) => setDraft((current) => ({ ...current, trigger: event.target.value }))}
                placeholder="At sunset"
                className="w-full rounded-lg bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
              />
            </Field>
            <Field label="Schedule">
              <input
                value={draft.schedule ?? ''}
                onChange={(event) => setDraft((current) => ({ ...current, schedule: event.target.value }))}
                placeholder="Optional schedule or cadence"
                className="w-full rounded-lg bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
              />
            </Field>
            <Field label="Actions (one per line)">
              <textarea
                value={draft.actions.join('\n')}
                onChange={(event) => setDraft((current) => ({ ...current, actions: event.target.value.split(/\r?\n/) }))}
                rows={6}
                placeholder="Turn on porch lights\nStart camera recording"
                className="w-full rounded-lg bg-gray-950 border border-gray-800 px-4 py-3 text-sm text-gray-100"
              />
            </Field>
          </div>
        </InfoPanel>

        <InfoPanel title="Persisted Automations" accent="text-cyan-400">
          <div className="space-y-4 max-h-[520px] overflow-y-auto pr-1">
            {automations.map((automation) => (
              <div key={automation.id} className="bg-gray-900/50 border border-gray-800/60 rounded-xl p-5">
                <div className="flex items-start justify-between gap-4 mb-4">
                  <div>
                    <h3 className="text-xl font-semibold text-gray-200 mb-1">{automation.trigger}</h3>
                    <div className="flex items-center gap-2 text-sm text-gray-400 mb-3">
                      <Clock className="w-4 h-4" />
                      <span>{automation.schedule || 'Runs on direct trigger'}</span>
                    </div>
                    <div className="space-y-1">
                      {automation.actions.map((action, index) => (
                        <div key={`${automation.id}:${index}`} className="text-sm text-gray-400 flex items-center gap-2">
                          <div className="w-1.5 h-1.5 rounded-full bg-cyan-500" />
                          {action}
                        </div>
                      ))}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => toggleAutomation(automation.id)}
                    className={[
                      'px-3 py-1.5 rounded-full text-xs border',
                      automation.isEnabled ? 'bg-green-500/20 text-green-400 border-green-500/30' : 'bg-gray-700/20 text-gray-400 border-gray-700/30',
                    ].join(' ')}
                  >
                    {automation.isEnabled ? 'active' : 'inactive'}
                  </button>
                </div>

                <div className="flex gap-2 pt-4 border-t border-gray-800">
                  <button type="button" onClick={() => runAutomation(automation.id)} className="px-3 py-2 text-sm rounded-md border border-gray-700 hover:border-cyan-500/40 inline-flex items-center gap-2">
                    Run
                  </button>
                  <button type="button" onClick={() => toggleAutomation(automation.id)} className="px-3 py-2 text-sm rounded-md border border-gray-700 hover:border-cyan-500/40">
                    {automation.isEnabled ? 'Disable' : 'Enable'}
                  </button>
                  <button type="button" onClick={() => deleteAutomation(automation.id)} className="px-3 py-2 text-sm rounded-md border border-red-500/40 text-red-400 hover:bg-red-500/10">
                    Delete
                  </button>
                </div>
              </div>
            ))}
            {automations.length === 0 ? <EmptyState text="No automations are stored yet. Use the create panel to add the first one." /> : null}
          </div>
        </InfoPanel>
      </div>

      <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
        <h2 className="text-xl font-semibold text-purple-400 mb-4">Suggested Automations</h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {suggestionCards.map((suggestion) => (
            <div key={suggestion.title} className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">{suggestion.title}</div>
              <div className="text-sm text-gray-400 mb-3">{suggestion.detail}</div>
              <button
                type="button"
                onClick={() => setDraft({ trigger: suggestion.trigger, actions: suggestion.actions, schedule: suggestion.schedule })}
                className="w-full bg-purple-600 hover:bg-purple-700 px-3 py-2 rounded-md text-white text-sm"
              >
                Create
              </button>
            </div>
          ))}
        </div>
      </div>
    </PageShell>
  );
}

function CustomCommandsPage({ snapshot }: { snapshot: SmartHomeSnapshot | null }) {
  return (
    <PageShell title="Custom Commands" subtitle="Saved Smart Home phrases, grouped actions, and agent preferences">
      <CommandStudio snapshot={snapshot} />
      <div className="mt-8 bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
        <h2 className="text-xl font-semibold text-cyan-400 mb-4">Agent Settings</h2>
        <SmartHomeSettingsPanel snapshot={snapshot} />
      </div>
    </PageShell>
  );
}

function AlertsPage({ alerts }: { alerts: SmartHomeSnapshot['alerts'] }) {
  return (
    <PageShell title="Alerts" subtitle={`${alerts.length} Smart Home alerts and provider health events`}>
      <div className="space-y-4">
        {alerts.map((alert) => (
          <div key={alert.id} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5">
            <div className="flex items-start justify-between gap-4 mb-3">
              <div>
                <div className="text-lg font-semibold text-gray-200">{alert.title}</div>
                <div className="text-sm text-gray-500">{alert.source || alert.providerId || 'Smart Home'} · {formatRelativeTime(alert.timestampUtc)}</div>
              </div>
              <SeverityBadge severity={alert.severity} />
            </div>
            <div className="text-sm text-gray-400">{alert.detail}</div>
          </div>
        ))}
        {alerts.length === 0 ? <EmptyState text="No alerts are active in the current snapshot." /> : null}
      </div>
    </PageShell>
  );
}

function ClimateEnergyPage({ devices }: { devices: LiveDevice[] }) {
  const climateDevices = getClimateTelemetry(devices);

  return (
    <PageShell title="Climate & Energy" subtitle="Live climate and energy telemetry when providers expose it">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <SummaryCard label="Climate Devices" value={String(climateDevices.length)} />
        <SummaryCard label="Lighting" value={String(devices.filter(looksLikeLight).length)} />
        <SummaryCard label="Media" value={String(devices.filter(looksLikeMedia).length)} />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {climateDevices.map((device) => (
          <div key={`${device.providerId}:${device.deviceId}`} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5">
            <div className="text-lg font-semibold text-gray-200 mb-1">{device.name}</div>
            <div className="text-sm text-gray-500 mb-3">{device.providerName}</div>
            <div className="space-y-2 text-sm">
              {device.capabilities.slice(0, 4).map((capability) => (
                <PairRow key={`${device.deviceId}:${capability.instance}`} label={capability.instance} value={formatCapabilityState(capability)} />
              ))}
            </div>
          </div>
        ))}
        {climateDevices.length === 0 ? <EmptyState text="No climate or energy telemetry is being published by your current providers." /> : null}
      </div>
    </PageShell>
  );
}

function AccessPage({ devices }: { devices: LiveDevice[] }) {
  const accessDevices = getAccessDevices(devices);

  return (
    <PageShell title="Access" subtitle="Locks, garage, and gate controls when access hardware is connected">
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {accessDevices.map((device) => (
          <div key={`${device.providerId}:${device.deviceId}`} className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5">
            <div className="flex items-center gap-3 mb-3">
              <div className="p-2 rounded-lg bg-cyan-500/10">
                <Lock className="w-5 h-5 text-cyan-400" />
              </div>
              <div>
                <div className="font-semibold text-gray-200">{device.name}</div>
                <div className="text-xs text-gray-500">{device.providerName}</div>
              </div>
            </div>
            <div className="space-y-2 text-sm mb-4">
              <PairRow label="Online" value={device.isOnline === false ? 'No' : 'Yes'} />
              <PairRow label="Capabilities" value={String(device.capabilities.length)} />
              <PairRow label="Primary State" value={describePrimaryState(device)} />
            </div>
            <button
              type="button"
              onClick={() => askAtlas(buildDeviceAskPrompt(device), device.providerId, device.deviceId)}
              className="w-full px-3 py-2 rounded-md border border-gray-700 hover:border-cyan-500/40 text-sm"
            >
              Ask Atlas For Action
            </button>
          </div>
        ))}
        {accessDevices.length === 0 ? <EmptyState text="No access-control devices are connected yet." /> : null}
      </div>
    </PageShell>
  );
}

function AIAssistantPage({
  snapshot,
  bridgeNotice,
  bridgeError,
  bridgeEventId,
}: {
  snapshot: SmartHomeSnapshot | null;
  bridgeNotice: string;
  bridgeError: string;
  bridgeEventId: number;
}) {
  return (
    <PageShell title="AI Assistant" subtitle="Your intelligent smart home companion">
      <AIAssistant snapshot={snapshot} latestNotice={bridgeNotice} latestError={bridgeError} bridgeEventId={bridgeEventId} />
    </PageShell>
  );
}

function PageShell({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">{title}</h1>
          <p className="text-gray-400">{subtitle}</p>
        </div>
        {children}
      </div>
    </div>
  );
}

function InfoPanel({ title, accent, icon, children }: { title: string; accent: string; icon?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className={`text-xl font-semibold ${accent}`}>{title}</h2>
        {icon}
      </div>
      {children}
    </div>
  );
}

function Banner({ tone, text }: { tone: 'error' | 'notice'; text: string }) {
  const classes = tone === 'error'
    ? 'border-red-500/40 bg-red-500/10 text-red-100'
    : 'border-cyan-500/40 bg-cyan-500/10 text-cyan-100';

  return <div className={`mb-4 rounded-lg border px-4 py-3 text-sm ${classes}`}>{text}</div>;
}

function SidebarStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-gray-800/60 bg-gray-900/50 px-3 py-2">
      <div className="text-[10px] uppercase tracking-[0.2em] text-gray-500">{label}</div>
      <div className="mt-1 text-sm font-semibold text-gray-200">{value}</div>
    </div>
  );
}

function SummaryCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
      <div className="text-sm text-gray-500 mb-1">{label}</div>
      <div className="text-3xl font-bold text-gray-100">{value}</div>
    </div>
  );
}

const PENDING_AUTOMATION_DRAFT_KEY = 'atlas.smartHome.pendingAutomationDraft';

function buildDeviceAskPrompt(device: LiveDevice) {
  const capabilities = device.capabilities
    .slice(0, 8)
    .map((capability) => capability.instance || capability.type)
    .filter(Boolean)
    .join(', ');

  return [
    `Help me use the Smart Home device "${device.name}" in Atlas.`,
    `Provider: ${device.providerName}.`,
    capabilities ? `Capabilities: ${capabilities}.` : '',
    'Tell me what Atlas can do with it right now, suggest one useful automation, and give me two natural-language commands I can try.',
  ]
    .filter(Boolean)
    .join(' ');
}

function openAutomationDraftForDevice(device: LiveDevice) {
  const primaryAction = buildDeviceAutomationAction(device);
  const draft: SmartHomeAutomationDraft = {
    trigger: `${device.name} quick action`,
    actions: [primaryAction],
    schedule: '',
  };

  try {
    window.sessionStorage.setItem(PENDING_AUTOMATION_DRAFT_KEY, JSON.stringify(draft));
  } catch {
  }

  window.location.hash = '#/automations';
}

function buildDeviceAutomationAction(device: LiveDevice) {
  const primaryToggle = findPrimaryBooleanCapability(device);
  if (primaryToggle) {
    return `turn ${device.name} on`;
  }

  const brightnessCapability = device.capabilities.find((capability) => capability.instance.toLowerCase().includes('brightness'));
  if (brightnessCapability) {
    return `set ${device.name} brightness to 50`;
  }

  return `show ${device.name}`;
}

function PairRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-gray-500">{label}</span>
      <span className="text-gray-300 text-right max-w-[60%] truncate">{value}</span>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <div className="text-sm text-gray-400 mb-2">{label}</div>
      {children}
    </label>
  );
}

function ModeButton({ active, label, value }: { active?: boolean; label: string; value: string }) {
  return (
    <div className={`w-full px-4 py-3 rounded-lg border ${active ? 'bg-cyan-600/20 border-cyan-500/40' : 'border-gray-700'}`}>
      <div className="text-sm font-medium text-gray-200">{label}</div>
      <div className="text-xs text-gray-400 mt-1">{value}</div>
    </div>
  );
}

function SeverityBadge({ severity, text }: { severity: string; text?: string }) {
  const normalized = severity.toLowerCase();
  const classes = normalized === 'critical' || normalized === 'high'
    ? 'bg-red-500/20 text-red-400 border-red-500/30'
    : normalized === 'medium' || normalized === 'limited'
      ? 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30'
      : normalized === 'info' || normalized === 'available'
        ? 'bg-cyan-500/20 text-cyan-400 border-cyan-500/30'
        : 'bg-gray-700/20 text-gray-300 border-gray-700/30';

  return <div className={`px-3 py-1 rounded-full text-xs border ${classes}`}>{text || severity}</div>;
}

function EmptyState({ text }: { text: string }) {
  return <div className="text-sm text-gray-400 rounded-lg border border-dashed border-gray-800 p-4">{text}</div>;
}

function useHashPage(): [PageId, (page: PageId) => void] {
  const [page, setPage] = useState<PageId>(() => pageFromHash(window.location.hash));

  useEffect(() => {
    const onHashChange = () => setPage(pageFromHash(window.location.hash));
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const navigate = (nextPage: PageId) => {
    const hash = hashForPage(nextPage);
    if (window.location.hash !== hash) {
      window.location.hash = hash;
    } else {
      setPage(nextPage);
    }
  };

  return [page, navigate];
}

function hashForPage(page: PageId) {
  const item = navigation.find((entry) => entry.id === page);
  return `#${item?.path || '/'}`;
}

function pageFromHash(hash: string): PageId {
  const path = hash.startsWith('#') ? hash.slice(1) || '/' : '/';
  const item = navigation.find((entry) => entry.path === path);
  return item?.id || 'overview';
}

function getDeviceIcon(device: LiveDevice) {
  if (looksLikeCamera(device)) return Camera;
  if (getAccessDevices([device]).length > 0) return Lock;
  if (looksLikeClimate(device)) return Thermometer;
  if (looksLikeLight(device)) return Lightbulb;
  if (looksLikeMedia(device)) return Cpu;
  return Wifi;
}

function findPrimaryBooleanCapability(device: LiveDevice) {
  return device.capabilities.find((capability) => capability.dataType.toLowerCase() === 'boolean' && capability.options.length === 0);
}

function describePrimaryState(device: LiveDevice) {
  const capability = device.capabilities.find((entry) => entry.hasState) || device.capabilities[0];
  if (!capability) {
    return device.isOnline === false ? 'Offline' : 'No live state';
  }

  return formatCapabilityState(capability);
}

function formatCapabilityState(capability: SmartHomeCapability) {
  const value = capability.hasState ? capability.stateValue : null;
  if (value === null || value === undefined || value === '') {
    return 'No live value';
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return `${String(value)}${capability.unit ? ` ${capability.unit}` : ''}`;
}

function buildSceneSuggestions(devices: LiveDevice[], snapshot: SmartHomeSnapshot | null) {
  const lights = devices.filter(looksLikeLight).slice(0, 2);
  const media = devices.filter(looksLikeMedia).slice(0, 1);
  const camera = devices.find(looksLikeCamera);
  const firstLightBrightness = lights[0]?.capabilities.find((capability) => capability.instance === 'brightness');
  const firstMediaPower = media[0]?.capabilities.find((capability) => capability.instance === 'powerSwitch');
  const secondLightBrightness = lights[1]?.capabilities.find((capability) => capability.instance === 'brightness');

  const scenes = [
    {
      title: 'Evening Wind Down',
      detail: 'Dim lights and quiet the home for the evening.',
      phrase: lights.length > 0 ? `turn ${lights[0].name} on` : 'run evening lights',
      action: lights[0] && firstLightBrightness
        ? {
            type: 'device-action' as const,
            label: 'Apply To Lights',
            request: {
              providerId: lights[0].providerId,
              deviceId: lights[0].deviceId,
              sku: lights[0].sku,
              capabilityType: firstLightBrightness.type,
              capabilityInstance: firstLightBrightness.instance,
              value: 28,
            },
          }
        : { type: 'phrase' as const, label: 'Run Phrase' },
    },
    {
      title: 'Movie Time',
      detail: 'Prepare media devices and lighting for entertainment.',
      phrase: media[0] ? `turn ${media[0].name} on` : 'run movie mode',
      action: media[0] && firstMediaPower
        ? {
            type: 'device-action' as const,
            label: 'Power Media Device',
            request: {
              providerId: media[0].providerId,
              deviceId: media[0].deviceId,
              sku: media[0].sku,
              capabilityType: firstMediaPower.type,
              capabilityInstance: firstMediaPower.instance,
              value: true,
            },
          }
        : { type: 'phrase' as const, label: 'Run Phrase' },
    },
    {
      title: 'Security Sweep',
      detail: 'Check cameras and raise awareness before bed or travel.',
      phrase: camera ? `show ${camera.name}` : 'run security check',
      action: camera
        ? { type: 'navigate' as const, label: 'Open Cameras', page: '#/cameras' }
        : { type: 'phrase' as const, label: 'Run Phrase' },
    },
    {
      title: 'Focus Lighting',
      detail: 'Raise a main light to a working brightness without blasting the whole room.',
      phrase: lights[0] ? `set ${lights[0].name} brightness to 72` : 'run focus lights',
      action: lights[0] && firstLightBrightness
        ? {
            type: 'device-action' as const,
            label: 'Boost Key Light',
            request: {
              providerId: lights[0].providerId,
              deviceId: lights[0].deviceId,
              sku: lights[0].sku,
              capabilityType: firstLightBrightness.type,
              capabilityInstance: firstLightBrightness.instance,
              value: 72,
            },
          }
        : { type: 'phrase' as const, label: 'Run Phrase' },
    },
    {
      title: 'Soft Companion Glow',
      detail: 'Lower a secondary light to a softer supporting level.',
      phrase: lights[1] ? `set ${lights[1].name} brightness to 38` : 'run companion glow',
      action: lights[1] && secondLightBrightness
        ? {
            type: 'device-action' as const,
            label: 'Set Accent Light',
            request: {
              providerId: lights[1].providerId,
              deviceId: lights[1].deviceId,
              sku: lights[1].sku,
              capabilityType: secondLightBrightness.type,
              capabilityInstance: secondLightBrightness.instance,
              value: 38,
            },
          }
        : { type: 'phrase' as const, label: 'Run Phrase' },
    },
  ];

  for (const command of (snapshot?.customCommands ?? []).slice(0, 2).reverse()) {
    scenes.unshift({
      title: command.targetLabel || 'Custom Command',
      detail: command.responseText || 'Replay one of your saved Smart Home command phrases.',
      phrase: command.phrase,
      action: { type: 'phrase' as const, label: 'Run Custom Command' },
    });
  }

  return scenes.slice(0, 6);
}

function buildAutomationSuggestions(recommendations: ReturnType<typeof buildRecommendations>) {
  const suggestions = [
    {
      title: 'Energy Saver',
      detail: 'Turn off selected devices when the house becomes idle.',
      trigger: 'When the house is idle',
      schedule: '',
      actions: ['Turn off idle lights', 'Reduce media standby usage'],
    },
    {
      title: 'Security Sweep',
      detail: 'Run a nightly security sweep and surface recent alerts.',
      trigger: '10:30 PM daily',
      schedule: 'Every day at 10:30 PM',
      actions: ['Review cameras', 'Lock access devices', 'Summarize security alerts'],
    },
    {
      title: 'Climate Adjust',
      detail: 'Prepare climate devices for the evening or for away mode.',
      trigger: 'At sunset',
      schedule: 'Daily at sunset',
      actions: ['Adjust thermostat', 'Check humidity', 'Confirm windows and access devices'],
    },
  ];

  if (recommendations[0]) {
    suggestions[0] = {
      title: recommendations[0].title,
      detail: recommendations[0].detail,
      trigger: 'When Atlas recommendation applies',
      schedule: '',
      actions: ['Review recommendation', 'Apply a matching Smart Home action'],
    };
  }

  return suggestions;
}