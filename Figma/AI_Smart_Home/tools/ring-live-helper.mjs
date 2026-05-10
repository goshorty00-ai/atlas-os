import express from 'express';
import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { RtpSplitter } from '@homebridge/camera-utils';
import { RingApi } from 'ring-client-api';
import { setFfmpegPath } from 'ring-client-api/ffmpeg';
import { RtpPacket } from 'werift';

const host = process.env.RING_HELPER_HOST || '127.0.0.1';
const port = Number(process.env.RING_HELPER_PORT || '43119');
const refreshToken = (process.env.RING_REFRESH_TOKEN || '').trim();
const ffmpegPath = (process.env.RING_FFMPEG_PATH || '').trim();

if (!refreshToken) {
  throw new Error('RING_REFRESH_TOKEN is required.');
}

if (!ffmpegPath) {
  throw new Error('RING_FFMPEG_PATH is required.');
}

setFfmpegPath(ffmpegPath);

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const runtimeDirectory = path.join(__dirname, '.ring-live-runtime');
const outputRoot = path.join(runtimeDirectory, 'output');
const hlsScriptPath = path.join(__dirname, '..', 'node_modules', 'hls.js', 'dist', 'hls.min.js');

const ringApi = new RingApi({
  refreshToken,
  ffmpegPath,
  debug: false,
});

const app = express();
const activeStreams = new Map();
const activeTalkbacks = new Map();
const pendingStarts = new Map();
const streamStates = new Map();
let managedStartQueue = Promise.resolve();
let idleShutdownTimer = null;

app.use((request, response, next) => {
  response.setHeader('Access-Control-Allow-Origin', '*');
  response.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
  response.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (request.method === 'OPTIONS') {
    response.sendStatus(204);
    return;
  }

  next();
});

app.use(express.json());
app.use('/output', express.static(outputRoot, { etag: false, cacheControl: false, maxAge: 0 }));

app.get('/health', (_request, response) => {
  response.json({ ok: true, activeStreams: activeStreams.size, pendingStarts: pendingStarts.size });
});

app.get('/stream/status/:cameraId', (request, response) => {
  const cameraId = sanitizeCameraId(request.params.cameraId);
  if (!cameraId) {
    response.status(400).json({ ok: false, state: 'error', message: 'Ring camera id is missing.' });
    return;
  }

  const state = streamStates.get(cameraId);
  if (!state) {
    response.status(404).json({ ok: false, state: 'idle', cameraId, message: 'Ring live view is not active.' });
    return;
  }

  response.json(state);
});

app.get('/assets/hls.min.js', (_request, response) => {
  response.sendFile(hlsScriptPath);
});

app.get('/player/:cameraId', async (request, response) => {
  const cameraId = sanitizeCameraId(request.params.cameraId);
  const embed = request.query?.embed === '1';
  const audioEnabled = request.query?.audio === '1';
  response.type('html').send(renderPlayerHtml(cameraId, embed, audioEnabled));
});

