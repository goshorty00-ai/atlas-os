import { useState } from "react";
import { Bell, AlertTriangle, Info, CheckCircle, Search, Filter } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";

const alerts = [
  {
    id: 1,
    title: "Motion detected at front door",
    description: "Front door camera detected person",
    time: "2 min ago",
    severity: "medium",
    type: "security",
    read: false,
  },
  {
    id: 2,
    title: "Living room temperature high",
    description: "Temperature reached 26°C",
    time: "15 min ago",
    severity: "low",
    type: "climate",
    read: false,
  },
  {
    id: 3,
    title: "Front door unlocked",
    description: "Door unlocked by John Smith",
    time: "1 hour ago",
    severity: "low",
    type: "access",
    read: true,
  },
  {
    id: 4,
    title: "Garage door left open",
    description: "Garage door has been open for 30 minutes",
    time: "2 hours ago",
    severity: "medium",
    type: "security",
    read: true,
  },
  {
    id: 5,
    title: "Low battery on bedroom sensor",
    description: "Motion sensor battery at 15%",
    time: "3 hours ago",
    severity: "low",
    type: "maintenance",
    read: true,
  },
  {
    id: 6,
    title: "Water leak detected",
    description: "Basement leak sensor triggered",
    time: "Yesterday",
    severity: "high",
    type: "emergency",
    read: true,
  },
  {
    id: 7,
    title: "Automation failed",
    description: "Good Morning routine could not complete",
    time: "Yesterday",
    severity: "medium",
    type: "system",
    read: true,
  },
  {
    id: 8,
    title: "Device offline",
    description: "Backyard camera lost connection",
    time: "2 days ago",
    severity: "medium",
    type: "system",
    read: true,
  },
];

const severityColors = {
  high: {
    bg: "bg-red-900/20",
    border: "border-red-500/30",
    text: "text-red-400",
    badge: "bg-red-500/20 text-red-400 border-red-500/30",
  },
  medium: {
    bg: "bg-yellow-900/20",
    border: "border-yellow-500/30",
    text: "text-yellow-400",
    badge: "bg-yellow-500/20 text-yellow-400 border-yellow-500/30",
  },
  low: {
    bg: "bg-blue-900/20",
    border: "border-blue-500/30",
    text: "text-blue-400",
    badge: "bg-blue-500/20 text-blue-400 border-blue-500/30",
  },
};

const getSeverityIcon = (severity: string) => {
  switch (severity) {
    case "high":
      return AlertTriangle;
    case "medium":
      return Bell;
    default:
      return Info;
  }
};

