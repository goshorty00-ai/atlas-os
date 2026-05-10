import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Activity, Shield, Wifi, Cpu, Database, Network } from "lucide-react";

interface InsightMessage {
  id: number;
  icon: any;
  message: string;
  severity: "info" | "success" | "warning";
}

const insightPool: Omit<InsightMessage, "id">[] = [
  { icon: Shield, message: "SECURITY PROTOCOLS ACTIVE", severity: "success" },
  { icon: Activity, message: "MONITORING 12 ACTIVE WORKFLOWS", severity: "info" },
  { icon: Cpu, message: "PROCESSING EFFICIENCY OPTIMAL", severity: "success" },
  { icon: Database, message: "MEMORY CACHE SYNCHRONIZED", severity: "info" },
  { icon: Network, message: "NETWORK LATENCY 14MS", severity: "success" },
  { icon: Wifi, message: "CLOUD SERVICES CONNECTED", severity: "info" },
];

export function SystemInsight() {
  const [currentInsight, setCurrentInsight] = useState<InsightMessage>({
    id: 0,
    ...insightPool[0],
  });
  const [metrics, setMetrics] = useState({
    cpu: 23,
    memory: 67,
    network: 14,
  });

  useEffect(() => {
    // Rotate insights
    const insightInterval = setInterval(() => {
      setCurrentInsight((prev) => {
        const nextIndex = (prev.id + 1) % insightPool.length;
        return { id: nextIndex, ...insightPool[nextIndex] };
      });
    }, 4000);

    // Update metrics
    const metricsInterval = setInterval(() => {
      setMetrics({
        cpu: Math.floor(Math.random() * 15) + 18,
        memory: Math.floor(Math.random() * 10) + 62,
        network: Math.floor(Math.random() * 8) + 10,
      });
    }, 2000);

    return () => {
      clearInterval(insightInterval);
      clearInterval(metricsInterval);
    };
  }, []);

  const severityColor = {
    info: "text-cyan-400 border-cyan-500/30",
    success: "text-green-400 border-green-500/30",
    warning: "text-orange-400 border-orange-500/30",
  };

  return (
    <div className="fixed bottom-4 left-4 space-y-3">
      {/* System Insight Messages */}
      <div className="bg-[#0b0f14]/95 backdrop-blur-sm border border-cyan-500/20 rounded-lg p-4 w-80 shadow-[0_0_30px_rgba(34,211,238,0.1)]">
        <div className="flex items-center gap-2 mb-3 pb-2 border-b border-cyan-500/10">
          <Activity className="w-3.5 h-3.5 text-cyan-400" />
          <span className="text-[10px] font-mono tracking-widest text-cyan-400 uppercase">
            System Insight
          </span>
        </div>

        <AnimatePresence mode="wait">
          <motion.div
            key={currentInsight.id}
            initial={{ opacity: 0, x: -20 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 20 }}
            transition={{ duration: 0.3 }}
            className={`flex items-center gap-3 p-2.5 rounded border ${
              severityColor[currentInsight.severity]
            } bg-slate-900/30`}
          >
            <currentInsight.icon className="w-4 h-4 flex-shrink-0" />
            <span className="text-xs font-mono tracking-wide">
              {currentInsight.message}
            </span>
          </motion.div>
        </AnimatePresence>

        {/* Real-time Metrics */}
        <div className="mt-3 pt-3 border-t border-cyan-500/10 space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider">
              CPU
            </span>
            <div className="flex items-center gap-2">
              <div className="w-20 h-1 bg-slate-800 rounded-full overflow-hidden">
                <motion.div
                  className="h-full bg-cyan-400"
                  animate={{ width: `${metrics.cpu}%` }}
                  transition={{ duration: 0.5 }}
                />
              </div>
              <span className="text-[10px] font-mono text-cyan-400 w-8 text-right">
                {metrics.cpu}%
              </span>
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider">
              Memory
            </span>
            <div className="flex items-center gap-2">
              <div className="w-20 h-1 bg-slate-800 rounded-full overflow-hidden">
                <motion.div
                  className="h-full bg-purple-400"
                  animate={{ width: `${metrics.memory}%` }}
                  transition={{ duration: 0.5 }}
                />
              </div>
              <span className="text-[10px] font-mono text-purple-400 w-8 text-right">
                {metrics.memory}%
              </span>
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider">
              Network
            </span>
            <div className="flex items-center gap-2">
              <div className="w-20 h-1 bg-slate-800 rounded-full overflow-hidden">
                <motion.div
                  className="h-full bg-green-400"
                  style={{ width: "90%" }}
                />
              </div>
              <span className="text-[10px] font-mono text-green-400 w-8 text-right">
                {metrics.network}ms
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
