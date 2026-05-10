import { useState } from "react";
import { Search, QrCode, Wifi, Bluetooth, Network, HelpCircle, Check, X, Loader2 } from "lucide-react";
import { Input } from "../components/ui/input";
import { Button } from "../components/ui/button";
import { Badge } from "../components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "../components/ui/tabs";
import { ScrollArea } from "../components/ui/scroll-area";

const deviceCategories = [
  {
    name: "Lighting",
    devices: ["Smart Bulbs", "LED Strips", "Smart Lamps", "Dimmers", "Light Switches"],
  },
  {
    name: "Power & Switches",
    devices: ["Smart Plugs", "Smart Switches", "Smart Outlets", "Power Strips"],
  },
  {
    name: "Climate Control",
    devices: ["Thermostats", "Smart Heaters", "AC Controllers", "Smart Fans", "Radiator Valves"],
  },
  {
    name: "Air Quality",
    devices: ["Air Purifiers", "Humidifiers", "Dehumidifiers", "Air Quality Monitors"],
  },
  {
    name: "Sensors",
    devices: [
      "Motion Sensors",
      "Occupancy Sensors",
      "Temperature Sensors",
      "Humidity Sensors",
      "Door/Window Sensors",
      "Leak Sensors",
      "Smoke Detectors",
      "CO Detectors",
      "Glass Break Sensors",
      "Vibration Sensors",
    ],
  },
  {
    name: "Security & Alarms",
    devices: ["Smart Sirens", "Alarm Systems", "Panic Buttons", "Security Keypads"],
  },
  {
    name: "Access Control",
    devices: ["Smart Locks", "Video Doorbells", "Garage Door Openers", "Smart Gates", "Keypads"],
  },
  {
    name: "Cameras",
    devices: [
      "Indoor Cameras",
      "Outdoor Cameras",
      "Floodlight Cameras",
      "Pan-Tilt Cameras",
      "Baby Monitors",
      "Doorbell Cameras",
    ],
  },
  {
    name: "Entertainment",
    devices: ["Smart Speakers", "Smart Displays", "Smart TVs", "Media Hubs", "Streaming Devices"],
  },
  {
    name: "Window Coverings",
    devices: ["Smart Blinds", "Smart Curtains", "Smart Shades", "Window Motors"],
  },
  {
    name: "Cleaning",
    devices: ["Robot Vacuums", "Robot Mops", "Smart Vacuum Cleaners"],
  },
  {
    name: "Appliances",
    devices: [
      "Smart Refrigerators",
      "Smart Ovens",
      "Smart Dishwashers",
      "Smart Washers",
      "Smart Dryers",
      "Coffee Makers",
    ],
  },
  {
    name: "Outdoor",
    devices: ["Smart Irrigation", "Garden Lighting", "Pool Controllers", "Weather Stations"],
  },
  {
    name: "Energy",
    devices: ["Solar Panels", "Battery Storage", "EV Chargers", "Energy Monitors", "Smart Meters"],
  },
  {
    name: "Hubs & Bridges",
    devices: ["Matter Controllers", "Zigbee Hubs", "Z-Wave Controllers", "Thread Border Routers", "Smart Home Hubs"],
  },
  {
    name: "Custom & Third-Party",
    devices: ["DIY Devices", "ESP32/ESP8266", "Arduino", "Raspberry Pi", "Custom Integrations"],
  },
];

