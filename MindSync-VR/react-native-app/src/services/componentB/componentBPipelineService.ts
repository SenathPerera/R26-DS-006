import {
  ComponentBFrame,
  ComponentBPipelineState,
  RawPpgBatch,
  WearableTelemetry,
} from '../../types/domain';
import {ComponentBFrameAssembler} from './componentBFrameAssembler';

type Callbacks = {
  onState: (patch: Partial<ComponentBPipelineState>) => void;
  onLog: (message: string) => void;
};

const MAX_QUEUED_FRAMES = 2;
const MAX_RECONNECT_DELAY_MS = 10_000;

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : 'Unexpected Component B relay error';
}

function validateEndpoint(endpoint: string) {
  const normalized = endpoint.trim();
  if (!/^wss?:\/\/[^\s]+\/ingest(?:\?[^\s]*)?$/.test(normalized)) {
    throw new Error('Component B endpoint must be a ws:// or wss:// URL ending in /ingest');
  }
  return normalized;
}

export class ComponentBPipelineService {
  private callbacks: Callbacks | null = null;
  private endpoint = '';
  private socket: WebSocket | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectAttempt = 0;
  private enabled = false;
  private queue: ComponentBFrame[] = [];
  private rawSamplesReceived = 0;
  private framesSent = 0;
  private framesAcknowledged = 0;
  private readonly assembler: ComponentBFrameAssembler;

  constructor() {
    this.assembler = new ComponentBFrameAssembler({
      onProgress: frameSamplesBuffered => this.patch({frameSamplesBuffered}),
      onFrame: frame => this.enqueue(frame),
      onDiscontinuity: message => this.log(message),
    });
  }

  configure(callbacks: Callbacks) {
    this.callbacks = callbacks;
  }

  setEndpoint(endpoint: string) {
    try {
      const normalized = validateEndpoint(endpoint);
      if (normalized === this.endpoint) return;
      this.endpoint = normalized;
      this.patch({endpoint: normalized, lastError: null});
      this.log(`Component B endpoint set to ${normalized}`);
      if (this.enabled) this.connect(false);
    } catch (error) {
      this.patch({connectionState: 'error', lastError: messageOf(error)});
    }
  }

  start() {
    if (this.enabled) return;
    this.enabled = true;
    this.reconnectAttempt = 0;
    this.rawSamplesReceived = 0;
    this.framesSent = 0;
    this.framesAcknowledged = 0;
    this.queue = [];
    this.assembler.reset();
    this.patch({
      rawSamplesReceived: 0,
      frameSamplesBuffered: 0,
      framesQueued: 0,
      framesSent: 0,
      framesAcknowledged: 0,
      lastFrameTimestamp: null,
      lastBackendMessage: null,
      lastError: null,
    });
    this.connect(false);
  }

  stop() {
    this.enabled = false;
    this.clearReconnectTimer();
    this.closeSocket();
    this.queue = [];
    this.assembler.reset();
    this.patch({connectionState: 'idle', frameSamplesBuffered: 0, framesQueued: 0});
  }

  setRawCharacteristicAvailable(available: boolean) {
    this.patch({rawCharacteristicAvailable: available});
    if (!available) this.log('Raw PPG characteristic is unavailable; Component B framing is paused');
  }

  acceptTelemetry(telemetry: WearableTelemetry) {
    this.assembler.setTemperature(telemetry.temperatureC);
  }

  acceptRawPpg(batch: RawPpgBatch) {
    if (!this.enabled) return;
    this.rawSamplesReceived += batch.samples.length;
    this.patch({rawSamplesReceived: this.rawSamplesReceived});
    this.assembler.ingest(batch);
  }

