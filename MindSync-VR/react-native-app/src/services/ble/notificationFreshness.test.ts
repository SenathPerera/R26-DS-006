import {DeviceUptimeFreshnessGuard} from './notificationFreshness';

describe('DeviceUptimeFreshnessGuard', () => {
  it('accepts notifications whose device and phone clocks advance together', () => {
    const guard = new DeviceUptimeFreshnessGuard(2000);

    expect(guard.isFresh(1000, 10_000)).toBe(true);
    expect(guard.isFresh(1200, 10_210)).toBe(true);
  });

  it('rejects a native notification backlog', () => {
    const guard = new DeviceUptimeFreshnessGuard(2000);

    expect(guard.isFresh(1000, 10_000)).toBe(true);
    expect(guard.isFresh(1200, 12_500)).toBe(false);
  });

  it('keeps tracking across the uint32 wearable uptime rollover', () => {
    const guard = new DeviceUptimeFreshnessGuard(2000);

    expect(guard.isFresh(0xffff_fff0, 10_000)).toBe(true);
    expect(guard.isFresh(20, 10_036)).toBe(true);
  });

  it('rebases when the wearable restarts', () => {
    const guard = new DeviceUptimeFreshnessGuard(2000);

    expect(guard.isFresh(50_000, 100_000)).toBe(true);
    expect(guard.isFresh(100, 100_200)).toBe(true);
  });
});
