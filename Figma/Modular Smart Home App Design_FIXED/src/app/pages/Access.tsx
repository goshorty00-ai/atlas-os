import { Lock, DoorOpen, Key, Clock, User, Plus } from "lucide-react";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";

const locks = [
  { name: "Front Door", status: "locked", battery: 85, location: "Entrance" },
  { name: "Back Door", status: "locked", battery: 72, location: "Kitchen" },
  { name: "Garage Door", status: "closed", battery: 90, location: "Garage" },
  { name: "Side Gate", status: "locked", battery: 65, location: "Backyard" },
];

const accessCodes = [
  { name: "Family Code", code: "****", type: "permanent", users: "All family members" },
  { name: "Cleaner", code: "****", type: "recurring", users: "Tuesday & Thursday 9-11 AM" },
  { name: "Dog Walker", code: "****", type: "recurring", users: "Weekdays 3-4 PM" },
  { name: "Guest - Mike", code: "****", type: "temporary", users: "Valid until Apr 10" },
];

const recentEntries = [
  { person: "John Smith", location: "Front Door", action: "Unlocked", time: "2 min ago" },
  { person: "Sarah Smith", location: "Garage Door", action: "Opened", time: "1 hour ago" },
  { person: "Guest Code", location: "Front Door", action: "Unlocked", time: "3 hours ago" },
  { person: "John Smith", location: "Front Door", action: "Locked", time: "8 hours ago" },
  { person: "Sarah Smith", location: "Back Door", action: "Unlocked", time: "Yesterday" },
];

