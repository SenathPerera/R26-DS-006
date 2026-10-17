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
});