  private connect(isReconnect: boolean) {
    if (!this.enabled || !this.endpoint) return;
    this.clearReconnectTimer();
    this.closeSocket();
    this.patch({connectionState: isReconnect ? 'reconnecting' : 'connecting', lastError: null});
    this.log(`${isReconnect ? 'Reconnecting' : 'Connecting'} to Component B`);

    let socket: WebSocket;
    try {
      socket = new WebSocket(this.endpoint);
    } catch (error) {
      this.handleSocketFailure(messageOf(error));
      return;
    }
    this.socket = socket;

    socket.onopen = () => {
      if (this.socket !== socket) return;
      this.reconnectAttempt = 0;
      this.patch({connectionState: 'connected', lastError: null});
      this.log('Component B ingest WebSocket connected');
      this.flushQueue();
    };
    socket.onmessage = event => {
      if (this.socket !== socket) return;
      this.handleBackendMessage(String(event.data));
    };
    socket.onerror = () => {
      if (this.socket !== socket) return;
      this.patch({lastError: 'Component B WebSocket transport error'});
      this.log('Component B WebSocket transport error');
    };
    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      if (this.enabled) this.scheduleReconnect();
    };
  }

  private enqueue(frame: ComponentBFrame) {
    this.patch({lastFrameTimestamp: frame.timestamp});
    if (this.socket?.readyState === WebSocket.OPEN && this.send(frame)) return;

    this.queue.push(frame);
    if (this.queue.length > MAX_QUEUED_FRAMES) {
      this.queue.shift();
      this.log('Dropped oldest unsent Component B frame to keep the relay live');
    }
    this.patch({framesQueued: this.queue.length});
  }

  private flushQueue() {
    while (this.queue.length > 0 && this.socket?.readyState === WebSocket.OPEN) {
      const frame = this.queue[0];
      if (!this.send(frame)) break;
      this.queue.shift();
    }
    this.patch({framesQueued: this.queue.length});
  }

  private send(frame: ComponentBFrame) {
    try {
      this.socket?.send(JSON.stringify(frame));
      this.framesSent += 1;
      this.patch({framesSent: this.framesSent});
      this.log(`Sent Component B frame ${frame.timestamp.toFixed(3)} with ${frame.ppg.length} samples`);
      return true;
    } catch (error) {
      this.patch({lastError: messageOf(error)});
      return false;
    }
  }

  private handleBackendMessage(raw: string) {
    try {
      const message = JSON.parse(raw) as {status?: string; detail?: unknown; timestamp?: number};
      const detail = typeof message.detail === 'string' ? message.detail : null;
      if (message.status === 'accepted') {
        this.framesAcknowledged += 1;
        this.patch({
          framesAcknowledged: this.framesAcknowledged,
          lastBackendMessage: `Frame ${Number(message.timestamp).toFixed(3)} accepted`,
        });
        return;
      }
      if (message.status === 'invalid_batch') {
        this.patch({lastBackendMessage: 'Frame rejected', lastError: 'Component B rejected a PPG frame'});
        this.log(`Component B rejected a frame: ${raw}`);
        return;
      }
      if (message.status === 'model_unavailable') {
        this.patch({lastBackendMessage: detail ?? 'Model unavailable', lastError: detail ?? 'Component B model unavailable'});
        this.log(`Component B model unavailable: ${detail ?? 'no detail'}`);
        return;
      }
      if (message.status === 'waiting_for_temperature') {
        this.patch({lastBackendMessage: detail ?? 'Waiting for temperature'});
        this.log('Component B is waiting for the first real temperature value');
        return;
      }
      if (message.status === 'processing_error') {
        this.patch({lastBackendMessage: detail ?? 'Frame processing failed', lastError: detail ?? 'Component B could not process the frame'});
        this.log(`Component B processing error: ${detail ?? 'no detail'}`);
        return;
      }
      this.patch({lastBackendMessage: message.status ?? raw});
    } catch {
      this.log(`Ignored unrecognized Component B message: ${raw}`);
    }
  }

  private handleSocketFailure(message: string) {
    this.patch({connectionState: 'error', lastError: message});
    this.log(message);
    if (this.enabled) this.scheduleReconnect();
  }

  private scheduleReconnect() {
    this.clearReconnectTimer();
    this.reconnectAttempt += 1;
    const delayMs = Math.min(1000 * 2 ** (this.reconnectAttempt - 1), MAX_RECONNECT_DELAY_MS);
    this.patch({connectionState: 'reconnecting'});
    this.log(`Component B reconnect in ${Math.round(delayMs / 1000)}s`);
    this.reconnectTimer = setTimeout(() => this.connect(true), delayMs);
  }

  private closeSocket() {
    const socket = this.socket;
    this.socket = null;
    if (socket) socket.close();
  }

  private clearReconnectTimer() {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }

  private patch(patch: Partial<ComponentBPipelineState>) {
    this.callbacks?.onState(patch);
  }

  private log(message: string) {
    this.callbacks?.onLog(message);
    console.info(`[MindSync Component B] ${message}`);
  }
}

export const componentBPipelineService = new ComponentBPipelineService();