app.post('/stream/start', async (request, response) => {
  const cameraId = sanitizeCameraId(request.body?.cameraId);
  if (!cameraId) {
    response.status(400).json({ ok: false, message: 'Ring camera id is missing.' });
    return;
  }

  try {
    clearIdleShutdown();
    await ensureDirectory(outputRoot);

    const cameras = await ringApi.getCameras();
    const camera = cameras.find((candidate) => `${candidate.id}` === cameraId);
    if (!camera) {
      response.status(404).json({ ok: false, message: 'That Ring camera is not available in Atlas right now.' });
      return;
    }

    const outputDirectory = path.join(outputRoot, cameraId);
    const manifestUrl = `http://${host}:${port}/output/${encodeURIComponent(cameraId)}/stream.m3u8`;
    const playerUrl = `http://${host}:${port}/player/${encodeURIComponent(cameraId)}`;

    await stopStream(cameraId);

    streamStates.set(cameraId, {
      ok: true,
      state: 'starting',
      cameraId,
      playerUrl,
      manifestUrl,
      message: `Ring live view is starting for ${camera.name}.`,
    });

    await startManagedStream(cameraId, camera, { playerUrl, manifestUrl });

    const readyState = {
      ok: true,
      state: 'ready',
      cameraId,
      playerUrl,
      manifestUrl,
      message: `Ring live view is ready for ${camera.name}.`,
    };

    streamStates.set(cameraId, readyState);
    response.json(readyState);
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Atlas could not start the managed Ring live stream.';
    const playerUrl = `http://${host}:${port}/player/${encodeURIComponent(cameraId)}`;
    const manifestUrl = `http://${host}:${port}/output/${encodeURIComponent(cameraId)}/stream.m3u8`;
    streamStates.set(cameraId, {
      ok: false,
      state: 'error',
      cameraId,
      playerUrl,
      manifestUrl,
      message,
    });
    response.status(500).json({
      ok: false,
      cameraId,
      playerUrl,
      manifestUrl,
      message,
    });
  }
});

app.post('/stream/stop', async (request, response) => {
  const cameraId = sanitizeCameraId(request.body?.cameraId);

  try {
    if (cameraId) {
      await stopStream(cameraId);
    } else {
      await stopAllStreams();
    }

    response.json({ ok: true, message: 'Ring live view stopped.' });
  } catch (error) {
    response.status(500).json({
      ok: false,
      message: error instanceof Error ? error.message : 'Atlas could not stop the managed Ring live stream.',
    });
  }
});

app.post('/talkback/start', async (request, response) => {
  const cameraId = sanitizeCameraId(request.body?.cameraId);
  const mimeType = `${request.body?.mimeType || ''}`.trim();
  if (!cameraId) {
    response.status(400).json({ ok: false, message: 'Ring camera id is missing.' });
    return;
  }

  try {
    const result = await startTalkback(cameraId, mimeType);
    response.json(result);
  } catch (error) {
    response.status(500).json({
      ok: false,
      cameraId,
      message: error instanceof Error ? error.message : 'Atlas could not start Ring talkback.',
    });
  }
});

app.post('/talkback/chunk/:cameraId', express.raw({ type: '*/*', limit: '4mb' }), (request, response) => {
  const cameraId = sanitizeCameraId(request.params.cameraId);
  if (!cameraId) {
    response.status(400).json({ ok: false, message: 'Ring camera id is missing.' });
    return;
  }

  const talkback = activeTalkbacks.get(cameraId);
  if (!talkback || !talkback.ffmpeg?.stdin || talkback.stopped) {
    response.status(404).json({ ok: false, cameraId, message: 'Ring talkback is not active.' });
    return;
  }

  const chunk = Buffer.isBuffer(request.body) ? request.body : Buffer.from(request.body || []);
  if (chunk.length === 0) {
    response.status(204).end();
    return;
  }

  try {
    talkback.ffmpeg.stdin.write(chunk);
    response.status(204).end();
  } catch (error) {
    response.status(500).json({
      ok: false,
      cameraId,
      message: error instanceof Error ? error.message : 'Atlas could not forward talkback audio.',
    });
  }
});

app.post('/talkback/stop', async (request, response) => {
  const cameraId = sanitizeCameraId(request.body?.cameraId);
  if (!cameraId) {
    response.status(400).json({ ok: false, message: 'Ring camera id is missing.' });
    return;
  }

  await stopTalkback(cameraId);
  response.json({ ok: true, cameraId, message: 'Ring talkback stopped.' });
});

const server = app.listen(port, host, async () => {
  await ensureDirectory(outputRoot);
  console.log(`Ring live helper listening on http://${host}:${port}`);
});

server.on('close', () => {
  clearIdleShutdown();
});

process.on('SIGINT', () => {
  void shutdown(0);
});

