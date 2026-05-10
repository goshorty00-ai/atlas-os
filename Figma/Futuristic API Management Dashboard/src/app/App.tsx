import { useEffect, useMemo, useState } from 'react';
import { Sidebar } from './components/Sidebar';
import { Header } from './components/Header';
import { APICards } from './components/APICards';
import { ServerCards } from './components/ServerCards';
import { NetworkVisualization } from './components/NetworkVisualization';
import { AddIntegrationPanel } from './components/AddIntegrationPanel';
import { IntegrationsCatalog } from './components/IntegrationsCatalog';
import { LogsPanel } from './components/LogsPanel';
import { onHostMessage, postToHost } from './atlasBridge';

type NavSection = 'apis' | 'integrations' | 'servers' | 'network' | 'storage' | 'credentials' | 'monitoring' | 'logs';

type ApiIntegration = {
  id: string;
  name: string;
  status: 'online' | 'warning' | 'offline' | 'unknown';
  configured?: boolean;
  latencyMs?: number;
  requests?: number;
  uptime?: number;
};

type ApiState = {
  integrations: ApiIntegration[];
  lastUpdatedUtc?: string;
};

export default function App() {
  const [activeSection, setActiveSection] = useState<NavSection>('apis');
  const [apiState, setApiState] = useState<ApiState>({ integrations: [] });
  const [connected, setConnected] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [catalogFocusId, setCatalogFocusId] = useState<string | null>(null);

  useEffect(() => {
    const off = onHostMessage((msg) => {
      setConnected(true);
      if (msg.type === 'api.state') {
        const payload = msg.payload as any;
        if (payload && Array.isArray(payload.integrations)) {
          setApiState({
            integrations: payload.integrations,
            lastUpdatedUtc: payload.lastUpdatedUtc
          });
        }
      }
      // Receive a mic transcript fill from the host — fills search only; never auto-executes.
      if (msg.type === 'api.mic.fillSearch') {
        const transcript = ((msg.payload as any)?.transcript ?? '').trim();
        if (transcript) setSearchQuery(transcript);
        return;
      }
      if (msg.type === 'api.testResult') {
        return;
      }
      if (msg.type === 'api.toast') {
        return;
      }
    });

    postToHost('api.getState');
    return off;
  }, []);

  const activeSectionLabel = useMemo(() => {
    const map: Record<NavSection, string> = {
      apis: "API's",
      integrations: 'Integrations',
      servers: 'Servers',
      network: 'Network',
      storage: 'Storage',
      credentials: 'Credentials',
      monitoring: 'Monitoring',
      logs: 'Logs',
    };
    return map[activeSection] ?? activeSection;
  }, [activeSection]);

  const filteredIntegrations = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return apiState.integrations;
    return (apiState.integrations ?? []).filter((x) => {
      const name = (x?.name ?? '').toLowerCase();
      const id = (x?.id ?? '').toLowerCase();
      return name.includes(q) || id.includes(q);
    });
  }, [apiState.integrations, searchQuery]);

  return (
    <div className="min-h-screen bg-[#0a0a0f] text-gray-100 overflow-hidden">
      {/* Animated background grid */}
      <div className="fixed inset-0 z-0 opacity-20">
        <div className="absolute inset-0" 
          style={{
            backgroundImage: `
              linear-gradient(to right, rgba(59, 130, 246, 0.1) 1px, transparent 1px),
              linear-gradient(to bottom, rgba(59, 130, 246, 0.1) 1px, transparent 1px)
            `,
            backgroundSize: '50px 50px'
          }}
        />
      </div>

      {/* Radial gradient accent */}
      <div className="fixed top-0 right-0 w-[800px] h-[800px] bg-gradient-to-br from-blue-500/10 via-violet-500/10 to-transparent rounded-full blur-3xl pointer-events-none" />
      <div className="fixed bottom-0 left-0 w-[600px] h-[600px] bg-gradient-to-tr from-violet-500/10 via-blue-500/10 to-transparent rounded-full blur-3xl pointer-events-none" />

      <div className="relative z-10 flex h-screen">
        <Sidebar activeSection={activeSection} onSectionChange={setActiveSection} />
        
        <div className="flex-1 flex flex-col overflow-hidden">
          <Header
            activeSection={activeSectionLabel}
            searchQuery={searchQuery}
            onSearchQueryChange={setSearchQuery}
            onOpenIntegrations={() => {
              setActiveSection('integrations');
              setCatalogFocusId(null);
            }}
            onOpenLogs={() => setActiveSection('logs')}
          />
          
          <main className="flex-1 overflow-y-auto px-8 py-6 space-y-6">
            {!connected && (
              <div className="rounded-xl border border-yellow-500/20 bg-yellow-500/5 px-4 py-3 text-sm text-yellow-200">
                Dashboard not connected to host yet.
              </div>
            )}
            {activeSection === 'apis' && (
              <>
                <APICards
                  integrations={filteredIntegrations}
                  onConfigure={(id) => {
                    setActiveSection('integrations');
                    setCatalogFocusId(id);
                  }}
                />
                <IntegrationsCatalog
                  integrations={filteredIntegrations}
                  focusId={catalogFocusId}
                  onFocusConsumed={() => setCatalogFocusId(null)}
                />
              </>
            )}
            
            {activeSection === 'integrations' && (
              <>
                <IntegrationsCatalog
                  integrations={filteredIntegrations}
                  focusId={catalogFocusId}
                  onFocusConsumed={() => setCatalogFocusId(null)}
                />
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                  <AddIntegrationPanel />
                  <NetworkVisualization />
                </div>
              </>
            )}

            {activeSection === 'servers' && <ServerCards />}

            {activeSection === 'network' && (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <NetworkVisualization />
                <AddIntegrationPanel />
              </div>
            )}

            {activeSection === 'credentials' && (
              <IntegrationsCatalog
                integrations={filteredIntegrations}
                focusId={catalogFocusId}
                onFocusConsumed={() => setCatalogFocusId(null)}
              />
            )}

            {activeSection === 'monitoring' && (
              <APICards
                integrations={filteredIntegrations}
                onConfigure={(id) => {
                  setActiveSection('integrations');
                  setCatalogFocusId(id);
                }}
              />
            )}

            {activeSection === 'logs' && <LogsPanel />}

            {activeSection === 'storage' && (
              <div className="flex items-center justify-center h-full">
                <div className="text-center space-y-4">
                  <div className="w-24 h-24 mx-auto rounded-full bg-gradient-to-br from-blue-500/20 to-violet-500/20 border border-blue-500/30 flex items-center justify-center">
                    <div className="w-12 h-12 rounded-full bg-gradient-to-br from-blue-400 to-violet-400 animate-pulse" />
                  </div>
                  <h3 className="text-2xl font-light text-gray-300">Storage Module</h3>
                  <p className="text-gray-500">Use Integrations to configure providers</p>
                </div>
              </div>
            )}
          </main>
        </div>
      </div>
    </div>
  );
}
