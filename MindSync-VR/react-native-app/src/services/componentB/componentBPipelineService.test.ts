import {ComponentBPipelineService} from './componentBPipelineService';

describe('ComponentBPipelineService endpoint configuration', () => {
  it('reports a successful reapply instead of silently ignoring it', () => {
    const service = new ComponentBPipelineService();
    const onState = jest.fn();
    const onLog = jest.fn();
    service.configure({onState, onLog});

    expect(service.setEndpoint(' ws://192.168.43.254:8000/ingest ')).toBe(true);
    expect(service.setEndpoint('ws://192.168.43.254:8000/ingest')).toBe(true);

    expect(onLog).toHaveBeenLastCalledWith(
      'Component B endpoint reapplied: ws://192.168.43.254:8000/ingest',
    );
    expect(onState).toHaveBeenLastCalledWith({lastError: null});
  });

  it('rejects an invalid endpoint and exposes the validation error', () => {
    const service = new ComponentBPipelineService();
    const onState = jest.fn();
    service.configure({onState, onLog: jest.fn()});

    expect(service.setEndpoint('192.168.43.254')).toBe(false);
    expect(onState).toHaveBeenLastCalledWith({
      connectionState: 'error',
      lastError: 'Component B endpoint must be a ws:// or wss:// URL ending in /ingest',
    });
  });

  it('coalesces high-rate raw progress updates without dropping samples', () => {
    jest.useFakeTimers();
    const service = new ComponentBPipelineService();
    const onState = jest.fn();
    service.configure({onState, onLog: jest.fn()});
    service.start();
    onState.mockClear();

    service.acceptRawPpg({samples: [{timestampMs: 1000, irValue: 24_000}], receivedAtMs: 10_000});
    service.acceptRawPpg({samples: [{timestampMs: 1010, irValue: 24_010}], receivedAtMs: 10_010});

    expect(onState).not.toHaveBeenCalled();
    jest.advanceTimersByTime(250);
    expect(onState).toHaveBeenCalledTimes(1);
    expect(onState).toHaveBeenCalledWith(expect.objectContaining({rawSamplesReceived: 2}));

    service.stop();
    jest.useRealTimers();
  });
});