process.on('SIGTERM', () => {
  void shutdown(0);
});

process.on('uncaughtException', (error) => {
  console.error(error);
  void shutdown(1);
});

process.on('unhandledRejection', (error) => {
  console.error(error);
  void shutdown(1);
});

function sanitizeCameraId(value) {
  const normalized = `${value || ''}`.trim();
  return normalized.replace(/[^a-zA-Z0-9_-]/g, '');
}

function enqueueManagedStart(cameraId, cameraName, operation) {
  const queuedOperation = managedStartQueue
    .catch(() => undefined)
    .then(async () => {
      console.log(`Managed stream start slot acquired for ${cameraName} (${cameraId})`);
      return operation();
    });

  managedStartQueue = queuedOperation.catch(() => undefined);
  return queuedOperation;
}

async function startManagedStream(cameraId, camera, urls = {}) {
  const outputDirectory = path.join(outputRoot, cameraId);
  const manifestPath = path.join(outputDirectory, 'stream.m3u8');
  const manifestTimeoutMs = getManifestTimeoutMs(camera);
  const maxAttempts = getMaxStartAttempts(camera);
  const playerUrl = urls.playerUrl || `http://${host}:${port}/player/${encodeURIComponent(cameraId)}`;
  const manifestUrl = urls.manifestUrl || `http://${host}:${port}/output/${encodeURIComponent(cameraId)}/stream.m3u8`;
  let lastError = null;

  pendingStarts.set(cameraId, {
    cameraId,
    cameraName: camera.name,
    outputDirectory,
    startedAt: Date.now(),
  });

  try {
    return await enqueueManagedStart(cameraId, camera.name, async () => {
      for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
        await fs.rm(outputDirectory, { recursive: true, force: true });
        await fs.mkdir(outputDirectory, { recursive: true });

        try {
          console.log(`Starting managed stream attempt ${attempt + 1} for ${camera.name} (${cameraId}) with manifest timeout ${manifestTimeoutMs}ms`);

          const call = await camera.streamVideo({
            input: [
              '-analyzeduration', '10000000',
              '-probesize', '5000000',
              '-fflags', '+genpts+discardcorrupt',
              '-reorder_queue_size', '500',
            ],
            video: ['-vcodec', 'copy'],
            audio: ['-acodec', 'aac', '-ar', '48000', '-ac', '2'],
            output: [
              '-f', 'hls',
              '-hls_time', '1',
              '-hls_list_size', '3',
              '-hls_flags', 'delete_segments+append_list+omit_endlist+split_by_time',
              '-hls_segment_filename', path.join(outputDirectory, 'segment_%03d.ts'),
              manifestPath,
            ],
          });

          const streamRecord = {
            cameraId,
            camera,
            cameraName: camera.name,
            call,
            outputDirectory,
            playerUrl,
            manifestUrl,
            intentionalStop: false,
            restartTimer: null,
            safetyTimer: setTimeout(() => {
              void stopStream(cameraId);
            }, 10 * 60 * 1000),
          };

          activeStreams.set(cameraId, streamRecord);
          clearIdleShutdown();
          call.onCallEnded.subscribe(() => {
            handleStreamEnded(cameraId, streamRecord);
          });

          await waitForManifest(manifestPath, manifestTimeoutMs);
          console.log(`Managed stream manifest ready for ${camera.name} (${cameraId})`);

          return { outputDirectory };
        } catch (error) {
          lastError = error;
          console.warn(`Managed stream attempt ${attempt + 1} failed for ${camera.name} (${cameraId})`, error);
          await stopStream(cameraId);

          if (attempt < maxAttempts - 1) {
            streamStates.set(cameraId, {
              ok: true,
              state: 'starting',
              cameraId,
              playerUrl,
              manifestUrl,
              message: `Ring live view is still connecting for ${camera.name}. Retrying ${attempt + 2}/${maxAttempts}.`,
            });
            await delay(2000);
          }
        }
      }

      throw lastError instanceof Error
        ? lastError
        : new Error('Atlas could not start the managed Ring live stream.');
    });
  } finally {
    pendingStarts.delete(cameraId);
  }
}

