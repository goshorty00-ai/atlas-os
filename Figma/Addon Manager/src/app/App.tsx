import { useEffect, useMemo, useState } from 'react';

type AddonItem = {
  name: string;
  version: string;
  description: string;
  typeSummary: string;
  resourceSummary: string;
  url: string;
  manifestUrl: string;
  iconUrl: string;
  host: string;
  directoryUrl: string;
  installManifestUrl: string;
  websiteUrl?: string;
  configureUrl?: string;
  isInstalled: boolean;
  hasError: boolean;
  errorText: string;
};

type AddonsState = {
  type: 'addons.state';
  installedAddons: AddonItem[];
  directoryAddons: AddonItem[];
  installedCount: number;
  statusText: string;
  networkSummary: string;
  wifiNetworksText: string;
  isBusy: boolean;
  lastAction?: string;
};

function hasBridge(): boolean {
  return typeof window !== 'undefined' && !!(window as any).chrome?.webview?.postMessage;
}

function post(message: unknown) {
  try {
    (window as any).chrome?.webview?.postMessage(message);
  } catch {
  }
}

function logAddonNavTest(message: string) {
  try {
    console.log(message);
  } catch {
  }

  try {
    (window as any).chrome?.webview?.postMessage({
      type: 'servers.clientError',
      payload: {
        message,
        source: 'Figma/Addon Manager/src/app/App.tsx',
      },
    });
  } catch {
  }
}

const emptyState: AddonsState = {
  type: 'addons.state',
  installedAddons: [],
  directoryAddons: [],
  installedCount: 0,
  statusText: 'Loading addons...',
  networkSummary: 'Refreshing network status...',
  wifiNetworksText: 'Run a Wi-Fi scan to list nearby networks.',
  isBusy: true,
};

