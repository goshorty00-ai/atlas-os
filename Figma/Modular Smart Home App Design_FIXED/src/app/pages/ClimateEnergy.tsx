import { Thermometer, Wind, Zap, Droplet, Sun, Battery, TrendingDown } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const climateZones = [
  { name: "Living Room", temp: "22°C", humidity: "45%", status: "optimal" },
  { name: "Bedroom", temp: "21°C", humidity: "50%", status: "optimal" },
  { name: "Kitchen", temp: "23°C", humidity: "55%", status: "high" },
  { name: "Bathroom", temp: "24°C", humidity: "60%", status: "high" },
];

const energyDevices = [
  { name: "HVAC System", usage: "1.2 kW", percentage: 40, status: "active" },
  { name: "Water Heater", usage: "0.8 kW", percentage: 27, status: "active" },
  { name: "Lighting", usage: "0.3 kW", percentage: 10, status: "active" },
  { name: "Appliances", usage: "0.5 kW", percentage: 17, status: "active" },
  { name: "Other", usage: "0.2 kW", percentage: 6, status: "active" },
];

export function ClimateEnergy() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            Climate & Energy
          </h1>
          <p className="text-gray-400">Monitor and optimize comfort and efficiency</p>
        </div>

        {/* Quick Stats */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          <div className="bg-gradient-to-br from-orange-900/20 to-gray-950/80 border border-orange-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <Thermometer className="w-6 h-6 text-orange-400" />
            </div>
            <div className="text-3xl font-bold text-gray-100">22°C</div>
            <div className="text-sm text-gray-400">Average Temperature</div>
          </div>

          <div className="bg-gradient-to-br from-blue-900/20 to-gray-950/80 border border-blue-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <Droplet className="w-6 h-6 text-blue-400" />
            </div>
            <div className="text-3xl font-bold text-gray-100">48%</div>
            <div className="text-sm text-gray-400">Average Humidity</div>
          </div>

          <div className="bg-gradient-to-br from-yellow-900/20 to-gray-950/80 border border-yellow-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <Zap className="w-6 h-6 text-yellow-400" />
            </div>
            <div className="text-3xl font-bold text-gray-100">2.8 kW</div>
            <div className="text-sm text-gray-400">Current Usage</div>
          </div>

          <div className="bg-gradient-to-br from-green-900/20 to-gray-950/80 border border-green-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <TrendingDown className="w-6 h-6 text-green-400" />
            </div>
            <div className="text-3xl font-bold text-gray-100">-12%</div>
            <div className="text-sm text-gray-400">vs Last Month</div>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Climate Control */}
          <div className="space-y-6">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Climate Zones</h2>
              <div className="space-y-3">
                {climateZones.map((zone) => (
                  <div
                    key={zone.name}
                    className="p-4 bg-gray-900/50 rounded-lg border border-gray-800"
                  >
                    <div className="flex items-center justify-between mb-3">
                      <div>
                        <div className="font-semibold text-gray-200">{zone.name}</div>
                        <Badge
                          className={
                            zone.status === "optimal"
                              ? "bg-green-500/20 text-green-400 border-green-500/30 mt-1"
                              : "bg-yellow-500/20 text-yellow-400 border-yellow-500/30 mt-1"
                          }
                        >
                          {zone.status}
                        </Badge>
                      </div>
                      <div className="text-right">
                        <div className="text-2xl font-bold text-gray-100">{zone.temp}</div>
                        <div className="text-sm text-gray-500">{zone.humidity} humidity</div>
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" className="flex-1 border-gray-700">
                        <Thermometer className="w-3 h-3 mr-1" />
                        Adjust
                      </Button>
                      <Button size="sm" variant="outline" className="border-gray-700">
                        Details
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Air Quality */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Air Quality</h2>
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Wind className="w-5 h-5 text-green-400" />
                    <span className="text-gray-200">Air Quality Index</span>
                  </div>
                  <div className="text-right">
                    <div className="text-xl font-bold text-green-400">Good</div>
                    <div className="text-xs text-gray-500">AQI: 42</div>
                  </div>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">CO₂ Level</span>
                  <span className="text-gray-300">420 ppm</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">VOC Level</span>
                  <span className="text-gray-300">Low</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">PM2.5</span>
                  <span className="text-gray-300">12 µg/m³</span>
                </div>
              </div>
              <Button className="w-full mt-4 bg-cyan-600 hover:bg-cyan-700">
                <Wind className="w-4 h-4 mr-2" />
                Run Air Purifiers
              </Button>
            </div>
          </div>

          {/* Energy Management */}
          <div className="space-y-6">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Energy Breakdown</h2>
              <div className="space-y-3 mb-6">
                {energyDevices.map((device) => (
                  <div key={device.name}>
                    <div className="flex items-center justify-between mb-2">
                      <span className="text-gray-200">{device.name}</span>
                      <span className="text-gray-400">{device.usage}</span>
                    </div>
                    <div className="w-full bg-gray-800 rounded-full h-2">
                      <div
                        className="bg-cyan-500 h-2 rounded-full"
                        style={{ width: `${device.percentage}%` }}
                      ></div>
                    </div>
                  </div>
                ))}
              </div>

              <div className="pt-4 border-t border-gray-800">
                <div className="flex items-center justify-between mb-2">
                  <span className="text-gray-400">Total Usage</span>
                  <span className="text-xl font-bold text-gray-100">2.8 kW</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">Estimated Cost Today</span>
                  <span className="text-lg font-semibold text-cyan-400">$2.45</span>
                </div>
              </div>
            </div>

            {/* Solar & Battery */}
            <div className="bg-gradient-to-br from-yellow-900/20 to-gray-950/80 border border-yellow-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-yellow-400 mb-4">Solar & Storage</h2>
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Sun className="w-5 h-5 text-yellow-400" />
                    <span className="text-gray-200">Solar Generation</span>
                  </div>
                  <span className="text-xl font-bold text-yellow-400">1.2 kW</span>
                </div>
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <Battery className="w-5 h-5 text-green-400" />
                    <span className="text-gray-200">Battery Level</span>
                  </div>
                  <span className="text-xl font-bold text-green-400">87%</span>
                </div>
                <div className="w-full bg-gray-800 rounded-full h-3">
                  <div className="bg-green-500 h-3 rounded-full" style={{ width: "87%" }}></div>
                </div>
              </div>
              <div className="mt-4 p-3 bg-gray-900/50 rounded-lg">
                <div className="text-sm text-gray-400 mb-1">Net Energy Flow</div>
                <div className="text-lg font-semibold text-green-400">
                  -1.6 kW (Exporting to Grid)
                </div>
              </div>
            </div>

            {/* Leak Detection */}
            <div className="bg-gradient-to-br from-blue-900/20 to-gray-950/80 border border-blue-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-blue-400 mb-4">Leak Monitoring</h2>
              <div className="space-y-3">
                <div className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                  <div className="flex items-center gap-3">
                    <Droplet className="w-5 h-5 text-blue-400" />
                    <span className="text-gray-200">Kitchen Sensor</span>
                  </div>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">Dry</Badge>
                </div>
                <div className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                  <div className="flex items-center gap-3">
                    <Droplet className="w-5 h-5 text-blue-400" />
                    <span className="text-gray-200">Bathroom Sensor</span>
                  </div>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">Dry</Badge>
                </div>
                <div className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg">
                  <div className="flex items-center gap-3">
                    <Droplet className="w-5 h-5 text-blue-400" />
                    <span className="text-gray-200">Basement Sensor</span>
                  </div>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">Dry</Badge>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
