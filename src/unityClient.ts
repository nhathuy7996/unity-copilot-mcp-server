import WebSocket from 'ws';
import { randomUUID } from 'crypto';
import type { ActionName, ActionParams, BridgeRequest, BridgeResponse } from './commands.js';

interface PendingRequest {
  resolve: (value: BridgeResponse) => void;
  reject: (reason: Error) => void;
  timer: ReturnType<typeof setTimeout>;
}

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

export class UnityClient {
  private ws: WebSocket | null = null;
  private pending = new Map<string, PendingRequest>();
  private _state: ConnectionState = 'disconnected';
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectDelay = 2000;
  private maxReconnectDelay = 30_000;
  private shouldReconnect = false;
  private readonly REQUEST_TIMEOUT_MS = 10_000;

  private readonly onStateChange: (state: ConnectionState) => void;

  constructor(
    private port: number,
    onStateChange: (state: ConnectionState) => void,
  ) {
    this.onStateChange = onStateChange;
  }

  get state(): ConnectionState {
    return this._state;
  }

  get isConnected(): boolean {
    return this._state === 'connected';
  }

  // ── Public API ────────────────────────────────────────────────

  connect(): void {
    this.shouldReconnect = true;
    this.reconnectDelay = 2000;
    this._doConnect();
  }

  disconnect(): void {
    this.shouldReconnect = false;
    this._clearReconnectTimer();
    if (this.ws) {
      this.ws.removeAllListeners();
      this.ws.close();
      this.ws = null;
    }
    this._setState('disconnected');
    // Reject all pending requests
    for (const [, pending] of this.pending) {
      clearTimeout(pending.timer);
      pending.reject(new Error('Disconnected from Unity Editor'));
    }
    this.pending.clear();
  }

  async sendCommand(action: ActionName, params: ActionParams): Promise<BridgeResponse> {
    if (!this.isConnected || !this.ws) {
      throw new Error('Not connected to Unity Editor. Run "Unity Copilot: Connect to Unity Editor" first, or open Unity with the bridge installed.');
    }

    const id = randomUUID();
    const request: BridgeRequest = { id, action, params };

    return new Promise<BridgeResponse>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Unity did not respond within ${this.REQUEST_TIMEOUT_MS / 1000}s for action "${action}"`));
      }, this.REQUEST_TIMEOUT_MS);

      this.pending.set(id, { resolve, reject, timer });
      this.ws!.send(JSON.stringify(request));
    });
  }

  async ping(): Promise<boolean> {
    try {
      const resp = await this.sendCommand('ping', {});
      return resp.success;
    } catch {
      return false;
    }
  }

  // ── Internal connection logic ─────────────────────────────────

  private _doConnect(): void {
    if (this.ws) {
      this.ws.removeAllListeners();
      this.ws.close();
      this.ws = null;
    }

    this._setState('connecting');
    const url = `ws://127.0.0.1:${this.port}`;
    const ws = new WebSocket(url, { handshakeTimeout: 5000 });
    this.ws = ws;

    ws.on('open', () => {
      this.reconnectDelay = 2000; // reset back-off
      this._setState('connected');
    });

    ws.on('message', (data: WebSocket.RawData) => {
      try {
        const text = data instanceof Buffer ? data.toString('utf8') : String(data);
        const response = JSON.parse(text) as BridgeResponse;
        const pending = this.pending.get(response.id);
        if (pending) {
          clearTimeout(pending.timer);
          this.pending.delete(response.id);
          pending.resolve(response);
        }
      } catch {
        // Ignore malformed messages
      }
    });

    ws.on('close', () => {
      this.ws = null;
      this._setState('disconnected');
      this._rejectAllPending('Connection to Unity Editor closed');
      if (this.shouldReconnect) {
        this._scheduleReconnect();
      }
    });

    ws.on('error', (err: Error) => {
      // 'error' is always followed by 'close', so we only log here
      if (this._state === 'connecting') {
        // Benign: Unity not yet running, will retry
      } else {
        this._setState('error');
      }
      void err; // suppress unused
    });
  }

  private _scheduleReconnect(): void {
    this._clearReconnectTimer();
    this.reconnectTimer = setTimeout(() => {
      if (this.shouldReconnect) {
        this._doConnect();
      }
    }, this.reconnectDelay);
    this.reconnectDelay = Math.min(this.reconnectDelay * 1.5, this.maxReconnectDelay);
  }

  private _clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private _setState(state: ConnectionState): void {
    if (this._state !== state) {
      this._state = state;
      this.onStateChange(state);
    }
  }

  private _rejectAllPending(reason: string): void {
    for (const [id, pending] of this.pending) {
      clearTimeout(pending.timer);
      pending.reject(new Error(reason));
      this.pending.delete(id);
    }
  }
}