export default function App() {
  const [state, setState] = useState<AddonsState>(emptyState);
  const [manifestUrl, setManifestUrl] = useState('');
  const [search, setSearch] = useState('');
  const [installingUrl, setInstallingUrl] = useState('');

  const isDirectorySource = (addon: AddonItem): boolean => {
    const fields = [addon.url, addon.manifestUrl]
      .map((value) => (value || '').trim().toLowerCase())
      .filter(Boolean);
    return fields.some((value) => value.includes('stremio-addons.net'));
  };

  useEffect(() => {
    const onMessage = (event: any) => {
      const data = event?.data as AddonsState | undefined;
      if (!data || typeof data !== 'object') return;
      if (data.type !== 'addons.state') return;
      logAddonNavTest(`[AddonNavTest] addon-frontend.state.received installed=${data.installedCount ?? 0}`);
      setState(data);
      setInstallingUrl('');
    };

    const onError = (event: ErrorEvent) => {
      logAddonNavTest(`[AddonNavTest] addon-frontend.error=${event.message || 'unknown'}`);
    };

    const onRejection = (event: PromiseRejectionEvent) => {
      const reason = event.reason instanceof Error ? event.reason.message : String(event.reason ?? 'unknown');
      logAddonNavTest(`[AddonNavTest] addon-frontend.error=${reason}`);
    };

    try {
      (window as any).chrome?.webview?.addEventListener('message', onMessage);
    } catch {
    }
    window.addEventListener('error', onError);
    window.addEventListener('unhandledrejection', onRejection);
    post({ type: 'addons.ready' });
    post({ type: 'addons.getState' });
    return () => {
      try {
        (window as any).chrome?.webview?.removeEventListener('message', onMessage);
      } catch {
      }
      window.removeEventListener('error', onError);
      window.removeEventListener('unhandledrejection', onRejection);
    };
  }, []);

  useEffect(() => {
    logAddonNavTest(`[AddonNavTest] addon-frontend.render installed=${state.installedCount ?? 0}`);
  }, [state.installedCount, state.directoryAddons.length, state.installedAddons.length]);

  const filteredDirectory = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return state.directoryAddons;
    return state.directoryAddons.filter((addon) =>
      [addon.name, addon.description, addon.host, addon.typeSummary]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  }, [search, state.directoryAddons]);

  const filteredInstalled = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return state.installedAddons;
    return state.installedAddons.filter((addon) =>
      [addon.name, addon.description, addon.host, addon.typeSummary]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  }, [search, state.installedAddons]);

  const isDirectoryAvailable = useMemo(() => {
    return state.installedAddons.some(isDirectorySource);
  }, [state.installedAddons]);

  const submitManifest = () => {
    const value = manifestUrl.trim();
    if (!value) return;
    setInstallingUrl(value);
    post({ type: 'addons.addManifest', payload: { url: value } });
  };

  return (
    <div className="h-full w-full overflow-x-hidden bg-[radial-gradient(circle_at_top,_rgba(22,78,99,0.18),_transparent_32%),linear-gradient(180deg,#050816_0%,#081224_46%,#050816_100%)] text-white">
      <div className="w-full max-w-full px-3 py-3 sm:px-4 sm:py-4 lg:px-6 lg:py-5">
        <div className="mb-5 space-y-4 lg:mb-6">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <button
              type="button"
              onClick={() => post({ type: 'addons.close' })}
              className="rounded-2xl border border-white/15 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:bg-white/10"
            >
              Back to MediaHub
            </button>
            <div className="rounded-full border border-emerald-400/20 bg-emerald-400/10 px-4 py-2 text-sm text-emerald-200 whitespace-nowrap">
              {state.installedCount} addons connected
            </div>
          </div>

          <div>
            <div className="text-xs uppercase tracking-[0.4em] text-cyan-200/70">Atlas Addons</div>
            <h1 className="mt-2 text-2xl font-semibold text-white sm:text-3xl lg:text-4xl">Stremio Addon Manager</h1>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-slate-300 lg:leading-7">
              Connect multiple Stremio-compatible addons, browse the full STREMIO-ADDONS.NET directory, and manage the live addon list Atlas is using for streams and metadata.
            </p>
          </div>

          <div className="w-full rounded-[22px] border border-cyan-500/20 bg-slate-950/50 p-4 sm:p-5 backdrop-blur-xl">
            <div className="grid grid-cols-1 gap-3 lg:grid-cols-[minmax(0,1fr)_auto_auto]">
              <input
                type="text"
                value={manifestUrl}
                onChange={(event) => setManifestUrl(event.target.value)}
                placeholder="Paste a Stremio addon base URL or manifest URL"
                className="min-w-0 w-full rounded-2xl border border-white/10 bg-slate-900/80 px-4 py-3.5 text-base text-white outline-none placeholder:text-slate-500"
              />
              <button
                type="button"
                onClick={submitManifest}
                className="shrink-0 whitespace-nowrap rounded-2xl bg-cyan-400 px-5 py-3.5 text-sm font-semibold text-slate-950 transition hover:bg-cyan-300 disabled:opacity-50"
                disabled={state.isBusy || !manifestUrl.trim()}
              >
                {installingUrl && installingUrl === manifestUrl.trim() ? 'Adding...' : 'Add Addon'}
              </button>
              <button
                type="button"
                onClick={() => post({ type: 'addons.refresh' })}
                className="shrink-0 whitespace-nowrap rounded-2xl border border-white/10 bg-white/5 px-5 py-3.5 text-sm font-semibold text-white transition hover:bg-white/10"
              >
                Refresh
              </button>
            </div>
            <div className="mt-3 text-sm text-slate-300">{state.statusText}</div>
            {state.lastAction ? <div className="mt-1 text-xs text-cyan-200/80">{state.lastAction}</div> : null}
          </div>
        </div>

        <div className="mb-5 grid gap-4 lg:mb-6">
          <div className="rounded-[22px] border border-white/10 bg-slate-950/45 p-4 sm:p-5 lg:p-6 backdrop-blur-xl">
            <div className="flex items-center justify-between gap-4">
              <h2 className="text-xl font-semibold">Installed Addons</h2>
              <div className="rounded-full border border-emerald-400/20 bg-emerald-400/10 px-4 py-2 text-sm text-emerald-200">
                {state.installedCount} connected
              </div>
            </div>
            <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
              {filteredInstalled.map((addon) => (
                <AddonCard key={`installed-${addon.url}`} addon={addon} actionLabel="Remove" actionTone="danger" onAction={() => post({ type: 'addons.remove', payload: { url: addon.url } })} busy={state.isBusy} showConfigure />
              ))}
              {filteredInstalled.length === 0 ? <EmptyCard text="No installed addons match the current search." /> : null}
            </div>
          </div>

          <div className="rounded-[22px] border border-white/10 bg-slate-950/45 p-4 sm:p-5 lg:p-6 backdrop-blur-xl">
            <div className="flex items-center justify-between gap-4">
              <h2 className="text-xl font-semibold">Network and Discovery</h2>
              <button
                type="button"
                onClick={() => post({ type: 'addons.scanWifi' })}
                className="rounded-2xl border border-white/10 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:bg-white/10"
              >
                Scan Wi-Fi
              </button>
            </div>
            <div className="mt-4 rounded-2xl border border-white/10 bg-slate-900/70 p-4 text-sm text-slate-300">{state.networkSummary}</div>
            <div className="mt-4 max-h-[240px] overflow-auto rounded-2xl border border-white/10 bg-slate-900/70 p-4 font-mono text-xs leading-6 text-slate-300 whitespace-pre-wrap">{state.wifiNetworksText}</div>
          </div>
        </div>

        {isDirectoryAvailable ? (
          <div className="rounded-[22px] border border-white/10 bg-slate-950/45 p-4 sm:p-5 lg:p-6 backdrop-blur-xl">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div>
                  <h2 className="text-xl font-semibold">STREMIO-ADDONS.NET Directory</h2>
                  <p className="mt-1 text-sm text-slate-400">Browse the live directory and install multiple addons directly into Atlas.</p>
                </div>
                <input
                  type="text"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search addons by name, host, or type"
                  className="w-full rounded-2xl border border-white/10 bg-slate-900/80 px-4 py-3 text-sm text-white outline-none placeholder:text-slate-500 sm:max-w-[360px]"
                />
              </div>
              <div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                {filteredDirectory.map((addon) => (
                  <AddonCard
                    key={`directory-${addon.url}`}
                    addon={addon}
                    actionLabel={addon.isInstalled ? 'Installed' : 'Install'}
                    actionTone={addon.isInstalled ? 'muted' : 'primary'}
                    onAction={() => !addon.isInstalled && post({ type: 'addons.install', payload: { url: addon.url, directoryUrl: addon.directoryUrl, manifestUrl: addon.installManifestUrl || addon.manifestUrl } })}
                    busy={state.isBusy}
                    disabled={addon.isInstalled}
                    showWebsite
                  />
                ))}
              </div>
          </div>
        ) : null}
        </div>
      </div>
  );
}

