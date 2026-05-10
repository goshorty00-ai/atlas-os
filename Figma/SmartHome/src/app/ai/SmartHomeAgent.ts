// Smart Home AI Agent - Domain-specific intelligence

import type { 
  SmartHomeDevice, 
  SmartHomeSnapshot, 
  SmartHomeProviderState 
} from '../useSmartHome';
import type { AITool, AIContext, StructuredResponse } from './types';
import { AICore } from './AICore';

export class SmartHomeAgent {
  private aiCore: AICore;
  private state: SmartHomeSnapshot | null = null;
  private executeAction: Function;
  private isDeviceOn: Function;

  constructor(
    aiCore: AICore,
    executeAction: Function,
    isDeviceOn: Function
  ) {
    this.aiCore = aiCore;
    this.executeAction = executeAction;
    this.isDeviceOn = isDeviceOn;
    this.registerTools();
  }

  updateState(state: SmartHomeSnapshot | null) {
    this.state = state;
  }

  async processQuery(query: string): Promise<{
    response: string;
    structured?: StructuredResponse;
    actions?: any[];
    usage?: any;
  }> {
    if (!this.state) {
      return {
        response: "Smart home system is not available. Please check your connection and try again."
      };
    }

    const context = this.buildContext();
    const aiResponse = await this.aiCore.execute(query, context, undefined, undefined, 'smart_home');

    return {
      response: aiResponse.content,
      structured: aiResponse.structured,
      actions: aiResponse.structured?.actions,
      usage: aiResponse.usage
    };
  }

  private buildContext(): AIContext {
    if (!this.state) {
      return {
        devices: [],
        providers: [],
        recentEvents: [],
        userPreferences: {},
        timeOfDay: this.getTimeOfDay()
      };
    }

    const allDevices = this.state.providers.flatMap(p => 
      p.devices.map(d => ({
        ...d,
        _providerId: p.providerId,
        _providerName: p.displayName,
        _isOn: this.isDeviceOn(d)
      }))
    );

    return {
      devices: allDevices,
      providers: this.state.providers,
      recentEvents: [], // TODO: Add event tracking
      userPreferences: this.state.agentSettings,
      timeOfDay: this.getTimeOfDay()
    };
  }

  private getTimeOfDay(): 'morning' | 'afternoon' | 'evening' | 'night' {
    const hour = new Date().getHours();
    if (hour < 6) return 'night';
    if (hour < 12) return 'morning';
    if (hour < 18) return 'afternoon';
    if (hour < 22) return 'evening';
    return 'night';
  }

  private registerTools() {
    // Get device state
    this.aiCore.registerTool({
      name: 'get_device_state',
      description: 'Get current state of specific devices or all devices',
      parameters: {
        type: 'object',
        properties: {
          deviceName: {
            type: 'string',
            description: 'Name of specific device, or "all" for all devices'
          },
          providerId: {
            type: 'string',
            description: 'Filter by provider ID (optional)'
          }
        },
        required: ['deviceName']
      },
      handler: async (args) => {
        return this.getDeviceState(args.deviceName, args.providerId);
      }
    });

    // Get devices by room/location
    this.aiCore.registerTool({
      name: 'get_room_devices',
      description: 'Get devices in a specific room or location',
      parameters: {
        type: 'object',
        properties: {
          room: {
            type: 'string',
            description: 'Room name (kitchen, bedroom, living room, etc.)'
          }
        },
        required: ['room']
      },
      handler: async (args) => {
        return this.getRoomDevices(args.room);
      }
    });

    // Control device
    this.aiCore.registerTool({
      name: 'device_control',
      description: 'Control a smart home device (turn on/off, adjust brightness, etc.)',
      parameters: {
        type: 'object',
        properties: {
          deviceName: {
            type: 'string',
            description: 'Name of the device to control'
          },
          action: {
            type: 'string',
            description: 'Action to perform: on, off, brightness, color, scene'
          },
          value: {
            description: 'Value for the action (brightness level, color, etc.)'
          }
        },
        required: ['deviceName', 'action']
      },
      handler: async (args) => {
        return this.controlDevice(args.deviceName, args.action, args.value);
      }
    });

    // Get offline devices
    this.aiCore.registerTool({
      name: 'get_offline_devices',
      description: 'Get list of devices that are currently offline',
      parameters: {
        type: 'object',
        properties: {},
        required: []
      },
      handler: async () => {
        return this.getOfflineDevices();
      }
    });

    // Get devices by type
    this.aiCore.registerTool({
      name: 'get_devices_by_type',
      description: 'Get devices of a specific type (lights, cameras, speakers, etc.)',
      parameters: {
        type: 'object',
        properties: {
          deviceType: {
            type: 'string',
            description: 'Type of device: light, camera, speaker, tv, etc.'
          }
        },
        required: ['deviceType']
      },
      handler: async (args) => {
        return this.getDevicesByType(args.deviceType);
      }
    });
  }

