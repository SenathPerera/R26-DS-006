import {environment} from '../../config/environment';
import {PreferredEnvironment, PreparedVrSession} from '../../types/domain';

export const SESSION_RELAY_SCHEMA_VERSION = 'mindsync-session-v1';

export type CreatePreparedSessionInput = {
  requestId: string;
  participantPseudonym: string;
  sceneId: string;
  preferredEnvironment: PreferredEnvironment;
};

type RelayHandlers = {
  onConnectionState: (state: 'connecting' | 'connected' | 'error', error?: string) => void;
  onMessage: (message: RelayEnvelope) => void;
};

export type RelayEnvelope = {
  schemaVersion: string;
  messageId: string;
  messageType: string;
  payload: Record<string, unknown>;
};

export class RealtimeService {
  private socket: WebSocket | null = null;
  private activeSessionId: string | null = null;

  async createPreparedSession(input: CreatePreparedSessionInput): Promise<PreparedVrSession> {
    const response = await fetch(`${environment.apiBaseUrl}/sessions`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(input),
    });
    const body = await response.json() as unknown;
    if (!response.ok) {
      const detail = isRecord(body) && typeof body.detail === 'string' ? body.detail : `relay-http-${response.status}`;
      throw new Error(detail);
    }
    if (!isPreparedSession(body)) throw new Error('relay-session-response-invalid');
    return body;
  }

  connectMobile(session: PreparedVrSession, handlers: RelayHandlers) {
    this.disconnect();
    this.activeSessionId = session.sessionId;
    handlers.onConnectionState('connecting');
    const url = `${environment.websocketUrl}?role=mobile&sessionId=${encodeURIComponent(session.sessionId)}&mobileToken=${encodeURIComponent(session.mobileToken)}`;
    this.socket = new WebSocket(url);
    this.socket.onopen = () => handlers.onConnectionState('connected');
    this.socket.onmessage = event => {
      try {
        const message = JSON.parse(event.data) as unknown;
        if (isRelayEnvelope(message)) handlers.onMessage(message);
      } catch {
        console.warn('[MindSync realtime] Ignored malformed message');
      }
    };
    this.socket.onerror = () => handlers.onConnectionState('error', 'relay-websocket-error');
    this.socket.onclose = event => {
      if (event.code !== 1000) handlers.onConnectionState('error', event.reason || `relay-closed-${event.code}`);
    };
  }

  async fetchVisualLog(session: PreparedVrSession): Promise<RelayEnvelope[]> {
    const url = `${environment.apiBaseUrl}/sessions/${encodeURIComponent(session.sessionId)}/visual-log?mobileToken=${encodeURIComponent(session.mobileToken)}`;
    const response = await fetch(url);
    const body = await response.json() as unknown;
    if (!response.ok) throw new Error(`relay-log-http-${response.status}`);
    if (!isRecord(body) || !Array.isArray(body.messages)) {
      throw new Error('relay-log-response-invalid');
    }
    return body.messages.filter(isRelayEnvelope);
  }

  sendCommand(command: 'pause' | 'resume' | 'stop' | 'emergency_stop') {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN || !this.activeSessionId) {
      throw new Error('relay-not-connected');
    }
    this.socket.send(JSON.stringify({
      schemaVersion: SESSION_RELAY_SCHEMA_VERSION,
      messageId: createMessageId(),
      messageType: 'session_command',
      payload: {sessionId: this.activeSessionId, command},
    }));
  }

  disconnect() {
    this.socket?.close();
    this.socket = null;
    this.activeSessionId = null;
  }
}

export function isPreparedSession(value: unknown): value is PreparedVrSession {
  if (!isRecord(value)) return false;
  return value.schemaVersion === SESSION_RELAY_SCHEMA_VERSION
    && typeof value.sessionId === 'string' && value.sessionId.length > 0
    && typeof value.pairingCode === 'string' && /^\d{6}$/.test(value.pairingCode)
    && typeof value.expiresAt === 'number' && Number.isFinite(value.expiresAt)
    && typeof value.mobileToken === 'string' && value.mobileToken.length > 0;
}

export function isRelayEnvelope(value: unknown): value is RelayEnvelope {
  return isRecord(value)
    && value.schemaVersion === SESSION_RELAY_SCHEMA_VERSION
    && typeof value.messageId === 'string'
    && typeof value.messageType === 'string'
    && isRecord(value.payload);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function createMessageId(): string {
  return `mobile-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export const realtimeService = new RealtimeService();
