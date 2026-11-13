export type Role = 'OrganizationOwner' | 'Manager' | 'Technician' | 'Viewer';
export interface TokenPair {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  refreshExpiresAt: string;
  sessionId: string;
  tenantId: string;
  role: Role;
  userId: string;
}
export interface Profile {
  id: string;
  email: string;
  displayName: string;
  emailConfirmed: boolean;
  activeOrganizationId: string;
  role: Role;
}
export interface Organization {
  id: string;
  name: string;
  timeZoneId: string;
  role: Role;
}
export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export const statuses = [
  'Draft',
  'Scheduled',
  'Assigned',
  'InProgress',
  'OnHold',
  'Completed',
  'Cancelled',
] as const;
export const priorities = ['Low', 'Medium', 'High', 'Critical'] as const;
export type Status = (typeof statuses)[number];
export interface WorkOrder {
  id: string;
  workOrderNumber: string;
  title: string;
  description: string | null;
  priority: (typeof priorities)[number];
  status: Status;
  plannedStartAt: string | null;
  plannedEndAt: string | null;
  actualStartAt: string | null;
  actualEndAt: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
}
export interface OrderDetail {
  order: WorkOrder;
  customer: { id: string; name: string };
  serviceLocation: {
    id: string;
    name: string;
    addressLine: string;
    city: string;
    stateOrProvince: string | null;
    country: string;
    postalCode: string | null;
    latitude: number | null;
    longitude: number | null;
    contactName: string | null;
    contactPhone: string | null;
  };
  asset: {
    id: string;
    assetCode: string;
    name: string;
    brand: string | null;
    model: string | null;
    serialNumber: string | null;
  } | null;
  assignments: {
    id: string;
    technicianUserId: string;
    displayName: string;
    isPrimary: boolean;
  }[];
}
export interface Checklist {
  workOrderId: string;
  workOrderVersion: number;
  totalItems: number;
  completedItems: number;
  progressPercent: number;
  requiredItemsComplete: boolean;
  items: {
    id: string;
    label: string;
    position: number;
    isRequired: boolean;
    isCompleted: boolean;
    completedAt: string | null;
  }[];
}
export interface Comment {
  id: string;
  authorDisplayName: string;
  body: string;
  visibility: 'TechnicianVisible';
  createdAt: string;
}
export interface History {
  id: string;
  previousStatus: Status | null;
  status: Status;
  displayName: string;
  changedAt: string;
  version: number;
  reason: string | null;
}
export interface Session {
  id: string;
  tenantId: string;
  transport: string;
  isCurrent: boolean;
  lastUsedAt: string;
  expiresAt: string;
}
export function parseTokenPair(value: unknown): TokenPair {
  if (typeof value !== 'object' || value === null) throw new Error('Invalid session response.');
  const record = value as Record<string, unknown>;
  for (const key of [
    'accessToken',
    'refreshToken',
    'sessionId',
    'tenantId',
    'userId',
    'expiresAt',
    'refreshExpiresAt',
  ])
    if (typeof record[key] !== 'string' || !record[key] || (record[key] as string).length > 16000)
      throw new Error('Invalid session response.');
  if (
    !['OrganizationOwner', 'Manager', 'Technician', 'Viewer'].includes(String(record.role)) ||
    !Number.isFinite(Date.parse(String(record.expiresAt))) ||
    !Number.isFinite(Date.parse(String(record.refreshExpiresAt)))
  )
    throw new Error('Invalid session response.');
  return record as unknown as TokenPair;
}
