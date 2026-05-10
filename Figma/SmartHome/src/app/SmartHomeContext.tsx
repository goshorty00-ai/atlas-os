// Smart Home Context - Provides smart home state and actions to components

import React, { createContext, useContext } from 'react';
import { useSmartHome } from './useSmartHome';

const SmartHomeContext = createContext<ReturnType<typeof useSmartHome> | null>(null);

export function SmartHomeProvider({ children }: { children: React.ReactNode }) {
  const smartHome = useSmartHome();
  
  return (
    <SmartHomeContext.Provider value={smartHome}>
      {children}
    </SmartHomeContext.Provider>
  );
}

export function useSmartHomeContext() {
  const context = useContext(SmartHomeContext);
  if (!context) {
    throw new Error('useSmartHomeContext must be used within a SmartHomeProvider');
  }
  return context;
}