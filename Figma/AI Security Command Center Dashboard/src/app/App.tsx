import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Shield, Clock, Scan, Eye, Zap, Activity } from 'lucide-react';
import { SecurityRadar } from './components/security-radar';
import { ActivityFeed } from './components/activity-feed';
import { SecurityModules } from './components/security-modules';
import { MetricsGrid } from './components/metric-card';
import { AIAnalysisPanel } from './components/ai-analysis-panel';
import { SystemStatus } from './components/system-status';
import { SecurityChat } from './components/security-chat';
import { bridge, TelemetryPayload } from './bridge';

type SecurityStatus = 'secure' | 'warning' | 'threat';

export default function App() {
  const [currentTime, setCurrentTime] = useState(new Date());
  const [securityStatus, setSecurityStatus] = useState<SecurityStatus>('secure');

  useEffect(() => {
    const timer = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  // Live status from telemetry bridge
  useEffect(() => {
    return bridge.on<TelemetryPayload>('telemetry', (d) => {
      setSecurityStatus(d.status as SecurityStatus);
    });
  }, []);

  const getStatusConfig = () => {
    switch (securityStatus) {
      case 'secure':
        return {
          text: 'SECURE',
          color: 'text-emerald-400',
          bg: 'bg-emerald-400/20',
          border: 'border-emerald-400/40',
          glow: 'shadow-[0_0_20px_rgba(52,211,153,0.4)]'
        };
      case 'warning':
        return {
          text: 'WARNING',
          color: 'text-amber-400',
          bg: 'bg-amber-400/20',
          border: 'border-amber-400/40',
          glow: 'shadow-[0_0_20px_rgba(251,191,36,0.4)]'
        };
      case 'threat':
        return {
          text: 'THREAT DETECTED',
          color: 'text-red-400',
          bg: 'bg-red-400/20',
          border: 'border-red-400/40',
          glow: 'shadow-[0_0_20px_rgba(239,68,68,0.4)]'
        };
    }
  };

  const status = getStatusConfig();

  return (
    <div className="min-h-screen bg-slate-950 text-white overflow-hidden">
      {/* Animated background */}
      <div className="fixed inset-0 opacity-20">
        <div className="absolute inset-0" style={{
          backgroundImage: `
            linear-gradient(rgba(14, 165, 233, 0.1) 1px, transparent 1px),
            linear-gradient(90deg, rgba(14, 165, 233, 0.1) 1px, transparent 1px)
          `,
          backgroundSize: '50px 50px'
        }} />
      </div>

      {/* Radial gradient overlays */}
      <div className="fixed inset-0 pointer-events-none">
        <motion.div
          animate={{
            opacity: [0.3, 0.5, 0.3],
            scale: [1, 1.1, 1],
          }}
          transition={{ duration: 8, repeat: Infinity }}
          className="absolute top-0 left-0 w-1/2 h-1/2 bg-blue-500/10 blur-[120px] rounded-full"
        />
        <motion.div
          animate={{
            opacity: [0.3, 0.5, 0.3],
            scale: [1, 1.1, 1],
          }}
          transition={{ duration: 8, repeat: Infinity, delay: 2 }}
          className="absolute bottom-0 right-0 w-1/2 h-1/2 bg-purple-500/10 blur-[120px] rounded-full"
        />
      </div>

      <div className="relative z-10 flex flex-col h-screen">
        {/* HEADER */}
        <header className="flex items-center justify-between px-6 py-3 border-b border-sky-500/20 bg-slate-950/80 backdrop-blur-xl shrink-0">
          {/* Left: branding */}
          <div className="flex items-center gap-3">
            <Shield className="w-6 h-6 text-sky-400" />
            <div>
              <h1 className="text-sky-100 font-bold text-sm tracking-widest uppercase">Atlas Guardian</h1>
              <p className="text-sky-400/60 text-xs">Autonomous Security Command Center</p>
            </div>
          </div>

          {/* Center: system stats */}
          <SystemStatus />

          {/* Right: status + clock + AI badge */}
          <div className="flex items-center gap-4">
            <div className={`flex items-center gap-2 px-3 py-1 rounded-full border text-xs font-bold ${status.bg} ${status.border} ${status.color} ${status.glow}`}>
              <div className="w-2 h-2 rounded-full bg-current animate-pulse" />
              {status.text}
            </div>
            <div className="flex items-center gap-2 text-sky-300 text-sm font-mono">
              <Clock className="w-4 h-4 text-sky-400" />
              {currentTime.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </div>
            <div className="flex items-center gap-2 px-3 py-1 rounded-full bg-purple-500/20 border border-purple-500/40 text-purple-300 text-xs">
              <Eye className="w-3 h-3" />
              AI Monitoring Active
            </div>
          </div>
        </header>

        {/* MAIN CONTENT AREA */}
        <div className="flex-1 flex gap-4 p-4 overflow-hidden">
          {/* LEFT SIDEBAR - SECURITY MODULES */}
          <aside className="w-64 flex flex-col gap-4">
            <div className="
              flex-1 p-4 rounded-xl
              bg-gradient-to-br from-slate-900/60 to-slate-950/60
              border border-sky-500/20
              backdrop-blur-xl
              overflow-y-auto
            ">
              <div className="flex items-center gap-2 mb-4">
                <Zap className="w-4 h-4 text-sky-400" />
                <h2 className="text-sky-100 text-sm font-semibold">Security Modules</h2>
              </div>
              <SecurityModules />
            </div>
          </aside>

          {/* CENTER PANEL - MAIN CONTENT */}
          <main className="flex-1 flex flex-col gap-4 overflow-hidden">
            {/* RADAR AND AI ANALYSIS */}
            <div className="flex gap-4 h-[500px]">
              {/* LIVE SECURITY RADAR */}
              <div className="
                flex-1 p-6 rounded-xl
                bg-gradient-to-br from-slate-900/60 to-slate-950/60
                border border-sky-500/20
                backdrop-blur-xl
                relative overflow-hidden
              ">
                <div className="absolute top-4 left-4 z-10">
                  <div className="flex items-center gap-2 mb-1">
                    <Scan className="w-5 h-5 text-sky-400" />
                    <h2 className="text-sky-100 font-semibold">Live Security Radar</h2>
                  </div>
                  <p className="text-sky-400/60 text-xs">Real-time threat detection & analysis</p>
                </div>
                
                <SecurityRadar />

                {/* Scanning indicators */}
                <div className="absolute bottom-4 left-4 right-4 flex items-center gap-4">
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 rounded-full bg-emerald-400" />
                    <span className="text-xs text-sky-300">Safe</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
                    <span className="text-xs text-sky-300">Monitoring</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
                    <span className="text-xs text-sky-300">Threat</span>
                  </div>
                </div>
              </div>

              {/* AI ANALYSIS PANEL */}
              <div className="w-96">
                <AIAnalysisPanel />
              </div>
            </div>

            {/* BOTTOM PANEL - SYSTEM METRICS */}
            <div className="
              p-4 rounded-xl
              bg-gradient-to-br from-slate-900/60 to-slate-950/60
              border border-sky-500/20
              backdrop-blur-xl
            ">
              <div className="flex items-center gap-2 mb-4">
                <Activity className="w-4 h-4 text-sky-400" />
                <h2 className="text-sky-100 text-sm font-semibold">System Security Overview</h2>
              </div>

              <MetricsGrid />
            </div>
          </main>

          {/* RIGHT PANEL - ACTIVITY FEED */}
          <aside className="w-80">
            <div className="
              h-full p-4 rounded-xl
              bg-gradient-to-br from-slate-900/60 to-slate-950/60
              border border-sky-500/20
              backdrop-blur-xl
              overflow-hidden
            ">
              <ActivityFeed />
            </div>
          </aside>
        </div>
      </div>

      {/* Security Chat */}
      <SecurityChat />
    </div>
  );
}




