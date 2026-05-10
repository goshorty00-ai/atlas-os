import type { AITaskType, ModelProviderId, PlatformId } from "../state/studioStore";

type Envelope<TPayload = unknown> = {
  type: string;
  payload: TPayload;
};

type MessageHandler = (message: Envelope) => void;

export type AtlasExecutionRequest = {
  requestId?: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
  taskType: AITaskType;
  objective: string;
  brief: string;
  requestPacket: string;
  platformId: PlatformId;
  targetSurface: string;
  variantsRequested: number;
  draftId?: string;
  draftTitle?: string;
  contentType: string;
  sceneName?: string;
  selectedLayerText?: string;
};

export type AtlasExecutionStartedPayload = {
  requestId: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
  startedAt: string;
};

export type AtlasExecutionSuccessPayload = {
  requestId: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
  responseText: string;
  routeSummary?: string;
  tokensUsed?: number;
  completedAt: string;
};

export type AtlasExecutionFailurePayload = {
  requestId: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
  errorMessage: string;
  routeSummary?: string;
  completedAt: string;
};

export type AtlasExecutionCancelledPayload = {
  requestId: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
  routeSummary?: string;
  completedAt: string;
};

export class AtlasExecutionError extends Error {
  readonly code: "failed" | "timed-out" | "cancelled";
  readonly requestId?: string;
  readonly routeSummary?: string;
  readonly completedAt?: string;

  constructor(
    code: "failed" | "timed-out" | "cancelled",
    message: string,
    details?: {
      requestId?: string;
      routeSummary?: string;
      completedAt?: string;
    },
  ) {
    super(message);
    this.name = "AtlasExecutionError";
    this.code = code;
    this.requestId = details?.requestId;
    this.routeSummary = details?.routeSummary;
    this.completedAt = details?.completedAt;
  }
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string | object) => void;
        addEventListener: (event: "message", listener: (event: MessageEvent<Envelope>) => void) => void;
        removeEventListener: (event: "message", listener: (event: MessageEvent<Envelope>) => void) => void;
      };
    };
  }
}

function getWebView() {
  return window.chrome?.webview;
}

function post(type: string, payload: unknown) {
  const webView = getWebView();
  if (!webView) {
    throw new Error("ATLAS execution bridge is unavailable in this host.");
  }

  webView.postMessage({ type, payload });
}

export function subscribeAtlasBridge(handler: MessageHandler) {
  const webView = getWebView();
  if (!webView) {
    return () => {};
  }

  const listener = (event: MessageEvent<Envelope>) => {
    handler(event.data);
  };

  webView.addEventListener("message", listener);
  return () => webView.removeEventListener("message", listener);
}

export function isAtlasBridgeAvailable() {
  return Boolean(getWebView());
}

export function cancelAtlasBrief(request: {
  requestId: string;
  briefId: string;
  providerId: ModelProviderId;
  modelId: string;
}) {
  post("atlas.cancelBrief", request);
}

function createRequestId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `atlas-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export function executeAtlasBrief(
  request: AtlasExecutionRequest,
  options?: {
    timeoutMs?: number;
    onStarted?: (payload: AtlasExecutionStartedPayload) => void;
  },
) {
  const requestId = request.requestId ?? createRequestId();
  const timeoutMs = options?.timeoutMs ?? 95000;

  return new Promise<AtlasExecutionSuccessPayload>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      unsubscribe();
      reject(new AtlasExecutionError("timed-out", "ATLAS execution timed out before a response was received.", { requestId }));
    }, timeoutMs);

    const unsubscribe = subscribeAtlasBridge((message) => {
      const payload = (message.payload ?? {}) as { requestId?: string };
      if (payload.requestId !== requestId) {
        return;
      }

      if (message.type === "atlas.requestStarted") {
        options?.onStarted?.(message.payload as AtlasExecutionStartedPayload);
        return;
      }

      if (message.type === "atlas.requestSucceeded") {
        window.clearTimeout(timer);
        unsubscribe();
        resolve(message.payload as AtlasExecutionSuccessPayload);
        return;
      }

      if (message.type === "atlas.requestFailed") {
        window.clearTimeout(timer);
        unsubscribe();
        const failure = message.payload as AtlasExecutionFailurePayload;
        reject(new AtlasExecutionError("failed", failure.errorMessage || "ATLAS execution failed.", {
          requestId: failure.requestId,
          routeSummary: failure.routeSummary,
          completedAt: failure.completedAt,
        }));
        return;
      }

      if (message.type === "atlas.requestCancelled") {
        window.clearTimeout(timer);
        unsubscribe();
        const cancelled = message.payload as AtlasExecutionCancelledPayload;
        reject(new AtlasExecutionError("cancelled", "ATLAS execution was cancelled.", {
          requestId: cancelled.requestId,
          routeSummary: cancelled.routeSummary,
          completedAt: cancelled.completedAt,
        }));
      }
    });

    try {
      post("atlas.executeBrief", { ...request, requestId });
    } catch (error) {
      window.clearTimeout(timer);
      unsubscribe();
      reject(error instanceof Error ? error : new Error("ATLAS bridge rejected the request."));
    }
  });
}