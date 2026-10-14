import { utcDay, utcRange } from './dates';

describe('utcRange', () => {
  it('turns inclusive UTC calendar days into a half-open interval', () => {
    expect(utcRange('2026-10-01', '2026-10-31')).toEqual({ fromUtc: '2026-10-01T00:00:00Z', toUtc: '2026-11-01T00:00:00Z' });
  });

  it('handles month, year and leap-day boundaries', () => {
    expect(utcRange(null, '2026-12-31').toUtc).toBe('2027-01-01T00:00:00Z');
    expect(utcRange(null, '2028-02-28').toUtc).toBe('2028-02-29T00:00:00Z');
  });

  it('omits missing or malformed bounds', () => {
    expect(utcRange('', undefined)).toEqual({});
    expect(utcRange('10/01/2026', 'yesterday')).toEqual({});
  });
});

describe('utcDay', () => {
  it('uses the UTC date, not the local date', () => {
    const lateEvening = new Date('2026-10-13T23:30:00Z');
    expect(utcDay(0, lateEvening)).toBe('2026-10-13');
    expect(utcDay(-29, lateEvening)).toBe('2026-09-14');
  });
});