function getManifestTimeoutMs(camera) {
  if (camera?.isDoorbot || camera?.hasBattery || camera?.operatingOnBattery) {
    return 75000;
  }

  return 45000;
}

function getMaxStartAttempts(camera) {
  if (camera?.isDoorbot || camera?.hasBattery || camera?.operatingOnBattery) {
    return 4;
  }

  return 3;
}

async function ensureDirectory(directoryPath) {
  await fs.mkdir(directoryPath, { recursive: true });
}

async function waitForManifest(manifestPath, timeoutMs = 30000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const stats = await fs.stat(manifestPath);
      if (stats.size > 0) {
        return;
      }
    } catch {
    }

    await delay(500);
  }

  throw new Error('Ring started the live call but Atlas did not receive an HLS manifest in time.');
}

async function stopStream(cameraId) {
  pendingStarts.delete(cameraId);
  await stopTalkback(cameraId);
  streamStates.delete(cameraId);

  const stream = activeStreams.get(cameraId);
  if (!stream) {
    scheduleIdleShutdown();
    return;
  }

  activeStreams.delete(cameraId);
  stream.intentionalStop = true;
  clearTimeout(stream.safetyTimer);
  if (stream.restartTimer) {
    clearTimeout(stream.restartTimer);
    stream.restartTimer = null;
  }

  try {
    stream.call.stop();
  } catch {
  }

  await fs.rm(stream.outputDirectory, { recursive: true, force: true });
  scheduleIdleShutdown();
}

function handleStreamEnded(cameraId, streamRecord) {
  void stopTalkback(cameraId);
  clearTimeout(streamRecord.safetyTimer);
  if (streamRecord.restartTimer) {
    clearTimeout(streamRecord.restartTimer);
    streamRecord.restartTimer = null;
  }

  if (activeStreams.get(cameraId) === streamRecord) {
    activeStreams.delete(cameraId);
  }

  if (streamRecord.intentionalStop) {
    scheduleIdleShutdown();
    return;
  }

  if (pendingStarts.has(cameraId)) {
    return;
  }

  console.warn(`Managed Ring stream ended for ${streamRecord.cameraName} (${cameraId}). Restarting.`);
  streamStates.set(cameraId, {
    ok: true,
    state: 'starting',
    cameraId,
    playerUrl: streamRecord.playerUrl,
    manifestUrl: streamRecord.manifestUrl,
    message: `Ring live view dropped for ${streamRecord.cameraName}. Atlas is reconnecting now.`,
  });

  clearIdleShutdown();
  streamRecord.restartTimer = setTimeout(() => {
    void restartManagedStream(cameraId, streamRecord).catch((error) => {
      streamStates.set(cameraId, {
        ok: false,
        state: 'error',
        cameraId,
        playerUrl: streamRecord.playerUrl,
        manifestUrl: streamRecord.manifestUrl,
        message: error instanceof Error ? error.message : `Atlas could not restore the Ring live view for ${streamRecord.cameraName}.`,
      });
      scheduleIdleShutdown();
    });
  }, 1500);
}

async function restartManagedStream(cameraId, streamRecord) {
  if (activeStreams.has(cameraId) || pendingStarts.has(cameraId)) {
    return;
  }

  await startManagedStream(cameraId, streamRecord.camera, {
    playerUrl: streamRecord.playerUrl,
    manifestUrl: streamRecord.manifestUrl,
  });

  streamStates.set(cameraId, {
    ok: true,
    state: 'ready',
    cameraId,
    playerUrl: streamRecord.playerUrl,
    manifestUrl: streamRecord.manifestUrl,
    message: `Ring live view reconnected for ${streamRecord.cameraName}.`,
  });
}

