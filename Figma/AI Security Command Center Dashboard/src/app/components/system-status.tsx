import { useEffect, useState } from "react";
import { motion } from "motion/react";
import { Cpu, HardDrive, Wifi, Activity } from "lucide-react";
import { bridge, TelemetryPayload } from "../bridge";

interface SystemMetric {
  label: string;
  value: number;
  icon: React.ElementType;
}

export function SystemStatus() {
  const [metrics, setMetrics] = useState<SystemMetric[]>([
    { label: "CPU", value: 0, icon: Cpu },
    { label: "RAM", value: 0, icon: HardDrive },
    { label: "NET", value: 0, icon: Wifi },
    { label: "PROC", value: 0, icon: Activity },
  ]);

  useEffect(() => {
    const unsub = bridge.on<TelemetryPayload>("telemetry", (d) => {
      setMetrics([
        { label: "CPU",  value: Math.round(d.cpu),  icon: Cpu },
        { label: "RAM",  value: Math.round(d.ram),  icon: HardDrive },
        { label: "NET",  value: Math.round(d.netKbps), icon: Wifi },
        { label: "PROC", value: d.processCount,     icon: Activity },
      ]);
    });
    return unsub;
  }, []);

  return (
    <div className="flex items-center gap-4">
      {metrics.map((metric) => {
        const Icon = metric.icon;
        return (
          <div key={metric.label} className="flex items-center gap-2">
            <Icon className="w-4 h-4 text-sky-400" />
            <div className="flex items-baseline gap-1">
              <motion.span
                key={metric.value}
                initial={{ opacity: 0, y: -5 }}
                animate={{ opacity: 1, y: 0 }}
                className="text-sky-100 text-sm font-mono font-semibold"
              >
                {metric.label === "PROC" ? metric.value : metric.value}
              </motion.span>
              <span className="text-sky-400 text-xs">
                {metric.label === "PROC" ? "" : "%"}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
