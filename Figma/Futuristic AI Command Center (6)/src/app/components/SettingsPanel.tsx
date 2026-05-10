import { useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import {
  Settings,
  ChevronDown,
  ChevronRight,
  Monitor,
  Volume2,
  Palette,
  Zap,
  Shield,
  Database,
  Network,
  Bell,
  User,
  Globe,
  Lock,
  Eye,
  Moon,
  Sun,
  Cpu,
  HardDrive,
  Wifi,
  Bluetooth,
  Save,
  RotateCcw,
  Check,
} from "lucide-react";

interface SettingSection {
  id: string;
  title: string;
  icon: any;
  description: string;
  expanded: boolean;
}

export function SettingsPanel() {
  const [sections, setSections] = useState<SettingSection[]>([
    {
      id: "appearance",
      title: "Appearance",
      icon: Palette,
      description: "Customize visual theme and interface settings",
      expanded: true,
    },
    {
      id: "audio",
      title: "Audio & Sound",
      icon: Volume2,
      description: "Configure audio output and sound preferences",
      expanded: false,
    },
    {
      id: "performance",
      title: "Performance",
      icon: Zap,
      description: "Optimize system performance and resource usage",
      expanded: false,
    },
    {
      id: "security",
      title: "Security & Privacy",
      icon: Shield,
      description: "Manage security settings and privacy controls",
      expanded: false,
    },
    {
      id: "ai",
      title: "AI Features",
      icon: Cpu,
      description: "Configure AI assistant behavior and capabilities",
      expanded: false,
    },
    {
      id: "network",
      title: "Network & Connectivity",
      icon: Network,
      description: "Network, Wi-Fi, and connection settings",
      expanded: false,
    },
    {
      id: "notifications",
      title: "Notifications",
      icon: Bell,
      description: "Manage alerts and notification preferences",
      expanded: false,
    },
    {
      id: "storage",
      title: "Storage & Data",
      icon: Database,
      description: "Storage management and data sync settings",
      expanded: false,
    },
    {
      id: "user",
      title: "User Preferences",
      icon: User,
      description: "Personal settings and user profile",
      expanded: false,
    },
    {
      id: "advanced",
      title: "Advanced Settings",
      icon: Settings,
      description: "Developer options and advanced configuration",
      expanded: false,
    },
  ]);

  // Appearance Settings
  const [theme, setTheme] = useState<"dark" | "light" | "auto">("dark");
  const [accentColor, setAccentColor] = useState("cyan");
  const [animationsEnabled, setAnimationsEnabled] = useState(true);
  const [blurEffects, setBlurEffects] = useState(true);
  const [glowEffects, setGlowEffects] = useState(true);
  const [fontSize, setFontSize] = useState(14);
  const [transparency, setTransparency] = useState(80);

  // Audio Settings
  const [masterVolume, setMasterVolume] = useState(80);
  const [notificationSounds, setNotificationSounds] = useState(true);
  const [voiceFeedback, setVoiceFeedback] = useState(false);
  const [audioOutput, setAudioOutput] = useState("default");

  // Performance Settings
  const [hardwareAcceleration, setHardwareAcceleration] = useState(true);
  const [lowPowerMode, setLowPowerMode] = useState(false);
  const [autoOptimize, setAutoOptimize] = useState(true);
  const [maxCpuUsage, setMaxCpuUsage] = useState(75);

  // Security Settings
  const [autoLock, setAutoLock] = useState(true);
  const [lockTimeout, setLockTimeout] = useState(15);
  const [encryptData, setEncryptData] = useState(true);
  const [allowAnalytics, setAllowAnalytics] = useState(false);

  const [showSaveNotification, setShowSaveNotification] = useState(false);

  const toggleSection = (sectionId: string) => {
    setSections(
      sections.map((section) =>
        section.id === sectionId
          ? { ...section, expanded: !section.expanded }
          : section
      )
    );
  };

  const handleSave = () => {
    setShowSaveNotification(true);
    setTimeout(() => setShowSaveNotification(false), 3000);
  };

  const handleReset = () => {
    // Reset to defaults
    setTheme("dark");
    setAccentColor("cyan");
    setAnimationsEnabled(true);
    setBlurEffects(true);
    setGlowEffects(true);
    setFontSize(14);
    setTransparency(80);
    setMasterVolume(80);
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-[#0b0f14]/20">
      {/* Header */}
      <div className="p-4 border-b border-cyan-500/10">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <motion.div
              animate={{ rotate: [0, 180, 360] }}
              transition={{ duration: 20, repeat: Infinity, ease: "linear" }}
            >
              <Settings className="w-8 h-8 text-cyan-400" />
            </motion.div>
            <div>
              <h2 className="text-xl font-mono tracking-wider text-cyan-400">
                SYSTEM SETTINGS
              </h2>
              <p className="text-xs text-slate-500 font-mono mt-1">
                CONFIGURE ATLAS AI · ALL MODULES AND PREFERENCES
              </p>
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-3">
            <motion.button
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              onClick={handleReset}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border border-slate-700/50 bg-slate-800/50 hover:border-orange-500/50 text-slate-400 hover:text-orange-400 transition-all"
            >
              <RotateCcw className="w-4 h-4" />
              <span className="text-xs font-mono uppercase">Reset</span>
            </motion.button>

            <motion.button
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              onClick={handleSave}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border border-cyan-500/50 bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 transition-all shadow-[0_0_20px_rgba(34,211,238,0.3)]"
            >
              <Save className="w-4 h-4" />
              <span className="text-xs font-mono uppercase">Save Changes</span>
            </motion.button>
          </div>
        </div>
      </div>

      {/* Save Notification */}
      <AnimatePresence>
        {showSaveNotification && (
          <motion.div
            initial={{ opacity: 0, y: -20 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -20 }}
            className="absolute top-24 right-4 flex items-center gap-3 px-4 py-3 bg-green-500/20 border border-green-500/50 rounded-lg shadow-[0_0_30px_rgba(34,197,94,0.4)] z-50"
          >
            <Check className="w-5 h-5 text-green-400" />
            <span className="text-sm font-mono text-green-400">
              Settings saved successfully
            </span>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Settings Content */}
      <div className="flex-1 overflow-y-auto scrollbar-thin scrollbar-thumb-cyan-500/20 scrollbar-track-transparent p-4">
        <div className="max-w-5xl mx-auto space-y-3">
          {sections.map((section) => {
            const Icon = section.icon;
            return (
              <div
                key={section.id}
                className="bg-[#0b0f14]/60 border border-cyan-500/10 rounded-2xl overflow-hidden shadow-[0_0_30px_rgba(34,211,238,0.05)]"
              >
                {/* Section Header */}
                <motion.button
                  onClick={() => toggleSection(section.id)}
                  className="w-full p-4 flex items-center justify-between hover:bg-cyan-500/5 transition-all"
                  whileHover={{ scale: 1.002 }}
                  whileTap={{ scale: 0.998 }}
                >
                  <div className="flex items-center gap-4">
                    <div className="p-2 rounded-lg bg-cyan-500/10 border border-cyan-500/20">
                      <Icon className="w-5 h-5 text-cyan-400" />
                    </div>
                    <div className="text-left">
                      <h3 className="text-base font-mono font-bold text-cyan-400 tracking-wide">
                        {section.title}
                      </h3>
                      <p className="text-xs text-slate-500 font-mono mt-0.5">
                        {section.description}
                      </p>
                    </div>
                  </div>
                  <motion.div
                    animate={{ rotate: section.expanded ? 180 : 0 }}
                    transition={{ duration: 0.3 }}
                  >
                    <ChevronDown className="w-5 h-5 text-slate-400" />
                  </motion.div>
                </motion.button>

                {/* Section Content */}
                <AnimatePresence>
                  {section.expanded && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: "auto", opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      transition={{ duration: 0.3 }}
                      className="border-t border-cyan-500/10"
                    >
                      <div className="p-6 space-y-6">
                        {/* APPEARANCE SECTION */}
                        {section.id === "appearance" && (
                          <>
                            {/* Theme */}
                            <div>
                              <label className="block text-sm font-mono text-slate-400 mb-2 uppercase tracking-wide">
                                Theme Mode
                              </label>
                              <div className="grid grid-cols-3 gap-3">
                                {[
                                  { value: "dark", icon: Moon, label: "Dark" },
                                  { value: "light", icon: Sun, label: "Light" },
                                  { value: "auto", icon: Monitor, label: "Auto" },
                                ].map((option) => {
                                  const ThemeIcon = option.icon;
                                  return (
                                    <motion.button
                                      key={option.value}
                                      whileHover={{ scale: 1.02 }}
                                      whileTap={{ scale: 0.98 }}
                                      onClick={() => setTheme(option.value as any)}
                                      className={`p-3 rounded-lg border transition-all ${
                                        theme === option.value
                                          ? "bg-cyan-500/20 border-cyan-500/50 text-cyan-400 shadow-[0_0_15px_rgba(34,211,238,0.3)]"
                                          : "bg-slate-900/50 border-slate-700/50 text-slate-400 hover:border-cyan-500/30"
                                      }`}
                                    >
                                      <ThemeIcon className="w-5 h-5 mx-auto mb-1" />
                                      <div className="text-xs font-mono uppercase">
                                        {option.label}
                                      </div>
                                    </motion.button>
                                  );
                                })}
                              </div>
                            </div>

                            {/* Accent Color */}
                            <div>
                              <label className="block text-sm font-mono text-slate-400 mb-2 uppercase tracking-wide">
                                Accent Color
                              </label>
                              <div className="grid grid-cols-6 gap-2">
                                {[
                                  { name: "cyan", color: "#22D3EE" },
                                  { name: "blue", color: "#3B82F6" },
                                  { name: "purple", color: "#A855F7" },
                                  { name: "orange", color: "#F97316" },
                                  { name: "green", color: "#22C55E" },
                                  { name: "pink", color: "#EC4899" },
                                ].map((color) => (
                                  <motion.button
                                    key={color.name}
                                    whileHover={{ scale: 1.1 }}
                                    whileTap={{ scale: 0.9 }}
                                    onClick={() => setAccentColor(color.name)}
                                    className={`w-12 h-12 rounded-lg border-2 transition-all ${
                                      accentColor === color.name
                                        ? "border-white shadow-[0_0_20px_rgba(255,255,255,0.5)]"
                                        : "border-transparent"
                                    }`}
                                    style={{ backgroundColor: color.color }}
                                  />
                                ))}
                              </div>
                            </div>

                            {/* Toggles */}
                            <div className="space-y-4">
                              <ToggleSetting
                                label="Animations Enabled"
                                description="Enable smooth UI animations and transitions"
                                value={animationsEnabled}
                                onChange={setAnimationsEnabled}
                              />
                              <ToggleSetting
                                label="Blur Effects"
                                description="Enable backdrop blur effects on modals and overlays"
                                value={blurEffects}
                                onChange={setBlurEffects}
                              />
                              <ToggleSetting
                                label="Glow Effects"
                                description="Enable neon glow effects on active elements"
                                value={glowEffects}
                                onChange={setGlowEffects}
                              />
                            </div>

                            {/* Sliders */}
                            <div className="space-y-4">
                              <SliderSetting
                                label="Font Size"
                                value={fontSize}
                                onChange={setFontSize}
                                min={10}
                                max={20}
                                unit="px"
                                color="cyan"
                              />
                              <SliderSetting
                                label="UI Transparency"
                                value={transparency}
                                onChange={setTransparency}
                                min={0}
                                max={100}
                                unit="%"
                                color="cyan"
                              />
                            </div>
                          </>
                        )}

                        {/* AUDIO SECTION */}
                        {section.id === "audio" && (
                          <>
                            <SliderSetting
                              label="Master Volume"
                              value={masterVolume}
                              onChange={setMasterVolume}
                              min={0}
                              max={100}
                              unit="%"
                              color="orange"
                            />

                            <DropdownSetting
                              label="Audio Output Device"
                              value={audioOutput}
                              onChange={setAudioOutput}
                              options={[
                                { value: "default", label: "Default System Output" },
                                { value: "speakers", label: "Speakers (HD Audio)" },
                                { value: "headphones", label: "Headphones (USB)" },
                                { value: "bluetooth", label: "Bluetooth Audio" },
                              ]}
                            />

                            <ToggleSetting
                              label="Notification Sounds"
                              description="Play sound effects for system notifications"
                              value={notificationSounds}
                              onChange={setNotificationSounds}
                            />

                            <ToggleSetting
                              label="Voice Feedback"
                              description="Enable AI voice responses and audio feedback"
                              value={voiceFeedback}
                              onChange={setVoiceFeedback}
                            />

                            {/* Placeholder for custom settings */}
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add custom audio settings here
                              </p>
                            </div>
                          </>
                        )}

                        {/* PERFORMANCE SECTION */}
                        {section.id === "performance" && (
                          <>
                            <ToggleSetting
                              label="Hardware Acceleration"
                              description="Use GPU for rendering and computations"
                              value={hardwareAcceleration}
                              onChange={setHardwareAcceleration}
                            />

                            <ToggleSetting
                              label="Low Power Mode"
                              description="Reduce performance to save battery"
                              value={lowPowerMode}
                              onChange={setLowPowerMode}
                            />

                            <ToggleSetting
                              label="Auto-Optimize"
                              description="Automatically optimize performance based on usage"
                              value={autoOptimize}
                              onChange={setAutoOptimize}
                            />

                            <SliderSetting
                              label="Maximum CPU Usage"
                              value={maxCpuUsage}
                              onChange={setMaxCpuUsage}
                              min={25}
                              max={100}
                              unit="%"
                              color="green"
                            />

                            {/* Placeholder for custom settings */}
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add custom performance settings here
                              </p>
                            </div>
                          </>
                        )}

                        {/* SECURITY SECTION */}
                        {section.id === "security" && (
                          <>
                            <ToggleSetting
                              label="Auto-Lock"
                              description="Automatically lock application when idle"
                              value={autoLock}
                              onChange={setAutoLock}
                            />

                            <SliderSetting
                              label="Lock Timeout"
                              value={lockTimeout}
                              onChange={setLockTimeout}
                              min={1}
                              max={60}
                              unit="min"
                              color="purple"
                            />

                            <ToggleSetting
                              label="Encrypt User Data"
                              description="Encrypt all stored user data and settings"
                              value={encryptData}
                              onChange={setEncryptData}
                            />

                            <ToggleSetting
                              label="Allow Analytics"
                              description="Send anonymous usage data to improve the application"
                              value={allowAnalytics}
                              onChange={setAllowAnalytics}
                            />

                            {/* Placeholder for custom settings */}
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add custom security settings here
                              </p>
                            </div>
                          </>
                        )}

                        {/* AI FEATURES SECTION */}
                        {section.id === "ai" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <div className="flex items-center gap-3 mb-2">
                                <Cpu className="w-5 h-5 text-cyan-400" />
                                <p className="text-sm text-cyan-400 font-mono font-bold">
                                  AI Model Configuration
                                </p>
                              </div>
                              <p className="text-xs text-slate-500 font-mono">
                                + Configure AI model, temperature, max tokens, etc.
                              </p>
                            </div>

                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add AI behavior settings here
                              </p>
                            </div>
                          </div>
                        )}

                        {/* NETWORK SECTION */}
                        {section.id === "network" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <div className="flex items-center gap-3 mb-2">
                                <Wifi className="w-5 h-5 text-cyan-400" />
                                <p className="text-sm text-cyan-400 font-mono font-bold">
                                  Wi-Fi Settings
                                </p>
                              </div>
                              <p className="text-xs text-slate-500 font-mono">
                                + Configure network connections and preferences
                              </p>
                            </div>

                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <div className="flex items-center gap-3 mb-2">
                                <Bluetooth className="w-5 h-5 text-cyan-400" />
                                <p className="text-sm text-cyan-400 font-mono font-bold">
                                  Bluetooth Devices
                                </p>
                              </div>
                              <p className="text-xs text-slate-500 font-mono">
                                + Manage paired devices and connections
                              </p>
                            </div>
                          </div>
                        )}

                        {/* NOTIFICATIONS SECTION */}
                        {section.id === "notifications" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add notification preferences here
                              </p>
                            </div>
                          </div>
                        )}

                        {/* STORAGE SECTION */}
                        {section.id === "storage" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <div className="flex items-center gap-3 mb-2">
                                <HardDrive className="w-5 h-5 text-cyan-400" />
                                <p className="text-sm text-cyan-400 font-mono font-bold">
                                  Storage Usage
                                </p>
                              </div>
                              <p className="text-xs text-slate-500 font-mono">
                                + Display storage usage stats and management
                              </p>
                            </div>
                          </div>
                        )}

                        {/* USER PREFERENCES SECTION */}
                        {section.id === "user" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add user profile and preference settings here
                              </p>
                            </div>
                          </div>
                        )}

                        {/* ADVANCED SECTION */}
                        {section.id === "advanced" && (
                          <div className="space-y-4">
                            <div className="p-4 border border-dashed border-slate-700/50 rounded-lg">
                              <p className="text-xs text-slate-500 font-mono text-center">
                                + Add developer options and advanced settings here
                              </p>
                            </div>
                          </div>
                        )}
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

// Reusable Toggle Setting Component
function ToggleSetting({
  label,
  description,
  value,
  onChange,
}: {
  label: string;
  description: string;
  value: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between p-4 rounded-lg bg-[#0f1419] border border-slate-800/50 hover:border-cyan-500/20 transition-all">
      <div className="flex-1">
        <div className="text-sm font-mono text-slate-300">{label}</div>
        <div className="text-xs text-slate-500 mt-1">{description}</div>
      </div>
      <motion.button
        onClick={() => onChange(!value)}
        className={`relative w-14 h-7 rounded-full transition-all ${
          value
            ? "bg-cyan-500 shadow-[0_0_20px_rgba(34,211,238,0.5)]"
            : "bg-slate-700"
        }`}
        whileTap={{ scale: 0.95 }}
      >
        <motion.div
          className="absolute top-1 w-5 h-5 bg-white rounded-full shadow-lg"
          animate={{ left: value ? "30px" : "4px" }}
          transition={{ type: "spring", stiffness: 500, damping: 30 }}
        />
      </motion.button>
    </div>
  );
}

// Reusable Slider Setting Component
function SliderSetting({
  label,
  value,
  onChange,
  min,
  max,
  unit,
  color,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  min: number;
  max: number;
  unit: string;
  color: "cyan" | "orange" | "green" | "purple";
}) {
  const colors = {
    cyan: "bg-cyan-500",
    orange: "bg-orange-500",
    green: "bg-green-500",
    purple: "bg-purple-500",
  };

  return (
    <div className="p-4 rounded-lg bg-[#0f1419] border border-slate-800/50 hover:border-cyan-500/20 transition-all">
      <div className="flex items-center justify-between mb-3">
        <span className="text-sm font-mono text-slate-300">{label}</span>
        <span className="text-sm font-mono text-cyan-400 font-bold">
          {value}
          {unit}
        </span>
      </div>
      <div className="relative">
        <div className="h-2 bg-slate-900 rounded-full overflow-hidden">
          <div
            className={`h-full ${colors[color]} shadow-[0_0_15px_rgba(34,211,238,0.6)] transition-all`}
            style={{ width: `${((value - min) / (max - min)) * 100}%` }}
          />
        </div>
        <input
          type="range"
          min={min}
          max={max}
          value={value}
          onChange={(e) => onChange(parseInt(e.target.value))}
          className="absolute inset-0 w-full opacity-0 cursor-pointer"
        />
      </div>
    </div>
  );
}

// Reusable Dropdown Setting Component
function DropdownSetting({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
}) {
  return (
    <div className="p-4 rounded-lg bg-[#0f1419] border border-slate-800/50 hover:border-cyan-500/20 transition-all">
      <label className="block text-sm font-mono text-slate-300 mb-2">
        {label}
      </label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full bg-slate-900 border border-slate-700 rounded-lg px-4 py-2 text-sm text-slate-200 font-mono outline-none focus:border-cyan-500/50 transition-all"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
