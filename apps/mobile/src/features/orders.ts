import { ApiError, queryString } from '../api/transport';
import type { Checklist, Comment, History, OrderDetail, Page, WorkOrder } from '../api/contracts';
import type { MobileSession } from '../auth/session';

export type OrderFilters = {
  search?: string;
  status?: string;
  priority?: string;
  page?: number;
  plannedFrom?: string;
  plannedTo?: string;
};
export class TechnicianApi {
  constructor(readonly session: MobileSession) {}
  private guard() {
    const access = this.session.snapshot().access;
    if (!access || access.role !== 'Technician')
      throw new ApiError(403, 'technician_required', 'Choose a Technician workspace.');
    return access;
  }
  list(filters: OrderFilters = {}, signal?: AbortSignal) {
    const access = this.guard();
    return this.session.request<Page<WorkOrder>>(
      `/work-orders?${queryString({ ...filters, technicianUserId: access.userId, pageSize: 20, sortBy: 'plannedStartAt' })}`,
      signal ? { signal } : {},
    );
  }
  detail(id: string, signal?: AbortSignal) {
    this.guard();
    return this.session.request<OrderDetail>(this.path(id), signal ? { signal } : {});
  }
  checklist(id: string, signal?: AbortSignal) {
    this.guard();
    return this.session.request<Checklist>(this.path(id) + '/checklist', signal ? { signal } : {});
  }
  comments(id: string, page: number, signal?: AbortSignal) {
    this.guard();
    return this.session.request<Page<Comment>>(
      this.path(id) + `/comments?page=${page}&pageSize=20`,
      signal ? { signal } : {},
    );
  }
  history(id: string, page: number, signal?: AbortSignal) {
    this.guard();
    return this.session.request<Page<History>>(
      this.path(id) + `/history?page=${page}&pageSize=20`,
      signal ? { signal } : {},
    );
  }
  action(id: string, action: 'start' | 'pause' | 'resume' | 'complete', expectedVersion: number) {
    this.guard();
    return this.session.request<WorkOrder>(this.path(id) + `/${action}`, {
      method: 'POST',
      body: { expectedVersion },
    });
  }
  check(id: string, itemId: string, isCompleted: boolean, expectedVersion: number) {
    this.guard();
    return this.session.request<Checklist>(
      this.path(id) + `/checklist/${encodeURIComponent(itemId)}`,
      { method: 'PATCH', body: { isCompleted, expectedVersion } },
    );
  }
  note(id: string, body: string, expectedVersion: number) {
    this.guard();
    return this.session.request<{ comment: Comment; workOrderVersion: number }>(
      this.path(id) + '/comments',
      {
        method: 'POST',
        body: { body, expectedVersion, visibility: 'TechnicianVisible' },
      },
    );
  }
  private path(id: string) {
    return `/work-orders/${encodeURIComponent(id)}`;
  }
}
export function message(error: unknown) {
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.';
}
export function statusLabel(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
export function mapUrl(location: OrderDetail['serviceLocation']) {
  const { latitude, longitude } = location;
  const query =
    latitude !== null &&
    longitude !== null &&
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180
      ? `${latitude},${longitude}`
      : [
          location.addressLine,
          location.city,
          location.stateOrProvince,
          location.postalCode,
          location.country,
        ]
          .filter(Boolean)
          .join(', ');
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}`;
}
export function dateTime(value: string | null, timeZone: string) {
  return value
    ? new Intl.DateTimeFormat('en', {
        timeZone,
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      }).format(new Date(value))
    : 'Not scheduled';
}
// Resolve local calendar boundaries rather than assuming every day has 24 hours.
export function todayRange(timeZone: string, now = new Date()) {
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
  const date = (ms: number) => formatter.format(new Date(ms));
  const target = date(now.getTime());
  function boundary(end: boolean) {
    let low = now.getTime() - 48 * 3600_000;
    let high = now.getTime() + 48 * 3600_000;
    while (high - low > 1) {
      const middle = Math.floor((low + high) / 2);
      if (date(middle) < target || (end && date(middle) === target)) low = middle;
      else high = middle;
    }
    return high;
  }
  return {
    plannedFrom: new Date(boundary(false)).toISOString(),
    plannedTo: new Date(boundary(true)).toISOString(),
  };
}