  private async getDeviceState(deviceName: string, providerId?: string) {
    if (!this.state) return { error: 'No smart home data available' };

    if (deviceName.toLowerCase() === 'all') {
      const allDevices = this.state.providers.flatMap(p => 
        p.devices.map(d => ({
          name: d.name,
          type: d.deviceType,
          online: d.isOnline !== false,
          on: this.isDeviceOn(d),
          provider: p.displayName,
          capabilities: d.capabilities.length
        }))
      );
      return { devices: allDevices, total: allDevices.length };
    }

    // Find specific device
    for (const provider of this.state.providers) {
      if (providerId && provider.providerId !== providerId) continue;
      
      const device = provider.devices.find(d => 
        d.name.toLowerCase().includes(deviceName.toLowerCase())
      );
      
      if (device) {
        return {
          name: device.name,
          type: device.deviceType,
          online: device.isOnline !== false,
          on: this.isDeviceOn(device),
          provider: provider.displayName,
          capabilities: device.capabilities.map(c => ({
            type: c.type,
            instance: c.instance,
            hasState: c.hasState,
            value: c.stateValue
          }))
        };
      }
    }

    return { error: `Device "${deviceName}" not found` };
  }

  private async getRoomDevices(room: string) {
    if (!this.state) return { error: 'No smart home data available' };

    const roomLower = room.toLowerCase();
    const allDevices = this.state.providers.flatMap(p => 
      p.devices.map(d => ({ ...d, _providerId: p.providerId, _providerName: p.displayName }))
    );

    // Simple room matching based on device names
    const roomDevices = allDevices.filter(d => 
      d.name.toLowerCase().includes(roomLower) ||
      d.name.toLowerCase().includes(roomLower.replace(' ', ''))
    );

    return {
      room,
      devices: roomDevices.map(d => ({
        name: d.name,
        type: d.deviceType,
        online: d.isOnline !== false,
        on: this.isDeviceOn(d),
        provider: d._providerName
      })),
      total: roomDevices.length
    };
  }

  private async controlDevice(deviceName: string, action: string, value?: any) {
    if (!this.state) return { error: 'No smart home data available' };

    // Find device
    let targetDevice = null;
    let targetProvider = null;

    for (const provider of this.state.providers) {
      const device = provider.devices.find(d => 
        d.name.toLowerCase().includes(deviceName.toLowerCase())
      );
      if (device) {
        targetDevice = device;
        targetProvider = provider;
        break;
      }
    }

    if (!targetDevice || !targetProvider) {
      return { error: `Device "${deviceName}" not found` };
    }

    try {
      switch (action.toLowerCase()) {
        case 'on':
          await this.executeAction(
            targetProvider.providerId,
            targetDevice.deviceId,
            targetDevice.sku,
            'devices.capabilities.on_off',
            'powerSwitch',
            true
          );
          return { success: true, message: `Turned on ${targetDevice.name}` };

        case 'off':
          await this.executeAction(
            targetProvider.providerId,
            targetDevice.deviceId,
            targetDevice.sku,
            'devices.capabilities.on_off',
            'powerSwitch',
            false
          );
          return { success: true, message: `Turned off ${targetDevice.name}` };

        case 'brightness':
          if (value === undefined) return { error: 'Brightness value required' };
          await this.executeAction(
            targetProvider.providerId,
            targetDevice.deviceId,
            targetDevice.sku,
            'devices.capabilities.range',
            'brightness',
            value
          );
          return { success: true, message: `Set ${targetDevice.name} brightness to ${value}%` };

        default:
          return { error: `Action "${action}" not supported` };
      }
    } catch (error) {
      return { error: `Failed to control device: ${error.message}` };
    }
  }

  private async getOfflineDevices() {
    if (!this.state) return { error: 'No smart home data available' };

    const offlineDevices = this.state.providers.flatMap(p => 
      p.devices
        .filter(d => d.isOnline === false)
        .map(d => ({
          name: d.name,
          type: d.deviceType,
          provider: p.displayName,
          lastSeen: 'Unknown' // TODO: Add timestamp tracking
        }))
    );

    return {
      devices: offlineDevices,
      total: offlineDevices.length
    };
  }

  private async getDevicesByType(deviceType: string) {
    if (!this.state) return { error: 'No smart home data available' };

    const typeLower = deviceType.toLowerCase();
    const matchingDevices = this.state.providers.flatMap(p => 
      p.devices
        .filter(d => d.deviceType.toLowerCase().includes(typeLower))
        .map(d => ({
          name: d.name,
          type: d.deviceType,
          online: d.isOnline !== false,
          on: this.isDeviceOn(d),
          provider: p.displayName
        }))
    );

    return {
      deviceType,
      devices: matchingDevices,
      total: matchingDevices.length
    };
  }
}