export function Access() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="mb-8">
          <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
            Access Control
          </h1>
          <p className="text-gray-400">Manage locks, codes, and entry logs</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Smart Locks */}
          <div className="lg:col-span-2 space-y-6">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-xl font-semibold text-cyan-400">Smart Locks & Doors</h2>
                <Button className="bg-cyan-600 hover:bg-cyan-700">
                  <Lock className="w-4 h-4 mr-2" />
                  Lock All
                </Button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {locks.map((lock) => (
                  <div
                    key={lock.name}
                    className="bg-gradient-to-br from-gray-900/50 to-gray-950/50 border border-gray-800 rounded-lg p-5"
                  >
                    <div className="flex items-start justify-between mb-4">
                      <div>
                        <h3 className="font-semibold text-gray-200 mb-1">{lock.name}</h3>
                        <div className="text-xs text-gray-500">{lock.location}</div>
                      </div>
                      {lock.status === "locked" || lock.status === "closed" ? (
                        <Lock className="w-6 h-6 text-green-400" />
                      ) : (
                        <DoorOpen className="w-6 h-6 text-yellow-400" />
                      )}
                    </div>

                    <div className="mb-4">
                      <Badge
                        className={
                          lock.status === "locked" || lock.status === "closed"
                            ? "bg-green-500/20 text-green-400 border-green-500/30"
                            : "bg-yellow-500/20 text-yellow-400 border-yellow-500/30"
                        }
                      >
                        {lock.status}
                      </Badge>
                    </div>

                    <div className="space-y-2 mb-4 text-sm">
                      <div className="flex justify-between">
                        <span className="text-gray-400">Battery:</span>
                        <span
                          className={
                            lock.battery > 80
                              ? "text-green-400"
                              : lock.battery > 50
                              ? "text-yellow-400"
                              : "text-red-400"
                          }
                        >
                          {lock.battery}%
                        </span>
                      </div>
                    </div>

                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" className="flex-1 border-gray-700">
                        {lock.status === "locked" ? "Unlock" : "Lock"}
                      </Button>
                      <Button size="sm" variant="outline" className="border-gray-700">
                        Details
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Access Codes */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-xl font-semibold text-cyan-400">Access Codes</h2>
                <Button className="bg-cyan-600 hover:bg-cyan-700">
                  <Plus className="w-4 h-4 mr-2" />
                  New Code
                </Button>
              </div>

              <div className="space-y-3">
                {accessCodes.map((code) => (
                  <div
                    key={code.name}
                    className="p-4 bg-gray-900/50 rounded-lg border border-gray-800"
                  >
                    <div className="flex items-start justify-between mb-3">
                      <div className="flex items-start gap-3">
                        <Key className="w-5 h-5 text-cyan-400 mt-0.5" />
                        <div>
                          <div className="font-semibold text-gray-200 mb-1">{code.name}</div>
                          <div className="text-sm text-gray-400 mb-2">{code.users}</div>
                          <div className="flex gap-2">
                            <Badge
                              className={
                                code.type === "permanent"
                                  ? "bg-green-500/20 text-green-400 border-green-500/30"
                                  : code.type === "recurring"
                                  ? "bg-blue-500/20 text-blue-400 border-blue-500/30"
                                  : "bg-yellow-500/20 text-yellow-400 border-yellow-500/30"
                              }
                            >
                              {code.type}
                            </Badge>
                            <Badge
                              variant="outline"
                              className="border-gray-700 text-gray-400 font-mono"
                            >
                              {code.code}
                            </Badge>
                          </div>
                        </div>
                      </div>
                      <Button size="sm" variant="outline" className="border-gray-700">
                        Edit
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Entry Logs */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-xl font-semibold text-cyan-400">Recent Entry Logs</h2>
                <Button size="sm" variant="outline" className="border-gray-700">
                  View All
                </Button>
              </div>

              <div className="space-y-3">
                {recentEntries.map((entry, index) => (
                  <div
                    key={index}
                    className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg border border-gray-800"
                  >
                    <div className="flex items-center gap-3">
                      <div className="p-2 bg-cyan-500/20 rounded-lg">
                        <User className="w-4 h-4 text-cyan-400" />
                      </div>
                      <div>
                        <div className="font-medium text-gray-200">{entry.person}</div>
                        <div className="text-sm text-gray-400">
                          {entry.action} • {entry.location}
                        </div>
                      </div>
                    </div>
                    <div className="text-right">
                      <div className="text-xs text-gray-500">{entry.time}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* Quick Actions & Stats */}
          <div className="space-y-6">
            <div className="bg-gradient-to-br from-cyan-900/20 to-gray-950/80 border border-cyan-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Quick Actions</h2>
              <div className="space-y-3">
                <Button className="w-full bg-cyan-600 hover:bg-cyan-700 h-14 text-lg">
                  <Lock className="w-5 h-5 mr-2" />
                  Lock Everything
                </Button>
                <Button
                  variant="outline"
                  className="w-full border-cyan-500/30 text-cyan-400 h-14 text-lg"
                >
                  <DoorOpen className="w-5 h-5 mr-2" />
                  Unlock Front Door
                </Button>
                <Button
                  variant="outline"
                  className="w-full border-cyan-500/30 text-cyan-400 h-14 text-lg"
                >
                  <Key className="w-5 h-5 mr-2" />
                  Generate Guest Code
                </Button>
              </div>
            </div>

            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Today's Stats</h2>
              <div className="space-y-4">
                <div>
                  <div className="text-sm text-gray-500 mb-1">Total Entries</div>
                  <div className="text-3xl font-bold text-gray-100">12</div>
                </div>
                <div>
                  <div className="text-sm text-gray-500 mb-1">Unique Visitors</div>
                  <div className="text-3xl font-bold text-gray-100">4</div>
                </div>
                <div>
                  <div className="text-sm text-gray-500 mb-1">Guest Access Used</div>
                  <div className="text-3xl font-bold text-gray-100">2</div>
                </div>
              </div>
            </div>

            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-purple-400 mb-4">Security Tips</h2>
              <div className="space-y-3 text-sm">
                <div className="p-3 bg-gray-900/50 rounded-lg">
                  <div className="text-gray-200 mb-1">Rotate Guest Codes</div>
                  <div className="text-gray-400">Change temporary codes regularly</div>
                </div>
                <div className="p-3 bg-gray-900/50 rounded-lg">
                  <div className="text-gray-200 mb-1">Check Battery Levels</div>
                  <div className="text-gray-400">Replace batteries before they run out</div>
                </div>
                <div className="p-3 bg-gray-900/50 rounded-lg">
                  <div className="text-gray-200 mb-1">Review Entry Logs</div>
                  <div className="text-gray-400">Monitor access patterns weekly</div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
