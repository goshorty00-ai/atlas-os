import type {
  CameraRecordingErrorPayload,
  CameraRecordingStartPayload,
  CameraRecordingStopPayload,
  ProviderFormValues,
  RingManagedLiveViewErrorPayload,
  RingManagedLiveViewStartPayload,
  RingManagedLiveViewStopPayload,
  RingLiveSessionErrorPayload,
  RingLiveSessionSpeakerPayload,
  RingLiveSessionStartPayload,
  RingLiveSessionStopPayload,
  SmartHomeMicrophoneAccessPayload,
  SmartHomeActionRequest,
  SmartHomeAgentSettings,
  SmartHomeAutomationDraft,
  SmartHomeCustomCommandDraft,
  SmartHomeCustomGreetingDraft,
  SmartHomeSceneDraft,
} from './types';

type Envelope = {
  type: string;
  payload: unknown;
};

type MessageHandler = (message: Envelope) => void;

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string | object) => void;
        addEventListener: (event: 'message', listener: (event: MessageEvent<Envelope>) => void) => void;
        removeEventListener: (event: 'message', listener: (event: MessageEvent<Envelope>) => void) => void;
      };
    };
  }
}

function getWebView() {
  return window.chrome?.webview;
}

function post(type: string, payload: unknown = {}) {
  const webView = getWebView();
  if (!webView) {
    return;
  }

  webView.postMessage({ type, payload });
}

export function subscribe(handler: MessageHandler) {
  const webView = getWebView();
  if (!webView) {
    return () => {};
  }

  const listener = (event: MessageEvent<Envelope>) => {
    handler(event.data);
  };

  webView.addEventListener('message', listener);
  return () => webView.removeEventListener('message', listener);
}

export function requestState() {
  post('smart-home.getState');
}

export function refreshState() {
  post('smart-home.refresh');
}

export function saveProviderSettings(providerId: string, settings: ProviderFormValues) {
  post('smart-home.saveSettings', { providerId, settings });
}

export function linkHueBridge(bridgeIp: string) {
  post('smart-home.linkHueBridge', { bridgeIp });
}

export function linkLgTv(host: string) {
  post('smart-home.linkLgTv', { host });
}

export function discoverLgTv() {
  post('smart-home.discoverLgTv');
}

export function discoverNetwork() {
  post('smart-home.discoverNetwork');
}

export function loginRing(email: string, password: string, code: string) {
  post('smart-home.loginRing', { email, password, code });
}

export function executeDeviceAction(request: SmartHomeActionRequest) {
  post('smart-home.executeAction', request);
}

export function runSmartHomeCommand(text: string) {
  post('smart-home.runCommand', { text });
}

export function askAtlas(prompt: string, providerId?: string, deviceId?: string) {
  post('smart-home.askAtlas', { prompt, providerId, deviceId });
}

export function saveCustomCommand(command: SmartHomeCustomCommandDraft) {
  post('smart-home.saveCustomCommand', command);
}

export function deleteCustomCommand(id: string) {
  post('smart-home.deleteCustomCommand', { id });
}

export function saveScene(scene: SmartHomeSceneDraft) {
  post('smart-home.saveScene', scene);
}

export function deleteScene(id: string) {
  post('smart-home.deleteScene', { id });
}

export function runScene(id: string) {
  post('smart-home.runScene', { id });
}

export function executeScenePreview(name: string, actions: SmartHomeSceneDraft['actions']) {
  post('smart-home.executeScenePreview', { name, actions });
}

export function saveCustomGreeting(greeting: SmartHomeCustomGreetingDraft) {
  post('smart-home.saveCustomGreeting', greeting);
}

export function deleteCustomGreeting(id: string) {
  post('smart-home.deleteCustomGreeting', { id });
}

export function generateGreetingPreset(preset: 'pro' | 'unfiltered') {
  post('smart-home.generateGreetingPreset', { preset });
}

export function saveSmartHomeAgentSettings(settings: SmartHomeAgentSettings) {
  post('smart-home.saveAgentSettings', settings);
}

export function createAutomation(draft: SmartHomeAutomationDraft) {
  post('smart-home.createAutomation', draft);
}

export function toggleAutomation(id: string) {
  post('smart-home.toggleAutomation', { id });
}

export function deleteAutomation(id: string) {
  post('smart-home.deleteAutomation', { id });
}

export function runAutomation(id: string) {
  post('smart-home.runAutomation', { id });
}

