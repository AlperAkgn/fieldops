export type Role = 'OrganizationOwner' | 'Manager' | 'Technician' | 'Viewer';
export const roles: Role[] = ['OrganizationOwner', 'Manager', 'Technician', 'Viewer'];
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
export interface Access {
  accessToken: string;
  expiresAt: string;
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
export interface RecordBase {
  id: string;
  name: string;
  version: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}
export interface Customer extends RecordBase {
  customerType: 'Business' | 'Individual';
  email: string | null;
  phone: string | null;
  notes: string | null;
}
export interface ServiceLocation extends RecordBase {
  customerId: string;
  addressLine: string;
  city: string;
  country: string;
  stateOrProvince: string | null;
  postalCode: string | null;
  latitude: number | null;
  longitude: number | null;
  contactName: string | null;
  contactPhone: string | null;
}
export interface Category extends RecordBase {
  description: string | null;
}
export interface Asset extends RecordBase {
  customerId: string;
  serviceLocationId: string;
  categoryId: string | null;
  assetCode: string;
  status: string;
  brand: string | null;
  model: string | null;
  serialNumber: string | null;
  installationDate: string | null;
  warrantyExpirationDate: string | null;
  notes: string | null;
}
export interface WorkOrder {
  id: string;
  workOrderNumber: string;
  customerId: string;
  serviceLocationId: string;
  assetId: string | null;
  title: string;
  description: string | null;
  priority: (typeof priorities)[number];
  status: (typeof statuses)[number];
  plannedStartAt: string | null;
  plannedEndAt: string | null;
  actualStartAt: string | null;
  actualEndAt: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
}
export interface Assignment {
  id: string;
  technicianUserId: string;
  displayName: string;
  isPrimary: boolean;
  assignedAt: string;
}
export interface OrderDetail {
  order: WorkOrder;
  customer: { id: string; name: string };
  serviceLocation: { id: string; name: string; addressLine: string; city: string };
  asset: { id: string; name: string; assetCode: string } | null;
  assignments: Assignment[];
}
export interface Technician {
  userId: string;
  displayName: string;
}
export interface Comment {
  id: string;
  authorDisplayName: string;
  body: string;
  visibility: string;
  createdAt: string;
}
export interface ChecklistItem {
  id: string;
  label: string;
  isRequired: boolean;
  isCompleted: boolean;
  position: number;
}
export interface Checklist {
  workOrderId: string;
  workOrderVersion: number;
  progressPercent: number;
  items: ChecklistItem[];
}
export interface Template extends RecordBase {
  items: ChecklistItem[];
}
export interface History {
  id: string;
  previousStatus: string | null;
  status: string;
  displayName: string;
  changedAt: string;
  version: number;
  reason: string | null;
}
export interface Change<T> {
  previous: T;
  current: T;
}
export interface Audit {
  id: string;
  actorUserId: string;
  resourceType: string;
  resourceId: string;
  action: string;
  reason: string | null;
  occurredAt: string;
  metadata: null | {
    previousVersion: number;
    entityVersion: number;
    changes: {
      plannedStartAt: Change<string | null> | null;
      plannedEndAt: Change<string | null> | null;
      technicianUserIds: Change<string[]> | null;
      primaryTechnicianUserId: Change<string | null> | null;
    };
  };
}
export interface Member {
  id: string;
  userId: string;
  email: string;
  role: Role;
  isActive: boolean;
}
export interface Invitation {
  id: string;
  email: string;
  role: Role;
  acceptedAt: string | null;
  revokedAt: string | null;
  expiresAt: string;
}
export interface Session {
  id: string;
  tenantId: string;
  transport: string;
  isCurrent: boolean;
  lastUsedAt: string;
  expiresAt: string;
}
export interface Report {
  totalOrders: number;
  activeOrders: number;
  completedOrders: number;
  overdueOrders: number;
  activeTechnicians: number;
  completionRate: number;
  statusDistribution: { name: string; count: number }[];
  priorityDistribution: { name: string; count: number }[];
  technicianWorkload: { userId: string; displayName: string; activeOrders: number }[];
  upcomingOrders: { id: string; title: string; status: string; plannedStartAt: string }[];
  recentActivities: {
    id: string;
    resourceType: string;
    resourceId: string;
    action: string;
    occurredAt: string;
  }[];
}
