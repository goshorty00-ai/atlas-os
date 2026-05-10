// Memory Manager - Session and persistent memory for ATLAS AI

import type { AIMemory } from './types';

export class MemoryManager {
  private memory: AIMemory;
  private storageKey = 'atlas_ai_memory';

  constructor(sessionId: string, userId?: string) {
    this.memory = this.loadMemory(sessionId, userId);
  }

  private loadMemory(sessionId: string, userId?: string): AIMemory {
    try {
      const stored = localStorage.getItem(this.storageKey);
      const existing = stored ? JSON.parse(stored) : {};
      
      return {
        sessionId,
        userId,
        shortTerm: {}, // Always start fresh for session
        longTerm: existing.longTerm || {},
        domain: existing.domain || {}
      };
    } catch {
      return {
        sessionId,
        userId,
        shortTerm: {},
        longTerm: {},
        domain: {}
      };
    }
  }

  // Session memory (cleared on new session)
  setSessionMemory(key: string, value: any) {
    this.memory.shortTerm[key] = value;
  }

  getSessionMemory(key: string): any {
    return this.memory.shortTerm[key];
  }

  // Long-term memory (persisted across sessions)
  setLongTermMemory(key: string, value: any) {
    this.memory.longTerm[key] = value;
    this.persistMemory();
  }

  getLongTermMemory(key: string): any {
    return this.memory.longTerm[key];
  }

  // Domain-specific memory (smart home preferences)
  setDomainMemory(key: string, value: any) {
    this.memory.domain[key] = value;
    this.persistMemory();
  }

  getDomainMemory(key: string): any {
    return this.memory.domain[key];
  }

  // Smart home specific memory helpers
  rememberDevicePreference(deviceName: string, preference: any) {
    const devicePrefs = this.getDomainMemory('devicePreferences') || {};
    devicePrefs[deviceName.toLowerCase()] = {
      ...devicePrefs[deviceName.toLowerCase()],
      ...preference,
      lastUpdated: Date.now()
    };
    this.setDomainMemory('devicePreferences', devicePrefs);
  }

  getDevicePreference(deviceName: string): any {
    const devicePrefs = this.getDomainMemory('devicePreferences') || {};
    return devicePrefs[deviceName.toLowerCase()];
  }

  rememberRoomPreference(room: string, preference: any) {
    const roomPrefs = this.getDomainMemory('roomPreferences') || {};
    roomPrefs[room.toLowerCase()] = {
      ...roomPrefs[room.toLowerCase()],
      ...preference,
      lastUpdated: Date.now()
    };
    this.setDomainMemory('roomPreferences', roomPrefs);
  }

  getRoomPreference(room: string): any {
    const roomPrefs = this.getDomainMemory('roomPreferences') || {};
    return roomPrefs[room.toLowerCase()];
  }

  rememberUserRoutine(routineName: string, routine: any) {
    const routines = this.getDomainMemory('userRoutines') || {};
    routines[routineName.toLowerCase()] = {
      ...routine,
      lastUsed: Date.now()
    };
    this.setDomainMemory('userRoutines', routines);
  }

  getUserRoutine(routineName: string): any {
    const routines = this.getDomainMemory('userRoutines') || {};
    return routines[routineName.toLowerCase()];
  }

  // Track frequently used devices
  trackDeviceUsage(deviceName: string) {
    const usage = this.getDomainMemory('deviceUsage') || {};
    const deviceKey = deviceName.toLowerCase();
    
    usage[deviceKey] = {
      count: (usage[deviceKey]?.count || 0) + 1,
      lastUsed: Date.now(),
      name: deviceName
    };
    
    this.setDomainMemory('deviceUsage', usage);
  }

  getFrequentlyUsedDevices(limit = 5): string[] {
    const usage = this.getDomainMemory('deviceUsage') || {};
    
    return Object.values(usage)
      .sort((a: any, b: any) => b.count - a.count)
      .slice(0, limit)
      .map((item: any) => item.name);
  }

  // Response style preferences
  setResponseStyle(style: 'concise' | 'detailed' | 'technical') {
    this.setLongTermMemory('responseStyle', style);
  }

  getResponseStyle(): 'concise' | 'detailed' | 'technical' {
    return this.getLongTermMemory('responseStyle') || 'concise';
  }

  // Get full memory for AI context
  getMemory(): AIMemory {
    return { ...this.memory };
  }

  // Clear session memory
  clearSession() {
    this.memory.shortTerm = {};
  }

  // Clear all memory
  clearAll() {
    this.memory = {
      sessionId: this.memory.sessionId,
      userId: this.memory.userId,
      shortTerm: {},
      longTerm: {},
      domain: {}
    };
    localStorage.removeItem(this.storageKey);
  }

  private persistMemory() {
    try {
      const toPersist = {
        longTerm: this.memory.longTerm,
        domain: this.memory.domain
      };
      localStorage.setItem(this.storageKey, JSON.stringify(toPersist));
    } catch (error) {
      console.warn('Failed to persist AI memory:', error);
    }
  }
}