import { useState } from "react";
import { Sparkles, Play, Save, Lightbulb, Thermometer, Lock, Music } from "lucide-react";
import { Button } from "../components/ui/button";
import { Textarea } from "../components/ui/textarea";
import { Badge } from "../components/ui/badge";

const presetScenes = [
  {
    name: "Good Morning",
    description: "Lights on, blinds open, coffee maker start",
    icon: "🌅",
    devices: 8,
  },
  {
    name: "Movie Time",
    description: "Dim lights, close blinds, TV on",
    icon: "🎬",
    devices: 5,
  },
  {
    name: "Bedtime",
    description: "All lights off, lock doors, arm security",
    icon: "🌙",
    devices: 12,
  },
  {
    name: "Party Mode",
    description: "Colorful lights, music on, adjust AC",
    icon: "🎉",
    devices: 7,
  },
  {
    name: "Away Mode",
    description: "All off, lock everything, security armed",
    icon: "✈️",
    devices: 15,
  },
  {
    name: "Cooking",
    description: "Kitchen lights bright, fan on, music",
    icon: "🍳",
    devices: 4,
  },
];

export function AIScenes() {
  const [scenePrompt, setScenePrompt] = useState("");
  const [generatedScene, setGeneratedScene] = useState<any>(null);

  const handleGenerateScene = () => {
    // Mock AI scene generation
    setGeneratedScene({
      name: "Custom Scene",
      actions: [
        { device: "Living Room Lights", action: "Set to 50% brightness", icon: Lightbulb },
        { device: "Thermostat", action: "Set to 22°C", icon: Thermometer },
        { device: "Front Door", action: "Lock", icon: Lock },
        { device: "Smart Speaker", action: "Play relaxing music", icon: Music },
      ],
    });
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            AI Scene Builder
          </h1>
          <p className="text-gray-400">Create scenes using natural language</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* AI Scene Builder */}
          <div className="lg:col-span-2 space-y-6">
            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
              <div className="flex items-center gap-3 mb-4">
                <Sparkles className="w-6 h-6 text-purple-400" />
                <h2 className="text-xl font-semibold text-purple-400">Natural Language Builder</h2>
              </div>
              <p className="text-gray-400 mb-4">
                Describe what you want to happen, and AI will create the scene for you
              </p>

              <Textarea
                placeholder="Example: When I say 'relax', dim all living room lights to 30%, set temperature to 22°C, play jazz music, and close the blinds..."
                value={scenePrompt}
                onChange={(e) => setScenePrompt(e.target.value)}
                className="bg-gray-900/50 border-gray-800 text-gray-100 min-h-32 mb-4"
              />

              <div className="flex gap-2">
                <Button className="flex-1 bg-purple-600 hover:bg-purple-700" onClick={handleGenerateScene}>
                  <Sparkles className="w-4 h-4 mr-2" />
                  Generate Scene
                </Button>
                <Button variant="outline" className="border-purple-500/30 text-purple-400">
                  Clear
                </Button>
              </div>

              {/* Example Prompts */}
              <div className="mt-6">
                <div className="text-sm text-gray-500 mb-3">Try these examples:</div>
                <div className="flex flex-wrap gap-2">
                  <Badge
                    variant="outline"
                    className="border-purple-500/30 text-purple-400 cursor-pointer hover:bg-purple-500/10"
                    onClick={() =>
                      setScenePrompt(
                        "Turn on all kitchen lights, start coffee maker, open blinds"
                      )
                    }
                  >
                    Morning routine
                  </Badge>
                  <Badge
                    variant="outline"
                    className="border-purple-500/30 text-purple-400 cursor-pointer hover:bg-purple-500/10"
                    onClick={() =>
                      setScenePrompt("Dim all lights, lock doors, arm security system")
                    }
                  >
                    Goodnight
                  </Badge>
                  <Badge
                    variant="outline"
                    className="border-purple-500/30 text-purple-400 cursor-pointer hover:bg-purple-500/10"
                    onClick={() => setScenePrompt("Turn off everything and lock all doors")}
                  >
                    Leaving home
                  </Badge>
                </div>
              </div>
            </div>

            {/* Generated Scene Preview */}
            {generatedScene && (
              <div className="bg-gradient-to-br from-cyan-900/20 to-gray-950/80 border border-cyan-500/30 rounded-xl p-6">
                <h2 className="text-xl font-semibold text-cyan-400 mb-4">Generated Scene Preview</h2>
                <div className="space-y-3 mb-6">
                  {generatedScene.actions.map((action: any, index: number) => {
                    const Icon = action.icon;
                    return (
                      <div
                        key={index}
                        className="flex items-center gap-3 p-3 bg-gray-900/50 rounded-lg border border-gray-800"
                      >
                        <div className="p-2 bg-cyan-500/20 rounded-lg">
                          <Icon className="w-5 h-5 text-cyan-400" />
                        </div>
                        <div>
                          <div className="font-medium text-gray-200">{action.device}</div>
                          <div className="text-sm text-gray-400">{action.action}</div>
                        </div>
                      </div>
                    );
                  })}
                </div>

                <div className="flex gap-2">
                  <Button className="flex-1 bg-cyan-600 hover:bg-cyan-700">
                    <Play className="w-4 h-4 mr-2" />
                    Test Scene
                  </Button>
                  <Button className="flex-1 bg-green-600 hover:bg-green-700">
                    <Save className="w-4 h-4 mr-2" />
                    Save Scene
                  </Button>
                </div>
              </div>
            )}

            {/* Preset Scenes */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Preset Scenes</h2>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {presetScenes.map((scene) => (
                  <div
                    key={scene.name}
                    className="p-4 bg-gray-900/50 rounded-lg border border-gray-800 hover:border-cyan-500/30 transition-all cursor-pointer group"
                  >
                    <div className="flex items-start gap-3 mb-3">
                      <div className="text-3xl">{scene.icon}</div>
                      <div className="flex-1">
                        <div className="font-semibold text-gray-200 group-hover:text-cyan-400 transition-colors">
                          {scene.name}
                        </div>
                        <div className="text-sm text-gray-500 mt-1">{scene.description}</div>
                      </div>
                    </div>
                    <div className="flex items-center justify-between">
                      <Badge variant="outline" className="border-gray-700 text-gray-400 text-xs">
                        {scene.devices} devices
                      </Badge>
                      <Button size="sm" variant="ghost" className="text-cyan-400">
                        <Play className="w-3 h-3 mr-1" />
                        Activate
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* Scene Tips */}
          <div className="space-y-6">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">AI Tips</h2>
              <div className="space-y-4 text-sm">
                <div>
                  <div className="font-medium text-gray-200 mb-1">Be Specific</div>
                  <div className="text-gray-400">
                    Include room names, device types, and specific settings
                  </div>
                </div>
                <div>
                  <div className="font-medium text-gray-200 mb-1">Use Natural Language</div>
                  <div className="text-gray-400">Write as if talking to a person</div>
                </div>
                <div>
                  <div className="font-medium text-gray-200 mb-1">Add Conditions</div>
                  <div className="text-gray-400">
                    Mention time, weather, or other triggers
                  </div>
                </div>
                <div>
                  <div className="font-medium text-gray-200 mb-1">Test First</div>
                  <div className="text-gray-400">Always test before saving permanently</div>
                </div>
              </div>
            </div>

            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-purple-400 mb-4">Popular Actions</h2>
              <div className="space-y-2">
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Adjust lighting
                </Badge>
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Control temperature
                </Badge>
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Lock/unlock doors
                </Badge>
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Play music/media
                </Badge>
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Open/close blinds
                </Badge>
                <Badge variant="outline" className="w-full justify-start border-gray-700 text-gray-300 py-2">
                  Arm security
                </Badge>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
