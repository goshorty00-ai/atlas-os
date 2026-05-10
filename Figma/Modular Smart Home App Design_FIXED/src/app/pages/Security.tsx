import { Shield, AlertTriangle, Lock, Siren, DoorOpen, Activity } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const sensors = [
  { name: "Front Door", type: "Door Sensor", status: "closed", room: "Entrance" },
  { name: "Back Door", type: "Door Sensor", status: "closed", room: "Kitchen" },
  { name: "Living Room Window", type: "Window Sensor", status: "closed", room: "Living Room" },
  { name: "Bedroom Window", type: "Window Sensor", status: "closed", room: "Bedroom" },
  { name: "Garage Door", type: "Door Sensor", status: "closed", room: "Garage" },
  { name: "Basement Window", type: "Window Sensor", status: "closed", room: "Basement" },
];

const recentIncidents = [
  { event: "Front door opened", time: "2 hours ago", severity: "low" },
  { event: "Motion detected in backyard", time: "3 hours ago", severity: "medium" },
  { event: "Garage door left open", time: "Yesterday", severity: "medium" },
];

export function Security() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            Security Control
          </h1>
          <p className="text-gray-400">Monitor and protect your home</p>
        </div>

        {/* Security Status */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-6">
          <div className="md:col-span-2 bg-gradient-to-br from-green-900/20 to-gray-950/80 border border-green-500/30 rounded-xl p-8">
            <div className="flex items-center gap-4 mb-6">
              <div className="p-4 bg-green-500/20 rounded-full">
                <Shield className="w-12 h-12 text-green-400" />
              </div>
              <div>
                <h2 className="text-3xl font-bold text-green-400">System Armed - Home</h2>
                <p className="text-gray-400">All entry points secured</p>
              </div>
            </div>

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <Button className="bg-green-600 hover:bg-green-700 h-20 flex-col">
                <Shield className="w-6 h-6 mb-2" />
                Armed Home
              </Button>
              <Button variant="outline" className="border-gray-700 h-20 flex-col">
                <Activity className="w-6 h-6 mb-2" />
                Armed Away
              </Button>
              <Button variant="outline" className="border-gray-700 h-20 flex-col">
                <Lock className="w-6 h-6 mb-2" />
                Night Mode
              </Button>
              <Button variant="outline" className="border-gray-700 h-20 flex-col">
                <DoorOpen className="w-6 h-6 mb-2" />
                Disarmed
              </Button>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800">
              <Button className="w-full bg-red-600 hover:bg-red-700 h-16 text-lg">
                <Siren className="w-6 h-6 mr-2" />
                PANIC / EMERGENCY
              </Button>
            </div>
          </div>

          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Quick Stats</h2>
            <div className="space-y-4">
              <div>
                <div className="text-sm text-gray-500 mb-1">Entry Points</div>
                <div className="text-2xl font-bold text-gray-100">12 Secure</div>
              </div>
              <div>
                <div className="text-sm text-gray-500 mb-1">Active Sensors</div>
                <div className="text-2xl font-bold text-gray-100">18</div>
              </div>
              <div>
                <div className="text-sm text-gray-500 mb-1">Cameras</div>
                <div className="text-2xl font-bold text-gray-100">6 Online</div>
              </div>
              <div>
                <div className="text-sm text-gray-500 mb-1">Last Activity</div>
                <div className="text-xl font-semibold text-gray-100">2 min ago</div>
              </div>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Entry Points */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Entry Points</h2>
            <div className="space-y-3">
              {sensors.map((sensor) => (
                <div
                  key={sensor.name}
                  className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg border border-gray-800"
                >
                  <div className="flex items-center gap-3">
                    <DoorOpen className="w-5 h-5 text-cyan-400" />
                    <div>
                      <div className="font-medium text-gray-200">{sensor.name}</div>
                      <div className="text-xs text-gray-500">
                        {sensor.type} • {sensor.room}
                      </div>
                    </div>
                  </div>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">
                    {sensor.status}
                  </Badge>
                </div>
              ))}
            </div>
          </div>

          {/* Recent Incidents */}
          <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
            <h2 className="text-xl font-semibold text-cyan-400 mb-4">Recent Activity</h2>
            <div className="space-y-3">
              {recentIncidents.map((incident, index) => (
                <div
                  key={index}
                  className={`p-4 rounded-lg border ${
                    incident.severity === "high"
                      ? "bg-red-900/20 border-red-500/30"
                      : incident.severity === "medium"
                      ? "bg-yellow-900/20 border-yellow-500/30"
                      : "bg-gray-900/50 border-gray-800"
                  }`}
                >
                  <div className="flex items-start justify-between">
                    <div className="flex items-start gap-3">
                      <AlertTriangle
                        className={`w-5 h-5 mt-0.5 ${
                          incident.severity === "high"
                            ? "text-red-400"
                            : incident.severity === "medium"
                            ? "text-yellow-400"
                            : "text-gray-400"
                        }`}
                      />
                      <div>
                        <div className="font-medium text-gray-200">{incident.event}</div>
                        <div className="text-sm text-gray-500 mt-1">{incident.time}</div>
                      </div>
                    </div>
                    <Button size="sm" variant="outline" className="border-gray-700">
                      View
                    </Button>
                  </div>
                </div>
              ))}
            </div>

            <Button className="w-full mt-4 bg-cyan-600 hover:bg-cyan-700">
              View Full History
            </Button>
          </div>
        </div>

        {/* Emergency Protocols */}
        <div className="bg-gradient-to-br from-red-900/20 to-gray-950/80 border border-red-500/30 rounded-xl p-6 mt-6">
          <h2 className="text-xl font-semibold text-red-400 mb-4">Emergency Protocols</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <Button
              size="lg"
              className="bg-red-600 hover:bg-red-700 h-24 flex-col justify-center"
            >
              <Siren className="w-8 h-8 mb-2" />
              <span>Trigger Alarm</span>
            </Button>
            <Button
              size="lg"
              variant="outline"
              className="border-red-500/30 text-red-400 hover:bg-red-900/20 h-24 flex-col justify-center"
            >
              <Lock className="w-8 h-8 mb-2" />
              <span>Lockdown All</span>
            </Button>
            <Button
              size="lg"
              variant="outline"
              className="border-red-500/30 text-red-400 hover:bg-red-900/20 h-24 flex-col justify-center"
            >
              <AlertTriangle className="w-8 h-8 mb-2" />
              <span>Alert Authorities</span>
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
