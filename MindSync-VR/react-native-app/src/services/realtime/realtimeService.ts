import {environment} from '../../config/environment';

export class RealtimeService {
  private socket: WebSocket | null = null;

  connect(sessionId: string, onMessage: (data: unknown) => void) {
    this.disconnect();
    this.socket = new WebSocket(`${environment.websocketUrl}?sessionId=${encodeURIComponent(sessionId)}`);
    this.socket.onmessage = event => {
      try {
        onMessage(JSON.parse(event.data));
      } catch {
        console.warn('[MindSync realtime] Ignored malformed message');
      }
    };
  }

  disconnect() {
    this.socket?.close();
    this.socket = null;
  }
}

export const realtimeService = new RealtimeService();