export function Alerts() {
  const [searchQuery, setSearchQuery] = useState("");
  const [filterSeverity, setFilterSeverity] = useState("all");

  const unreadCount = alerts.filter((a) => !a.read).length;

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Alerts & Events
            </h1>
            <p className="text-gray-400">
              {unreadCount} unread alert{unreadCount !== 1 ? "s" : ""}
            </p>
          </div>
          <Button className="bg-cyan-600 hover:bg-cyan-700">Mark All as Read</Button>
        </div>

        {/* Summary Cards */}
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
          <div className="bg-gradient-to-br from-red-900/20 to-gray-950/80 border border-red-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <AlertTriangle className="w-6 h-6 text-red-400" />
              <Badge className="bg-red-500/20 text-red-400 border-red-500/30">High</Badge>
            </div>
            <div className="text-3xl font-bold text-gray-100">
              {alerts.filter((a) => a.severity === "high").length}
            </div>
            <div className="text-sm text-gray-400">Critical Alerts</div>
          </div>

          <div className="bg-gradient-to-br from-yellow-900/20 to-gray-950/80 border border-yellow-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <Bell className="w-6 h-6 text-yellow-400" />
              <Badge className="bg-yellow-500/20 text-yellow-400 border-yellow-500/30">Medium</Badge>
            </div>
            <div className="text-3xl font-bold text-gray-100">
              {alerts.filter((a) => a.severity === "medium").length}
            </div>
            <div className="text-sm text-gray-400">Important Alerts</div>
          </div>

          <div className="bg-gradient-to-br from-blue-900/20 to-gray-950/80 border border-blue-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <Info className="w-6 h-6 text-blue-400" />
              <Badge className="bg-blue-500/20 text-blue-400 border-blue-500/30">Low</Badge>
            </div>
            <div className="text-3xl font-bold text-gray-100">
              {alerts.filter((a) => a.severity === "low").length}
            </div>
            <div className="text-sm text-gray-400">Info Alerts</div>
          </div>

          <div className="bg-gradient-to-br from-green-900/20 to-gray-950/80 border border-green-500/30 rounded-xl p-6">
            <div className="flex items-center justify-between mb-2">
              <CheckCircle className="w-6 h-6 text-green-400" />
              <Badge className="bg-green-500/20 text-green-400 border-green-500/30">Read</Badge>
            </div>
            <div className="text-3xl font-bold text-gray-100">
              {alerts.filter((a) => a.read).length}
            </div>
            <div className="text-sm text-gray-400">Acknowledged</div>
          </div>
        </div>

        {/* Search and Filter */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
          <div className="md:col-span-2">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
              <Input
                placeholder="Search alerts..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="pl-10 bg-gray-900/50 border-gray-800 text-gray-100"
              />
            </div>
          </div>
          <select
            value={filterSeverity}
            onChange={(e) => setFilterSeverity(e.target.value)}
            className="px-4 py-2 bg-gray-900/50 border border-gray-800 rounded-lg text-gray-100"
          >
            <option value="all">All Severities</option>
            <option value="high">High</option>
            <option value="medium">Medium</option>
            <option value="low">Low</option>
          </select>
        </div>

        {/* Alerts Timeline */}
        <div className="space-y-3">
          {alerts.map((alert) => {
            const colors = severityColors[alert.severity as keyof typeof severityColors];
            const Icon = getSeverityIcon(alert.severity);

            return (
              <div
                key={alert.id}
                className={`${colors.bg} border ${colors.border} rounded-xl p-5 hover:border-cyan-500/30 transition-all ${
                  !alert.read ? "border-l-4" : ""
                }`}
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start gap-4">
                    <div className={`p-2 ${colors.bg} rounded-lg`}>
                      <Icon className={`w-6 h-6 ${colors.text}`} />
                    </div>
                    <div className="flex-1">
                      <div className="flex items-center gap-2 mb-1">
                        <h3 className="font-semibold text-gray-200">{alert.title}</h3>
                        {!alert.read && (
                          <Badge className="bg-cyan-500/20 text-cyan-400 border-cyan-500/30 text-xs">
                            New
                          </Badge>
                        )}
                      </div>
                      <p className="text-gray-400 mb-2">{alert.description}</p>
                      <div className="flex items-center gap-4 text-sm">
                        <span className="text-gray-500">{alert.time}</span>
                        <Badge className={colors.badge}>{alert.severity}</Badge>
                        <Badge variant="outline" className="border-gray-700 text-gray-400">
                          {alert.type}
                        </Badge>
                      </div>
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button size="sm" variant="outline" className="border-gray-700">
                      View
                    </Button>
                    {!alert.read && (
                      <Button size="sm" variant="outline" className="border-gray-700">
                        Dismiss
                      </Button>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Alert Settings */}
        <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6 mt-8">
          <h2 className="text-xl font-semibold text-purple-400 mb-4">Alert Preferences</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Push Notifications</div>
              <div className="text-sm text-gray-400 mb-3">
                Get instant alerts on your devices
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Configure
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">Email Alerts</div>
              <div className="text-sm text-gray-400 mb-3">
                Receive daily summaries via email
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Configure
              </Button>
            </div>
            <div className="p-4 bg-gray-900/50 rounded-lg">
              <div className="font-medium text-gray-200 mb-2">SMS Alerts</div>
              <div className="text-sm text-gray-400 mb-3">
                Critical alerts sent via SMS
              </div>
              <Button size="sm" className="w-full bg-purple-600 hover:bg-purple-700">
                Configure
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