export function openExternalUrl(url: string, title?: string, recordingUrl?: string, sessionId?: string) {
  post('smart-home.openExternalUrl', { url, title, recordingUrl, sessionId });
}

function createRequestId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `ring-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function waitForBridgeResponse<TSuccess, TFailure extends { message?: string }>(
  requestId: string,
  successType: string,
  failureType: string,
  timeoutMs = 60000,
) {
  return new Promise<TSuccess>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      unsubscribe();
      reject(new Error('Atlas did not receive a Ring live-session response in time.'));
    }, timeoutMs);

    const unsubscribe = subscribe((message) => {
      const payload = (message.payload ?? {}) as { requestId?: string; message?: string };
      if (payload.requestId !== requestId) {
        return;
      }

      if (message.type === successType) {
        window.clearTimeout(timer);
        unsubscribe();
        resolve(message.payload as TSuccess);
        return;
      }

      if (message.type === failureType) {
        window.clearTimeout(timer);
        unsubscribe();
        const failure = message.payload as TFailure;
        reject(new Error(failure.message ?? 'Bridge request failed'));
      }
    });
  });
}

export function startRingLiveSession(deviceId: string, offerSdp: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<RingLiveSessionStartPayload, RingLiveSessionErrorPayload>(
    requestId,
    'smart-home.ringLiveSessionStarted',
    'smart-home.ringLiveSessionFailed',
    120000,
  );

  post('smart-home.startRingLiveSession', { requestId, deviceId, offerSdp });
  return response;
}

export function stopRingLiveSession(sessionId: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<RingLiveSessionStopPayload, RingLiveSessionErrorPayload>(
    requestId,
    'smart-home.ringLiveSessionStopped',
    'smart-home.ringLiveSessionFailed',
  );

  post('smart-home.stopRingLiveSession', { requestId, sessionId });
  return response;
}

export function activateRingLiveSessionSpeaker(sessionId: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<RingLiveSessionSpeakerPayload, RingLiveSessionErrorPayload>(
    requestId,
    'smart-home.ringLiveSessionSpeakerActivated',
    'smart-home.ringLiveSessionFailed',
    120000,
  );

  post('smart-home.activateRingLiveSessionSpeaker', { requestId, sessionId });
  return response;
}

export function requestSmartHomeMicrophone(deviceId?: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<SmartHomeMicrophoneAccessPayload, RingLiveSessionErrorPayload>(
    requestId,
    'smart-home.microphoneAccessGranted',
    'smart-home.microphoneAccessFailed',
  );

  post('smart-home.requestMicrophoneAccess', { requestId, deviceId });
  return response;
}

export function releaseSmartHomeMicrophone(deviceId?: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<SmartHomeMicrophoneAccessPayload, RingLiveSessionErrorPayload>(
    requestId,
    'smart-home.microphoneAccessReleased',
    'smart-home.microphoneAccessFailed',
  );

  post('smart-home.releaseMicrophoneAccess', { requestId, deviceId });
  return response;
}

export function startRingManagedLiveView(deviceId: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<RingManagedLiveViewStartPayload, RingManagedLiveViewErrorPayload>(
    requestId,
    'smart-home.ringManagedLiveViewStarted',
    'smart-home.ringManagedLiveViewFailed',
    120000,
  );

  post('smart-home.startRingManagedLiveView', { requestId, deviceId });
  return response;
}

export function stopRingManagedLiveView(deviceId: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<RingManagedLiveViewStopPayload, RingManagedLiveViewErrorPayload>(
    requestId,
    'smart-home.ringManagedLiveViewStopped',
    'smart-home.ringManagedLiveViewFailed',
  );

  post('smart-home.stopRingManagedLiveView', { requestId, deviceId });
  return response;
}

export function startCameraRecording(cameraId: string, cameraName: string, recordingUrl: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<CameraRecordingStartPayload, CameraRecordingErrorPayload>(
    requestId,
    'smart-home.cameraRecordingStarted',
    'smart-home.cameraRecordingFailed',
  );

  post('smart-home.startCameraRecording', { requestId, cameraId, cameraName, recordingUrl });
  return response;
}

export function stopCameraRecording(cameraId: string) {
  const requestId = createRequestId();
  const response = waitForBridgeResponse<CameraRecordingStopPayload, CameraRecordingErrorPayload>(
    requestId,
    'smart-home.cameraRecordingStopped',
    'smart-home.cameraRecordingFailed',
  );

  post('smart-home.stopCameraRecording', { requestId, cameraId });
  return response;
}

export function hasNativeBridge() {
  return Boolean(getWebView());
}