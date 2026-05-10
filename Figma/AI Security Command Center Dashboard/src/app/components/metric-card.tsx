import { useEffect, useState } from "react";
import { motion } from "motion/react";
import { LucideIcon, FileSearch, Activity, Shield, Network, Zap } from "lucide-react";
import { AreaChart, Area, ResponsiveContainer } from "recharts";
import { bridge, TelemetryPayload } from "../bridge";

interface MetricCardProps {
  title: string; value: number; icon: LucideIcon; suffix?: string;
  trend?: "up" | "down" | "stable"; color?: "blue" | "green" | "purple" | "amber";
  showChart?: boolean; chartData?: Array<{ value: number }>;
}

export function MetricCard({ title, value, icon: Icon, suffix = "", trend, color = "blue", showChart = false, chartData = [] }: MetricCardProps) {
  const c = { blue: { bg: "from-sky-500/20 to-blue-500/20", border: "border-sky-400/30", text: "text-sky-300", chart: "#0ea5e9" }, green: { bg: "from-emerald-500/20 to-green-500/20", border: "border-emerald-400/30", text: "text-emerald-300", chart: "#34d399" }, purple: { bg: "from-purple-500/20 to-violet-500/20", border: "border-purple-400/30", text: "text-purple-300", chart: "#a855f7" }, amber: { bg: "from-amber-500/20 to-orange-500/20", border: "border-amber-400/30", text: "text-amber-300", chart: "#fbbf24" } }[color];
  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className={"relative p-4 rounded-xl bg-gradient-to-br " + c.bg + " border " + c.border + " backdrop-blur-sm overflow-hidden h-full"}>
      <div className="relative">
        <div className="flex items-start justify-between mb-3">
          <div className={"p-2 rounded-lg bg-gradient-to-br " + c.bg + " border " + c.border}><Icon className={"w-4 h-4 " + c.text} /></div>
          {trend && <div className={"px-2 py-0.5 rounded text-[10px] font-medium " + (trend === "up" ? "bg-emerald-400/20 text-emerald-300" : trend === "down" ? "bg-red-400/20 text-red-300" : "bg-slate-400/20 text-slate-300")}>{trend === "up" ? "↑" : trend === "down" ? "↓" : "→"} {trend}</div>}
        </div>
        <div className={"text-3xl font-bold " + c.text + " font-mono tracking-tight mb-1"}>{value.toLocaleString()}{suffix}</div>
        <div className="text-sky-200/60 text-xs font-medium mb-2">{title}</div>
        {showChart && chartData.length > 0 && (
          <div className="h-12 -mx-2 -mb-2 mt-3">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={chartData}>
                <defs><linearGradient id={"grad-" + color} x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor={c.chart} stopOpacity={0.3} /><stop offset="95%" stopColor={c.chart} stopOpacity={0} /></linearGradient></defs>
                <Area type="monotone" dataKey="value" stroke={c.chart} strokeWidth={2} fill={"url(#grad-" + color + ")"} isAnimationActive={false} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
    </motion.div>
  );
}

export function MetricsGrid() {
  const [d, setD] = useState({ filesScanned: 0, processCount: 0, suspicious: 0, networkConnections: 0, vulnerabilityScore: 0 });
  const [cpuH, setCpuH] = useState<Array<{ value: number }>>([]);
  const [ramH, setRamH] = useState<Array<{ value: number }>>([]);
  useEffect(() => {
    return bridge.on<TelemetryPayload>("telemetry", (t) => {
      setD({ filesScanned: t.filesScanned, processCount: t.processCount, suspicious: t.suspicious, networkConnections: t.networkConnections, vulnerabilityScore: t.vulnerabilityScore });
      setCpuH((p) => [...p.slice(-19), { value: Math.round(t.cpu) }]);
      setRamH((p) => [...p.slice(-19), { value: Math.round(t.ram) }]);
    });
  }, []);
  return (
    <div className="grid grid-cols-6 gap-3">
      <MetricCard title="Files Scanned Today"   value={d.filesScanned}       icon={FileSearch} color="blue"   trend="up"     showChart chartData={cpuH} />
      <MetricCard title="Active Processes"       value={d.processCount}       icon={Activity}   color="green"  trend="stable" showChart chartData={ramH} />
      <MetricCard title="Suspicious Flagged"     value={d.suspicious}         icon={Shield}     color="amber"  trend="down" />
      <MetricCard title="Network Connections"    value={d.networkConnections} icon={Network}    color="purple" showChart chartData={cpuH} />
      <MetricCard title="AI Predictions"         value={d.filesScanned + d.processCount} icon={Zap} color="blue" trend="up" />
      <MetricCard title="Vulnerability Score"    value={d.vulnerabilityScore} icon={Shield}     suffix="/100"  color="green"  trend="up" />
    </div>
  );
}

