/**
 * Date-range helpers. The API filters on UTC timestamps as a half-open interval [fromUtc, toUtc). Date inputs in the
 * UI are whole UTC calendar days and the end date is inclusive, so the API upper bound is the start of the next day.
 */
const DAY = /^\d{4}-\d{2}-\d{2}$/;

export interface UtcRange { fromUtc?: string; toUtc?: string; }

export function utcRange(fromDate: string | null | undefined, toDate: string | null | undefined): UtcRange {
  const range: UtcRange = {};
  if (fromDate && DAY.test(fromDate)) range.fromUtc = `${fromDate}T00:00:00Z`;
  if (toDate && DAY.test(toDate)) {
    const next = new Date(`${toDate}T00:00:00Z`);
    next.setUTCDate(next.getUTCDate() + 1);
    range.toUtc = next.toISOString().replace('.000Z', 'Z');
  }
  return range;
}

/** Today's UTC date as yyyy-MM-dd, optionally shifted by whole days. */
export function utcDay(offsetDays = 0, now = new Date()): string {
  const day = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + offsetDays));
  return day.toISOString().slice(0, 10);
}
