import { motion } from 'motion/react';
import { Shield, Download, PackageCheck, Network, Activity, FileCheck, Flame, Database } from 'lucide-react';

interface SecurityModule {
  id: string;
  name: string;
  icon: React.ElementType;
  status: 'active' | 'monitoring' | 'standby';
}

const modules: SecurityModule[] = [
  { id: '1', name: 'Real-Time Protection', icon: Shield, status: 'active' },
  { id: '2', name: 'Download Monitoring', icon: Download, status: 'monitoring' },
  { id: '3', name: 'Installation Monitoring', icon: PackageCheck, status: 'monitoring' },
  { id: '4', name: 'Network Intrusion Detection', icon: Network, status: 'active' },
  { id: '5', name: 'Process Behavior Analysis', icon: Activity, status: 'active' },
  { id: '6', name: 'File Integrity Monitoring', icon: FileCheck, status: 'monitoring' },
  { id: '7', name: 'Firewall Intelligence', icon: Flame, status: 'active' },
  { id: '8', name: 'Threat Intelligence Feed', icon: Database, status: 'monitoring' },
];

export function SecurityModules() {
  const getStatusColor = (status: SecurityModule['status']) => {
    switch (status) {
      case 'active': return 'bg-emerald-400';
      case 'monitoring': return 'bg-sky-400';
      case 'standby': return 'bg-slate-500';
    }
  };

  return (
    <div className="flex flex-col gap-1">
      {modules.map((module, index) => {
        const Icon = module.icon;
        return (
          <motion.div
            key={module.id}
            initial={{ opacity: 0, x: -20 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ delay: index * 0.05 }}
            className="
              group relative p-3 rounded-lg
              bg-gradient-to-br from-slate-800/30 to-slate-900/30
              border border-sky-500/10
              hover:border-sky-400/30 hover:bg-slate-800/50
              transition-all duration-300 cursor-pointer
            "
          >
            <div className="flex items-center gap-3">
              <div className="
                relative p-2 rounded-lg
                bg-gradient-to-br from-sky-500/20 to-purple-500/20
                border border-sky-400/30
                group-hover:from-sky-500/30 group-hover:to-purple-500/30
                transition-all duration-300
              ">
                <Icon className="w-4 h-4 text-sky-300" />
                <motion.div
                  animate={module.status === 'active' ? {
                    scale: [1, 1.2, 1],
                    opacity: [0.5, 0.8, 0.5]
                  } : {}}
                  transition={{ duration: 2, repeat: Infinity }}
                  className="absolute inset-0 rounded-lg bg-sky-400/20 blur-sm"
                />
              </div>
              
              <div className="flex-1 min-w-0">
                <h4 className="text-sky-100 text-xs font-medium truncate">
                  {module.name}
                </h4>
                <div className="flex items-center gap-1.5 mt-1">
                  <div className={`w-1.5 h-1.5 rounded-full ${getStatusColor(module.status)}`} />
                  <span className="text-sky-400/60 text-[10px] capitalize">
                    {module.status}
                  </span>
                </div>
              </div>
            </div>

            {/* Hover effect */}
            <div className="
              absolute inset-0 rounded-lg
              bg-gradient-to-r from-sky-400/0 via-sky-400/5 to-sky-400/0
              opacity-0 group-hover:opacity-100
              transition-opacity duration-300
            " />
          </motion.div>
        );
      })}
    </div>
  );
}
