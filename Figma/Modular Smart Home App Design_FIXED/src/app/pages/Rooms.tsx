import { Lightbulb, Thermometer, Lock, Camera, Plus } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const rooms = [
  {
    name: "Living Room",
    devices: 8,
    activeDevices: 6,
    icon: "🛋️",
    temperature: "22°C",
    deviceList: ["Smart Bulb x3", "Smart Plug x2", "Motion Sensor", "Smart Speaker", "TV"],
  },
  {
    name: "Kitchen",
    devices: 5,
    activeDevices: 3,
    icon: "🍳",
    temperature: "23°C",
    deviceList: ["Smart Bulb x2", "Smart Plug", "Leak Sensor", "Smart Speaker"],
  },
  {
    name: "Bedroom",
    devices: 6,
    activeDevices: 2,
    icon: "🛏️",
    temperature: "21°C",
    deviceList: ["Smart Bulb x2", "Thermostat", "Smart Blinds", "Motion Sensor", "Air Purifier"],
  },
  {
    name: "Bathroom",
    devices: 3,
    activeDevices: 1,
    icon: "🚿",
    temperature: "24°C",
    deviceList: ["Smart Bulb", "Leak Sensor", "Humidity Sensor"],
  },
  {
    name: "Entrance",
    devices: 4,
    activeDevices: 4,
    icon: "🚪",
    temperature: "20°C",
    deviceList: ["Smart Lock", "Video Doorbell", "Motion Sensor", "Smart Light"],
  },
  {
    name: "Garage",
    devices: 3,
    activeDevices: 1,
    icon: "🚗",
    temperature: "18°C",
    deviceList: ["Garage Door Opener", "Smart Plug", "Motion Sensor"],
  },
  {
    name: "Backyard",
    devices: 5,
    activeDevices: 3,
    icon: "🌳",
    temperature: "16°C",
    deviceList: ["Outdoor Camera x2", "Smart Lights x2", "Irrigation Controller"],
  },
  {
    name: "Office",
    devices: 4,
    activeDevices: 3,
    icon: "💼",
    temperature: "22°C",
    deviceList: ["Smart Bulb x2", "Smart Plug", "Temperature Sensor"],
  },
];

export function Rooms() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Rooms
            </h1>
            <p className="text-gray-400">{rooms.length} rooms configured</p>
          </div>
          <Button className="bg-cyan-600 hover:bg-cyan-700">
            <Plus className="w-4 h-4 mr-2" />
            Add Room
          </Button>
        </div>

        {/* Rooms Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
          {rooms.map((room) => (
            <div
              key={room.name}
              className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all cursor-pointer group"
            >
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-center gap-3">
                  <div className="text-3xl">{room.icon}</div>
                  <div>
                    <h3 className="font-semibold text-gray-200 group-hover:text-cyan-400 transition-colors">
                      {room.name}
                    </h3>
                    <div className="text-xs text-gray-500">{room.devices} devices</div>
                  </div>
                </div>
                <Thermometer className="w-4 h-4 text-gray-500" />
              </div>

              <div className="mb-4">
                <div className="text-2xl font-bold text-gray-100 mb-1">{room.temperature}</div>
                <div className="flex items-center gap-2">
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30 text-xs">
                    {room.activeDevices} active
                  </Badge>
                  <Badge variant="outline" className="border-gray-700 text-gray-400 text-xs">
                    {room.devices - room.activeDevices} off
                  </Badge>
                </div>
              </div>

              <div className="mb-4">
                <div className="text-xs text-gray-500 mb-2">Devices:</div>
                <div className="space-y-1">
                  {room.deviceList.slice(0, 3).map((device, index) => (
                    <div key={index} className="text-xs text-gray-400">
                      • {device}
                    </div>
                  ))}
                  {room.deviceList.length > 3 && (
                    <div className="text-xs text-gray-500">
                      +{room.deviceList.length - 3} more
                    </div>
                  )}
                </div>
              </div>

              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  className="flex-1 border-gray-700 hover:border-cyan-500/50"
                >
                  <Lightbulb className="w-3 h-3 mr-1" />
                  All On
                </Button>
                <Button size="sm" variant="outline" className="border-gray-700">
                  All Off
                </Button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
