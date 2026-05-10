import { Clock, MapPin, Cloud, Plus, Edit, Play } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Switch } from "../components/ui/switch";

const automations = [
  {
    name: "Good Morning Routine",
    trigger: "7:00 AM weekdays",
    actions: ["Turn on bedroom lights", "Start coffee maker", "Open living room blinds"],
    status: "active",
    icon: "🌅",
  },
  {
    name: "Away Mode",
    trigger: "When everyone leaves",
    actions: ["Turn off all lights", "Lock doors", "Arm security", "Set thermostat to eco"],
    status: "active",
    icon: "✈️",
  },
  {
    name: "Sunset Lighting",
    trigger: "At sunset",
    actions: ["Turn on outdoor lights", "Dim indoor lights to 60%"],
    status: "active",
    icon: "🌆",
  },
  {
    name: "Movie Time",
    trigger: "When TV turns on after 6 PM",
    actions: ["Dim living room lights", "Close blinds", "Pause music"],
    status: "inactive",
    icon: "🎬",
  },
  {
    name: "Security Alert",
    trigger: "Motion detected at night",
    actions: ["Turn on all lights", "Send notification", "Start recording cameras"],
    status: "active",
    icon: "🚨",
  },
  {
    name: "Bedtime",
    trigger: "10:30 PM daily",
    actions: ["Turn off all lights except bedroom", "Lock doors", "Lower thermostat"],
    status: "active",
    icon: "🌙",
  },
];

const triggers = [
  { type: "Time", icon: Clock, count: 8 },
  { type: "Location", icon: MapPin, count: 3 },
  { type: "Weather", icon: Cloud, count: 2 },
];

export function Automations() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Automations
            </h1>
            <p className="text-gray-400">{automations.length} automations configured</p>
          </div>
          <Button className="bg-cyan-600 hover:bg-cyan-700">
            <Plus className="w-4 h-4 mr-2" />
            New Automation
          </Button>
        </div>

        {/* Trigger Types Summary */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
          {triggers.map((trigger) => {
            const Icon = trigger.icon;
            return (
              <div
                key={trigger.type}
                className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all"
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Icon className="w-6 h-6 text-cyan-400" />
                    <div>
                      <div className="font-semibold text-gray-200">{trigger.type} Based</div>
                      <div className="text-sm text-gray-500">{trigger.count} automations</div>
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Automations List */}
        <div className="space-y-4">
          {automations.map((automation) => (
            <div
              key={automation.name}
              className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all"
            >
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-start gap-4">
                  <div className="text-3xl">{automation.icon}</div>
                  <div>
                    <h3 className="text-xl font-semibold text-gray-200 mb-1">
                      {automation.name}
                    </h3>
                    <div className="flex items-center gap-2 text-sm text-gray-400 mb-3">
                      <Clock className="w-4 h-4" />
                      <span>{automation.trigger}</span>
                    </div>
                    <div className="space-y-1">
                      {automation.actions.map((action, index) => (
                        <div key={index} className="text-sm text-gray-400 flex items-center gap-2">
                          <div className="w-1.5 h-1.5 rounded-full bg-cyan-500" />
                          {action}
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <Badge
                    className={
                      automation.status === "active"
                        ? "bg-green-500/20 text-green-400 border-green-500/30"
                        : "bg-gray-700/20 text-gray-400 border-gray-700/30"
                    }
                  >
                    {automation.status}
                  </Badge>
                  <Switch checked={automation.status === "active"} />
                </div>
              </div>

              <div className="flex gap-2 pt-4 border-t border-gray-800">
                <Button size="sm" variant="outline" className="border-gray-700">
                  <Play className="w-3 h-3 mr-1" />
                  Test Run
                </Button>
                <Button size="sm" variant="outline" className="border-gray-700">
                  <Edit className="w-3 h-3 mr-1" />
                  Edit
                </Button>
                <Button size="sm" variant="outline" className="border-gray-700 text-red-400">
                  Delete
                </Button>
              </div>
            </div>
          ))}
        </div>

        {/* AI Suggestions */}
        <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6 mt-8">
          <h2 className="text-xl font-semibold text-purple-400 mb-4">AI Suggested Automations</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Energy Saver</div>
              <div className="text-sm text-gray-400 mb-3">
                Turn off devices when electricity prices are high
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Create
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Rainy Day</div>
              <div className="text-sm text-gray-400 mb-3">
                Close windows and adjust AC when it starts raining
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Create
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Welcome Home</div>
              <div className="text-sm text-gray-400 mb-3">
                Turn on lights and adjust temperature 10 min before arrival
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Create
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
