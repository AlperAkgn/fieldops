import { describe, expect, it } from 'vitest';
import { calendarPeriod, scheduleInstant, scheduleLocal } from './calendar';

describe('workspace calendar', () => {
  it('resolves day boundaries through both DST transitions', () => {
    const spring = calendarPeriod('2027-03-14', 'America/New_York', false);
    expect(spring.from).toBe('2027-03-14T05:00:00Z');
    expect(spring.to).toBe('2027-03-15T04:00:00Z');
    const fall = calendarPeriod('2027-11-07', 'America/New_York', true);
    expect(fall.from).toBe('2027-11-01T04:00:00Z');
    expect(fall.to).toBe('2027-11-08T05:00:00Z');
  });

  it('rejects gaps and ambiguous edits and preserves an already stored occurrence', () => {
    const zone = 'America/New_York';
    expect(() => scheduleInstant('2027-03-14T02:30', zone, null)).toThrow('daylight-saving');
    expect(() => scheduleInstant('2027-11-07T01:30', zone, null)).toThrow('daylight-saving');
    expect(scheduleLocal('2027-11-07T06:30:00Z', zone)).toBe('2027-11-07T01:30');
    expect(scheduleInstant('2027-11-07T01:30', zone, '2027-11-07T06:30:00Z')).toBe(
      '2027-11-07T06:30:00Z',
    );
  });
});
