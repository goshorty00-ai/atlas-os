import express from 'express';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { RingApi } from 'ring-client-api';

const host = process.env.RING_DOORBELL_MONITOR_HOST || '127.0.0.1';
const port = Number(process.env.RING_DOORBELL_MONITOR_PORT || '43120');
const refreshToken = (process.env.RING_REFRESH_TOKEN || '').trim();

if (!refreshToken) {
  throw new Error('RING_REFRESH_TOKEN is required.');
}

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const runtimeDirectory = path.join(__dirname, '.ring-live-runtime');
const runtimeLogPath = path.join(runtimeDirectory, 'ring-doorbell-monitor.log');

const ringApi = new RingApi({
  refreshToken,
  debug: false,
});

const app = express();
const queuedEvents = [];
const seenEvents = new Set();
let monitoredCameras = [];

app.get('/health', (_request, response) => {
  response.json({ ok: true, cameras: monitoredCameras.length, queuedEvents: queuedEvents.length });
});

app.get('/events/next', (_request, response) => {
  response.json({ ok: true, event: queuedEvents.shift() ?? null });
});

const server = app.listen(port, host, async () => {
  await ensureDirectory(runtimeDirectory);
  await appendRuntimeLog(`Ring doorbell monitor listening on http://${host}:${port}`);
  await initializeMonitor();
});

server.on('close', () => {
  void appendRuntimeLog('Ring doorbell monitor server closed.');
});

process.on('SIGINT', () => {
  void shutdown(0);
});

process.on('SIGTERM', () => {
  void shutdown(0);
});

process.on('uncaughtException', (error) => {
  console.error(error);
  void appendRuntimeLog(`uncaughtException: ${error instanceof Error ? error.stack || error.message : String(error)}`);
  void shutdown(1);
});

process.on('unhandledRejection', (error) => {
  console.error(error);
  void appendRuntimeLog(`unhandledRejection: ${error instanceof Error ? error.stack || error.message : String(error)}`);
  void shutdown(1);
});

async function initializeMonitor() {
  const cameras = await ringApi.getCameras();
  monitoredCameras = cameras;

  for (const camera of cameras) {
    try {
      await camera.subscribeToDingEvents();
      await appendRuntimeLog(`Subscribed to ding events for ${camera.id} (${resolveCameraName(camera)})`);
    } catch (error) {
      await appendRuntimeLog(`Failed to subscribe ding events for ${camera.id}: ${error instanceof Error ? error.message : String(error)}`);
    }

    try {
      await camera.subscribeToMotionEvents();
      await appendRuntimeLog(`Subscribed to motion events for ${camera.id} (${resolveCameraName(camera)})`);
    } catch (error) {
      await appendRuntimeLog(`Failed to subscribe motion events for ${camera.id}: ${error instanceof Error ? error.message : String(error)}`);
    }

    camera.onDoorbellPressed.subscribe((notification) => {
      void queueRingEvent(camera, notification, 'doorbell');
    });

    camera.onMotionDetected.subscribe((notification) => {
      void queueRingEvent(camera, notification, 'motion');
    });
  }
}

async function queueRingEvent(camera, notification, eventType) {
  const dingId = notification?.data?.event?.ding?.id ? String(notification.data.event.ding.id) : '';
  const eventId = dingId || `${camera.id}:${eventType}:${Date.now()}`;
  if (seenEvents.has(eventId)) {
    return;
  }

  seenEvents.add(eventId);
  setTimeout(() => {
    seenEvents.delete(eventId);
  }, 5 * 60 * 1000);

  const payload = {
    eventId,
    eventType,
    cameraId: String(camera.id),
    cameraName: resolveCameraName(camera),
    occurredAtUtc: new Date().toISOString(),
  };

  queuedEvents.push(payload);
  if (queuedEvents.length > 20) {
    queuedEvents.splice(0, queuedEvents.length - 20);
  }

  await appendRuntimeLog(`${eventType} event queued for ${payload.cameraName} (${payload.cameraId}) with event ${payload.eventId}`);
}

function resolveCameraName(camera) {
  return camera?.name || camera?.data?.description || `Ring ${camera?.id || 'doorbell'}`;
}

async function ensureDirectory(directoryPath) {
  await fs.mkdir(directoryPath, { recursive: true });
}

async function appendRuntimeLog(message) {
  try {
    await ensureDirectory(runtimeDirectory);
    await fs.appendFile(runtimeLogPath, `[${new Date().toISOString()}] ${message}\n`);
    console.log(message);
  } catch {
  }
}

async function shutdown(exitCode) {
  try {
    for (const camera of monitoredCameras) {
      try {
        await camera.unsubscribeFromDingEvents();
      } catch {
      }

      try {
        await camera.unsubscribeFromMotionEvents();
      } catch {
      }
    }
  } catch {
  }

  ringApi.disconnect();
  server.close(() => {
    process.exit(exitCode);
  });
}