function EmptyCard(props: { text: string }) {
  return <div className="rounded-[24px] border border-dashed border-white/10 bg-white/[0.03] p-5 text-sm text-slate-400">{props.text}</div>;
}

function AddonCard(props: { addon: AddonItem; actionLabel: string; actionTone: 'primary' | 'danger' | 'muted'; onAction: () => void; busy: boolean; disabled?: boolean; showConfigure?: boolean; showWebsite?: boolean; }) {
  const { addon, actionLabel, actionTone, onAction, busy, disabled, showConfigure, showWebsite } = props;
  const buttonClassName = actionTone === 'primary'
    ? 'bg-cyan-400 text-slate-950 hover:bg-cyan-300'
    : actionTone === 'danger'
      ? 'bg-rose-500/90 text-white hover:bg-rose-400'
      : 'bg-white/10 text-slate-300';

  return (
    <div className="rounded-[24px] border border-white/10 bg-[linear-gradient(180deg,rgba(15,23,42,0.9),rgba(2,6,23,0.92))] p-5 shadow-[0_24px_80px_rgba(0,0,0,0.28)]">
          <div className="flex items-start gap-4">
        {addon.iconUrl ? <img src={addon.iconUrl} alt="" className="h-14 w-14 rounded-2xl border border-white/10 object-cover" /> : <div className="flex h-14 w-14 items-center justify-center rounded-2xl border border-white/10 bg-cyan-400/10 text-xl font-semibold text-cyan-200">{addon.name?.slice(0, 1) || '+'}</div>}
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h3 className="text-base font-semibold text-white">{addon.name}</h3>
              <div className="mt-1 truncate text-xs text-slate-400">{addon.version} • {addon.host || addon.url}</div>
            </div>
            <button
              type="button"
              className={`shrink-0 whitespace-nowrap rounded-2xl px-4 py-2 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-50 ${buttonClassName}`}
              onClick={onAction}
              disabled={busy || disabled}
            >
              {actionLabel}
            </button>
          </div>
          <p className="mt-3 text-sm leading-6 text-slate-300">{addon.description}</p>
          <div className="mt-4 flex flex-wrap gap-2 text-xs">
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 text-slate-200">{addon.typeSummary || 'Other'}</span>
            <span className="rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 text-slate-200">{addon.resourceSummary || 'Manifest only'}</span>
            {addon.hasError ? <span className="rounded-full border border-rose-400/20 bg-rose-400/10 px-3 py-1 text-rose-200">{addon.errorText || 'Manifest error'}</span> : null}
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            {showConfigure ? (
              <button
                type="button"
                className="rounded-2xl border border-cyan-300/20 bg-cyan-300/10 px-4 py-2 text-sm font-semibold text-cyan-100 transition hover:bg-cyan-300/16"
                onClick={() => post({ type: 'addons.openUrl', payload: { url: addon.configureUrl || addon.url } })}
              >
                Configure
              </button>
            ) : null}
            {showWebsite ? (
              <button
                type="button"
                className="rounded-2xl border border-white/10 bg-white/[0.05] px-4 py-2 text-sm font-semibold text-slate-200 transition hover:bg-white/[0.09]"
                onClick={() => post({ type: 'addons.openUrl', payload: { url: addon.websiteUrl || addon.url } })}
              >
                Website
              </button>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  );
}
