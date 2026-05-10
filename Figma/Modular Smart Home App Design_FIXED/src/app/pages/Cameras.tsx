import { useState } from "react";
import { Camera, Grid3x3, Maximize2, Play, Pause, Download, Disc } from "lucide-react";
import { Button } from "../components/ui/button";
import { Badge } from "../components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "../components/ui/tabs";

const cameras = [
  {
    id: 1,
    name: "Front Door",
    location: "Entrance",
    status: "recording",
    lastMotion: "2 min ago",
    resolution: "1080p",
  },
  {
    id: 2,
    name: "Backyard",
    location: "Outdoor",
    status: "recording",
    lastMotion: "5 min ago",
    resolution: "1080p",
  },
  {
    id: 3,
    name: "Garage",
    location: "Garage",
    status: "recording",
    lastMotion: "15 min ago",
    resolution: "720p",
  },
  {
    id: 4,
    name: "Living Room",
    location: "Indoor",
    status: "idle",
    lastMotion: "1 hour ago",
    resolution: "1080p",
  },
  {
    id: 5,
    name: "Driveway",
    location: "Outdoor",
    status: "recording",
    lastMotion: "3 min ago",
    resolution: "2K",
  },
  {
    id: 6,
    name: "Side Entrance",
    location: "Outdoor",
    status: "idle",
    lastMotion: "30 min ago",
    resolution: "1080p",
  },
];

const motionEvents = [
  { camera: "Front Door", time: "2 min ago", type: "Person detected" },
  { camera: "Driveway", time: "3 min ago", type: "Vehicle detected" },
  { camera: "Backyard", time: "5 min ago", type: "Motion detected" },
  { camera: "Garage", time: "15 min ago", type: "Motion detected" },
];

