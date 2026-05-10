import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "motion/react";
import { X, Shield, Zap, Settings as SettingsIcon, CheckCircle2, AlertCircle, Cloud, Download } from "lucide-react";

interface DownloadManagerSettingsProps {
  isOpen: boolean;
  onClose: () => void;
}

interface ProviderConfig {
  enabled: boolean;
  token: string;
}

export function DownloadManagerSettings({ isOpen, onClose }: DownloadManagerSettingsProps) {
  const [providers, setProviders] = useState<{
    realDebrid: ProviderConfig;
    allDebrid: ProviderConfig;
    premiumize: ProviderConfig;
    debridLink: ProviderConfig;
  }>({
    realDebrid: { enabled: true, token: "" },
    allDebrid: { enabled: false, token: "" },
    premiumize: { enabled: false, token: "" },
    debridLink: { enabled: false, token: "" },
  });
  
  const [maxParallel, setMaxParallel] = useState(3);
  const [testStatus, setTestStatus] = useState<{ provider: string; ok: boolean; message: string } | null>(null);
  const [isTesting, setIsTesting] = useState(false);

  const hasBridge = () => {
    try {
      return typeof window !== "undefined" && (window as any).chrome && (window as any).chrome.webview;
    } catch {
      return false;
    }
  };

  const post = (type: string, payload: any = {}) => {
    const msg = { type, payload };
    try {
      if (hasBridge()) (window as any).chrome.webview.postMessage(msg);
    } catch {}
  };

  useEffect(() => {
    if (!hasBridge() || !isOpen) return;

    const handler = (event: any) => {
      const msg = event?.data;
      if (!msg || typeof msg.type !== "string") return;
      
      if (msg.type === "downloader.state") {
        const settings = msg.payload?.settings;
        if (settings) {
          setMaxParallel(settings.maxParallelDownloads || 3);
          setProviders({
            realDebrid: {
              enabled: settings.providers?.realDebrid?.enabled !== false,
              token: "",
            },
            allDebrid: {
              enabled: settings.providers?.allDebrid?.enabled === true,
              token: "",
            },
            premiumize: {
              enabled: settings.providers?.premiumize?.enabled === true,
              token: "",
            },
            debridLink: {
              enabled: settings.providers?.debridLink?.enabled === true,
              token: "",
            },
          });
        }
      }
      
      if (msg.type === "downloader.providerStatus") {
        setIsTesting(false);
        setTestStatus({ 
          provider: msg.payload.provider,
          ok: msg.payload.ok, 
          message: msg.payload.message 
        });
      }
    };

    (window as any).chrome.webview.addEventListener("message", handler);
    post("downloader.getState", {});

    return () => {
      try {
        (window as any).chrome.webview.removeEventListener("message", handler);
      } catch {}
    };
  }, [isOpen]);

  const handleSave = () => {
    const payload: any = {
      maxParallelDownloads: maxParallel,
      providers: {
        realDebrid: {
          enabled: providers.realDebrid.enabled,
        },
        allDebrid: {
          enabled: providers.allDebrid.enabled,
        },
        premiumize: {
          enabled: providers.premiumize.enabled,
        },
        debridLink: {
          enabled: providers.debridLink.enabled,
        },
      },
    };

    // Add tokens if provided
    if (providers.realDebrid.token.trim()) {
      payload.providers.realDebrid.token = providers.realDebrid.token.trim();
    }
    if (providers.allDebrid.token.trim()) {
      payload.providers.allDebrid.token = providers.allDebrid.token.trim();
    }
    if (providers.premiumize.token.trim()) {
      payload.providers.premiumize.token = providers.premiumize.token.trim();
    }
    if (providers.debridLink.token.trim()) {
      payload.providers.debridLink.token = providers.debridLink.token.trim();
    }

    post("downloader.applySettings", payload);
    
    // Clear token inputs after saving
    setProviders(prev => ({
      realDebrid: { ...prev.realDebrid, token: "" },
      allDebrid: { ...prev.allDebrid, token: "" },
      premiumize: { ...prev.premiumize, token: "" },
      debridLink: { ...prev.debridLink, token: "" },
    }));
    
    onClose();
  };

  const handleTest = (provider: string) => {
    setIsTesting(true);
    setTestStatus(null);
    post("downloader.provider.test", { provider });
  };

  const renderProvider = (key: keyof typeof providers, name: string, tokenUrl: string, config: ProviderConfig) => (
    <div key={key} className="p-4 rounded-lg bg-slate-900/30 border border-slate-800 space-y-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Shield className="w-4 h-4 text-cyan-400" />
          <h5 className="text-sm font-mono font-bold text-slate-200">{name}</h5>
        </div>
        <input
          type="checkbox"
          checked={config.enabled}
          onChange={(e) => setProviders(prev => ({
            ...prev,
            [key]: { ...prev[key], enabled: e.target.checked }
          }))}
          className="w-4 h-4 rounded border-slate-700 bg-slate-900 text-cyan-500 focus:ring-cyan-500/50"
        />
      </div>

      {config.enabled && (
        <motion.div
          initial={{ opacity: 0, height: 0 }}
          animate={{ opacity: 1, height: "auto" }}
          exit={{ opacity: 0, height: 0 }}
          className="space-y-2"
        >
          <label className="text-xs font-mono text-slate-400">
            API Token
            <a
              href={tokenUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="ml-2 text-cyan-400 hover:text-cyan-300 underline"
            >
              (Get token)
            </a>
          </label>
          <input
            type="password"
            value={config.token}
            onChange={(e) => setProviders(prev => ({
              ...prev,
              [key]: { ...prev[key], token: e.target.value }
            }))}
            placeholder={`Enter your ${name} API token...`}
            className="w-full px-4 py-2 rounded-lg bg-slate-900/50 border border-slate-700 text-slate-200 font-mono text-sm focus:outline-none focus:border-cyan-500/50 placeholder:text-slate-600"
          />
          
          <div className="flex items-center gap-3">
            <button
              onClick={() => handleTest(name.replace("-", ""))}
              disabled={isTesting}
              className="px-3 py-1.5 rounded-lg bg-purple-500/20 border border-purple-500/50 text-purple-400 hover:bg-purple-500/30 disabled:opacity-50 disabled:cursor-not-allowed text-xs font-mono uppercase transition-all"
            >
              {isTesting ? "Testing..." : "Test"}
            </button>
            
            {testStatus && testStatus.provider === name.replace("-", "") && (
              <motion.div
                initial={{ opacity: 0, x: -10 }}
                animate={{ opacity: 1, x: 0 }}
                className="flex items-center gap-2"
              >
                {testStatus.ok ? (
                  <>
                    <CheckCircle2 className="w-3 h-3 text-green-400" />
                    <span className="text-xs font-mono text-green-400">{testStatus.message}</span>
                  </>
                ) : (
                  <>
                    <AlertCircle className="w-3 h-3 text-red-400" />
                    <span className="text-xs font-mono text-red-400">{testStatus.message}</span>
                  </>
                )}
              </motion.div>
            )}
          </div>
          
          <p className="text-xs text-slate-500 font-mono">
            Token is encrypted and stored securely. Leave empty to keep existing token.
          </p>
        </motion.div>
      )}
    </div>
  );

  if (!isOpen) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60 backdrop-blur-sm"
        onClick={onClose}
      >
        <motion.div
          initial={{ opacity: 0, scale: 0.95, y: 20 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.95, y: 20 }}
          onClick={(e) => e.stopPropagation()}
          className="w-full max-w-2xl mx-4 rounded-xl border border-cyan-500/30 bg-slate-950/95 backdrop-blur-md shadow-[0_0_30px_rgba(34,211,238,0.2)] overflow-hidden"
        >
          {/* Header */}
          <div className="px-6 py-4 border-b border-cyan-500/20 bg-gradient-to-r from-cyan-500/10 to-purple-500/10">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <div className="p-2 rounded-lg bg-cyan-500/20 border border-cyan-500/30">
                  <SettingsIcon className="w-5 h-5 text-cyan-400" />
                </div>
                <div>
                  <h3 className="text-lg font-mono font-bold text-cyan-400">Download Manager Settings</h3>
                  <p className="text-xs text-slate-400 font-mono">Configure providers and performance</p>
                </div>
              </div>
              <button
                onClick={onClose}
                className="p-2 rounded-lg hover:bg-slate-800/50 text-slate-400 hover:text-cyan-400 transition-colors"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
          </div>

          {/* Content */}
          <div className="p-6 space-y-6 max-h-[70vh] overflow-y-auto atlas-scrollbar">
            {/* Performance Settings */}
            <div className="space-y-3">
              <div className="flex items-center gap-2">
                <Zap className="w-4 h-4 text-orange-400" />
                <h4 className="text-sm font-mono font-bold text-slate-200 uppercase">Performance</h4>
              </div>
              <div className="space-y-2">
                <label className="text-xs font-mono text-slate-400">Max Parallel Downloads</label>
                <input
                  type="number"
                  min="1"
                  max="12"
                  value={maxParallel}
                  onChange={(e) => setMaxParallel(Math.max(1, Math.min(12, parseInt(e.target.value) || 3)))}
                  className="w-full px-4 py-2 rounded-lg bg-slate-900/50 border border-slate-700 text-slate-200 font-mono text-sm focus:outline-none focus:border-cyan-500/50"
                />
              </div>
            </div>

            {/* Premium Hosters */}
            <div className="space-y-4">
              <div className="flex items-center gap-2">
                <Cloud className="w-4 h-4 text-cyan-400" />
                <h4 className="text-sm font-mono font-bold text-slate-200 uppercase">Premium Hosters</h4>
              </div>

              {/* Real-Debrid */}
              {renderProvider("realDebrid", "Real-Debrid", "https://real-debrid.com/apitoken", providers.realDebrid)}
              
              {/* AllDebrid */}
              {renderProvider("allDebrid", "AllDebrid", "https://alldebrid.com/apikeys", providers.allDebrid)}
              
              {/* Premiumize */}
              {renderProvider("premiumize", "Premiumize", "https://www.premiumize.me/account", providers.premiumize)}
              
              {/* Debrid-Link */}
              {renderProvider("debridLink", "Debrid-Link", "https://debrid-link.com/webapp/apikey", providers.debridLink)}
            </div>
          </div>

          {/* Footer */}
          <div className="px-6 py-4 border-t border-cyan-500/20 bg-slate-900/50 flex items-center justify-end gap-3">
            <button
              onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-700 text-slate-400 hover:bg-slate-800/50 text-sm font-mono uppercase transition-all"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              className="px-4 py-2 rounded-lg bg-cyan-500/20 border border-cyan-500/50 text-cyan-400 hover:bg-cyan-500/30 shadow-[0_0_15px_rgba(34,211,238,0.3)] text-sm font-mono uppercase transition-all"
            >
              Save Settings
            </button>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