const ecosystems = [
  {
    name: "Google Home",
    logo: "🏠",
    status: "connected",
    devices: 14,
    lastSync: "2 min ago",
    types: ["Lights", "Cameras", "Thermostats", "Speakers"],
  },
  {
    name: "Amazon Alexa",
    logo: "🔵",
    status: "connected",
    devices: 12,
    lastSync: "5 min ago",
    types: ["Lights", "Plugs", "Locks", "Sensors"],
  },
  {
    name: "Apple Home",
    logo: "🍎",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["All HomeKit devices"],
  },
  {
    name: "Home Assistant",
    logo: "🏡",
    status: "connected",
    devices: 47,
    lastSync: "Just now",
    types: ["All device types"],
  },
  {
    name: "SmartThings",
    logo: "⚡",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Zigbee", "Z-Wave", "WiFi"],
  },
  {
    name: "Philips Hue",
    logo: "💡",
    status: "connected",
    devices: 8,
    lastSync: "1 min ago",
    types: ["Lights", "Motion Sensors"],
  },
  {
    name: "Ring",
    logo: "🔔",
    status: "connected",
    devices: 3,
    lastSync: "3 min ago",
    types: ["Doorbells", "Cameras", "Alarms"],
  },
  {
    name: "Nest",
    logo: "🔥",
    status: "connected",
    devices: 5,
    lastSync: "4 min ago",
    types: ["Thermostats", "Cameras", "Smoke"],
  },
  {
    name: "TP-Link / Tapo",
    logo: "🔌",
    status: "connected",
    devices: 6,
    lastSync: "2 min ago",
    types: ["Plugs", "Bulbs", "Cameras"],
  },
  {
    name: "Eufy",
    logo: "🛡️",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Cameras", "Vacuums", "Locks"],
  },
  {
    name: "Aqara",
    logo: "📱",
    status: "connected",
    devices: 11,
    lastSync: "1 min ago",
    types: ["Sensors", "Switches", "Hubs"],
  },
  {
    name: "Arlo",
    logo: "📹",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Cameras", "Doorbells"],
  },
  {
    name: "Ecobee",
    logo: "🌡️",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Thermostats", "Sensors"],
  },
  {
    name: "Sonos",
    logo: "🔊",
    status: "connected",
    devices: 4,
    lastSync: "10 min ago",
    types: ["Speakers", "Soundbars"],
  },
  {
    name: "IKEA Dirigera",
    logo: "🪑",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Lights", "Blinds", "Sensors"],
  },
  {
    name: "Yale",
    logo: "🔐",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Locks", "Keypads"],
  },
  {
    name: "Reolink",
    logo: "📷",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Cameras", "NVRs"],
  },
  {
    name: "Shelly",
    logo: "⚙️",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Relays", "Switches", "Sensors"],
  },
  {
    name: "Tuya / Smart Life",
    logo: "☁️",
    status: "connected",
    devices: 9,
    lastSync: "6 min ago",
    types: ["Various WiFi devices"],
  },
  {
    name: "SwitchBot",
    logo: "🤖",
    status: "not_connected",
    devices: 0,
    lastSync: "Never",
    types: ["Bots", "Curtains", "Sensors"],
  },
];

const setupMethods = [
  {
    name: "QR Code Pairing",
    icon: QrCode,
    description: "Scan device QR code for instant setup",
    protocols: ["Matter", "HomeKit", "WiFi"],
  },
  {
    name: "WiFi Discovery",
    icon: Wifi,
    description: "Auto-discover devices on network",
    protocols: ["WiFi", "Local"],
  },
  {
    name: "Bluetooth Pairing",
    icon: Bluetooth,
    description: "Pair nearby Bluetooth devices",
    protocols: ["Bluetooth", "BLE"],
  },
  {
    name: "Local Network",
    icon: Network,
    description: "Manual IP/hostname configuration",
    protocols: ["Local", "HTTP", "MQTT"],
  },
];

const recentlyAdded = [
  { name: "Living Room Light 3", type: "Smart Bulb", time: "5 min ago", status: "online" },
  { name: "Kitchen Motion Sensor", type: "Motion Sensor", time: "12 min ago", status: "online" },
  { name: "Front Door Camera", type: "Camera", time: "1 hour ago", status: "online" },
  { name: "Bedroom Thermostat", type: "Thermostat", time: "2 hours ago", status: "online" },
];

