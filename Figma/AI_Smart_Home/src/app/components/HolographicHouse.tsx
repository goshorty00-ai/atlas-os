import { motion } from 'motion/react';
import { useState } from 'react';
import { RoomControlPanel } from './RoomControlPanel';

export interface Room {
  id: string;
  name: string;
  position: { x: number; y: number; z: number };
  size: { width: number; height: number; depth: number };
  color: string;
  temperature: number;
  brightness: number;
  lightsOn: boolean;
  devicesActive: number;
}

const rooms: Room[] = [
  {
    id: 'living-room',
    name: 'Living Room',
    position: { x: -120, y: 0, z: 0 },
    size: { width: 140, height: 100, depth: 100 },
    color: '#00d4ff',
    temperature: 22,
    brightness: 75,
    lightsOn: true,
    devicesActive: 3,
  },
  {
    id: 'kitchen',
    name: 'Kitchen',
    position: { x: 40, y: 0, z: 0 },
    size: { width: 120, height: 100, depth: 80 },
    color: '#00d4ff',
    temperature: 21,
    brightness: 85,
    lightsOn: true,
    devicesActive: 5,
  },
  {
    id: 'bedroom',
    name: 'Bedroom',
    position: { x: -120, y: -120, z: 0 },
    size: { width: 140, height: 100, depth: 100 },
    color: '#00d4ff',
    temperature: 20,
    brightness: 30,
    lightsOn: true,
    devicesActive: 2,
  },
  {
    id: 'office',
    name: 'Office',
    position: { x: 40, y: -120, z: 0 },
    size: { width: 120, height: 100, depth: 80 },
    color: '#00d4ff',
    temperature: 22,
    brightness: 90,
    lightsOn: true,
    devicesActive: 4,
  },
  {
    id: 'bathroom',
    name: 'Bathroom',
    position: { x: -50, y: 110, z: 0 },
    size: { width: 60, height: 80, depth: 60 },
    color: '#00d4ff',
    temperature: 23,
    brightness: 60,
    lightsOn: false,
    devicesActive: 1,
  },
  {
    id: 'garage',
    name: 'Garage',
    position: { x: 180, y: 0, z: 0 },
    size: { width: 100, height: 90, depth: 120 },
    color: '#00d4ff',
    temperature: 18,
    brightness: 40,
    lightsOn: false,
    devicesActive: 1,
  },
];

