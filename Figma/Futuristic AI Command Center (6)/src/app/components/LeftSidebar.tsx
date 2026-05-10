import { MessageSquare, Film, Music, Download, Shield, Sparkles, Code, Settings, ChevronLeft } from "lucide-react";
import { motion } from "motion/react";
import React from "react";

interface LeftSidebarProps {
  activeTab: string;
  onTabChange: (tab: string) => void;
  onClose: () => void;
}

const navItems = [
  { id: "AI Chat", icon: MessageSquare, label: "AI Chat" },
  { id: "AI Media Centre", icon: Film, label: "AI Media Centre" },
  { id: "AI DJ Booth", icon: Music, label: "AI DJ Booth" },
  { id: "AI Downloads", icon: Download, label: "AI Downloads" },
  { id: "AI Security", icon: Shield, label: "AI Security" },
  { id: "AI Create", icon: Sparkles, label: "AI Create" },
  { id: "AI Code", icon: Code, label: "AI Code" },
  { id: "AI Settings", icon: Settings, label: "AI Settings" },
];

export function LeftSidebar({ activeTab, onTabChange, onClose }: LeftSidebarProps) {
  const mainItems = navItems.filter((i) => i.id !== "AI Settings");
  const settingsItem = navItems.find((i) => i.id === "AI Settings");

  return (
    <motion.div 
      initial={{ x: -50, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: -50, opacity: 0 }}
      transition={{ type: "spring", stiffness: 300, damping: 30 }}
      className="w-16 border-r border-cyan-500/10 bg-[#0f1419] flex flex-col items-center py-2 h-full shrink-0 relative z-50 pointer-events-auto"
    >
      <div className="flex flex-col items-center gap-2 mt-2">
        {mainItems.map((item) => (
          <motion.button
            key={item.id}
            onClick={() => onTabChange(item.id)}
            className={`w-12 h-12 flex items-center justify-center rounded-lg transition-colors ${
              activeTab === item.id
                ? "bg-cyan-500/20 text-cyan-400"
                : "text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10"
            }`}
            whileHover={{ scale: 1.03 }}
            whileTap={{ scale: 0.98 }}
            title={item.label}
          >
            <item.icon className="w-6 h-6" />
          </motion.button>
        ))}
      </div>

      <div className="flex-1" />

      <button
        onClick={onClose}
        className="w-12 h-12 flex items-center justify-center rounded-lg text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10 transition-colors"
        title="Hide sidebar"
      >
        <ChevronLeft className="w-6 h-6" />
      </button>

      <div className="w-full border-t border-cyan-500/10 mt-2 pt-2 flex items-center justify-center">
        {settingsItem && (
          <motion.button
            onClick={() => onTabChange(settingsItem.id)}
            className={`w-12 h-12 flex items-center justify-center rounded-lg transition-colors ${
              activeTab === settingsItem.id
                ? "bg-cyan-500/20 text-cyan-400"
                : "text-slate-500 hover:text-cyan-400 hover:bg-cyan-500/10"
            }`}
            whileHover={{ scale: 1.03 }}
            whileTap={{ scale: 0.98 }}
            title={settingsItem.label}
          >
            <settingsItem.icon className="w-6 h-6" />
          </motion.button>
        )}
      </div>
    </motion.div>
  );
}