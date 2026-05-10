import Hls from 'hls.js';
import { Camera, ExternalLink, RefreshCcw, Siren, Sparkles, Zap } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  executeDeviceAction,
  openExternalUrl,
  refreshState,
  releaseSmartHomeMicrophone,
  requestSmartHomeMicrophone,
  startCameraRecording,
  startRingManagedLiveView,
  stopCameraRecording,
  stopRingManagedLiveView,
  subscribe,
} from '../bridge';
import type { SmartHomeActionRequest, SmartHomeProviderState } from '../types';

interface CameraDeckProps {
  providers: SmartHomeProviderState[];
}

interface CameraEntry {
  providerId: string;
  providerName: string;
  deviceId: string;
  name: string;
  sku: string;
  deviceType: string;
  isOnline?: boolean | null;
  previewImageUrl: string;
  previewVideoUrl: string;
  externalUrl: string;
  capabilities: SmartHomeProviderState['devices'][number]['capabilities'];
}

type RingSessionState = {
  status: 'idle' | 'connecting' | 'connected' | 'error';
  playerUrl?: string;
  manifestUrl?: string;
  error?: string;
  reloadToken?: number;
};

type RecordingState = {
  status: 'idle' | 'starting' | 'recording' | 'stopping' | 'error';
  message?: string;
  recordingPath?: string;
};

type TalkbackState = {
  status: 'idle' | 'connecting' | 'active' | 'error';
  sessionId?: string;
  error?: string;
};

type SmartHomeFocusCameraPayload = {
  cameraId?: string;
  cameraName?: string;
  useManagedRingLiveView?: boolean;
  playerUrl?: string;
  manifestUrl?: string;
};

const defaultRingHelperOrigin = 'http://127.0.0.1:43119';

