import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Download, Shield, Activity, Network, FileCheck, AlertTriangle, CheckCircle2, Lock } from "lucide-react";
import { bridge, ActivityPayload } from "../bridge";

interface ActivityEvent {
  id: string;
  type: "download" | "install" | "process" | "network" | "permission" | "scan";
  title: string;
  description: string;
  timestamp: number;
  risk: "safe" | "medium" | "high";
}

export function ActivityFeed() {
  const [events, setEvents] = useState<ActivityEvent[]>([]);

  useEffect(() => {
    const unsub = bridge.on<ActivityPayload>("activity", (d) => {
      const evt: ActivityEvent = {
        id: d.id,
        type: (d.eventType as ActivityEvent["type"]) || "process",
        title: d.title,
        description: d.description,
        timestamp: d.timestamp,
        risk: d.risk,
      };
      setEvents((prev) => [evt, ...prev].slice(0, 30));
    });
    return unsub;
  }, []);

  const getIcon = (type: ActivityEvent["type"]) => {
    switch (type) {
      case "download": return Download;
      case "install": return Lock;
      case "process": return Activity;
      case "network": return Network;
      case "permission": return AlertTriangle;
      case "scan": return FileCheck;
      default: return Shield;
    }
  };

  const getRiskColor = (risk: ActivityEvent["risk"]) => {
    switch (risk) {
      case "safe":   return "text-emerald-400 bg-emerald-400/10 border-emerald-400/30";
      case "medium": return "text-amber-400 bg-amber-400/10 border-amber-400/30";
      case "high":   return "text-red-400 bg-red-400/10 border-red-400/30";
    }
  };

  const formatTime = (ts: number) => {
    const diff = Math.floor((Date.now() - ts) / 1000);
    if (diff < 60) return `${diff}s ago`;
    if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
    return new Date(ts).toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" });
  };

  return (
    <div className="flex flex-col gap-2 h-full overflow-hidden">
      <div className="flex items-center gap-2 mb-2 flex-shrink-0">
        <Activity className="w-4 h-4 text-sky-400" />
        <h3 className="text-sky-100 text-sm font-semibold">Live Activity Feed</h3>
        <div className="ml-auto">
          <motion.div
            animate={{ opacity: [0.3, 1, 0.3] }}
            transition={{ duration: 2, repeat: Infinity }}
            className="w-2 h-2 rounded-full bg-emerald-400"
          />
        </div>
      </div>

      {events.length === 0 && (
        <div className="flex-1 flex items-center justify-center">
          <p className="text-sky-400/40 text-xs font-mono">Waiting for system events…</p>
        </div>
      )}

      <div className="flex-1 overflow-y-auto space-y-2 pr-2 min-h-0">
        <AnimatePresence mode="popLayout">
          {events.map((event, index) => {
            const Icon = getIcon(event.type);
            return (
              <motion.div
                key={event.id}
                initial={{ opacity: 0, x: 20, scale: 0.95 }}
                animate={{ opacity: 1, x: 0, scale: 1 }}
                exit={{ opacity: 0, scale: 0.95 }}
                transition={{ duration: 0.3 }}
                className={`relative p-3 rounded-lg border backdrop-blur-sm bg-gradient-to-br from-slate-800/40 to-slate-900/40 border-sky-500/20 hover:border-sky-400/40 transition-all duration-300 ${index === 0 ? "ring-1 ring-sky-400/30" : ""}`}
              >
                <div className="flex items-start gap-3">
                  <div className={`p-2 rounded-lg border ${getRiskColor(event.risk)}`}>
                    <Icon className="w-4 h-4" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <h4 className="text-sky-100 text-xs font-medium truncate">{event.title}</h4>
                      <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium uppercase ${event.risk === "safe" ? "bg-emerald-400/20 text-emerald-300" : event.risk === "medium" ? "bg-amber-400/20 text-amber-300" : "bg-red-400/20 text-red-300"}`}>
                        {event.risk}
                      </span>
                    </div>
                    <p className="text-sky-400/70 text-xs mb-1 truncate">{event.description}</p>
                    <div className="flex items-center gap-2">
                      <span className="text-sky-500 text-[10px] font-mono">{formatTime(event.timestamp)}</span>
                      {event.risk === "safe" && <CheckCircle2 className="w-3 h-3 text-emerald-400" />}
                    </div>
                  </div>
                </div>
              </motion.div>
            );
          })}
        </AnimatePresence>
      </div>
    </div>
  );
}
