/**
 * Atlas Guardian – WebView2 message bridge.
 * Receives real telemetry from the C# backend and distributes it via a simple event bus.
 * The UI never generates fake data – all values come through here.
 */

export interface TelemetryPayload {
  type: "telemetry";
  cpu: number;
  ram: number;
  ramUsedMb: number;
  ramTotalMb: number;
  netKbps: number;
  processCount: number;
  filesScanned: number;
  suspicious: number;
  networkConnections: number;
  vulnerabilityScore: number;
  status: "secure" | "warning" | "threat";
  timestamp: number;
}

export interface ActivityPayload {
  type: "activity";
  id: string;
  eventType: string;
  title: string;
  description: string;
  risk: "safe" | "medium" | "high";
  riskScore: number;
  timestamp: number;
}

export interface ChatResponsePayload {
  type: "chat_response";
  text: string;
  timestamp: number;
}

export interface ChatTypingPayload {
  type: "chat_typing";
  typing: boolean;
}

export interface SecurityMicTranscriptPayload {
  type: "security-mic-transcript";
  transcript: string;
}

export type BridgePayload = TelemetryPayload | ActivityPayload | ChatResponsePayload | ChatTypingPayload | SecurityMicTranscriptPayload | { type: string };

type Listener<T> = (payload: T) => void;

class AtlasBridge {
  private listeners = new Map<string, Set<Listener<any>>>();
  private _isWebView2 = false;

  constructor() {
    this._isWebView2 =
      typeof window !== "undefined" &&
      "chrome" in window &&
      !!(window as any).chrome?.webview;
    if (this._isWebView2) {
      (window as any).chrome.webview.addEventListener("message", (e: any) =>
        this._dispatch(e.data)
      );
    }
  }

  on<T extends BridgePayload>(type: T["type"], listener: Listener<T>) {
    if (!this.listeners.has(type)) this.listeners.set(type, new Set());
    this.listeners.get(type)!.add(listener as Listener<any>);
    return () => this.listeners.get(type)?.delete(listener as Listener<any>);
  }

  send(payload: object) {
    try {
      const json = JSON.stringify(payload);
      if (this._isWebView2) (window as any).chrome.webview.postMessage(json);
    } catch {}
  }

  sendChat(text: string) { this.send({ type: "chat", text }); }
  sendCommand(command: string) { this.send({ type: "command", command }); }

  get isConnected() { return this._isWebView2; }

  private _dispatch(raw: unknown) {
    try {
      const payload: BridgePayload =
        typeof raw === "string" ? JSON.parse(raw) : raw;
      const set = this.listeners.get(payload.type);
      if (set) set.forEach((fn) => fn(payload));
    } catch {}
  }
}

export const bridge = new AtlasBridge();