async function stopAllStreams() {
  pendingStarts.clear();
  streamStates.clear();

  const talkbackCameraIds = [...activeTalkbacks.keys()];
  for (const cameraId of talkbackCameraIds) {
    await stopTalkback(cameraId);
  }

  const cameraIds = [...activeStreams.keys()];
  for (const cameraId of cameraIds) {
    await stopStream(cameraId);
  }
}

async function startTalkback(cameraId, mimeType) {
  const stream = activeStreams.get(cameraId);
  if (!stream) {
    throw new Error('Ring live view is not active. Start the camera feed before using talkback.');
  }

  await stopTalkback(cameraId);

  const usingOpus = await stream.call.isUsingOpus.catch(() => true);
  const inputFormat = getTalkbackInputFormat(mimeType);
  const audioOutForwarder = new RtpSplitter(({ message }) => {
    try {
      const rtp = RtpPacket.deSerialize(message);
      stream.call.sendAudioPacket(rtp);
    } catch {
    }

    return null;
  });

  const rtpPort = await audioOutForwarder.portPromise;
  stream.call.activateCameraSpeaker();

  const ffmpeg = spawn(ffmpegPath || 'ffmpeg', [
    '-hide_banner',
    '-loglevel', 'warning',
    '-fflags', '+genpts+discardcorrupt',
    '-f', inputFormat,
    '-i', 'pipe:0',
    '-acodec', usingOpus ? 'libopus' : 'pcm_mulaw',
    '-ac', usingOpus ? '2' : '1',
    '-ar', usingOpus ? '48000' : '8000',
    '-flags', '+global_header',
    '-f', 'rtp',
    `rtp://127.0.0.1:${rtpPort}`,
  ], {
    stdio: ['pipe', 'ignore', 'pipe'],
  });

  const talkbackRecord = {
    cameraId,
    cameraName: stream.cameraName,
    ffmpeg,
    audioOutForwarder,
    stopped: false,
  };

  activeTalkbacks.set(cameraId, talkbackRecord);
  clearIdleShutdown();

  ffmpeg.stderr?.on('data', (data) => {
    const text = `${data || ''}`.trim();
    if (text) {
      console.log(`Talkback (${stream.cameraName}): ${text}`);
    }
  });

  ffmpeg.stdin?.on('error', () => {
  });

  ffmpeg.on('exit', () => {
    if (activeTalkbacks.get(cameraId) === talkbackRecord) {
      void stopTalkback(cameraId);
    }
  });

  console.log(`Managed talkback activated for ${stream.cameraName} (${cameraId}) using ${inputFormat}.`);
  return {
    ok: true,
    cameraId,
    message: `Ring talkback is active for ${stream.cameraName}.`,
  };
}

async function stopTalkback(cameraId) {
  const talkback = activeTalkbacks.get(cameraId);
  if (!talkback) {
    scheduleIdleShutdown();
    return;
  }

  activeTalkbacks.delete(cameraId);
  talkback.stopped = true;

  try {
    talkback.ffmpeg.stdin?.end();
  } catch {
  }

  try {
    talkback.ffmpeg.kill();
  } catch {
  }

  try {
    talkback.audioOutForwarder.close();
  } catch {
  }

  console.log(`Managed talkback stopped for ${talkback.cameraName} (${cameraId}).`);
  scheduleIdleShutdown();
}

function getTalkbackInputFormat(mimeType) {
  const normalized = `${mimeType || ''}`.trim().toLowerCase();
  if (normalized.includes('wav') || normalized.includes('wave')) {
    return 'wav';
  }

  if (normalized.includes('ogg')) {
    return 'ogg';
  }

  if (normalized.includes('mp4') || normalized.includes('aac')) {
    return 'mp4';
  }

  return 'webm';
}