function HlsTile({ sourceUrl, title }: { sourceUrl: string; title: string }) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [statusText, setStatusText] = useState('Atlas is buffering the Ring live stream.');
  const [showStatus, setShowStatus] = useState(true);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !sourceUrl) {
      return undefined;
    }

    let retryCount = 0;
    let hls: Hls | null = null;
    let retryTimer = 0;
    let stallTimer = 0;
    let lastPlaybackTime = 0;
    let disposed = false;
    const maxRetries = 40;
    const retryDelayMs = 1500;
    const stallThresholdMs = 1800;

    const showPlaybackStatus = (text: string) => {
      setStatusText(text);
      setShowStatus(true);
    };

    const clearStallTimer = () => {
      if (stallTimer) {
        window.clearTimeout(stallTimer);
        stallTimer = 0;
      }
    };

    const notePlaybackProgress = () => {
      lastPlaybackTime = video.currentTime;
      clearStallTimer();
      setShowStatus(false);
    };

    const showBufferingIfStillStalled = () => {
      clearStallTimer();
      stallTimer = window.setTimeout(() => {
        const playbackAdvanced = Math.abs(video.currentTime - lastPlaybackTime) > 0.05;
        const hasFutureData = video.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA;
        if (!playbackAdvanced && !hasFutureData && !video.ended) {
          showPlaybackStatus('Ring is still buffering the current segment.');
        }
      }, stallThresholdMs);
    };

    const scheduleRetry = (text: string) => {
      if (disposed) {
        return;
      }

      clearStallTimer();
      if (retryTimer) {
        window.clearTimeout(retryTimer);
      }

      if (retryCount >= maxRetries) {
        showPlaybackStatus('Atlas could not start the Ring stream in time. Try refreshing this camera.');
        return;
      }

      retryCount += 1;
      showPlaybackStatus(`${text} Retrying ${retryCount}/${maxRetries}...`);
      retryTimer = window.setTimeout(() => {
        startPlayback();
      }, retryDelayMs);
    };

    const cleanupPlayback = () => {
      try {
        video.pause();
      } catch {
      }

      try {
        video.removeAttribute('src');
        video.load();
      } catch {
      }

      if (hls) {
        hls.destroy();
        hls = null;
      }
    };

    const startPlayback = () => {
      cleanupPlayback();
      showPlaybackStatus('Atlas is buffering the Ring live stream.');

      if (Hls.isSupported()) {
        hls = new Hls({
          liveDurationInfinity: true,
          lowLatencyMode: true,
          liveSyncDurationCount: 1,
          liveMaxLatencyDurationCount: 2,
          maxBufferLength: 2,
          backBufferLength: 0,
          enableWorker: true,
        });
        hls.loadSource(`${sourceUrl}&retry=${retryCount}`);
        hls.attachMedia(video);
        hls.on(Hls.Events.MANIFEST_PARSED, () => {
          video.play().catch(() => undefined);
        });
        hls.on(Hls.Events.FRAG_BUFFERED, notePlaybackProgress);
        hls.on(Hls.Events.LEVEL_LOADED, () => {
          if (video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
            notePlaybackProgress();
          }
        });
        hls.on(Hls.Events.ERROR, (_event, data) => {
          if (data?.fatal) {
            scheduleRetry('Atlas is still waiting for the Ring manifest.');
          }
        });
        return;
      }

      if (video.canPlayType('application/vnd.apple.mpegurl')) {
        video.src = `${sourceUrl}&retry=${retryCount}`;
        video.load();
        video.addEventListener('loadedmetadata', () => {
          video.play().catch(() => undefined);
        }, { once: true });
        return;
      }

      showPlaybackStatus('This embedded browser cannot play Atlas-managed HLS streams.');
    };

    video.addEventListener('playing', notePlaybackProgress);
    video.addEventListener('loadeddata', notePlaybackProgress);
    video.addEventListener('canplay', notePlaybackProgress);
    video.addEventListener('timeupdate', notePlaybackProgress);
    video.addEventListener('progress', notePlaybackProgress);
    video.addEventListener('waiting', showBufferingIfStillStalled);
    video.addEventListener('seeking', showBufferingIfStillStalled);
    video.addEventListener('stalled', showBufferingIfStillStalled);
    video.addEventListener('error', () => {
      scheduleRetry('Atlas is still waiting for the Ring stream to become playable.');
    });

    startPlayback();

    return () => {
      disposed = true;
      clearStallTimer();
      if (retryTimer) {
        window.clearTimeout(retryTimer);
      }

      video.removeEventListener('playing', notePlaybackProgress);
      video.removeEventListener('loadeddata', notePlaybackProgress);
      video.removeEventListener('canplay', notePlaybackProgress);
      video.removeEventListener('timeupdate', notePlaybackProgress);
      video.removeEventListener('progress', notePlaybackProgress);
      video.removeEventListener('waiting', showBufferingIfStillStalled);
      video.removeEventListener('seeking', showBufferingIfStillStalled);
      video.removeEventListener('stalled', showBufferingIfStillStalled);
      video.removeEventListener('error', () => {
        scheduleRetry('Atlas is still waiting for the Ring stream to become playable.');
      });

      cleanupPlayback();
    };
  }, [sourceUrl]);

  return (
    <div className="relative w-full h-full bg-black">
      <video
        ref={videoRef}
        muted
        autoPlay
        playsInline
        controls={false}
        title={title}
        className="w-full h-full object-cover"
      />
      {showStatus ? (
        <div className="absolute inset-0 flex items-end p-5"
             style={{ background: 'linear-gradient(180deg, rgba(3, 9, 18, 0.28), rgba(3, 9, 18, 0.82))' }}>
          <div className="rounded-2xl px-4 py-3 text-sm text-cyan-100/88"
               style={{ background: 'rgba(4, 18, 30, 0.86)', border: '1px solid rgba(0, 212, 255, 0.18)' }}>
            {statusText}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function CameraSurface({ camera, ringSession, playbackAudioEnabled = false }: { camera: CameraEntry; ringSession?: RingSessionState; playbackAudioEnabled?: boolean }) {
  const [imageFailed, setImageFailed] = useState(false);
  const [videoFailed, setVideoFailed] = useState(false);
  const previewVideoUrl = (camera.previewVideoUrl ?? '').trim();
  const previewImageUrl = (camera.previewImageUrl ?? '').trim();
  const reloadToken = ringSession?.reloadToken ?? 0;
  const manifestUrl = (ringSession?.manifestUrl ?? '').trim();
  const playerUrl = (ringSession?.playerUrl ?? '').trim();
  const isRing = camera.providerId === 'ring';
  const cacheBustSuffix = reloadToken > 0 ? `atlasSession=${reloadToken}` : '';
  const playerQuerySuffix = playbackAudioEnabled ? '&audio=1' : '';
  const cacheBustQuerySuffix = cacheBustSuffix ? `&${cacheBustSuffix}` : '';
  const embeddedPlayerUrl = playerUrl ? `${playerUrl}${playerUrl.includes('?') ? '&' : '?'}embed=1${playerQuerySuffix}${cacheBustQuerySuffix}` : '';
  const fullPlayerUrl = playerUrl ? `${playerUrl}${playerUrl.includes('?') ? '&' : '?'}${playbackAudioEnabled ? 'audio=1' : 'audio=0'}${cacheBustQuerySuffix}` : '';
  const manifestPlaybackUrl = manifestUrl ? `${manifestUrl}${manifestUrl.includes('?') ? '&' : '?'}${cacheBustSuffix || `atlasSession=${Date.now()}`}` : '';

  if (manifestPlaybackUrl) {
    return <HlsTile sourceUrl={manifestPlaybackUrl} title={camera.name} />;
  }

  if (isRing && embeddedPlayerUrl) {
    return (
      <iframe
        key={embeddedPlayerUrl}
        src={embeddedPlayerUrl}
        title={`${camera.name} live player`}
        className="w-full h-full border-0 bg-black"
        allow="autoplay; fullscreen"
      />
    );
  }

  if (previewVideoUrl && !videoFailed) {
    return (
      <video
        src={previewVideoUrl}
        muted
        autoPlay
        loop
        playsInline
        onError={() => setVideoFailed(true)}
        className="w-full h-full object-cover"
      />
    );
  }

  if (previewImageUrl && !imageFailed) {
    return (
      <img
        src={previewImageUrl}
        alt={camera.name}
        onError={() => setImageFailed(true)}
        className="w-full h-full object-cover"
      />
    );
  }

  if (isRing && playerUrl) {
    return (
      <iframe
        key={fullPlayerUrl}
        src={fullPlayerUrl}
        title={`${camera.name} live player`}
        className="w-full h-full border-0"
        allow="autoplay; fullscreen"
      />
    );
  }

  return (
    <div className="w-full h-full rounded-[22px] p-5 flex items-end"
         style={{ background: camera.isOnline === false ? 'radial-gradient(circle at top right, rgba(255,185,112,0.12), transparent 48%), linear-gradient(180deg, rgba(20, 20, 24, 0.95), rgba(8, 8, 10, 0.98))' : 'radial-gradient(circle at top right, rgba(0,212,255,0.16), transparent 48%), linear-gradient(180deg, rgba(8, 30, 44, 0.96), rgba(4, 10, 18, 0.98))' }}>
      <div>
        <div className="w-12 h-12 rounded-2xl flex items-center justify-center mb-4"
             style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)' }}>
          <Camera className="w-6 h-6 text-cyan-200" />
        </div>
        <p className="text-sm text-cyan-100/88">
          {isRing
            ? 'Atlas is preparing the Ring feed. The tile will switch to live video as soon as the managed stream becomes ready.'
            : 'This provider did not expose an embeddable preview feed, so Atlas is keeping direct controls and provider access available.'}
        </p>
      </div>
    </div>
  );
}

function getRecordingUrl(camera: CameraEntry, ringSession?: RingSessionState) {
  const manifestUrl = (ringSession?.manifestUrl ?? '').trim();
  if (manifestUrl) {
    return manifestUrl;
  }

  const previewVideoUrl = (camera.previewVideoUrl ?? '').trim();
  if (previewVideoUrl) {
    return previewVideoUrl;
  }

  return '';
}

function getRingHelperOrigin(ringSession?: RingSessionState) {
  const candidateUrls = [ringSession?.playerUrl, ringSession?.manifestUrl];
  for (const candidate of candidateUrls) {
    const value = (candidate ?? '').trim();
    if (!value) {
      continue;
    }

    try {
      return new URL(value).origin;
    } catch {
    }
  }

  return defaultRingHelperOrigin;
}

function getSupportedTalkbackMimeType() {
  if (typeof MediaRecorder === 'undefined') {
    return '';
  }

  const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus'];
  for (const candidate of candidates) {
    if (typeof MediaRecorder.isTypeSupported !== 'function' || MediaRecorder.isTypeSupported(candidate)) {
      return candidate;
    }
  }

  return '';
}

async function postRingHelperJson<T>(helperOrigin: string, route: string, body: unknown) {
  const response = await fetch(`${helperOrigin}${route}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  const text = await response.text();
  const payload = text ? JSON.parse(text) as { message?: string } & T : {} as T;
  if (!response.ok) {
    throw new Error((payload as { message?: string }).message ?? 'Ring helper request failed.');
  }

  return payload;
}

async function postRingHelperAudioChunk(helperOrigin: string, cameraId: string, chunk: Blob, mimeType: string) {
  const response = await fetch(`${helperOrigin}/talkback/chunk/${encodeURIComponent(cameraId)}`, {
    method: 'POST',
    headers: {
      'Content-Type': mimeType || 'application/octet-stream',
    },
    body: await chunk.arrayBuffer(),
  });

  if (response.ok) {
    return;
  }

  let message = 'Ring talkback upload failed.';
  try {
    const payload = await response.json() as { message?: string };
    message = payload.message ?? message;
  } catch {
  }

  throw new Error(message);
}

function isRequestedDeviceNotFoundError(error: unknown) {
  if (!(error instanceof Error)) {
    return false;
  }

  return error.name === 'NotFoundError' ||
    error.message.toLowerCase().includes('requested device not found');
}

async function acquireTalkbackMicrophone() {
  if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== 'function') {
    throw new Error('Atlas could not access browser microphone APIs in Smart Home.');
  }

  try {
    return await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
  } catch (error) {
    if (!isRequestedDeviceNotFoundError(error) || typeof navigator.mediaDevices.enumerateDevices !== 'function') {
      throw error;
    }

    const devices = await navigator.mediaDevices.enumerateDevices();
    const audioInputs = devices.filter((device) => device.kind === 'audioinput' && device.deviceId);
    if (audioInputs.length === 0) {
      throw new Error('Atlas could not find any microphone devices in Smart Home. Check Windows microphone privacy for desktop apps and confirm a default input device is available.');
    }

    for (const device of audioInputs) {
      try {
        return await navigator.mediaDevices.getUserMedia({
          audio: {
            deviceId: { exact: device.deviceId },
          },
          video: false,
        });
      } catch (deviceError) {
        if (!isRequestedDeviceNotFoundError(deviceError)) {
          throw deviceError;
        }
      }
    }

    throw new Error('Atlas could see microphone devices, but the embedded browser could not open any of them. Check the current Windows default microphone and Smart Home browser permissions.');
  }
}

function isCameraDevice(
  providerId: string,
  deviceType: string,
  name: string,
  sku: string,
  previewImageUrl: string,
  previewVideoUrl: string,
  externalUrl: string,
  capabilities: SmartHomeProviderState['devices'][number]['capabilities'],
) {
  const normalizedLightingHardware = `${deviceType} ${sku}`.toLowerCase();
  const looksLikeLightingHardware = ['light', 'bulb', 'lamp', 'strip', 'panel', 'backlight'].some((term) => normalizedLightingHardware.includes(term));

  if ((previewVideoUrl ?? '').trim() || (previewImageUrl ?? '').trim()) {
    return true;
  }

  if (looksLikeLightingHardware) {
    const hasCameraCapability = capabilities.some((capability) => {
      return isCameraLikeCapability(capability.type, capability.instance);
    });

    if (!hasCameraCapability) {
      return false;
    }
  }

  const normalized = `${providerId} ${deviceType} ${sku} ${name}`.toLowerCase();
  if (['camera', 'doorbell', 'ring', 'doorcam', 'videodoor'].some((term) => normalized.includes(term))) {
    return true;
  }

  const normalizedExternalUrl = (externalUrl ?? '').trim().toLowerCase();
  if (normalizedExternalUrl && ['camera', 'doorbell', 'video', 'stream'].some((term) => normalizedExternalUrl.includes(term))) {
    return true;
  }

  return capabilities.some((capability) => isCameraLikeCapability(capability.type, capability.instance));
}

function isCameraLikeCapability(type: string, instance: string) {
  const normalizedType = (type ?? '').toLowerCase();
  const normalizedInstance = (instance ?? '').toLowerCase();
  const isDynamicSceneSnapshot = normalizedInstance.includes('snapshot') &&
    (normalizedType.includes('dynamic_scene') || normalizedType.includes('scene'));

  if (isDynamicSceneSnapshot) {
    return false;
  }

  const normalizedCapability = `${normalizedType} ${normalizedInstance}`;
  return ['camera', 'doorbell', 'snapshot', 'stream'].some((term) => normalizedCapability.includes(term));
}

export function CameraDeck({ providers }: CameraDeckProps) {
  const ringProvider = providers.find((provider) => provider.providerId === 'ring');
  const ringProviderError = (ringProvider?.error ?? '').trim();
  const [cameraError, setCameraError] = useState('');
  const [focusedCameraId, setFocusedCameraId] = useState('');
  const [ringSessions, setRingSessions] = useState<Record<string, RingSessionState>>({});
  const [recordings, setRecordings] = useState<Record<string, RecordingState>>({});
  const [talkbacks, setTalkbacks] = useState<Record<string, TalkbackState>>({});
  const [ringPlaybackAudio, setRingPlaybackAudio] = useState<Record<string, boolean>>({});
  const [refreshingCameras, setRefreshingCameras] = useState<Record<string, boolean>>({});
  const autoStartedRingCameras = useRef(new Set<string>());
  const reservedMicrophones = useRef(new Set<string>());
  const talkRecorders = useRef(new Map<string, MediaRecorder>());
  const talkUploadChains = useRef(new Map<string, Promise<void>>());
  const talkLocalStreams = useRef(new Map<string, MediaStream>());
  const talkAudioTracks = useRef(new Map<string, MediaStreamTrack>());
  const talkSessionIds = useRef(new Map<string, string>());
  const camerasRef = useRef<CameraEntry[]>([]);
  const cameras: CameraEntry[] = useMemo(() => providers.flatMap((provider) =>
    provider.devices
      .filter((device) => isCameraDevice(provider.providerId, device.deviceType, device.name, device.sku, device.previewImageUrl, device.previewVideoUrl, device.externalUrl, device.capabilities))
      .map((device) => ({
        providerId: provider.providerId,
        providerName: provider.displayName,
        deviceId: device.deviceId,
        name: device.name,
        sku: device.sku,
        deviceType: device.deviceType,
        isOnline: device.isOnline,
        previewImageUrl: device.previewImageUrl,
        previewVideoUrl: device.previewVideoUrl,
        externalUrl: device.externalUrl,
        capabilities: device.capabilities,
      })),
  ), [providers]);

  const execute = (request: SmartHomeActionRequest) => {
    executeDeviceAction(request);
    window.setTimeout(() => refreshState(), 900);
  };

  const updateRingSession = (deviceId: string, patch: Partial<RingSessionState>) => {
    setRingSessions((current) => ({
      ...current,
      [deviceId]: {
        ...current[deviceId],
        status: current[deviceId]?.status ?? 'idle',
        ...patch,
      },
    }));
  };

  const updateRecording = (deviceId: string, patch: Partial<RecordingState>) => {
    setRecordings((current) => ({
      ...current,
      [deviceId]: {
        ...current[deviceId],
        status: current[deviceId]?.status ?? 'idle',
        ...patch,
      },
    }));
  };

  const updateTalkback = (deviceId: string, patch: Partial<TalkbackState>) => {
    setTalkbacks((current) => ({
      ...current,
      [deviceId]: {
        ...current[deviceId],
        status: current[deviceId]?.status ?? 'idle',
        ...patch,
      },
    }));
  };

  const toggleRingPlaybackAudio = (deviceId: string) => {
    setRingPlaybackAudio((current) => ({
      ...current,
      [deviceId]: !current[deviceId],
    }));
  };

  const disposeTalkback = async (deviceId: string) => {
    const sessionId = talkSessionIds.current.get(deviceId);
    const recorder = talkRecorders.current.get(deviceId);
    const localStream = talkLocalStreams.current.get(deviceId);
    const uploadChain = talkUploadChains.current.get(deviceId);
    const hadTalkbackResources = Boolean(sessionId || recorder || localStream || talkAudioTracks.current.get(deviceId));
    const hadMicrophoneReservation = reservedMicrophones.current.has(deviceId);

    talkSessionIds.current.delete(deviceId);
    talkRecorders.current.delete(deviceId);
    talkUploadChains.current.delete(deviceId);
    talkLocalStreams.current.delete(deviceId);
    talkAudioTracks.current.delete(deviceId);
    reservedMicrophones.current.delete(deviceId);

    try {
      if (recorder && recorder.state !== 'inactive') {
        await new Promise<void>((resolve) => {
          recorder.addEventListener('stop', () => resolve(), { once: true });
          recorder.stop();
        });
      }

      localStream?.getTracks().forEach((track) => track.stop());
    } catch {
    }

    if (sessionId) {
      try {
        await uploadChain?.catch(() => undefined);
        await postRingHelperJson(getRingHelperOrigin(ringSessions[deviceId]), '/talkback/stop', { cameraId: sessionId });
      } catch {
      }
    }

    if (hadTalkbackResources || hadMicrophoneReservation) {
      try {
        await releaseSmartHomeMicrophone(deviceId);
      } catch {
      }
    }

    updateTalkback(deviceId, { status: 'idle', sessionId: undefined, error: '' });
  };

  const ensureManagedRingLiveView = async (camera: CameraEntry, forceRestart = false) => {
    const session = ringSessions[camera.deviceId];
    if (camera.providerId !== 'ring') {
      return;
    }

    if (!forceRestart && (session?.status === 'connecting' || session?.status === 'connected')) {
      return;
    }

    const reloadToken = Date.now();
    updateRingSession(camera.deviceId, {
      status: 'connecting',
      error: '',
      playerUrl: undefined,
      manifestUrl: undefined,
      reloadToken,
    });

    try {
      if (forceRestart) {
        try {
          await stopRingManagedLiveView(camera.deviceId);
        } catch {
        }
      }

      const response = await startRingManagedLiveView(camera.deviceId);
      updateRingSession(camera.deviceId, {
        status: 'connected',
        playerUrl: response.playerUrl,
        manifestUrl: response.manifestUrl,
        error: '',
        reloadToken: Date.now(),
      });
      setCameraError('');
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Atlas could not start the Ring managed live view.';
      updateRingSession(camera.deviceId, { status: 'error', error: message, reloadToken: Date.now() });
      setCameraError(message);
    }
  };

  const refreshCamera = async (camera: CameraEntry) => {
    if (camera.providerId !== 'ring') {
      refreshState();
      return;
    }

    setRefreshingCameras((current) => ({
      ...current,
      [camera.deviceId]: true,
    }));

    setCameraError('');
    updateRingSession(camera.deviceId, {
      status: 'connecting',
      error: '',
      playerUrl: undefined,
      manifestUrl: undefined,
      reloadToken: Date.now(),
    });
    updateTalkback(camera.deviceId, { status: 'idle', sessionId: undefined, error: '' });

    try {
      await disposeTalkback(camera.deviceId);
      await ensureManagedRingLiveView(camera, true);
      window.setTimeout(() => refreshState(), 250);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Atlas could not refresh this Ring camera.';
      updateRingSession(camera.deviceId, { status: 'error', error: message, reloadToken: Date.now() });
      setCameraError(message);
    } finally {
      setRefreshingCameras((current) => ({
        ...current,
        [camera.deviceId]: false,
      }));
    }
  };

  const toggleRecording = async (camera: CameraEntry) => {
    const current = recordings[camera.deviceId];

    if (current?.status === 'starting' || current?.status === 'stopping') {
      return;
    }

    if (current?.status === 'recording') {
      updateRecording(camera.deviceId, { status: 'stopping', message: 'Stopping recording...' });
      try {
        const result = await stopCameraRecording(camera.deviceId);
        updateRecording(camera.deviceId, {
          status: 'idle',
          message: result.message,
          recordingPath: result.recordingPath,
        });
      } catch (error) {
        updateRecording(camera.deviceId, {
          status: 'error',
          message: error instanceof Error ? error.message : 'Atlas could not stop the recording.',
        });
      }
      return;
    }

    const recordingUrl = getRecordingUrl(camera, ringSessions[camera.deviceId]);
    if (!recordingUrl) {
      updateRecording(camera.deviceId, {
        status: 'error',
        message: 'Atlas needs a live stream URL before it can record this camera.',
      });
      return;
    }

    updateRecording(camera.deviceId, { status: 'starting', message: 'Starting recording...' });
    try {
      const result = await startCameraRecording(camera.deviceId, camera.name, recordingUrl);
      updateRecording(camera.deviceId, {
        status: 'recording',
        message: result.message,
        recordingPath: result.recordingPath,
      });
    } catch (error) {
      updateRecording(camera.deviceId, {
        status: 'error',
        message: error instanceof Error ? error.message : 'Atlas could not start recording this camera.',
      });
    }
  };

  const toggleTalkback = async (camera: CameraEntry) => {
    if (camera.providerId !== 'ring') {
      return;
    }

    if (talkRecorders.current.has(camera.deviceId)) {
      await disposeTalkback(camera.deviceId);
      return;
    }

    updateTalkback(camera.deviceId, { status: 'connecting', error: '' });

    try {
      if (ringSessions[camera.deviceId]?.status === 'error') {
        await ensureManagedRingLiveView(camera, true);
      }

      await requestSmartHomeMicrophone(camera.deviceId);
      reservedMicrophones.current.add(camera.deviceId);
      const localStream = await acquireTalkbackMicrophone();
      const audioTrack = localStream.getAudioTracks()[0];
      if (!audioTrack) {
        throw new Error('Atlas could not access a microphone for two-way talk.');
      }

      const helperOrigin = getRingHelperOrigin(ringSessions[camera.deviceId]);
      const mimeType = getSupportedTalkbackMimeType();
      if (typeof MediaRecorder === 'undefined') {
        throw new Error('Atlas could not access browser recording APIs for talkback.');
      }

      const recorder = mimeType
        ? new MediaRecorder(localStream, { mimeType })
        : new MediaRecorder(localStream);

      await postRingHelperJson(helperOrigin, '/talkback/start', {
        cameraId: camera.deviceId,
        mimeType: recorder.mimeType || mimeType,
      });

      recorder.addEventListener('dataavailable', (event) => {
        if (!event.data || event.data.size === 0) {
          return;
        }

        const currentUpload = talkUploadChains.current.get(camera.deviceId) ?? Promise.resolve();
        const nextUpload = currentUpload
          .catch(() => undefined)
          .then(() => postRingHelperAudioChunk(helperOrigin, camera.deviceId, event.data, recorder.mimeType || mimeType || 'application/octet-stream'));

        talkUploadChains.current.set(camera.deviceId, nextUpload);
      });

      recorder.start(250);

      talkRecorders.current.set(camera.deviceId, recorder);
      talkLocalStreams.current.set(camera.deviceId, localStream);
      talkAudioTracks.current.set(camera.deviceId, audioTrack);
      talkSessionIds.current.set(camera.deviceId, camera.deviceId);

      audioTrack.enabled = true;
      updateTalkback(camera.deviceId, { status: 'active', sessionId: camera.deviceId, error: '' });
    } catch (error) {
      await disposeTalkback(camera.deviceId);
      updateTalkback(camera.deviceId, {
        status: 'error',
        error: error instanceof Error ? error.message : 'Atlas could not enable two-way talk for this camera.',
      });

      void ensureManagedRingLiveView(camera);
    }
  };

  useEffect(() => {
    camerasRef.current = cameras;
  }, [cameras]);

  useEffect(() => {
    const unsubscribe = subscribe((message) => {
      const payload = (message.payload ?? {}) as { cameraId?: string; playerUrl?: string; manifestUrl?: string; message?: string; recordingPath?: string };
      if (message.type === 'smart-home.ringManagedLiveViewStarted' && payload.cameraId) {
        updateRingSession(payload.cameraId, { status: 'connected', playerUrl: payload.playerUrl ?? '', manifestUrl: payload.manifestUrl ?? '', error: '', reloadToken: Date.now() });
        setCameraError('');
      }

      if (message.type === 'smart-home.ringManagedLiveViewStopped' && payload.cameraId) {
        updateRingSession(payload.cameraId, { status: 'idle', playerUrl: undefined, manifestUrl: undefined, error: '', reloadToken: Date.now() });
      }

      if (message.type === 'smart-home.ringManagedLiveViewFailed' && payload.cameraId) {
        updateRingSession(payload.cameraId, { status: 'error', error: payload.message ?? 'Ring live view failed.', reloadToken: Date.now() });
      }

      if (message.type === 'smart-home.cameraRecordingStarted' && payload.cameraId) {
        updateRecording(payload.cameraId, { status: 'recording', message: payload.message, recordingPath: payload.recordingPath });
      }

      if (message.type === 'smart-home.cameraRecordingStopped' && payload.cameraId) {
        updateRecording(payload.cameraId, { status: 'idle', message: payload.message, recordingPath: payload.recordingPath });
      }

      if (message.type === 'smart-home.cameraRecordingFailed' && payload.cameraId) {
        updateRecording(payload.cameraId, { status: 'error', message: payload.message ?? 'Camera recording failed.' });
      }

      if (message.type === 'smart-home.ringLiveSessionFailed' && payload.cameraId) {
        updateTalkback(payload.cameraId, { status: 'error', error: payload.message ?? 'Two-way talk failed.' });
      }

      if (message.type === 'smart-home.focusCamera') {
        const focusPayload = (message.payload ?? {}) as SmartHomeFocusCameraPayload;
        const cameraId = (focusPayload.cameraId ?? '').trim();
        if (!cameraId) {
          return;
        }

        setFocusedCameraId(cameraId);
        window.setTimeout(() => {
          document.getElementById(`camera-tile-${cameraId}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }, 50);

        const targetCamera = camerasRef.current.find((entry) => entry.deviceId === cameraId);
        if (!targetCamera) {
          return;
        }

        if (focusPayload.playerUrl || focusPayload.manifestUrl) {
          updateRingSession(cameraId, {
            status: 'connected',
            playerUrl: focusPayload.playerUrl ?? '',
            manifestUrl: focusPayload.manifestUrl ?? '',
            error: '',
            reloadToken: Date.now(),
          });
        }

        if (focusPayload.useManagedRingLiveView && targetCamera.providerId === 'ring') {
          void ensureManagedRingLiveView(targetCamera);
        }
      }

      if (payload.message && (message.type === 'smart-home.ringManagedLiveViewFailed' || message.type === 'smart-home.cameraRecordingFailed')) {
        setCameraError(payload.message);
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  useEffect(() => {
    cameras
      .filter((camera) => camera.providerId === 'ring')
      .forEach((camera) => {
        if (autoStartedRingCameras.current.has(camera.deviceId)) {
          return;
        }

        autoStartedRingCameras.current.add(camera.deviceId);
        void ensureManagedRingLiveView(camera);
      });
  }, [cameras]);

  useEffect(() => {
    return () => {
      camerasRef.current.forEach((camera) => {
        void disposeTalkback(camera.deviceId);
      });
    };
  }, []);

  return (
    <section className="mt-8 scroll-mt-24">
      <div className="flex items-center justify-between gap-4 mb-4">
        <h3 className="text-sm text-cyan-400/80 flex items-center gap-2">
          <div className="w-1 h-4 bg-cyan-400 rounded-full" style={{ boxShadow: '0 0 10px #00d4ff' }} />
          Camera Centre
        </h3>
        <div className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.18em] text-cyan-200"
             style={{ background: 'rgba(0,212,255,0.06)', border: '1px solid rgba(0,212,255,0.14)' }}>
          {cameras.length} camera{cameras.length === 1 ? '' : 's'} visible
        </div>
      </div>

      {cameraError ? (
        <div className="rounded-2xl p-4 mb-4 text-sm"
             style={{ background: 'rgba(255, 102, 102, 0.08)', border: '1px solid rgba(255, 102, 102, 0.2)', color: '#FFB5B5' }}>
          {cameraError}
        </div>
      ) : null}

      {cameras.length === 0 ? (
        <div className="rounded-2xl p-6 text-sm text-cyan-100/72"
             style={{ background: 'rgba(5, 10, 18, 0.52)', border: '1px solid rgba(0, 212, 255, 0.2)' }}>
          {ringProviderError
            ? `Ring is currently unavailable: ${ringProviderError} Reconnect Ring in Provider Connections to restore cameras and doorbells.`
            : 'No cameras are live in the current Smart Home snapshot yet. Ring cameras and doorbells will show here as soon as the provider reports them.'}
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {cameras.map((camera) => {
            const lightCapability = camera.capabilities.find((capability) => capability.instance === 'cameraLight');
            const sirenCapability = camera.capabilities.find((capability) => capability.instance === 'cameraSiren');
            const isRing = camera.providerId === 'ring';
            const ringSession = ringSessions[camera.deviceId];
            const recording = recordings[camera.deviceId];
            const talkback = talkbacks[camera.deviceId];
            const isRefreshing = Boolean(refreshingCameras[camera.deviceId]);
            const recordingUrl = getRecordingUrl(camera, ringSession);
            const isFocused = focusedCameraId === camera.deviceId;

            return (
              <article
                key={`${camera.providerId}:${camera.deviceId}`}
                id={`camera-tile-${camera.deviceId}`}
                className={`rounded-[30px] p-5 backdrop-blur-xl ${isFocused ? 'md:col-span-2' : ''}`}
                style={{ background: 'rgba(5, 10, 18, 0.72)', border: '1px solid rgba(0,212,255,0.16)', boxShadow: '0 18px 40px rgba(0,0,0,0.2)' }}
              >
                 <div className="rounded-[24px] p-5 mb-4 relative overflow-hidden"
                     style={{ background: camera.isOnline === false ? 'linear-gradient(135deg, rgba(38, 20, 20, 0.9), rgba(12, 12, 18, 0.95))' : 'linear-gradient(135deg, rgba(4, 24, 38, 0.94), rgba(6, 11, 20, 0.98))', border: camera.isOnline === false ? '1px solid rgba(255,185,112,0.18)' : '1px solid rgba(0,212,255,0.16)' }}>
                  <div className="absolute inset-0 opacity-30"
                       style={{ backgroundImage: 'linear-gradient(rgba(255,255,255,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.03) 1px, transparent 1px)', backgroundSize: '26px 26px' }} />
                  <div className="relative flex items-start justify-between gap-4">
                    <div>
                      <p className="text-lg text-cyan-100 font-semibold">{camera.name}</p>
                      <p className="text-xs text-cyan-300/58 mt-1">{camera.providerName} · {camera.deviceType || camera.sku || 'Camera'}</p>
                    </div>
                    <div className="flex gap-2">
                      <div className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-[0.16em]"
                           style={{ color: camera.isOnline === false ? '#FFB970' : '#7CFFB2', border: camera.isOnline === false ? '1px solid rgba(255,185,112,0.28)' : '1px solid rgba(124,255,178,0.24)', background: camera.isOnline === false ? 'rgba(255,185,112,0.08)' : 'rgba(124,255,178,0.08)' }}>
                        {camera.isOnline === false ? 'Offline' : 'Visible'}
                      </div>
                      <button
                        type="button"
                        onClick={() => setFocusedCameraId(isFocused ? '' : camera.deviceId)}
                        className="px-2.5 py-1 rounded-full text-[11px] uppercase tracking-[0.16em] text-cyan-100"
                        style={{ background: 'rgba(4, 18, 30, 0.72)', border: '1px solid rgba(0,212,255,0.18)' }}
                      >
                        {isFocused ? 'Exit Focus' : 'Focus'}
                      </button>
                    </div>
                  </div>

                  <button
                    type="button"
                    onClick={() => setFocusedCameraId(isFocused ? '' : camera.deviceId)}
                    className={`relative mt-6 w-full rounded-[22px] overflow-hidden text-left ${isFocused ? 'min-h-[460px]' : 'min-h-[260px]'}`}
                    style={{ background: '#08111D' }}
                  >
                    <span className="sr-only">Focus {camera.name}</span>
                    <div className="absolute inset-0 opacity-20"
                         style={{ backgroundImage: 'linear-gradient(rgba(255,255,255,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.03) 1px, transparent 1px)', backgroundSize: '26px 26px' }} />
                    <div className="absolute inset-0">
                      <CameraSurface camera={camera} ringSession={ringSession} playbackAudioEnabled={Boolean(ringPlaybackAudio[camera.deviceId])} />
                    </div>
                    <div className="absolute left-4 bottom-4 flex flex-wrap gap-2">
                      <div className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.16em]"
                           style={{ color: '#D8F9FF', border: '1px solid rgba(0,212,255,0.22)', background: 'rgba(4, 18, 30, 0.72)', backdropFilter: 'blur(10px)' }}>
                        {isRing
                          ? ringSession?.status === 'connecting'
                            ? 'Starting live view...'
                            : ringSession?.status === 'connected'
                              ? 'Atlas live view active'
                              : ringSession?.status === 'error'
                                ? 'Live view needs attention'
                                : 'Waiting for live feed'
                          : recordingUrl
                            ? 'Live preview visible'
                            : 'Provider preview unavailable'}
                      </div>
                      {recording?.status === 'recording' ? (
                        <div className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.16em]"
                             style={{ color: '#FFD3D3', border: '1px solid rgba(255,92,92,0.22)', background: 'rgba(42, 8, 12, 0.72)' }}>
                          Recording
                        </div>
                      ) : null}
                    </div>
                  </button>
                </div>

                {ringSession?.error ? (
                  <div className="mb-4 px-3 py-2 rounded-2xl text-xs"
                       style={{ background: 'rgba(255,92,92,0.08)', border: '1px solid rgba(255,92,92,0.18)', color: '#FFD3D3' }}>
                    {ringSession.error}
                  </div>
                ) : null}

                <div className="flex flex-wrap gap-2 mb-4">
                  <div className="px-3 py-1.5 rounded-full text-[11px] uppercase tracking-[0.16em] text-cyan-200 flex items-center gap-2"
                       style={{ background: 'rgba(0,212,255,0.06)', border: '1px solid rgba(0,212,255,0.14)' }}>
                    <Sparkles className="w-3.5 h-3.5" />
                    {camera.capabilities.length} controls
                  </div>
                  {recording?.message ? (
                    <div className="px-3 py-1.5 rounded-full text-[11px] text-cyan-100/85"
                         style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.08)' }}>
                      {recording.message}
                    </div>
                  ) : null}
                  {talkback?.error ? (
                    <div className="px-3 py-1.5 rounded-full text-[11px]"
                         style={{ background: 'rgba(255,92,92,0.08)', border: '1px solid rgba(255,92,92,0.18)', color: '#FFD3D3' }}>
                      {talkback.error}
                    </div>
                  ) : null}
                </div>

                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() => void refreshCamera(camera)}
                    className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                    style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.1)', color: '#D8F9FF' }}
                    disabled={isRefreshing}
                  >
                    <RefreshCcw className="w-3.5 h-3.5" />
                    {isRefreshing ? 'Refreshing Camera...' : 'Refresh Camera'}
                  </button>

                  {isRing && (
                    <button
                      type="button"
                      onClick={() => void ensureManagedRingLiveView(camera, true)}
                      className="px-3 py-2 rounded-2xl text-xs"
                      style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)', color: '#D8F9FF' }}
                    >
                      {ringSession?.status === 'connecting' ? 'Opening Camera...' : 'Reconnect Live View'}
                    </button>
                  )}

                  <button
                    type="button"
                    onClick={() => void toggleRecording(camera)}
                    className="px-3 py-2 rounded-2xl text-xs"
                    style={{ background: recording?.status === 'recording' ? 'rgba(255,92,92,0.12)' : 'rgba(255,255,255,0.04)', border: recording?.status === 'recording' ? '1px solid rgba(255,92,92,0.22)' : '1px solid rgba(255,255,255,0.1)', color: '#D8F9FF' }}
                    disabled={recording?.status === 'starting' || recording?.status === 'stopping'}
                  >
                    {recording?.status === 'starting'
                      ? 'Starting Recording...'
                      : recording?.status === 'stopping'
                        ? 'Stopping Recording...'
                        : recording?.status === 'recording'
                          ? 'Stop Recording'
                          : 'Start Recording'}
                  </button>

                  {isRing && (
                    <button
                      type="button"
                      onClick={() => toggleRingPlaybackAudio(camera.deviceId)}
                      className="px-3 py-2 rounded-2xl text-xs"
                      style={{ background: ringPlaybackAudio[camera.deviceId] ? 'rgba(124,255,178,0.12)' : 'rgba(255,255,255,0.04)', border: ringPlaybackAudio[camera.deviceId] ? '1px solid rgba(124,255,178,0.24)' : '1px solid rgba(255,255,255,0.1)', color: '#D8F9FF' }}
                    >
                      {ringPlaybackAudio[camera.deviceId] ? 'Ring Audio On' : 'Ring Audio Off'}
                    </button>
                  )}

                  {isRing && (
                    <button
                      type="button"
                      onClick={() => void toggleTalkback(camera)}
                      className="px-3 py-2 rounded-2xl text-xs"
                      style={{ background: talkback?.status === 'active' ? 'rgba(124,255,178,0.12)' : 'rgba(255,255,255,0.04)', border: talkback?.status === 'active' ? '1px solid rgba(124,255,178,0.24)' : '1px solid rgba(255,255,255,0.1)', color: '#D8F9FF' }}
                    >
                      {talkback?.status === 'connecting'
                        ? 'Connecting Talk...'
                        : talkback?.status === 'active'
                          ? 'Talkback On'
                          : 'Talkback Off'}
                    </button>
                  )}

                  {camera.externalUrl && (
                    <button
                      type="button"
                      onClick={() => openExternalUrl(camera.externalUrl, camera.name, undefined, camera.deviceId)}
                      className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                      style={{ background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.1)', color: '#D8F9FF' }}
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      Open Provider Page
                    </button>
                  )}

                  {lightCapability && (
                    <button
                      type="button"
                      onClick={() => execute({ providerId: camera.providerId, deviceId: camera.deviceId, sku: camera.sku, capabilityType: lightCapability.type, capabilityInstance: lightCapability.instance, value: !(lightCapability.stateValue === true) })}
                      className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                      style={{ background: 'rgba(0,212,255,0.08)', border: '1px solid rgba(0,212,255,0.16)', color: '#D8F9FF' }}
                    >
                      <Zap className="w-3.5 h-3.5" />
                      {lightCapability.stateValue === true ? 'Camera Light Off' : 'Camera Light On'}
                    </button>
                  )}

                  {sirenCapability && (
                    <button
                      type="button"
                      onClick={() => execute({ providerId: camera.providerId, deviceId: camera.deviceId, sku: camera.sku, capabilityType: sirenCapability.type, capabilityInstance: sirenCapability.instance, value: !(sirenCapability.stateValue === true) })}
                      className="px-3 py-2 rounded-2xl text-xs flex items-center gap-2"
                      style={{ background: 'rgba(255,92,92,0.08)', border: '1px solid rgba(255,92,92,0.18)', color: '#FFD3D3' }}
                    >
                      <Siren className="w-3.5 h-3.5" />
                      {sirenCapability.stateValue === true ? 'Stop Siren' : 'Trigger Siren'}
                    </button>
                  )}

                  {!lightCapability && !sirenCapability && (
                    <div className="px-3 py-2 rounded-2xl text-xs"
                         style={{ background: 'rgba(255,255,255,0.03)', border: '1px solid rgba(255,255,255,0.08)', color: '#BFD6E5' }}>
                      {recordingUrl
                        ? 'Atlas is keeping this camera visible in the grid and can record it directly from the live stream.'
                        : isRing
                          ? 'Atlas will keep retrying the managed Ring feed here so the camera stays part of the inline grid.'
                          : 'This camera does not expose direct light, siren, or preview media in the provider snapshot, so Atlas is giving you refresh and provider access instead.'}
                    </div>
                  )}
                </div>
              </article>
            );
          })}
        </div>
      )}

    </section>
  );
}