export function HolographicHouse() {
  const [selectedRoom, setSelectedRoom] = useState<Room | null>(null);
  const [hoveredRoom, setHoveredRoom] = useState<string | null>(null);
  const [roomStates, setRoomStates] = useState<Room[]>(rooms);

  const updateRoom = (roomId: string, updates: Partial<Room>) => {
    setRoomStates(prev => 
      prev.map(room => room.id === roomId ? { ...room, ...updates } : room)
    );
  };

  return (
    <div className="relative w-full h-full flex items-center justify-center">
      {/* Circular Platform */}
      <div className="absolute bottom-0 w-[500px] h-[40px] rounded-full bg-gradient-to-r from-cyan-500/20 via-blue-500/30 to-cyan-500/20 blur-xl" />
      <div className="absolute bottom-0 w-[480px] h-[4px] rounded-full bg-gradient-to-r from-cyan-400 via-blue-400 to-cyan-400" 
           style={{ boxShadow: '0 0 40px rgba(0, 212, 255, 0.8)' }} />

      {/* 3D House Container */}
      <div className="relative w-full h-[600px] flex items-center justify-center perspective-[1200px]">
        <motion.div
          className="relative preserve-3d"
          animate={{
            rotateX: -20,
            rotateY: 0,
          }}
          style={{
            transformStyle: 'preserve-3d',
            transform: 'rotateX(-20deg) rotateY(0deg)',
          }}
        >
          {/* Scan Lines Effect */}
          <div className="absolute inset-0 pointer-events-none overflow-hidden">
            <motion.div
              className="absolute inset-x-0 h-px bg-gradient-to-r from-transparent via-cyan-400 to-transparent opacity-50"
              animate={{
                top: ['0%', '100%'],
              }}
              transition={{
                duration: 3,
                repeat: Infinity,
                ease: 'linear',
              }}
            />
          </div>

          {/* Rooms */}
          {roomStates.map((room) => {
            const isHovered = hoveredRoom === room.id;
            const isSelected = selectedRoom?.id === room.id;
            const isActive = isHovered || isSelected;

            return (
              <motion.div
                key={room.id}
                className="absolute cursor-pointer group"
                style={{
                  left: `${room.position.x}px`,
                  top: `${room.position.y}px`,
                  width: `${room.size.width}px`,
                  height: `${room.size.height}px`,
                  transformStyle: 'preserve-3d',
                }}
                onMouseEnter={() => setHoveredRoom(room.id)}
                onMouseLeave={() => setHoveredRoom(null)}
                onClick={() => setSelectedRoom(room)}
                whileHover={{ scale: 1.05 }}
              >
                {/* Room Box */}
                <div
                  className="absolute inset-0 border-2 rounded-lg transition-all duration-300"
                  style={{
                    borderColor: isActive ? '#ff8c00' : room.color,
                    backgroundColor: isActive 
                      ? 'rgba(0, 212, 255, 0.15)' 
                      : 'rgba(0, 212, 255, 0.05)',
                    boxShadow: isActive 
                      ? `0 0 40px ${room.color}, inset 0 0 40px rgba(0, 212, 255, 0.2)`
                      : `0 0 20px ${room.color}40, inset 0 0 20px rgba(0, 212, 255, 0.1)`,
                  }}
                >
                  {/* Holographic Grid */}
                  <div className="absolute inset-0 opacity-30"
                       style={{
                         backgroundImage: 'linear-gradient(0deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent), linear-gradient(90deg, transparent 24%, rgba(0, 212, 255, 0.3) 25%, rgba(0, 212, 255, 0.3) 26%, transparent 27%, transparent 74%, rgba(0, 212, 255, 0.3) 75%, rgba(0, 212, 255, 0.3) 76%, transparent 77%, transparent)',
                         backgroundSize: '50px 50px',
                       }}
                  />

                  {/* Energy Particles */}
                  {isActive && (
                    <>
                      {[...Array(6)].map((_, i) => (
                        <motion.div
                          key={i}
                          className="absolute w-1 h-1 rounded-full bg-cyan-400"
                          style={{
                            left: `${Math.random() * 100}%`,
                            top: `${Math.random() * 100}%`,
                          }}
                          animate={{
                            opacity: [0, 1, 0],
                            scale: [0, 1.5, 0],
                          }}
                          transition={{
                            duration: 2,
                            repeat: Infinity,
                            delay: i * 0.3,
                          }}
                        />
                      ))}
                    </>
                  )}
                </div>

                {/* Room Label */}
                <div className="absolute -top-8 left-1/2 -translate-x-1/2 whitespace-nowrap">
                  <motion.div
                    className="px-3 py-1 rounded-full text-xs font-medium backdrop-blur-sm"
                    style={{
                      background: isActive 
                        ? 'rgba(255, 140, 0, 0.2)' 
                        : 'rgba(0, 212, 255, 0.1)',
                      border: `1px solid ${isActive ? '#ff8c00' : room.color}`,
                      color: isActive ? '#ff8c00' : room.color,
                      boxShadow: `0 0 20px ${isActive ? '#ff8c0040' : room.color}40`,
                    }}
                    animate={{
                      opacity: isActive ? 1 : 0.7,
                    }}
                  >
                    {room.name}
                  </motion.div>
                </div>

                {/* Status Indicator */}
                {room.lightsOn && (
                  <motion.div
                    className="absolute -top-2 -right-2 w-3 h-3 rounded-full bg-cyan-400"
                    style={{
                      boxShadow: '0 0 10px #00d4ff',
                    }}
                    animate={{
                      opacity: [0.5, 1, 0.5],
                    }}
                    transition={{
                      duration: 2,
                      repeat: Infinity,
                    }}
                  />
                )}
              </motion.div>
            );
          })}
        </motion.div>
      </div>

      {/* Room Control Panel */}
      {selectedRoom && (
        <RoomControlPanel
          room={selectedRoom}
          onClose={() => setSelectedRoom(null)}
          onUpdate={(updates) => updateRoom(selectedRoom.id, updates)}
        />
      )}
    </div>
  );
}
