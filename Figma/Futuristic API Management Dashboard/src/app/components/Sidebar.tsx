import { Server, Zap, Network, HardDrive, Database, KeyRound, Activity, FileText } from 'lucide-react';

type NavSection = 'apis' | 'integrations' | 'servers' | 'network' | 'storage' | 'credentials' | 'monitoring' | 'logs';

interface SidebarProps {
  activeSection: NavSection;
  onSectionChange: (section: NavSection) => void;
}

const navItems: { id: NavSection; icon: typeof Server; label: string }[] = [
  { id: 'apis', icon: Zap, label: "API's" },
  { id: 'integrations', icon: Network, label: 'Integrations' },
  { id: 'servers', icon: Server, label: 'Servers' },
  { id: 'network', icon: Network, label: 'Network' },
  { id: 'storage', icon: HardDrive, label: 'Storage' },
  { id: 'credentials', icon: KeyRound, label: 'Credentials' },
  { id: 'monitoring', icon: Activity, label: 'Monitoring' },
  { id: 'logs', icon: FileText, label: 'Logs' },
];

export function Sidebar({ activeSection, onSectionChange }: SidebarProps) {
  return (
    <aside className="w-20 border-r border-blue-500/20 bg-gradient-to-b from-[#0f0f1a] to-[#0a0a0f] relative">
      {/* Glowing edge */}
      <div className="absolute top-0 right-0 w-[1px] h-full bg-gradient-to-b from-transparent via-blue-400/50 to-transparent" />
      
      {/* Logo */}
      <div className="h-20 flex items-center justify-center border-b border-blue-500/20">
        <div className="relative">
          <div className="w-10 h-10 rounded-lg bg-gradient-to-br from-blue-500 to-violet-600 flex items-center justify-center font-bold text-lg shadow-lg shadow-blue-500/50">
            A
          </div>
          <div className="absolute -inset-1 bg-gradient-to-br from-blue-500 to-violet-600 rounded-lg blur opacity-50 -z-10" />
        </div>
      </div>

      {/* Navigation */}
      <nav className="py-8 flex flex-col items-center space-y-2">
        {navItems.map((item) => {
          const Icon = item.icon;
          const isActive = activeSection === item.id;
          
          return (
            <button
              key={item.id}
              onClick={() => onSectionChange(item.id)}
              className={`
                w-12 h-12 rounded-xl flex items-center justify-center transition-all duration-300 relative group
                ${isActive 
                  ? 'bg-gradient-to-br from-blue-500/30 to-violet-500/30 border border-blue-400/50 shadow-lg shadow-blue-500/30' 
                  : 'hover:bg-blue-500/10 border border-transparent'
                }
              `}
              title={item.label}
            >
              <Icon 
                className={`w-5 h-5 transition-colors ${isActive ? 'text-blue-300' : 'text-gray-400 group-hover:text-blue-400'}`} 
              />
              
              {isActive && (
                <div className="absolute -left-1 top-1/2 -translate-y-1/2 w-1 h-6 bg-gradient-to-b from-blue-400 to-violet-400 rounded-full" />
              )}

              {/* Tooltip */}
              <div className="absolute left-full ml-4 px-3 py-1.5 bg-gray-900/95 border border-blue-500/30 rounded-lg text-sm whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none backdrop-blur-sm">
                {item.label}
              </div>
            </button>
          );
        })}
      </nav>

      {/* Status indicator */}
      <div className="absolute bottom-8 left-1/2 -translate-x-1/2">
        <div className="w-2 h-2 rounded-full bg-green-400 animate-pulse shadow-lg shadow-green-400/50" />
      </div>
    </aside>
  );
}