export function Cameras() {
  const [viewMode, setViewMode] = useState<"grid" | "single">("grid");
  const [isRecording, setIsRecording] = useState(false);

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-950 via-blue-950/20 to-black p-8">
      <div className="max-w-[1800px] mx-auto">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-4xl font-bold text-transparent bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text mb-2">
              Camera System
            </h1>
            <p className="text-gray-400">{cameras.length} cameras active</p>
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              className={`border-gray-700 ${viewMode === "grid" ? "bg-cyan-500/20 border-cyan-500/30" : ""}`}
              onClick={() => setViewMode("grid")}
            >
              <Grid3x3 className="w-4 h-4" />
            </Button>
            <Button
              variant="outline"
              className={`border-gray-700 ${viewMode === "single" ? "bg-cyan-500/20 border-cyan-500/30" : ""}`}
              onClick={() => setViewMode("single")}
            >
              <Maximize2 className="w-4 h-4" />
            </Button>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Camera Feeds */}
          <div className="lg:col-span-2">
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-xl font-semibold text-cyan-400">Live Feeds</h2>
                <Button
                  size="sm"
                  className={`${
                    isRecording
                      ? "bg-red-600 hover:bg-red-700"
                      : "bg-cyan-600 hover:bg-cyan-700"
                  }`}
                  onClick={() => setIsRecording(!isRecording)}
                >
                  {isRecording ? (
                    <>
                      <Pause className="w-4 h-4 mr-2" />
                      Stop Recording
                    </>
                  ) : (
                    <>
                      <Disc className="w-4 h-4 mr-2" />
                      Record All
                    </>
                  )}
                </Button>
              </div>

              {viewMode === "grid" ? (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {cameras.map((camera) => (
                    <div
                      key={camera.id}
                      className="bg-gray-950 border border-gray-800 rounded-lg overflow-hidden hover:border-cyan-500/30 transition-all cursor-pointer group"
                    >
                      <div className="relative aspect-video bg-gradient-to-br from-gray-800 to-gray-900 flex items-center justify-center">
                        <Camera className="w-12 h-12 text-gray-600 group-hover:text-cyan-500 transition-colors" />
                        {camera.status === "recording" && (
                          <div className="absolute top-2 left-2">
                            <Badge className="bg-red-600/90 text-white border-red-600">
                              <div className="w-2 h-2 bg-white rounded-full mr-1 animate-pulse" />
                              REC
                            </Badge>
                          </div>
                        )}
                        <div className="absolute top-2 right-2">
                          <Badge className="bg-black/70 text-white border-gray-700">
                            {camera.resolution}
                          </Badge>
                        </div>
                        <div className="absolute bottom-2 left-2 right-2 flex gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            className="flex-1 bg-black/70 border-gray-700 hover:bg-black/90"
                          >
                            <Play className="w-3 h-3 mr-1" />
                            Play
                          </Button>
                          <Button
                            size="sm"
                            variant="outline"
                            className="bg-black/70 border-gray-700 hover:bg-black/90"
                          >
                            <Download className="w-3 h-3" />
                          </Button>
                        </div>
                      </div>
                      <div className="p-3">
                        <div className="font-semibold text-gray-200">{camera.name}</div>
                        <div className="flex items-center justify-between mt-1">
                          <span className="text-xs text-gray-500">{camera.location}</span>
                          <span className="text-xs text-gray-400">{camera.lastMotion}</span>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="space-y-4">
                  <div className="bg-gray-950 border border-gray-800 rounded-lg overflow-hidden">
                    <div className="relative aspect-video bg-gradient-to-br from-gray-800 to-gray-900 flex items-center justify-center">
                      <Camera className="w-24 h-24 text-gray-600" />
                      <div className="absolute top-4 left-4">
                        <Badge className="bg-red-600/90 text-white border-red-600">
                          <div className="w-2 h-2 bg-white rounded-full mr-1 animate-pulse" />
                          REC
                        </Badge>
                      </div>
                      <div className="absolute top-4 right-4">
                        <Badge className="bg-black/70 text-white border-gray-700">1080p</Badge>
                      </div>
                      <div className="absolute bottom-4 left-4 right-4 flex gap-2">
                        <Button
                          size="sm"
                          variant="outline"
                          className="bg-black/70 border-gray-700 hover:bg-black/90"
                        >
                          <Play className="w-4 h-4 mr-2" />
                          Play
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          className="bg-black/70 border-gray-700 hover:bg-black/90"
                        >
                          <Download className="w-4 h-4 mr-2" />
                          Export Clip
                        </Button>
                      </div>
                    </div>
                    <div className="p-4">
                      <div className="font-semibold text-gray-200 text-lg">Front Door</div>
                      <div className="text-sm text-gray-500">Entrance • Last motion: 2 min ago</div>
                    </div>
                  </div>
                  <div className="grid grid-cols-6 gap-2">
                    {cameras.map((camera) => (
                      <div
                        key={camera.id}
                        className="bg-gray-950 border border-gray-800 rounded-lg overflow-hidden hover:border-cyan-500/30 transition-all cursor-pointer"
                      >
                        <div className="aspect-video bg-gradient-to-br from-gray-800 to-gray-900 flex items-center justify-center">
                          <Camera className="w-4 h-4 text-gray-600" />
                        </div>
                        <div className="p-1 text-xs text-gray-400 text-center truncate">
                          {camera.name}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Motion Events & Controls */}
          <div className="space-y-6">
            {/* Motion Events */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Motion Events</h2>
              <div className="space-y-3">
                {motionEvents.map((event, index) => (
                  <div
                    key={index}
                    className="p-3 bg-gray-900/50 rounded-lg border border-gray-800 hover:border-cyan-500/30 transition-all cursor-pointer"
                  >
                    <div className="flex items-center gap-2 mb-1">
                      <Camera className="w-4 h-4 text-cyan-400" />
                      <div className="font-medium text-gray-200">{event.camera}</div>
                    </div>
                    <div className="text-sm text-gray-400">{event.type}</div>
                    <div className="text-xs text-gray-500 mt-1">{event.time}</div>
                  </div>
                ))}
              </div>
            </div>

            {/* AI Detection Settings */}
            <div className="bg-gradient-to-br from-purple-900/20 to-gray-950/80 border border-purple-500/30 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-purple-400 mb-4">AI Detection</h2>
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">Person Detection</span>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">
                    Active
                  </Badge>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">Vehicle Detection</span>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">
                    Active
                  </Badge>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">Animal Detection</span>
                  <Badge className="bg-gray-700/20 text-gray-400 border-gray-700/30">
                    Off
                  </Badge>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-gray-400">Package Detection</span>
                  <Badge className="bg-green-500/20 text-green-400 border-green-500/30">
                    Active
                  </Badge>
                </div>
              </div>
            </div>

            {/* Recording Storage */}
            <div className="bg-gradient-to-br from-gray-900/80 to-gray-950/80 border border-gray-800/50 rounded-xl p-6">
              <h2 className="text-xl font-semibold text-cyan-400 mb-4">Storage</h2>
              <div className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-gray-400">Used:</span>
                  <span className="text-gray-300">245 GB</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-400">Available:</span>
                  <span className="text-gray-300">755 GB</span>
                </div>
                <div className="w-full bg-gray-800 rounded-full h-2 mt-2">
                  <div
                    className="bg-cyan-500 h-2 rounded-full"
                    style={{ width: "24.5%" }}
                  ></div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