export function DeviceSetup() {
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("all");
  const [activeTab, setActiveTab] = useState("catalog");

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            Device Setup Hub
          </h1>
          <p className="text-gray-400">
            Master onboarding center for all smart home devices, ecosystems, and protocols
          </p>
        </div>

        {/* Main Content Tabs */}
        <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-6">
          <TabsList className="bg-gray-900/50 border border-gray-800">
            <TabsTrigger value="catalog">Device Catalog</TabsTrigger>
            <TabsTrigger value="ecosystems">Ecosystems & Brands</TabsTrigger>
            <TabsTrigger value="setup">Setup Methods</TabsTrigger>
            <TabsTrigger value="scan">Quick Add</TabsTrigger>
          </TabsList>

          {/* Device Catalog Tab */}
          <TabsContent value="catalog" className="space-y-6">
            {/* Search and Filters */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
              <div className="lg:col-span-2">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
                  <Input
                    placeholder="Search devices, brands, protocols..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    className="pl-10 bg-gray-900/50 border-gray-800 text-gray-100 h-12"
                  />
                </div>
              </div>
              <select
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
                className="h-12 px-4 bg-gray-900/50 border border-gray-800 rounded-lg text-gray-100"
              >
                <option value="all">All Categories</option>
                {deviceCategories.map((cat) => (
                  <option key={cat.name} value={cat.name}>
                    {cat.name}
                  </option>
                ))}
              </select>
            </div>

            {/* Device Categories Grid */}
            <ScrollArea className="h-[calc(100vh-350px)]">
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4 pr-4">
                {deviceCategories.map((category) => (
                  <div
                    key={category.name}
                    className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-5 hover:border-cyan-500/30 transition-all cursor-pointer group"
                  >
                    <h3 className="text-lg font-semibold text-cyan-400 mb-3 group-hover:text-cyan-300">
                      {category.name}
                    </h3>
                    <div className="space-y-2">
                      {category.devices.map((device) => (
                        <div
                          key={device}
                          className="text-sm text-gray-400 hover:text-gray-200 flex items-center justify-between group/device"
                        >
                          <span>{device}</span>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="opacity-0 group-hover/device:opacity-100 h-6 text-xs text-cyan-400"
                          >
                            Add
                          </Button>
                        </div>
                      ))}
                    </div>
                    <div className="mt-4 pt-4 border-t border-gray-800">
                      <div className="text-xs text-gray-500">
                        {category.devices.length} device types
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </ScrollArea>
          </TabsContent>

          {/* Ecosystems & Brands Tab */}
          <TabsContent value="ecosystems" className="space-y-6">
            <div className="mb-4">
              <h2 className="text-xl font-semibold text-gray-200 mb-2">
                Connected Ecosystems & Integrations
              </h2>
              <p className="text-sm text-gray-500">
                Sign in to brand accounts and cloud services to import devices
              </p>
            </div>

            <ScrollArea className="h-[calc(100vh-350px)]">
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 pr-4">
                {ecosystems.map((eco) => (
                  <div
                    key={eco.name}
                    className={`bg-gradient-to-br ${
                      eco.status === "connected"
                        ? "from-gray-900/80 to-gray-950/80 border-cyan-500/30"
                        : "from-gray-900/40 to-gray-950/40 border-gray-800/30"
                    } border rounded-xl p-5 hover:border-cyan-500/50 transition-all`}
                  >
                    <div className="flex items-start justify-between mb-4">
                      <div className="flex items-center gap-3">
                        <div className="text-3xl">{eco.logo}</div>
                        <div>
                          <h3 className="font-semibold text-gray-200">{eco.name}</h3>
                          <div className="flex items-center gap-2 mt-1">
                            {eco.status === "connected" ? (
                              <Badge className="bg-green-500/20 text-green-400 border-green-500/30">
                                <Check className="w-3 h-3 mr-1" />
                                Connected
                              </Badge>
                            ) : (
                              <Badge className="bg-gray-700/20 text-gray-400 border-gray-700/30">
                                Not Connected
                              </Badge>
                            )}
                          </div>
                        </div>
                      </div>
                    </div>

                    <div className="space-y-2 mb-4">
                      <div className="flex justify-between text-sm">
                        <span className="text-gray-500">Devices:</span>
                        <span className="text-gray-300">{eco.devices}</span>
                      </div>
                      <div className="flex justify-between text-sm">
                        <span className="text-gray-500">Last Sync:</span>
                        <span className="text-gray-300">{eco.lastSync}</span>
                      </div>
                    </div>

                    <div className="mb-4">
                      <div className="text-xs text-gray-500 mb-2">Supported:</div>
                      <div className="flex flex-wrap gap-1">
                        {eco.types.map((type) => (
                          <Badge
                            key={type}
                            variant="outline"
                            className="text-xs border-gray-700 text-gray-400"
                          >
                            {type}
                          </Badge>
                        ))}
                      </div>
                    </div>

                    <div className="flex gap-2">
                      {eco.status === "connected" ? (
                        <>
                          <Button size="sm" className="flex-1 bg-cyan-500/20 text-cyan-400 hover:bg-cyan-500/30">
                            Sync Now
                          </Button>
                          <Button size="sm" variant="outline" className="border-gray-700">
                            Settings
                          </Button>
                        </>
                      ) : (
                        <Button size="sm" className="flex-1 bg-cyan-600 hover:bg-cyan-700">
                          Sign In & Connect
                        </Button>
                      )}
                    </div>

                    <Button
                      size="sm"
                      variant="ghost"
                      className="w-full mt-2 text-xs text-cyan-400 hover:text-cyan-300"
                    >
                      <HelpCircle className="w-3 h-3 mr-1" />
                      AI Setup Help
                    </Button>
                  </div>
                ))}
              </div>
            </ScrollArea>
          </TabsContent>

          {/* Setup Methods Tab */}
          <TabsContent value="setup" className="space-y-6">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
              {setupMethods.map((method) => {
                const Icon = method.icon;
                return (
                  <div
                    key={method.name}
                    className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6 hover:border-cyan-500/30 transition-all cursor-pointer group"
                  >
                    <Icon className="w-8 h-8 text-cyan-400 mb-4 group-hover:text-cyan-300" />
                    <h3 className="font-semibold text-gray-200 mb-2">{method.name}</h3>
                    <p className="text-sm text-gray-500 mb-4">{method.description}</p>
                    <div className="flex flex-wrap gap-1">
                      {method.protocols.map((protocol) => (
                        <Badge
                          key={protocol}
                          variant="outline"
                          className="text-xs border-gray-700 text-gray-400"
                        >
                          {protocol}
                        </Badge>
                      ))}
                    </div>
                    <Button className="w-full mt-4 bg-cyan-600 hover:bg-cyan-700">
                      Start Setup
                    </Button>
                  </div>
                );
              })}
            </div>

            {/* Protocol-Specific Setup Sections */}
            <div className="space-y-4">
              <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
                <h3 className="text-lg font-semibold text-purple-400 mb-4">Matter Onboarding</h3>
                <p className="text-sm text-gray-400 mb-4">
                  Universal smart home standard for seamless device setup
                </p>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <Button className="bg-purple-600 hover:bg-purple-700">
                    Scan Matter QR Code
                  </Button>
                  <Button variant="outline" className="border-purple-500/30 text-purple-400">
                    Enter Setup Code
                  </Button>
                  <Button variant="outline" className="border-purple-500/30 text-purple-400">
                    Browse Matter Devices
                  </Button>
                </div>
              </div>

              <div className="bg-gradient-to-br from-amber-900/20 to-gray-950/80 border border-amber-500/30 rounded-xl p-6">
                <h3 className="text-lg font-semibold text-amber-400 mb-4">Zigbee Onboarding</h3>
                <p className="text-sm text-gray-400 mb-4">
                  Low-power mesh network protocol for smart devices
                </p>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <Button className="bg-amber-600 hover:bg-amber-700">
                    Start Pairing Mode
                  </Button>
                  <Button variant="outline" className="border-amber-500/30 text-amber-400">
                    View Paired Devices
                  </Button>
                  <Button variant="outline" className="border-amber-500/30 text-amber-400">
                    Network Map
                  </Button>
                </div>
              </div>

              <div className="bg-gradient-to-br from-blue-900/20 to-gray-950/80 border border-blue-500/30 rounded-xl p-6">
                <h3 className="text-lg font-semibold text-blue-400 mb-4">Z-Wave Onboarding</h3>
                <p className="text-sm text-gray-400 mb-4">
                  Reliable wireless protocol for home automation
                </p>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <Button className="bg-blue-600 hover:bg-blue-700">
                    Add Z-Wave Device
                  </Button>
                  <Button variant="outline" className="border-blue-500/30 text-blue-400">
                    Remove Device
                  </Button>
                  <Button variant="outline" className="border-blue-500/30 text-blue-400">
                    Network Health
                  </Button>
                </div>
              </div>

              <div className="bg-gradient-to-br from-cyan-900/20 to-gray-950/80 border border-cyan-500/30 rounded-xl p-6">
                <h3 className="text-lg font-semibold text-cyan-400 mb-4">
                  Local Network Discovery
                </h3>
                <p className="text-sm text-gray-400 mb-4">
                  Find and configure devices on your local network
                </p>
                <Button className="bg-cyan-600 hover:bg-cyan-700">
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  Scan Network
                </Button>
              </div>
            </div>

            {/* Recently Added Devices */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h3 className="text-lg font-semibold text-gray-200 mb-4">Recently Added</h3>
              <div className="space-y-3">
                {recentlyAdded.map((device) => (
                  <div
                    key={device.name}
                    className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg"
                  >
                    <div>
                      <div className="font-medium text-gray-200">{device.name}</div>
                      <div className="text-sm text-gray-500">{device.type}</div>
                    </div>
                    <div className="text-right">
                      <Badge className="bg-green-500/20 text-green-400 border-green-500/30 mb-1">
                        {device.status}
                      </Badge>
                      <div className="text-xs text-gray-500">{device.time}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </TabsContent>

          {/* Quick Add Tab */}
          <TabsContent value="scan" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              {/* QR Code Scanner */}
              <div className="bg-gradient-to-br from-cyan-900/20 to-gray-950/80 border border-cyan-500/30 rounded-xl p-8">
                <div className="text-center">
                  <QrCode className="w-24 h-24 mx-auto text-cyan-400 mb-6" />
                  <h3 className="text-2xl font-semibold text-cyan-400 mb-3">Scan QR Code</h3>
                  <p className="text-gray-400 mb-6">
                    Point your camera at the device QR code for instant setup
                  </p>
                  <Button className="bg-cyan-600 hover:bg-cyan-700 w-full mb-4">
                    Open Camera Scanner
                  </Button>
                  <div className="text-sm text-gray-500">
                    Supports Matter, HomeKit, and WiFi QR codes
                  </div>
                </div>
              </div>

              {/* Auto Discovery */}
              <div className="bg-gradient-to-br from-blue-900/20 to-gray-950/80 border border-blue-500/30 rounded-xl p-8">
                <div className="text-center">
                  <Wifi className="w-24 h-24 mx-auto text-blue-400 mb-6" />
                  <h3 className="text-2xl font-semibold text-blue-400 mb-3">
                    Discover Nearby Devices
                  </h3>
                  <p className="text-gray-400 mb-6">
                    Automatically find devices on your network and ready to pair
                  </p>
                  <Button className="bg-blue-600 hover:bg-blue-700 w-full mb-4">
                    <Loader2 className="w-4 h-4 mr-2" />
                    Start Discovery
                  </Button>
                  <div className="text-sm text-gray-500">
                    Scans WiFi, Bluetooth, Zigbee, and Z-Wave
                  </div>
                </div>
              </div>
            </div>

            {/* AI Setup Assistant */}
            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-8">
              <div className="flex items-start gap-4">
                <div className="p-3 bg-purple-500/20 rounded-lg">
                  <HelpCircle className="w-8 h-8 text-purple-400" />
                </div>
                <div className="flex-1">
                  <h3 className="text-xl font-semibold text-purple-400 mb-2">
                    AI Setup Assistant
                  </h3>
                  <p className="text-gray-400 mb-4">
                    Get intelligent help with device identification, pairing troubleshooting,
                    compatibility checking, and setup recommendations
                  </p>
                  <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                    <Button
                      size="sm"
                      variant="outline"
                      className="border-purple-500/30 text-purple-400"
                    >
                      Best Setup Method
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      className="border-purple-500/30 text-purple-400"
                    >
                      Fix Pairing Issue
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      className="border-purple-500/30 text-purple-400"
                    >
                      Check Compatibility
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      className="border-purple-500/30 text-purple-400"
                    >
                      Suggest Automations
                    </Button>
                  </div>
                </div>
              </div>
            </div>

            {/* Compatibility Checker */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h3 className="text-lg font-semibold text-gray-200 mb-4">
                Device Compatibility Checker
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <Input
                  placeholder="Device brand..."
                  className="bg-gray-900/50 border-gray-800 text-gray-100"
                />
                <Input
                  placeholder="Device model..."
                  className="bg-gray-900/50 border-gray-800 text-gray-100"
                />
                <Button className="bg-cyan-600 hover:bg-cyan-700">Check Compatibility</Button>
              </div>
            </div>

            {/* Suggested Next Devices */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h3 className="text-lg font-semibold text-gray-200 mb-4">
                AI Suggested Next Devices
              </h3>
              <p className="text-sm text-gray-500 mb-4">
                Based on your current setup, consider adding:
              </p>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800">
                  <div className="font-medium text-gray-200 mb-1">Motion Sensor</div>
                  <div className="text-sm text-gray-500 mb-3">For hallway automation</div>
                  <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700">
                    Add Device
                  </Button>
                </div>
                <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800">
                  <div className="font-medium text-gray-200 mb-1">Smart Thermostat</div>
                  <div className="text-sm text-gray-500 mb-3">For climate control</div>
                  <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700">
                    Add Device
                  </Button>
                </div>
                <div className="p-4 bg-gray-900/50 rounded-lg border border-gray-800">
                  <div className="font-medium text-gray-200 mb-1">Door Sensor</div>
                  <div className="text-sm text-gray-500 mb-3">For security monitoring</div>
                  <Button size="sm" className="w-full bg-cyan-600 hover:bg-cyan-700">
                    Add Device
                  </Button>
                </div>
              </div>
            </div>
          </TabsContent>
        </Tabs>
      </div>
    </div>
  );
}
