export interface SmartHomeCapabilityOption {
  name: string;
  value: unknown;
}

export interface SmartHomeCapability {
  type: string;
  instance: string;
  dataType: string;
  unit: string;
  min?: number | null;
  max?: number | null;
  hasState: boolean;
  stateValue: unknown;
  options: SmartHomeCapabilityOption[];
}

export interface SmartHomeDevice {
  deviceId: string;
  name: string;
  sku: string;
  deviceType: string;
  isOnline?: boolean | null;
  previewImageUrl: string;
  previewVideoUrl: string;
  externalUrl: string;
  capabilities: SmartHomeCapability[];
}

export interface SmartHomeProviderDescriptor {
  providerId: string;
  displayName: string;
  status: string;
  isConfigured: boolean;
  requiredFields: string[];
  configuredFields: string[];
  detail: string;
}

export interface SmartHomeProviderFormState {
  enabled: boolean;
  apiKey: string;
  bridgeIp: string;
  applicationKey: string;
  refreshToken: string;
  host: string;
  clientKey: string;
  accessToken: string;
  baseUrl: string;
  username: string;
  rtspUrl: string;
  locationId: string;
  email?: string;
  password?: string;
  twoFactorCode?: string;
}

export interface SmartHomeProviderState {
  providerId: string;
  displayName: string;
  descriptor: SmartHomeProviderDescriptor;
  savedSettings: SmartHomeProviderFormState;
  devices: SmartHomeDevice[];
  error: string;
}

export interface SmartHomeAlertState {
  id: string;
  timestampUtc: string;
  category: string;
  severity: string;
  title: string;
  detail: string;
  source: string;
  providerId: string;
  deviceId: string;
  isResolved: boolean;
}

export interface SmartHomeAutomationState {
  id: string;
  trigger: string;
  actions: string[];
  schedule: string;
  createdAtUtc: string;
  lastTriggeredUtc?: string | null;
  triggerCount: number;
  isEnabled: boolean;
}

export interface SmartHomeSecurityState {
  mode: string;
  threatLevel: number;
  isScanning: boolean;
  scanProgress: number;
  recentSecurityEventCount: number;
  criticalAlertCount: number;
  activeCameraCount: number;
  sirenActive: boolean;
}

export interface SmartHomeCompanionPairingState {
  isAvailable: boolean;
  availabilityMessage: string;
  baseUrl: string;
  protocol: string;
  host: string;
  port: number;
  displayName: string;
  apiVersion: string;
  payloadFormat: string;
  payload: string;
  qrCodeDataUrl: string;
}

export interface SmartHomeNetworkDevice {
  ipAddress: string;
  hostname: string;
  macAddress: string;
  deviceType: string;
  isOnline: boolean;
  responseTime: number;
  lastSeenUtc: string;
  openPorts: number[];
  portServices: string;
  vendor: string;
}

export interface SmartHomeNetworkDiscoveryState {
  isScanning: boolean;
  lastScanUtc?: string | null;
  summary: string;
  localIp: string;
  subnetMask: string;
  gateway: string;
  dnsServer: string;
  adapterName: string;
  devices: SmartHomeNetworkDevice[];
}

export interface SmartHomeSnapshot {
  generatedAtUtc: string;
  providers: SmartHomeProviderState[];
  totalDevices: number;
  onlineDevices: number;
  configuredProviders: number;
  agentSettings: SmartHomeAgentSettings;
  customGreetings: SmartHomeSavedGreeting[];
  customCommands: SmartHomeSavedCommand[];
  customScenes: SmartHomeSavedScene[];
  alerts: SmartHomeAlertState[];
  automations: SmartHomeAutomationState[];
  security: SmartHomeSecurityState;
  companionPairing: SmartHomeCompanionPairingState;
  networkDiscovery: SmartHomeNetworkDiscoveryState;
}

