import { motion } from "motion/react";
import { X } from "lucide-react";

interface DesignAnnotationsProps {
  onClose: () => void;
}

export function DesignAnnotations({ onClose }: DesignAnnotationsProps) {
  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-8"
      onClick={onClose}
    >
      <motion.div
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        onClick={(e) => e.stopPropagation()}
        className="bg-[#0b0f14] border border-cyan-500/30 rounded-2xl p-8 max-w-4xl w-full max-h-[90vh] overflow-y-auto shadow-[0_0_60px_rgba(34,211,238,0.2)]"
      >
        {/* Header */}
        <div className="flex items-center justify-between mb-6 pb-4 border-b border-cyan-500/20">
          <div>
            <h2 className="text-2xl font-mono text-cyan-400 tracking-wider">
              ATLAS AI DESIGN SYSTEM
            </h2>
            <p className="text-sm text-slate-400 mt-1">
              Production-Ready Futuristic Command Center
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-2 hover:bg-slate-800/50 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-slate-400" />
          </button>
        </div>

        {/* Content Grid */}
        <div className="grid grid-cols-2 gap-6">
          {/* Color Palette */}
          <div className="space-y-3">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Color Palette
            </h3>
            <div className="space-y-2 text-xs font-mono">
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-[#0b0f14] border border-slate-700" />
                <div>
                  <div className="text-slate-300">#0b0f14</div>
                  <div className="text-slate-500">Background</div>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-cyan-400 border border-cyan-500" />
                <div>
                  <div className="text-slate-300">#22d3ee</div>
                  <div className="text-slate-500">Primary (Cyan)</div>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-orange-400 border border-orange-500" />
                <div>
                  <div className="text-slate-300">#f97316</div>
                  <div className="text-slate-500">Accent (Thinking)</div>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-purple-400 border border-purple-500" />
                <div>
                  <div className="text-slate-300">Purple</div>
                  <div className="text-slate-500">Secondary</div>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <div className="w-8 h-8 rounded bg-slate-200 border border-slate-300" />
                <div>
                  <div className="text-slate-300">Slate 200</div>
                  <div className="text-slate-500">Text (Off-white)</div>
                </div>
              </div>
            </div>
          </div>

          {/* Motion System */}
          <div className="space-y-3">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Motion System
            </h3>
            <div className="space-y-2 text-xs font-mono text-slate-300">
              <div>
                <div className="text-cyan-400">IDLE State:</div>
                <div className="text-slate-400 ml-2">
                  • Gentle breathing (2s cycle)
                </div>
                <div className="text-slate-400 ml-2">
                  • Particle floating motion
                </div>
                <div className="text-slate-400 ml-2">• easeInOut easing</div>
              </div>
              <div>
                <div className="text-orange-400">THINKING State:</div>
                <div className="text-slate-400 ml-2">
                  • Particles gather inward
                </div>
                <div className="text-slate-400 ml-2">
                  • Orange accent core appears
                </div>
                <div className="text-slate-400 ml-2">• Faster pulse (1.5s)</div>
              </div>
              <div>
                <div className="text-purple-400">SPEAKING State:</div>
                <div className="text-slate-400 ml-2">• Rapid pulse effect</div>
                <div className="text-slate-400 ml-2">
                  • Increased opacity variation
                </div>
              </div>
            </div>
          </div>

          {/* Layout Structure */}
          <div className="space-y-3">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Layout Structure
            </h3>
            <div className="space-y-2 text-xs font-mono text-slate-300">
              <div>
                <div className="text-cyan-400">Top Nav (48px):</div>
                <div className="text-slate-400 ml-2">• Centered tab bar</div>
                <div className="text-slate-400 ml-2">
                  • Date/time + window controls
                </div>
              </div>
              <div>
                <div className="text-cyan-400">Left Sidebar (64px):</div>
                <div className="text-slate-400 ml-2">• Icon-only vertical</div>
                <div className="text-slate-400 ml-2">
                  • Cyan accent on active
                </div>
              </div>
              <div>
                <div className="text-cyan-400">Chat Area (Flex 1):</div>
                <div className="text-slate-400 ml-2">
                  • Message cards with glow
                </div>
                <div className="text-slate-400 ml-2">• Voice input indicator</div>
              </div>
              <div>
                <div className="text-cyan-400">HoloCore (Flex 1):</div>
                <div className="text-slate-400 ml-2">• Particle orb system</div>
                <div className="text-slate-400 ml-2">• Orbiting action icons</div>
              </div>
            </div>
          </div>

          {/* Typography */}
          <div className="space-y-3">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Typography
            </h3>
            <div className="space-y-2 text-xs">
              <div>
                <div className="font-mono text-cyan-400">Monospace Font:</div>
                <div className="text-slate-400 ml-2">
                  • Status labels, code, timestamps
                </div>
                <div className="text-slate-400 ml-2">
                  • Uppercase + tracking-wider
                </div>
              </div>
              <div>
                <div className="font-sans text-cyan-400">Sans-serif Font:</div>
                <div className="text-slate-400 ml-2">
                  • Body text, user input
                </div>
                <div className="text-slate-400 ml-2">• Clean hierarchy</div>
              </div>
              <div className="text-slate-500 mt-2">
                No pure white (#fff) - always slightly off-white
              </div>
            </div>
          </div>

          {/* Message Copy Guidelines */}
          <div className="space-y-3">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Message Copy
            </h3>
            <div className="space-y-2 text-xs">
              <div className="bg-cyan-500/10 border border-cyan-500/30 p-2 rounded">
                <div className="text-cyan-400 font-mono mb-1">
                  STATUS CONSOLE, NOT CHAT
                </div>
                <div className="text-slate-400">
                  • Brief, authoritative responses
                </div>
                <div className="text-slate-400">• No casual language</div>
                <div className="text-slate-400">• No filler phrases</div>
              </div>
              <div className="font-mono text-[10px] space-y-1 text-slate-400">
                <div className="text-green-400">✓ "SYSTEM INITIALIZED"</div>
                <div className="text-green-400">
                  ✓ "DIAGNOSTICS COMPLETE"
                </div>
                <div className="text-green-400">
                  ✓ "CPU 23% · MEMORY NOMINAL"
                </div>
                <div className="text-red-400 mt-2">
                  ✗ "Good evening, how may I assist?"
                </div>
                <div className="text-red-400">✗ "Let me help you with that"</div>
              </div>
            </div>
          </div>

          {/* Component Details */}
          <div className="col-span-2 space-y-3 pt-4 border-t border-cyan-500/20">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Key Components
            </h3>
            <div className="grid grid-cols-3 gap-4 text-xs font-mono">
              <div className="bg-slate-900/30 p-3 rounded border border-slate-700/50">
                <div className="text-cyan-400 mb-2">HoloCore Orb</div>
                <div className="text-slate-400 space-y-1">
                  <div>• 60 dynamic particles</div>
                  <div>• 2 rotating rings</div>
                  <div>• Central core with glow</div>
                  <div>• 6 orbiting action icons</div>
                </div>
              </div>
              <div className="bg-slate-900/30 p-3 rounded border border-slate-700/50">
                <div className="text-cyan-400 mb-2">Message Cards</div>
                <div className="text-slate-400 space-y-1">
                  <div>• Soft rounded corners</div>
                  <div>• Cyan outline + glow</div>
                  <div>• Sender color coding</div>
                  <div>• Timestamp metadata</div>
                </div>
              </div>
              <div className="bg-slate-900/30 p-3 rounded border border-slate-700/50">
                <div className="text-cyan-400 mb-2">Floating HUD</div>
                <div className="text-slate-400 space-y-1">
                  <div>• Mini particle orb</div>
                  <div>• Status indicators</div>
                  <div>• Draggable anywhere</div>
                  <div>• Always-on-top</div>
                </div>
              </div>
            </div>
          </div>

          {/* Implementation Notes */}
          <div className="col-span-2 space-y-3 pt-4 border-t border-cyan-500/20">
            <h3 className="text-sm font-mono text-orange-400 uppercase tracking-wider">
              Implementation Notes
            </h3>
            <div className="text-xs text-slate-300 space-y-2">
              <div className="bg-slate-900/30 p-3 rounded border border-cyan-500/20">
                <div className="text-cyan-400 mb-1">✓ WPF-Compatible</div>
                <div className="text-slate-400">
                  All effects use standard CSS transforms, opacity, and filters.
                  No WebGL or shaders required.
                </div>
              </div>
              <div className="bg-slate-900/30 p-3 rounded border border-cyan-500/20">
                <div className="text-cyan-400 mb-1">✓ Performance</div>
                <div className="text-slate-400">
                  Animations use CSS transforms (GPU-accelerated). Particle
                  count optimized for smooth 60fps.
                </div>
              </div>
              <div className="bg-slate-900/30 p-3 rounded border border-cyan-500/20">
                <div className="text-cyan-400 mb-1">✓ Minimal & Calm</div>
                <div className="text-slate-400">
                  No excessive gradients, no neon overload. Everything is
                  purposeful and restrained.
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="mt-6 pt-4 border-t border-cyan-500/20 text-center">
          <div className="text-xs font-mono text-slate-500">
            Designed for production · Built with React + Motion + Tailwind CSS
          </div>
        </div>
      </motion.div>
    </motion.div>
  );
}