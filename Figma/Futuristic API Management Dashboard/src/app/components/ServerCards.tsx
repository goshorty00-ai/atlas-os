import { Cpu, HardDrive, Zap } from 'lucide-react';
import { motion } from 'motion/react';
import { postToHost } from '../atlasBridge';

const servers = [
  {
    id: 1,
    name: 'Atlas-Web-01',
    region: 'US-East',
    cpu: 67,
    ram: 82,
    uptime: '45d 12h',
    status: 'healthy'
  },
  {
    id: 2,
    name: 'Atlas-API-02',
    region: 'EU-West',
    cpu: 45,
    ram: 58,
    uptime: '89d 3h',
    status: 'healthy'
  },
  {
    id: 3,
    name: 'Atlas-DB-03',
    region: 'Asia-Pacific',
    cpu: 91,
    ram: 94,
    uptime: '12d 8h',
    status: 'warning'
  },
  {
    id: 4,
    name: 'Atlas-Cache-04',
    region: 'US-West',
    cpu: 34,
    ram: 41,
    uptime: '156d 22h',
    status: 'healthy'
  }
];

function MeterBar({ value, color }: { value: number; color: string }) {
  return (
    <div className="relative h-2 bg-gray-800/50 rounded-full overflow-hidden">
      <motion.div
        initial={{ width: 0 }}
        animate={{ width: `${value}%` }}
        transition={{ duration: 1, ease: 'easeOut' }}
        className={`absolute inset-y-0 left-0 rounded-full ${color}`}
        style={{
          boxShadow: value > 80 
            ? '0 0 10px rgba(239, 68, 68, 0.5)' 
            : '0 0 10px rgba(59, 130, 246, 0.5)'
        }}
      />
      <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/10 to-transparent animate-pulse" />
    </div>
  );
}

export function ServerCards() {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-light text-gray-300">
          Server Infrastructure
        </h2>
        <div className="flex items-center space-x-2 text-sm text-gray-500">
          <Zap className="w-4 h-4 text-blue-400" />
          <span>{servers.length} nodes active</span>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {servers.map((server) => (
          <div
            key={server.id}
            className="group relative rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-6 hover:border-blue-400/40 transition-all"
            style={{
              boxShadow: '0 8px 32px 0 rgba(31, 38, 135, 0.15)'
            }}
          >
            {/* Glassmorphism overlay */}
            <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-white/5 to-white/0 pointer-events-none" />

            <div className="relative space-y-5">
              {/* Header */}
              <div className="flex items-start justify-between">
                <div className="space-y-1">
                  <h3 className="text-lg font-medium text-gray-200">{server.name}</h3>
                  <div className="flex items-center space-x-2">
                    <div className={`w-2 h-2 rounded-full ${
                      server.status === 'healthy' ? 'bg-green-400 shadow-lg shadow-green-400/50' : 'bg-yellow-400 shadow-lg shadow-yellow-400/50'
                    } animate-pulse`} />
                    <span className="text-xs text-gray-500">{server.region}</span>
                  </div>
                </div>
                
                <div className="px-3 py-1 rounded-md text-xs font-medium bg-blue-500/10 text-blue-300 border border-blue-500/30">
                  {server.uptime}
                </div>
              </div>

              {/* CPU Usage */}
              <div className="space-y-2">
                <div className="flex items-center justify-between text-sm">
                  <div className="flex items-center space-x-2">
                    <Cpu className="w-4 h-4 text-blue-400" />
                    <span className="text-gray-400">CPU</span>
                  </div>
                  <span className={`font-medium ${
                    server.cpu > 80 ? 'text-red-400' : 'text-gray-200'
                  }`}>
                    {server.cpu}%
                  </span>
                </div>
                <MeterBar 
                  value={server.cpu} 
                  color={server.cpu > 80 ? 'bg-gradient-to-r from-red-500 to-orange-500' : 'bg-gradient-to-r from-blue-500 to-cyan-400'}
                />
              </div>

              {/* RAM Usage */}
              <div className="space-y-2">
                <div className="flex items-center justify-between text-sm">
                  <div className="flex items-center space-x-2">
                    <HardDrive className="w-4 h-4 text-violet-400" />
                    <span className="text-gray-400">RAM</span>
                  </div>
                  <span className={`font-medium ${
                    server.ram > 80 ? 'text-red-400' : 'text-gray-200'
                  }`}>
                    {server.ram}%
                  </span>
                </div>
                <MeterBar 
                  value={server.ram} 
                  color={server.ram > 80 ? 'bg-gradient-to-r from-red-500 to-orange-500' : 'bg-gradient-to-r from-violet-500 to-purple-400'}
                />
              </div>

              {/* Divider */}
              <div className="h-[1px] bg-gradient-to-r from-transparent via-blue-500/30 to-transparent" />

              {/* Footer actions */}
              <div className="flex items-center justify-between text-xs">
                <button
                  className="text-blue-400 hover:text-blue-300 transition-colors"
                  onClick={() => postToHost('api.openLogsFolder')}
                >
                  Terminal →
                </button>
                <button
                  className="text-gray-500 hover:text-gray-400 transition-colors"
                  onClick={() => postToHost('api.getState')}
                >
                  Restart
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
