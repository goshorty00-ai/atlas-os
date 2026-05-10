import { useState } from "react";
import {
  Search,
  Plus,
  CheckCircle,
  Wifi,
  Download,
  Settings,
  RefreshCw,
  Power,
  TrendingUp,
  Activity,
  Network,
  Copy,
  X,
} from "lucide-react";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from "recharts";

interface Addon {
  id: string;
  name: string;
  version: string;
  source: string;
  enabled: boolean;
  description?: string;
  category?: string;
}

interface Manifest {
  name: string;
  url: string;
  version: string;
}

interface NetworkProvider {
  name: string;
  source: string;
  latency: number;
}

interface ProviderRanking {
  rank: number;
  name: string;
  source: string;
  reliability: number;
  latency: number;
  rating: number;
}

interface ManifestDetails {
  url: string;
  metadata: Record<string, string>;
  capabilities: string[];
  version: string;
  dependencies: string[];
}

export function AddonManagerPanel() {
  const [manifestUrl, setManifestUrl] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedManifest, setSelectedManifest] =
    useState<ManifestDetails | null>(null);

  // Mock data
  const [installedAddons] = useState<Addon[]>([
    {
      id: "1",
      name: "StreamProvider Pro",
      version: "2.1.4",
      source: "Registry",
      enabled: true,
    },
    {
      id: "2",
      name: "MediaHub Connect",
      version: "1.8.0",
      source: "Network",
      enabled: true,
    },
    {
      id: "3",
      name: "Content Scraper",
      version: "3.0.1",
      source: "Manifest",
      enabled: false,
    },
    {
      id: "4",
      name: "AI Metadata Engine",
      version: "1.5.2",
      source: "Registry",
      enabled: true,
    },
  ]);

  const [manifests] = useState<Manifest[]>([
    {
      name: "CustomStream",
      url: "https://provider.io/manifest.json",
      version: "1.2.0",
    },
    {
      name: "MediaSource",
      url: "https://media.xyz/addon.json",
      version: "2.0.3",
    },
  ]);

  const [networkProviders] = useState<NetworkProvider[]>([
    {
      name: "LocalMediaServer",
      source: "Local Network",
      latency: 12,
    },
    {
      name: "PrivateNode-01",
      source: "Private Nodes",
      latency: 45,
    },
    {
      name: "CloudProvider",
      source: "Remote Hosts",
      latency: 89,
    },
  ]);

  const [registryAddons] = useState<Addon[]>([
    {
      id: "r1",
      name: "VideoStream Plus",
      version: "3.1.0",
      source: "Registry",
      enabled: false,
      description: "Advanced video streaming addon",
      category: "Streaming",
    },
    {
      id: "r2",
      name: "Subtitle Master",
      version: "2.0.5",
      source: "Registry",
      enabled: false,
      description: "Multi-language subtitle support",
      category: "Utilities",
    },
    {
      id: "r3",
      name: "MetaFetch AI",
      version: "1.9.2",
      source: "Registry",
      enabled: false,
      description: "AI-powered metadata fetching",
      category: "AI Tools",
    },
    {
      id: "r4",
      name: "Content Analyzer",
      version: "4.2.1",
      source: "Registry",
      enabled: false,
      description: "Deep content analysis engine",
      category: "Analytics",
    },
  ]);

  const [providerRankings] = useState<ProviderRanking[]>([
    {
      rank: 1,
      name: "StreamProvider Pro",
      source: "Registry",
      reliability: 98,
      latency: 15,
      rating: 9.7,
    },
    {
      rank: 2,
      name: "MediaHub Connect",
      source: "Network",
      reliability: 95,
      latency: 22,
      rating: 9.3,
    },
    {
      rank: 3,
      name: "AI Metadata Engine",
      source: "Registry",
      reliability: 92,
      latency: 35,
      rating: 8.9,
    },
    {
      rank: 4,
      name: "Content Scraper",
      source: "Manifest",
      reliability: 87,
      latency: 48,
      rating: 8.2,
    },
  ]);

  const chartData = [
    { name: "StreamPro", reliability: 98, responseTime: 15 },
    { name: "MediaHub", reliability: 95, responseTime: 22 },
    { name: "MetaEngine", reliability: 92, responseTime: 35 },
    { name: "Scraper", reliability: 87, responseTime: 48 },
  ];

  const handleManifestClick = (manifest: Manifest) => {
    setSelectedManifest({
      url: manifest.url,
      metadata: {
        name: manifest.name,
        version: manifest.version,
        author: "Provider Team",
        license: "MIT",
      },
      capabilities: ["streaming", "metadata", "search"],
      version: manifest.version,
      dependencies: ["core@2.0.0", "network@1.5.0"],
    });
  };

  return (
    <div className="p-8 max-w-[1800px] mx-auto">
      {/* Top Row - System Overview */}
      <div className="grid grid-cols-3 gap-6 mb-8">
        <MetricCard
          title="Installed Add-Ons"
          value={installedAddons.length}
          progress={75}
          icon={<Download className="w-6 h-6" />}
        />
        <MetricCard
          title="Available Providers"
          value={
            registryAddons.length + networkProviders.length
          }
          progress={60}
          icon={<Network className="w-6 h-6" />}
        />
        <MetricCard
          title="Connected Networks"
          value={3}
          progress={90}
          icon={<Wifi className="w-6 h-6" />}
        />
      </div>

      {/* Second Row - Add-On Sources */}
      <div className="grid grid-cols-3 gap-6 mb-8">
        {/* Manifest Sources */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400">
            Manifest Sources
          </h3>

          <div className="mb-4">
            <label className="text-sm text-gray-400 block mb-2">
              Add Manifest URL
            </label>
            <input
              type="text"
              value={manifestUrl}
              onChange={(e) => setManifestUrl(e.target.value)}
              placeholder="https://provider.io/manifest.json"
              className="w-full bg-[#1a1d23] border border-cyan-500/30 rounded px-3 py-2 text-sm text-gray-200 placeholder-gray-600 focus:outline-none focus:border-cyan-500"
            />
          </div>

          <div className="flex gap-2 mb-4">
            <button className="flex-1 bg-cyan-600 hover:bg-cyan-500 text-white px-4 py-2 rounded text-sm transition-all">
              Add Manifest
            </button>
            <button className="flex-1 bg-purple-600 hover:bg-purple-500 text-white px-4 py-2 rounded text-sm transition-all">
              Validate Manifest
            </button>
          </div>

          <div className="space-y-2 max-h-64 overflow-y-auto">
            {manifests.map((manifest, idx) => (
              <div
                key={idx}
                onClick={() => handleManifestClick(manifest)}
                className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 hover:border-cyan-500/50 transition-all cursor-pointer"
              >
                <div className="flex justify-between items-start mb-1">
                  <span className="text-sm text-gray-200">
                    {manifest.name}
                  </span>
                  <button className="text-xs bg-cyan-600 hover:bg-cyan-500 text-white px-3 py-1 rounded transition-all">
                    Install
                  </button>
                </div>
                <p className="text-xs text-gray-500 truncate mb-1">
                  {manifest.url}
                </p>
                <p className="text-xs text-purple-400">
                  v{manifest.version}
                </p>
              </div>
            ))}
          </div>
        </div>

        {/* Network Discovery */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <div className="flex justify-between items-center mb-4">
            <h3 className="text-lg text-cyan-400">
              Network Discovery
            </h3>
            <button className="bg-blue-600 hover:bg-blue-500 text-white px-3 py-1.5 rounded text-sm flex items-center gap-2 transition-all">
              <Wifi className="w-4 h-4" />
              Scan Network
            </button>
          </div>

          <div className="space-y-2 max-h-80 overflow-y-auto">
            {networkProviders.map((provider, idx) => (
              <div
                key={idx}
                className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 hover:border-cyan-500/50 transition-all"
              >
                <div className="flex justify-between items-start mb-2">
                  <span className="text-sm text-gray-200">
                    {provider.name}
                  </span>
                  <button className="text-xs bg-cyan-600 hover:bg-cyan-500 text-white px-3 py-1 rounded transition-all">
                    Install
                  </button>
                </div>
                <div className="flex justify-between text-xs">
                  <span className="text-gray-500">
                    {provider.source}
                  </span>
                  <span className="text-purple-400">
                    {provider.latency}ms
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Add-On Registry */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400">
            Add-On Registry
          </h3>

          <div className="mb-4 relative">
            <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-500" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search registry..."
              className="w-full bg-[#1a1d23] border border-cyan-500/30 rounded pl-10 pr-3 py-2 text-sm text-gray-200 placeholder-gray-600 focus:outline-none focus:border-cyan-500"
            />
          </div>

          <div className="space-y-2 max-h-64 overflow-y-auto">
            {registryAddons.map((addon) => (
              <div
                key={addon.id}
                className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 hover:border-cyan-500/50 transition-all"
              >
                <div className="flex justify-between items-start mb-1">
                  <span className="text-sm text-gray-200">
                    {addon.name}
                  </span>
                  <button className="text-xs bg-cyan-600 hover:bg-cyan-500 text-white px-3 py-1 rounded transition-all">
                    Install
                  </button>
                </div>
                <p className="text-xs text-gray-500 mb-1">
                  {addon.description}
                </p>
                <div className="flex justify-between text-xs">
                  <span className="text-purple-400">
                    {addon.category}
                  </span>
                  <span className="text-gray-500">
                    v{addon.version}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Third Row - Installed Add-Ons */}
      <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5 mb-8">
        <h3 className="text-lg mb-4 text-cyan-400">
          Installed Add-Ons
        </h3>

        <div className="grid grid-cols-4 gap-4">
          {installedAddons.map((addon) => (
            <div
              key={addon.id}
              className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4 hover:border-cyan-500/50 transition-all"
            >
              <div className="flex justify-between items-start mb-3">
                <h4 className="text-sm text-gray-200">
                  {addon.name}
                </h4>
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    checked={addon.enabled}
                    onChange={() => {}}
                    className="sr-only peer"
                  />
                  <div className="w-9 h-5 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-cyan-600"></div>
                </label>
              </div>

              <div className="space-y-2 mb-3">
                <div className="flex justify-between text-xs">
                  <span className="text-gray-500">Version</span>
                  <span className="text-purple-400">
                    {addon.version}
                  </span>
                </div>
                <div className="flex justify-between text-xs">
                  <span className="text-gray-500">Source</span>
                  <span className="text-gray-400">
                    {addon.source}
                  </span>
                </div>
              </div>

              <div className="flex gap-2">
                <button className="flex-1 bg-blue-600 hover:bg-blue-500 text-white px-3 py-1.5 rounded text-xs flex items-center justify-center gap-1 transition-all">
                  <RefreshCw className="w-3 h-3" />
                  Update
                </button>
                <button className="bg-gray-700 hover:bg-gray-600 text-white px-3 py-1.5 rounded text-xs transition-all">
                  <Settings className="w-3 h-3" />
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Fourth Row - AI Provider Analysis */}
      <div className="grid grid-cols-2 gap-6 mb-8">
        {/* Provider Ranking */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400 flex items-center gap-2">
            <TrendingUp className="w-5 h-5" />
            Provider Ranking
          </h3>

          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-cyan-500/20">
                  <th className="text-left py-2 text-gray-400">
                    Rank
                  </th>
                  <th className="text-left py-2 text-gray-400">
                    Provider
                  </th>
                  <th className="text-left py-2 text-gray-400">
                    Source
                  </th>
                  <th className="text-right py-2 text-gray-400">
                    Reliability
                  </th>
                  <th className="text-right py-2 text-gray-400">
                    Latency
                  </th>
                  <th className="text-right py-2 text-gray-400">
                    Rating
                  </th>
                </tr>
              </thead>
              <tbody>
                {providerRankings.map((provider) => (
                  <tr
                    key={provider.rank}
                    className="border-b border-cyan-500/10 hover:bg-[#1a1d23] transition-colors"
                  >
                    <td className="py-3">
                      <span
                        className={`inline-flex items-center justify-center w-6 h-6 rounded-full text-xs ${
                          provider.rank === 1
                            ? "bg-yellow-500/20 text-yellow-400"
                            : provider.rank === 2
                              ? "bg-gray-400/20 text-gray-300"
                              : provider.rank === 3
                                ? "bg-orange-500/20 text-orange-400"
                                : "bg-gray-700 text-gray-400"
                        }`}
                      >
                        {provider.rank}
                      </span>
                    </td>
                    <td className="py-3 text-gray-200">
                      {provider.name}
                    </td>
                    <td className="py-3 text-gray-500">
                      {provider.source}
                    </td>
                    <td className="py-3 text-right">
                      <span className="text-green-400">
                        {provider.reliability}%
                      </span>
                    </td>
                    <td className="py-3 text-right">
                      <span className="text-purple-400">
                        {provider.latency}ms
                      </span>
                    </td>
                    <td className="py-3 text-right">
                      <span className="text-cyan-400">
                        {provider.rating}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Provider Performance */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400 flex items-center gap-2">
            <Activity className="w-5 h-5" />
            Provider Performance
          </h3>

          <ResponsiveContainer width="100%" height={260}>
            <BarChart data={chartData}>
              <CartesianGrid
                key="grid"
                strokeDasharray="3 3"
                stroke="#374151"
              />
              <XAxis
                key="xaxis"
                dataKey="name"
                stroke="#9CA3AF"
              />
              <YAxis key="yaxis" stroke="#9CA3AF" />
              <Tooltip
                key="tooltip"
                contentStyle={{
                  backgroundColor: "#1a1d23",
                  border: "1px solid rgba(6, 182, 212, 0.3)",
                  borderRadius: "8px",
                }}
                labelStyle={{ color: "#06b6d4" }}
              />
              <Bar
                key="reliability-bar"
                dataKey="reliability"
                fill="#06b6d4"
                name="Reliability %"
              />
              <Bar
                key="responseTime-bar"
                dataKey="responseTime"
                fill="#a855f7"
                name="Response Time (ms)"
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Fifth Row - Network Status */}
      <div className="grid grid-cols-2 gap-6">
        {/* Network Nodes */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400 flex items-center gap-2">
            <Network className="w-5 h-5" />
            Network Nodes
          </h3>

          <div className="space-y-3">
            {[
              {
                name: "Local Network",
                count: 4,
                health: 95,
                latency: 8,
              },
              {
                name: "Private Nodes",
                count: 2,
                health: 88,
                latency: 35,
              },
              {
                name: "Remote Nodes",
                count: 6,
                health: 78,
                latency: 92,
              },
            ].map((node, idx) => (
              <div
                key={idx}
                className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4"
              >
                <div className="flex justify-between items-center mb-2">
                  <span className="text-sm text-gray-200">
                    {node.name}
                  </span>
                  <span className="text-xs text-purple-400">
                    {node.count} providers
                  </span>
                </div>
                <div className="flex gap-4 text-xs">
                  <div>
                    <span className="text-gray-500">
                      Health:{" "}
                    </span>
                    <span className="text-green-400">
                      {node.health}%
                    </span>
                  </div>
                  <div>
                    <span className="text-gray-500">
                      Latency:{" "}
                    </span>
                    <span className="text-cyan-400">
                      {node.latency}ms
                    </span>
                  </div>
                </div>
                <div className="mt-2 w-full bg-gray-700 rounded-full h-1.5">
                  <div
                    className="bg-gradient-to-r from-cyan-500 to-blue-500 h-1.5 rounded-full transition-all"
                    style={{ width: `${node.health}%` }}
                  ></div>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Discovery Activity */}
        <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5">
          <h3 className="text-lg mb-4 text-cyan-400 flex items-center gap-2">
            <Activity className="w-5 h-5" />
            Discovery Activity
          </h3>

          <div className="space-y-4">
            <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4">
              <div className="flex justify-between items-center mb-2">
                <span className="text-sm text-gray-400">
                  Last Scan Time
                </span>
                <span className="text-sm text-gray-200">
                  2 minutes ago
                </span>
              </div>
              <div className="w-full bg-gray-700 rounded-full h-1.5">
                <div className="bg-cyan-500 h-1.5 rounded-full w-full"></div>
              </div>
            </div>

            <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4">
              <div className="flex justify-between items-center mb-2">
                <span className="text-sm text-gray-400">
                  Providers Found
                </span>
                <span className="text-sm text-cyan-400">
                  12 providers
                </span>
              </div>
              <div className="w-full bg-gray-700 rounded-full h-1.5">
                <div className="bg-purple-500 h-1.5 rounded-full w-[85%]"></div>
              </div>
            </div>

            <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4">
              <div className="flex justify-between items-center mb-2">
                <span className="text-sm text-gray-400">
                  Active Discovery
                </span>
                <span className="text-sm text-green-400 flex items-center gap-2">
                  <CheckCircle className="w-4 h-4" />
                  Running
                </span>
              </div>
              <div className="w-full bg-gray-700 rounded-full h-1.5">
                <div className="bg-gradient-to-r from-green-500 to-emerald-500 h-1.5 rounded-full w-[92%]"></div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Manifest Inspector Slide Panel */}
      {selectedManifest && (
        <div className="fixed top-0 right-0 h-full w-[500px] bg-[#252930] border-l border-cyan-500/30 shadow-2xl shadow-cyan-500/10 z-50 overflow-y-auto">
          <div className="sticky top-0 bg-[#252930] border-b border-cyan-500/20 p-4 flex justify-between items-center">
            <h3 className="text-lg text-cyan-400">
              Manifest Inspector
            </h3>
            <button
              onClick={() => setSelectedManifest(null)}
              className="text-gray-400 hover:text-gray-200 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          <div className="p-6 space-y-6">
            <div>
              <label className="text-sm text-gray-400 block mb-2">
                Manifest URL
              </label>
              <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 text-sm text-gray-200 break-all">
                {selectedManifest.url}
              </div>
            </div>

            <div>
              <label className="text-sm text-gray-400 block mb-2">
                Provider Metadata
              </label>
              <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 space-y-2">
                {Object.entries(selectedManifest.metadata).map(
                  ([key, value]) => (
                    <div
                      key={key}
                      className="flex justify-between text-sm"
                    >
                      <span className="text-gray-500">
                        {key}:
                      </span>
                      <span className="text-gray-200">
                        {value}
                      </span>
                    </div>
                  ),
                )}
              </div>
            </div>

            <div>
              <label className="text-sm text-gray-400 block mb-2">
                Capabilities
              </label>
              <div className="flex flex-wrap gap-2">
                {selectedManifest.capabilities.map(
                  (cap, idx) => (
                    <span
                      key={idx}
                      className="bg-cyan-600/20 text-cyan-400 px-3 py-1 rounded text-xs border border-cyan-500/30"
                    >
                      {cap}
                    </span>
                  ),
                )}
              </div>
            </div>

            <div>
              <label className="text-sm text-gray-400 block mb-2">
                Dependencies
              </label>
              <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-3 space-y-1">
                {selectedManifest.dependencies.map(
                  (dep, idx) => (
                    <div
                      key={idx}
                      className="text-sm text-purple-400 font-mono"
                    >
                      {dep}
                    </div>
                  ),
                )}
              </div>
            </div>

            <div>
              <label className="text-sm text-gray-400 block mb-2">
                JSON Manifest
              </label>
              <div className="bg-[#1a1d23] border border-cyan-500/20 rounded p-4 text-xs font-mono text-gray-300 overflow-x-auto">
                <pre>
                  {JSON.stringify(selectedManifest, null, 2)}
                </pre>
              </div>
            </div>

            <div className="flex gap-2 pt-4">
              <button className="flex-1 bg-cyan-600 hover:bg-cyan-500 text-white px-4 py-2 rounded text-sm flex items-center justify-center gap-2 transition-all">
                <Copy className="w-4 h-4" />
                Copy
              </button>
              <button className="flex-1 bg-purple-600 hover:bg-purple-500 text-white px-4 py-2 rounded text-sm flex items-center justify-center gap-2 transition-all">
                <RefreshCw className="w-4 h-4" />
                Refresh
              </button>
              <button className="flex-1 bg-blue-600 hover:bg-blue-500 text-white px-4 py-2 rounded text-sm flex items-center justify-center gap-2 transition-all">
                <CheckCircle className="w-4 h-4" />
                Validate
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function MetricCard({
  title,
  value,
  progress,
  icon,
}: {
  title: string;
  value: number;
  progress: number;
  icon: React.ReactNode;
}) {
  return (
    <div className="bg-[#252930] rounded-lg p-6 border border-cyan-500/20 shadow-lg shadow-cyan-500/5 hover:border-cyan-500/40 transition-all">
      <div className="flex justify-between items-start mb-4">
        <div>
          <p className="text-sm text-gray-400 mb-1">{title}</p>
          <p className="text-3xl text-cyan-400">{value}</p>
        </div>
        <div className="text-cyan-400/60">{icon}</div>
      </div>
      <div className="w-full bg-gray-700 rounded-full h-2">
        <div
          className="bg-gradient-to-r from-cyan-500 to-blue-500 h-2 rounded-full transition-all"
          style={{ width: `${progress}%` }}
        ></div>
      </div>
      <p className="text-xs text-gray-500 mt-2">
        {progress}% capacity
      </p>
    </div>
  );
}