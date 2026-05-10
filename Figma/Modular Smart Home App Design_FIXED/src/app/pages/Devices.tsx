import { Lightbulb, Plug, Thermometer, Camera, Lock, Wifi, Activity } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Switch } from "../components/ui/switch";

const devices = [
  {
    name: "Living Room Light 1",
    type: "Smart Bulb",
    room: "Living Room",
    status: "on",
    icon: Lightbulb,
    battery: null,
    signal: 95,
  },
  {
    name: "Living Room Light 2",
    type: "Smart Bulb",
    room: "Living Room",
    status: "on",
    icon: Lightbulb,
    battery: null,
    signal: 92,
  },
  {
    name: "Kitchen Plug",
    type: "Smart Plug",
    room: "Kitchen",
    status: "on",
    icon: Plug,
    battery: null,
    signal: 88,
  },
  {
    name: "Bedroom Thermostat",
    type: "Thermostat",
    room: "Bedroom",
    status: "on",
    icon: Thermometer,
    battery: null,
    signal: 90,
  },
  {
    name: "Front Door Camera",
    type: "Camera",
    room: "Entrance",
    status: "on",
    icon: Camera,
    battery: null,
    signal: 85,
  },
  {
    name: "Front Door Lock",
    type: "Smart Lock",
    room: "Entrance",
    status: "locked",
    icon: Lock,
    battery: 78,
    signal: 82,
  },
  {
    name: "Backyard Camera",
    type: "Camera",
    room: "Backyard",
    status: "on",
    icon: Camera,
    battery: null,
    signal: 75,
  },
  {
    name: "Garage Plug",
    type: "Smart Plug",
    room: "Garage",
    status: "off",
    icon: Plug,
    battery: null,
    signal: 80,
  },
];

export function Devices() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Connected Devices
            </h1>
            <p className="text-gray-400">{devices.length} devices connected</p>
          </div>
          <Button className="bg-cyan-600 hover:bg-cyan-700">Add Device</Button>
        </div>

        {/* Device Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
          {devices.map((device) => {
            const Icon = device.icon;
            const isOn = device.status === "on" || device.status === "locked";

            return (
              <div
                key={device.name}
                className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all"
              >
                <div className="flex items-start justify-between mb-4">
                  <div className="flex items-center gap-3">
                    <div className={`p-2 rounded-lg ${isOn ? "bg-cyan-500/20" : "bg-gray-800/50"}`}>
                      <Icon className={`w-6 h-6 ${isOn ? "text-cyan-400" : "text-gray-500"}`} />
                    </div>
                    <div>
                      <div className="font-semibold text-gray-200">{device.name}</div>
                      <div className="text-xs text-gray-500">{device.type}</div>
                    </div>
                  </div>
                  <Switch checked={isOn} />
                </div>

                <div className="space-y-2 mb-4">
                  <div className="flex items-center justify-between text-sm">
                    <span className="text-gray-500">Room:</span>
                    <span className="text-gray-300">{device.room}</span>
                  </div>
                  <div className="flex items-center justify-between text-sm">
                    <span className="text-gray-500">Signal:</span>
                    <div className="flex items-center gap-1">
                      <Wifi className="w-3 h-3 text-cyan-400" />
                      <span className="text-gray-300">{device.signal}%</span>
                    </div>
                  </div>
                  {device.battery && (
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-gray-500">Battery:</span>
                      <span className="text-gray-300">{device.battery}%</span>
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-2">
                  <Badge
                    className={
                      isOn
                        ? "bg-green-500/20 text-green-400 border-green-500/30"
                        : "bg-gray-700/20 text-gray-400 border-gray-700/30"
                    }
                  >
                    {device.status}
                  </Badge>
                  <Badge
                    variant="outline"
                    className={
                      device.signal > 80
                        ? "border-green-500/30 text-green-400"
                        : device.signal > 60
                        ? "border-yellow-500/30 text-yellow-400"
                        : "border-red-500/30 text-red-400"
                    }
                  >
                    <Activity className="w-3 h-3 mr-1" />
                    {device.signal > 80 ? "Strong" : device.signal > 60 ? "Good" : "Weak"}
                  </Badge>
                </div>

                <div className="flex gap-2 mt-4">
                  <Button size="sm" variant="outline" className="flex-1 border-gray-700">
                    Details
                  </Button>
                  <Button size="sm" variant="outline" className="flex-1 border-gray-700">
                    Settings
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