function renderPlayerHtml(cameraId, embed = false, audioEnabled = false) {
  const streamUrl = `/output/${encodeURIComponent(cameraId)}/stream.m3u8?ts=${Date.now()}`;

  return `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Ring Live View</title>
    <style>
      :root {
        color-scheme: dark;
        font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif;
      }

      html, body {
        height: 100%;
      }

      body {
        margin: 0;
        min-height: 100vh;
        background: radial-gradient(circle at top, rgba(0, 212, 255, 0.18), transparent 42%), linear-gradient(180deg, #07111d 0%, #02060b 100%);
        color: #dffaff;
        overflow: hidden;
      }

      .shell {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: 24px;
      }

      .card {
        width: min(1100px, 100%);
        border-radius: 28px;
        border: 1px solid rgba(0, 212, 255, 0.18);
        background: rgba(2, 8, 14, 0.92);
        box-shadow: 0 28px 90px rgba(0, 0, 0, 0.45);
        overflow: hidden;
      }

      .header {
        padding: 18px 22px;
        border-bottom: 1px solid rgba(0, 212, 255, 0.12);
      }

      .eyebrow {
        font-size: 11px;
        text-transform: uppercase;
        letter-spacing: 0.18em;
        color: rgba(165, 241, 255, 0.72);
      }

      .title {
        margin: 8px 0 0;
        font-size: 24px;
        font-weight: 650;
      }

      .frame {
        position: relative;
        background: #000;
        aspect-ratio: 16 / 9;
      }

      video {
        width: 100%;
        height: 100%;
        object-fit: contain;
        background: #000;
      }

      .status {
        position: absolute;
        inset: 0;
        display: grid;
        place-items: center;
        text-align: center;
        padding: 24px;
        background: radial-gradient(circle at center, rgba(0, 212, 255, 0.08), rgba(0, 0, 0, 0.88));
      }

      .status.hidden {
        display: none;
      }

      .pulse {
        width: 68px;
        height: 68px;
        border-radius: 24px;
        margin: 0 auto 18px;
        border: 1px solid rgba(0, 212, 255, 0.22);
        background: rgba(255, 255, 255, 0.04);
        display: grid;
        place-items: center;
        box-shadow: 0 0 0 0 rgba(0, 212, 255, 0.2);
        animation: pulse 2s infinite;
      }

      @keyframes pulse {
        0% { box-shadow: 0 0 0 0 rgba(0, 212, 255, 0.22); }
        70% { box-shadow: 0 0 0 18px rgba(0, 212, 255, 0); }
        100% { box-shadow: 0 0 0 0 rgba(0, 212, 255, 0); }
      }

      .message {
        font-size: 14px;
        color: rgba(220, 248, 255, 0.82);
        max-width: 520px;
      }

      body.embed {
        min-height: 100%;
        background: #000;
      }

      body.embed .shell {
        min-height: 100%;
        padding: 0;
      }

      body.embed .card {
        width: 100%;
        height: 100%;
        border-radius: 0;
        border: 0;
        box-shadow: none;
        background: #000;
      }

      body.embed .header {
        display: none;
      }

      body.embed .frame {
        height: 100%;
        aspect-ratio: auto;
      }

      body.embed .status {
        padding: 16px;
      }

      body.embed .message {
        font-size: 12px;
      }
    </style>
    <script src="/assets/hls.min.js"></script>
  </head>
  <body class="${embed ? 'embed' : ''}">
    <div class="shell">
      <div class="card">
        <div class="header">
          <div class="eyebrow">Atlas Managed Ring Stream</div>
          <h1 class="title">Ring Live View</h1>
        </div>
        <div class="frame">
          <video id="video" controls autoplay playsinline${audioEnabled ? '' : ' muted'}></video>
          <div id="status" class="status">
            <div>
              <div class="pulse">LIVE</div>
              <div class="message" id="message">Atlas is buffering the Ring live stream.</div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <script>
      const video = document.getElementById('video');
      const status = document.getElementById('status');
      const message = document.getElementById('message');
      const streamUrl = ${JSON.stringify(streamUrl)};
      const audioEnabled = ${audioEnabled ? 'true' : 'false'};

      video.muted = !audioEnabled;
      video.volume = audioEnabled ? 1 : 0;

      let retryCount = 0;
      let hls = null;
      let stallTimer = 0;
      let lastPlaybackTime = 0;
      const maxRetries = 40;
      const retryDelayMs = 1500;
      const stallThresholdMs = 1800;

      const showStatus = (text) => {
        message.textContent = text;
        status.classList.remove('hidden');
      };

      const hideStatus = () => {
        status.classList.add('hidden');
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
        hideStatus();
      };

      const showBufferingIfStillStalled = () => {
        clearStallTimer();
        stallTimer = window.setTimeout(() => {
          const playbackAdvanced = Math.abs(video.currentTime - lastPlaybackTime) > 0.05;
          const hasFutureData = video.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA;
          if (!playbackAdvanced && !hasFutureData && !video.ended) {
            showStatus('Ring is still buffering the current segment.');
          }
        }, stallThresholdMs);
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
        clearStallTimer();
        scheduleRetry('Atlas is still waiting for the Ring stream to become playable.');
      });

      const scheduleRetry = (text) => {
        if (retryCount >= maxRetries) {
          showStatus('Atlas could not start the Ring stream in time. Close this view and try again.');
          return;
        }

        retryCount += 1;
        showStatus(text + ' Retrying ' + retryCount + '/' + maxRetries + '...');
        window.setTimeout(() => {
          startPlayback();
        }, retryDelayMs);
      };

      const startPlayback = () => {
        clearStallTimer();
        if (hls) {
          try {
            hls.destroy();
          } catch {
          }
          hls = null;
        }

        if (window.Hls && window.Hls.isSupported()) {
          hls = new window.Hls({
            liveDurationInfinity: true,
            lowLatencyMode: true,
            liveSyncDurationCount: 1,
            liveMaxLatencyDurationCount: 2,
            maxBufferLength: 2,
            backBufferLength: 0,
          });
          hls.loadSource(streamUrl + '&retry=' + retryCount);
          hls.attachMedia(video);
          hls.on(window.Hls.Events.MANIFEST_PARSED, () => {
            video.play().catch(() => undefined);
          });
          hls.on(window.Hls.Events.FRAG_BUFFERED, notePlaybackProgress);
          hls.on(window.Hls.Events.LEVEL_LOADED, () => {
            if (video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
              notePlaybackProgress();
            }
          });
          hls.on(window.Hls.Events.ERROR, (_event, data) => {
            if (data?.fatal) {
              scheduleRetry('Atlas is still waiting for the Ring manifest.');
            }
          });
          return;
        }

        if (video.canPlayType('application/vnd.apple.mpegurl')) {
          video.src = streamUrl + '&retry=' + retryCount;
          video.load();
          video.addEventListener('loadedmetadata', () => {
            video.play().catch(() => undefined);
          }, { once: true });
          return;
        }

        showStatus('This embedded browser cannot play Atlas-managed HLS streams.');
      };

      startPlayback();
    </script>
  </body>
</html>`;
}

function clearIdleShutdown() {
  if (idleShutdownTimer) {
    clearTimeout(idleShutdownTimer);
    idleShutdownTimer = null;
  }
}

function scheduleIdleShutdown() {
  clearIdleShutdown();
  if (activeStreams.size > 0) {
    return;
  }

  idleShutdownTimer = setTimeout(() => {
    void shutdown(0);
  }, 2 * 60 * 1000);
}

async function shutdown(exitCode) {
  clearIdleShutdown();
  await stopAllStreams();
  ringApi.disconnect();
  server.close(() => {
    process.exit(exitCode);
  });
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}