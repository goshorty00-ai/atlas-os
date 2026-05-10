import { Activity, Zap, Thermometer, Camera, Shield, Clock, TrendingUp } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const quickStats = [
  { label: "Active Devices", value: "47", icon: Activity, color: "text-cyan-400" },
  { label: "Energy Usage", value: "2.4 kW", icon: Zap, color: "text-yellow-400" },
  { label: "Temperature", value: "22°C", icon: Thermometer, color: "text-orange-400" },
  { label: "Security Status", value: "Armed", icon: Shield, color: "text-green-400" },
];

const recentEvents = [
  { event: "Front door unlocked", time: "2 min ago", type: "access" },
  { event: "Living room motion detected", time: "5 min ago", type: "motion" },
  { event: "Kitchen lights turned on", time: "12 min ago", type: "light" },
  { event: "Thermostat adjusted to 22°C", time: "30 min ago", type: "climate" },
];

const activeCameras = [
  { name: "Front Door", status: "active" },
  { name: "Backyard", status: "active" },
  { name: "Garage", status: "active" },
];

export function Overview() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            Command Center
          </h1>
          <p className="text-gray-400">Your smart home at a glance</p>
        </div>

        {/* Quick Stats */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          {quickStats.map((stat) => {
            const Icon = stat.icon;
            return (
              <div
                key={stat.label}
                className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all"
              >
                <div className="flex items-center justify-between mb-2">
                  <Icon className={`w-6 h-6 ${stat.color}`} />
                  <TrendingUp className="w-4 h-4 text-green-400" />
                </div>
                <div className="text-3xl font-bold text-gray-100 mb-1">{stat.value}</div>
                <div className="text-sm text-gray-500">{stat.label}</div>
              </div>
            );
          })}
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Home Mode */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Home Mode</h2>
            <div className="space-y-3">
              <Button className="w-full bg-cyan-600 hover:bg-cyan-700 justify-start">
                <Activity className="w-4 h-4 mr-2" />
                Home
              </Button>
              <Button variant="outline" className="w-full border-gray-700 justify-start">
                Away
              </Button>
              <Button variant="outline" className="w-full border-gray-700 justify-start">
                Sleep
              </Button>
              <Button variant="outline" className="w-full border-gray-700 justify-start">
                Vacation
              </Button>
            </div>
          </div>

          {/* Security Status */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Security</h2>
            <div className="mb-4">
              <Badge className="bg-green-500/20 text-green-400 border-green-500/30 mb-2">
                <Shield className="w-3 h-3 mr-1" />
                Armed - Home
              </Badge>
            </div>
            <div className="space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-gray-400">All Entry Points</span>
                <span className="text-green-400">Secure</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Active Sensors</span>
                <span className="text-gray-300">12</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Last Activity</span>
                <span className="text-gray-300">2 min ago</span>
              </div>
            </div>
          </div>

          {/* Climate */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Climate</h2>
            <div className="text-center mb-4">
              <div className="text-5xl font-bold text-gray-100">22°C</div>
              <div className="text-sm text-gray-500 mt-1">Target: 22°C</div>
            </div>
            <div className="space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-gray-400">Humidity</span>
                <span className="text-gray-300">45%</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Air Quality</span>
                <span className="text-green-400">Good</span>
              </div>
            </div>
          </div>
        </div>

        {/* Recent Events and Camera Feeds */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
          {/* Recent Events */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-cyan-400">Recent Events</h2>
              <Clock className="w-5 h-5 text-gray-500" />
            </div>
            <div className="space-y-3">
              {recentEvents.map((event, index) => (
                <div
                  key={index}
                  className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg"
                >
                  <div>
                    <div className="text-gray-200">{event.event}</div>
                    <div className="text-sm text-gray-500">{event.time}</div>
                  </div>
                  <Badge variant="outline" className="border-gray-700 text-gray-400">
                    {event.type}
                  </Badge>
                </div>
              ))}
            </div>
          </div>

          {/* Active Cameras */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-xl font-semibold text-cyan-400">Active Cameras</h2>
              <Camera className="w-5 h-5 text-gray-500" />
            </div>
            <div className="space-y-3">
              {activeCameras.map((camera) => (
                <div
                  key={camera.name}
                  className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg"
                >
                  <div>
                    <div className="text-gray-200">{camera.name}</div>
                    <Badge className="bg-green-500/20 text-green-400 border-green-500/30 mt-1">
                      <div className="w-2 h-2 bg-green-500 rounded-full mr-1 animate-pulse" />
                      {camera.status}
                    </Badge>
                  </div>
                  <Button size="sm" variant="outline" className="border-gray-700">
                    View
                  </Button>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* AI Recommendations */}
        <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6 mt-6">
          <h2 className="text-xl font-semibold text-purple-400 mb-4">AI Recommendations</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Energy Savings</div>
              <div className="text-sm text-gray-400">
                Reduce AC by 1°C to save 8% energy during the day
              </div>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Security Tip</div>
              <div className="text-sm text-gray-400">
                Consider adding motion sensor to back entrance
              </div>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Automation Idea</div>
              <div className="text-sm text-gray-400">
                Create "Movie Mode" scene for evening entertainment
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
