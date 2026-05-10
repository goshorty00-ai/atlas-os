import { MessageSquareText, Plus, Edit, Mic } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const commands = [
  {
    name: "Goodnight",
    trigger: "goodnight",
    actions: ["Turn off all lights", "Lock doors", "Arm security", "Set thermostat to 20°C"],
    type: "voice",
    icon: "🌙",
  },
  {
    name: "I'm Home",
    trigger: "I'm home",
    actions: ["Unlock door", "Turn on entry lights", "Disarm security", "Set AC to 22°C"],
    type: "voice",
    icon: "🏠",
  },
  {
    name: "Movie Time",
    trigger: "movie time",
    actions: ["Dim lights to 20%", "Close blinds", "Turn on TV", "Mute notifications"],
    type: "voice",
    icon: "🎬",
  },
  {
    name: "Party Mode",
    trigger: "party mode",
    actions: ["Colorful lights", "Max brightness", "Play party playlist", "Lock bedroom doors"],
    type: "voice",
    icon: "🎉",
  },
  {
    name: "Focus Mode",
    trigger: "focus mode",
    actions: ["Bright white lights", "Do not disturb on", "Close office door", "Mute speaker"],
    type: "text",
    icon: "🎯",
  },
  {
    name: "Cooking Mode",
    trigger: "cooking mode",
    actions: ["Kitchen lights 100%", "Turn on exhaust fan", "Play cooking playlist"],
    type: "voice",
    icon: "🍳",
  },
];

export function CustomCommands() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Custom Commands
            </h1>
            <p className="text-gray-400">Create personalized voice and text commands</p>
          </div>
          <Button className="bg-cyan-600 hover:bg-cyan-700">
            <Plus className="w-4 h-4 mr-2" />
            New Command
          </Button>
        </div>

        {/* Voice Command Input */}
        <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-8 mb-8">
          <div className="flex items-center gap-3 mb-4">
            <Mic className="w-6 h-6 text-purple-400" />
            <h2 className="text-xl font-semibold text-purple-400">Try a Command</h2>
          </div>
          <div className="flex gap-2">
            <input
              type="text"
              placeholder='Say or type "goodnight" to test...'
              className="flex-1 px-4 py-3 bg-gray-900/50 border border-gray-800 rounded-lg text-gray-100 placeholder-gray-500"
            />
            <Button className="bg-purple-600 hover:bg-purple-700 px-8">
              <Mic className="w-5 h-5" />
            </Button>
          </div>
          <div className="mt-4 text-sm text-gray-400">
            Tip: Commands work with Google Assistant, Alexa, and Siri
          </div>
        </div>

        {/* Commands List */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {commands.map((command) => (
            <div
              key={command.name}
              className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all"
            >
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-center gap-3">
                  <div className="text-3xl">{command.icon}</div>
                  <div>
                    <h3 className="font-semibold text-gray-200">{command.name}</h3>
                    <Badge
                      className={`mt-1 ${
                        command.type === "voice"
                          ? "bg-cyan-500/20 text-cyan-400 border-cyan-500/30"
                          : "bg-purple-500/20 text-purple-400 border-purple-500/30"
                      }`}
                    >
                      {command.type === "voice" ? <Mic className="w-3 h-3 mr-1" /> : <MessageSquareText className="w-3 h-3 mr-1" />}
                      {command.type}
                    </Badge>
                  </div>
                </div>
              </div>

              <div className="mb-4 p-3 bg-gray-900/50 rounded-lg border border-gray-800">
                <div className="text-xs text-gray-500 mb-1">Trigger phrase:</div>
                <div className="text-sm text-cyan-400 font-mono">"{command.trigger}"</div>
              </div>

              <div className="mb-4">
                <div className="text-xs text-gray-500 mb-2">Actions:</div>
                <div className="space-y-1">
                  {command.actions.map((action, index) => (
                    <div key={index} className="text-sm text-gray-400 flex items-center gap-2">
                      <div className="w-1.5 h-1.5 rounded-full bg-cyan-500" />
                      {action}
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex gap-2">
                <Button size="sm" variant="outline" className="flex-1 border-gray-700">
                  <Edit className="w-3 h-3 mr-1" />
                  Edit
                </Button>
                <Button size="sm" variant="outline" className="border-gray-700">
                  Test
                </Button>
              </div>
            </div>
          ))}
        </div>

        {/* Command Templates */}
        <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 mt-8">
          <h2 className="text-xl font-semibold text-cyan-400 mb-4">Popular Command Templates</h2>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800 text-center">
              <div className="text-2xl mb-2">☀️</div>
              <div className="font-medium text-gray-200 mb-1">Morning</div>
              <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700 mt-2">
                Use Template
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800 text-center">
              <div className="text-2xl mb-2">🚪</div>
              <div className="font-medium text-gray-200 mb-1">Leaving</div>
              <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700 mt-2">
                Use Template
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800 text-center">
              <div className="text-2xl mb-2">🛋️</div>
              <div className="font-medium text-gray-200 mb-1">Relax</div>
              <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700 mt-2">
                Use Template
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800 text-center">
              <div className="text-2xl mb-2">🚨</div>
              <div className="font-medium text-gray-200 mb-1">Emergency</div>
              <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700 mt-2">
                Use Template
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