export interface SmartHomeAgentSettings {
  voiceCommandsEnabled: boolean;
  answerDoorEnabled: boolean;
  showDeviceShortcutsInSidebar: boolean;
  defaultVolumeStep: number;
}

export interface SmartHomeSavedCommand {
  id: string;
  enabled: boolean;
  phrase: string;
  targetKind?: string;
  targetScope?: string;
  targetLabel?: string;
  providerId: string;
  deviceId: string;
  sku: string;
  capabilityType: string;
  capabilityInstance: string;
  value: unknown;
  responseText: string;
  doorbellResponseText: string;
}

export interface SmartHomeSavedGreeting {
  id: string;
  enabled: boolean;
  phrase: string;
  responseText: string;
}

export interface SmartHomeSceneAction {
  providerId: string;
  deviceId: string;
  deviceName: string;
  sku: string;
  capabilityType: string;
  capabilityInstance: string;
  value: unknown;
  hexColor?: string;
}

export interface SmartHomeSavedScene {
  id: string;
  enabled: boolean;
  name: string;
  phrase: string;
  previewColors: string[];
  actions: SmartHomeSceneAction[];
}

export interface ProviderFormValues {
  enabled?: boolean;
  apiKey?: string;
  bridgeIp?: string;
  applicationKey?: string;
  refreshToken?: string;
  host?: string;
  clientKey?: string;
  accessToken?: string;
  baseUrl?: string;
  username?: string;
  rtspUrl?: string;
  locationId?: string;
  email?: string;
  password?: string;
  twoFactorCode?: string;
}

export interface SmartHomeActionRequest {
  providerId: string;
  deviceId: string;
  sku: string;
  capabilityType: string;
  capabilityInstance: string;
  value: unknown;
}

export interface RingLiveSessionStartPayload {
  requestId: string;
  sessionId: string;
  answerSdp: string;
}

export interface RingLiveSessionStopPayload {
  requestId: string;
  message?: string;
}

export interface RingLiveSessionSpeakerPayload {
  requestId: string;
  sessionId: string;
  message?: string;
}

export interface RingLiveSessionErrorPayload {
  requestId: string;
  cameraId?: string;
  message?: string;
}

export interface SmartHomeMicrophoneAccessPayload {
  requestId: string;
  cameraId?: string;
  message?: string;
}

export interface RingManagedLiveViewStartPayload {
  requestId: string;
  cameraId: string;
  playerUrl: string;
  manifestUrl?: string;
}

export interface RingManagedLiveViewStopPayload {
  requestId: string;
  cameraId?: string;
  message?: string;
}

export interface RingManagedLiveViewErrorPayload {
  requestId: string;
  cameraId?: string;
  message?: string;
}

export interface CameraRecordingStartPayload {
  requestId: string;
  cameraId: string;
  message?: string;
  recordingPath?: string;
}

export interface CameraRecordingStopPayload {
  requestId: string;
  cameraId: string;
  message?: string;
  recordingPath?: string;
}

export interface CameraRecordingErrorPayload {
  requestId: string;
  cameraId?: string;
  message?: string;
}

export interface SmartHomeCustomCommandDraft {
  id?: string;
  enabled?: boolean;
  phrase: string;
  targetKind?: string;
  targetScope?: string;
  targetLabel?: string;
  providerId: string;
  deviceId: string;
  sku: string;
  capabilityType: string;
  capabilityInstance: string;
  value: unknown;
  responseText?: string;
  doorbellResponseText?: string;
}

export interface SmartHomeCustomGreetingDraft {
  id?: string;
  enabled?: boolean;
  phrase: string;
  responseText?: string;
}

export interface SmartHomeSceneDraft {
  id?: string;
  enabled?: boolean;
  name: string;
  phrase?: string;
  previewColors: string[];
  actions: SmartHomeSceneAction[];
}

export interface SmartHomeAutomationDraft {
  trigger: string;
  actions: string[];
  schedule?: string;
}