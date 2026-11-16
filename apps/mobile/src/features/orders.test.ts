import { describe, expect, it } from 'vitest';
import { TechnicianApi, todayRange, upcomingRange, mapUrl } from './orders';
import { NativeTransport } from '../api/transport';
import { MobileSession } from '../auth/session';
import type { OrderDetail } from '../api/contracts';
describe('technician operations', () => {
  it.each([
    ['2026-03-08T12:00:00Z', 23],
    ['2026-11-01T12:00:00Z', 25],
  ])('uses organization calendar boundaries across DST %s', (instant, hours) => {
    const range = todayRange('America/New_York', new Date(instant));
    expect((Date.parse(range.plannedTo) - Date.parse(range.plannedFrom)) / 3600_000).toBe(hours);
  });
  it.each([
    ['America/New_York', '2027-03-11T04:30:00.000Z', '2027-03-17T04:00:00.000Z'],
    ['America/New_York', '2027-11-05T04:30:00.000Z', '2027-11-12T05:00:00.000Z'],
    ['America/New_York', '2027-02-05T14:30:00.000Z', '2027-02-12T05:00:00.000Z'],
    ['Asia/Kathmandu', '2027-01-30T20:00:00.000Z', '2027-02-06T18:15:00.000Z'],
    ['Pacific/Auckland', '2027-12-29T22:00:00.000Z', '2028-01-05T11:00:00.000Z'],
    ['UTC', '2027-11-05T04:30:00.000Z', '2027-11-12T00:00:00.000Z'],
  ])('ends upcoming work after the sixth local day in %s (%s)', (zone, instant, end) => {
    expect(upcomingRange(zone, new Date(instant))).toEqual({
      plannedFrom: instant,
      plannedTo: end,
    });
  });
  it('opens encoded static map URLs without location permission', () => {
    const location = {
      latitude: null,
      longitude: null,
      addressLine: '12 Main & Center',
      city: 'London',
      country: 'UK',
    } as OrderDetail['serviceLocation'];
    expect(mapUrl(location)).toBe(
      'https://www.google.com/maps/search/?api=1&query=12%20Main%20%26%20Center%2C%20London%2C%20UK',
    );
    expect(mapUrl({ ...location, latitude: 51.5, longitude: -0.12 })).toContain('51.5%2C-0.12');
  });
  it('rejects operational calls without a Technician membership', () => {
    const session = new MobileSession(
      new NativeTransport('https://api.test'),
      {
        get: async () => null,
        set: async () => undefined,
        remove: async () => undefined,
      },
      () => undefined,
    );
    expect(() => new TechnicianApi(session).list()).toThrow('Technician workspace');
  });
});
