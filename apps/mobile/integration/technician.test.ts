import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { MobileSession } from '../src/auth/session';
import { NativeTransport } from '../src/api/transport';
import { TechnicianApi } from '../src/features/orders';
import type { Checklist, TokenPair, WorkOrder } from '../src/api/contracts';
const origin = process.env.FIELDOPS_MOBILE_TEST_API;
if (!origin || !process.env.FIELDOPS_MOBILE_TEST_MAIL)
  throw new Error(
    'Set FIELDOPS_MOBILE_TEST_API and FIELDOPS_MOBILE_TEST_MAIL to an isolated test stack. Never use the default development database.',
  );
const mailOrigin = process.env.FIELDOPS_MOBILE_TEST_MAIL;
const transport = new NativeTransport(origin);
const suffix = `${Date.now()}`;
const password = 'Mobile-Integration-42!';
let owner: TokenPair;
let tech: { email: string; tokens: TokenPair };
let other: TokenPair;
let customer: { id: string };
let location: { id: string };
let template: { id: string; version: number };
let session: MobileSession;
let api: TechnicianApi;
let stored: string | null;
let clearCount = 0;
async function call<T>(
  path: string,
  body?: unknown,
  token = owner,
  method = body === undefined ? 'GET' : 'POST',
) {
  return transport.request<T>(path, {
    method,
    ...(body === undefined ? {} : { body }),
    ...(token ? { token: token.accessToken } : {}),
  });
}
async function mail(email: string, subject: string) {
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    const inbox = (await (await fetch(`${mailOrigin}/api/v1/messages?limit=200`)).json()) as {
      messages: { ID: string; Subject: string; To: { Address: string }[] }[];
    };
    const entry = inbox.messages.find(
      (item) => item.Subject.includes(subject) && item.To.some((to) => to.Address === email),
    );
    if (entry) {
      const { Text } = (await (await fetch(`${mailOrigin}/api/v1/message/${entry.ID}`)).json()) as {
        Text: string;
      };
      const userId = /^User ID:\s*(\S+)/m.exec(Text)?.[1];
      const token = /^Token:\s*(\S+)/m.exec(Text)?.[1];
      if (!token) throw new Error('Activation proof missing from isolated Mailpit message.');
      return { userId, token };
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error(`Mailpit did not deliver ${subject}.`);
}
async function account(label: string) {
  const email = `mobile-${label}-${suffix}@fieldops.test`;
  await transport.request('/auth/register', {
    method: 'POST',
    body: {
      email,
      displayName: `Mobile ${label}`,
      organizationName: `Mobile ${label} ${suffix}`,
      timeZoneId: 'UTC',
    },
  });
  await transport.request('/auth/confirm-email', {
    method: 'POST',
    body: { ...(await mail(email, 'Confirm')), password },
  });
  return {
    email,
    tokens: await transport.request<TokenPair>('/auth/login', {
      method: 'POST',
      body: { email, password },
    }),
  };
}
async function order(assigned = true, withChecklist = false) {
  let row = await call<WorkOrder>('/work-orders', {
    customerId: customer.id,
    serviceLocationId: location.id,
    title: 'Mobile pump inspection',
    priority: 'High',
  });
  if (withChecklist) {
    const checklist = await call<Checklist>(`/work-orders/${row.id}/checklist/apply`, {
      templateId: template.id,
      expectedVersion: row.version,
    });
    row = { ...row, version: checklist.workOrderVersion };
  }
  row = await call<WorkOrder>(`/work-orders/${row.id}/schedule`, {
    expectedVersion: row.version,
    plannedStartAt: '2027-01-11T10:00:00Z',
    plannedEndAt: '2027-01-11T11:00:00Z',
  });
  if (assigned)
    row = await call<WorkOrder>(`/work-orders/${row.id}/assign`, {
      expectedVersion: row.version,
      technicianUserIds: [tech.tokens.userId],
      primaryTechnicianUserId: tech.tokens.userId,
      overrideReason: 'Independent mobile integration fixture',
    });
  return row;
}
beforeAll(async () => {
  owner = (await account('owner')).tokens;
  tech = await account('tech');
  other = (await account('other')).tokens;
  await call(`/organizations/${owner.tenantId}/invitations`, {
    email: tech.email,
    role: 'Technician',
  });
  await call(
    '/invitations/accept',
    { token: (await mail(tech.email, 'invitation')).token },
    tech.tokens,
  );
  customer = await call('/customers', {
    customerType: 'Business',
    name: `Mobile customer ${suffix}`,
  });
  location = await call('/service-locations', {
    customerId: customer.id,
    name: 'Pump room',
    addressLine: '12 Main Street',
    city: 'London',
    country: 'GB',
    contactName: 'Sam',
    contactPhone: '+44 20 1234 5678',
  });
  template = await call('/checklist-templates', { name: `Safety ${suffix}` });
  template = await call(`/checklist-templates/${template.id}/items`, {
    label: 'Isolate power',
    position: 1,
    isRequired: true,
    expectedVersion: template.version,
  });
});
beforeEach(async () => {
  stored = null;
  session = new MobileSession(
    transport,
    {
      get: async () => stored,
      set: async (value) => {
        stored = value;
      },
      remove: async () => {
        stored = null;
      },
    },
    () => {
      clearCount++;
    },
  );
  await session.login(tech.email, password);
  await session.selectOrganization(owner.tenantId);
  api = new TechnicianApi(session);
});
describe('real native transport + API + PostgreSQL', () => {
  it('logs in, selects the Technician membership, and lists only own assignments', async () => {
    const assigned = await order();
    const hidden = await order(false);
    const page = await api.list({ search: 'Mobile pump' });
    expect(page.items.some((item) => item.id === assigned.id)).toBe(true);
    expect(page.items.some((item) => item.id === hidden.id)).toBe(false);
    expect(session.snapshot().access?.role).toBe('Technician');
    expect(stored).not.toContain('accessToken');
  });
  it('executes start, required checklist, service note, pause/resume and complete with real versions and server times', async () => {
    let row = await order(true, true);
    expect((await api.detail(row.id)).customer.name).toContain('Mobile customer');
    row = await api.action(row.id, 'start', row.version);
    expect(row.actualStartAt).not.toBeNull();
    await expect(api.action(row.id, 'complete', row.version)).rejects.toMatchObject({
      status: 409,
    });
    const checklist = await api.checklist(row.id);
    const checked = await api.check(
      row.id,
      checklist.items[0]!.id,
      true,
      checklist.workOrderVersion,
    );
    expect(checked.requiredItemsComplete).toBe(true);
    const noted = await api.note(
      row.id,
      'Pressure measured and safety verified.',
      checked.workOrderVersion,
    );
    expect(noted.workOrderVersion).toBe(checked.workOrderVersion + 1);
    row = await api.action(row.id, 'pause', noted.workOrderVersion);
    expect(row.status).toBe('OnHold');
    row = await api.action(row.id, 'resume', row.version);
    expect(row.status).toBe('InProgress');
    row = await api.action(row.id, 'complete', row.version);
    expect(row.status).toBe('Completed');
    expect(row.actualEndAt).not.toBeNull();
    const history = await api.history(row.id, 1);
    expect(history.items.map((item) => item.status)).toEqual(
      expect.arrayContaining(['Assigned', 'InProgress', 'OnHold', 'Completed']),
    );
    expect(history.items.every((item) => item.reason === null)).toBe(true);
  });
  it('rejects stale writes without replay or a duplicate note', async () => {
    const row = await order();
    const first = await api.note(row.id, 'First note', row.version);
    await expect(api.note(row.id, 'Stale note', row.version)).rejects.toMatchObject({
      status: 409,
    });
    expect((await api.detail(row.id)).order.version).toBe(first.workOrderVersion);
    expect((await api.comments(row.id, 1)).items.map((item) => item.body)).toEqual(['First note']);
  });
  it('rejects unassigned IDs across detail, checklist, notes, history and mutations', async () => {
    const row = await order(false);
    for (const read of [
      () => api.detail(row.id),
      () => api.checklist(row.id),
      () => api.comments(row.id, 1),
      () => api.history(row.id, 1),
      () => api.note(row.id, 'Forbidden', row.version),
      () => api.action(row.id, 'start', row.version),
    ])
      await expect(read()).rejects.toMatchObject({ status: 404 });
  });
  it('does not disclose internal comments or another tenant resource', async () => {
    const row = await order();
    await call(`/work-orders/${row.id}/comments`, {
      body: 'Management only',
      visibility: 'Internal',
      expectedVersion: row.version,
    });
    expect((await api.comments(row.id, 1)).items).toHaveLength(0);
    await expect(call(`/work-orders/${row.id}`, undefined, other)).rejects.toMatchObject({
      status: 404,
    });
  });
  it('rotates once, restores after process restart, and clears cache on logout', async () => {
    await session.refresh();
    const previous = stored;
    const resumed = new MobileSession(
      transport,
      {
        get: async () => stored,
        set: async (value) => {
          stored = value;
        },
        remove: async () => {
          stored = null;
        },
      },
      () => {
        clearCount++;
      },
    );
    await resumed.bootstrap();
    expect(resumed.snapshot().status).toBe('authenticated');
    expect(stored).not.toBe(previous);
    const proof = JSON.parse(stored!).refreshToken as string;
    const before = clearCount;
    expect(await resumed.logout()).toBe(true);
    expect(stored).toBeNull();
    expect(clearCount).toBeGreaterThan(before);
    await expect(
      transport.request('/auth/refresh', {
        method: 'POST',
        body: { refreshToken: proof },
      }),
    ).rejects.toMatchObject({ status: 401 });
  });
  it('clears local scope when the server revokes its session', async () => {
    const current = session.snapshot().access!;
    await call(`/auth/sessions/${current.sessionId}`, undefined, tech.tokens, 'DELETE');
    await expect(api.list()).rejects.toMatchObject({ status: 401 });
    expect(stored).toBeNull();
    expect(session.snapshot().access).toBeNull();
  });
});
