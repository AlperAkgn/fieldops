import {
  test,
  expect,
  request,
  type APIRequestContext,
  type Page,
  type WebSocketRoute,
} from '@playwright/test';
const apiOrigin = process.env.FIELDOPS_E2E_API || 'https://localhost:5443';
const mailOrigin = process.env.FIELDOPS_E2E_MAIL || 'http://localhost:8039';
const password = 'Browser-Test-Password-42!';
type Tokens = { accessToken: string; refreshToken: string; tenantId: string; userId: string };
let setup: APIRequestContext;
let owner: Tokens;
let manager: { email: string; tokens: Tokens };
let viewer: { email: string; tokens: Tokens };
const accountSuffix = `${Date.now()}`;
let suffix = accountSuffix;
let accounts: Record<string, { email: string; tokens: Tokens }>;
const managerHomeName = `manager Workspace ${accountSuffix}`;
async function call(path: string, body?: unknown, tokens?: Tokens, method = 'POST') {
  const result = await setup.fetch(`${apiOrigin}/api/v1${path}`, {
    method,
    ...(body === undefined ? {} : { data: body }),
    ...(tokens ? { headers: { Authorization: `Bearer ${tokens.accessToken}` } } : {}),
  });
  expect(result.ok(), `${method} ${path}: HTTP ${result.status()}`).toBeTruthy();
  return result.status() === 204 ? undefined : result.json();
}
async function mail(email: string, subject: string, expectedText = '') {
  let text = '';
  await expect
    .poll(
      async () => {
        const messages = await (await setup.get(`${mailOrigin}/api/v1/messages?limit=100`)).json();
        const item = messages.messages.find(
          (message: { Subject: string; To: { Address: string }[] }) =>
            message.Subject.includes(subject) &&
            message.To.some((recipient) => recipient.Address === email),
        );
        if (!item) return false;
        text = (await (await setup.get(`${mailOrigin}/api/v1/message/${item.ID}`)).json()).Text;
        return !expectedText || text.includes(expectedText);
      },
      { timeout: 20000 },
    )
    .toBeTruthy();
  return {
    userId: /^User ID:\s*(\S+)/m.exec(text)?.[1],
    token: /^Token:\s*(\S+)/m.exec(text)?.[1],
  };
}
async function account(label: string) {
  const email = `${label}-${suffix}@fieldops.test`;
  await call('/auth/register', {
    email,
    displayName: label === 'tech' ? 'Jamie Rivera' : `${label} User`,
    organizationName: `${label} Workspace ${suffix}`,
  });
  await call('/auth/confirm-email', { ...(await mail(email, 'Confirm')), password });
  return { email, tokens: (await call('/auth/login', { email, password })) as Tokens };
}
async function join(member: { email: string; tokens: Tokens }, role: string) {
  await call(`/organizations/${owner.tenantId}/invitations`, { email: member.email, role }, owner);
  const invitation = await mail(member.email, 'invitation', `Test workspace ${suffix}`);
  await call('/invitations/accept', { token: invitation.token }, member.tokens);
  member.tokens = (await call(
    `/organizations/${owner.tenantId}/select`,
    { refreshToken: member.tokens.refreshToken },
    member.tokens,
  )) as Tokens;
}
async function signIn(page: Page, email: string, organization?: string) {
  await page.goto('/login');
  await page.getByLabel(/^Email address/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.getByRole('button', { name: 'Sign in to workspace' }).click();
  await expect(page.getByLabel('Active organization')).toBeVisible();
  if (organization) {
    const selected = page.waitForResponse(
      (response) => response.url().endsWith('/select') && response.request().method() === 'POST',
    );
    await page.getByLabel('Active organization').selectOption(organization);
    await selected;
    await expect(page.getByLabel('Active organization')).toBeEnabled();
  }
  await expect(page.locator('main#main h1')).toBeVisible();
}

async function controlOrderEvents(page: Page) {
  let connected = false;
  const gates = new Map<string, { forward?: () => void }>();
  await page.routeWebSocket('**/api/v1/live?*', (socket) => {
    socket.connectToServer().onMessage((message) => {
      for (const frame of message.toString().split('\u001e').filter(Boolean)) {
        const received = JSON.parse(frame) as {
          target?: string;
          arguments?: { workOrderId: string; version: number }[];
        };
        const change = received.target === 'OrderChanged' ? received.arguments?.[0] : undefined;
        const gate = change && gates.get(`${change.workOrderId}:${change.version}`);
        const forward = () => socket.send(`${frame}\u001e`);
        if (gate) gate.forward = forward;
        else forward();
        if (frame === '{}') connected = true;
      }
    });
  });
  return {
    ready: () => expect.poll(() => connected).toBe(true),
    hold(order: { id: string; version: number }, version = order.version + 1) {
      const key = `${order.id}:${version}`;
      const gate: { forward?: () => void } = {};
      gates.set(key, gate);
      return async () => {
        await expect
          .poll(() => !!gate.forward, { message: `Committed order event v${version}` })
          .toBe(true);
        gates.delete(key);
        gate.forward!();
      };
    },
  };
}

async function controlDetailResponses(page: Page, path: string) {
  let paused: Promise<void> | undefined;
  await page.route(
    (url) => url.pathname === `/api/v1${path}`,
    async (route) => {
      if (route.request().method() !== 'GET') return route.fallback();
      const response = await route.fetch();
      await paused;
      await route.fulfill({ response });
    },
  );
  return () => {
    let resume: () => void = () => undefined;
    paused = new Promise<void>((resolve) => {
      resume = resolve;
    });
    return () => {
      paused = undefined;
      resume();
    };
  };
}
test.beforeAll(async () => {
  setup = await request.newContext({ ignoreHTTPSErrors: true });
  accounts = {
    owner: await account('owner'),
    manager: await account('manager'),
    viewer: await account('viewer'),
    tech: await account('tech'),
  };
});
test.beforeEach(async ({ browserName }, info) => {
  suffix = `${Date.now()}-${info.testId.slice(-6)}-${browserName}`;
  for (const member of Object.values(accounts))
    member.tokens = await call('/auth/login', {
      email: member.email,
      password,
      organizationId: member.tokens.tenantId,
    });
  owner = accounts.owner!.tokens;
  const organization = await call(
    '/organizations',
    { name: `Test workspace ${suffix}`, timeZoneId: 'UTC' },
    owner,
  );
  owner = (await call(
    `/organizations/${organization.id}/select`,
    { refreshToken: owner.refreshToken },
    owner,
  )) as Tokens;
  manager = { ...accounts.manager! };
  viewer = { ...accounts.viewer! };
  const technician = { ...accounts.tech! };
  await join(manager, 'Manager');
  await join(viewer, 'Viewer');
  await join(technician, 'Technician');
  if (!info.title.startsWith('manager completes')) await seedWorkflow();
});
test.afterAll(async () => setup.dispose());

test('the open work order refreshes collaboration queries on live changes and reconnect', async ({
  page,
}) => {
  let live: WebSocketRoute | undefined;
  let disconnected = false;
  let connections = 0;
  await page.routeWebSocket('**/api/v1/live?*', (socket) => {
    if (disconnected) {
      void socket.close({ code: 1012 });
      return;
    }
    socket.connectToServer();
    live = socket;
    connections++;
  });
  const source = (await call('/work-orders?pageSize=1', undefined, owner, 'GET')).items[0];
  const order = await call(
    '/work-orders',
    {
      customerId: source.customerId,
      serviceLocationId: source.serviceLocationId,
      title: 'Live collaboration regression',
      priority: 'Medium',
    },
    owner,
  );
  const path = `/work-orders/${order.id}`;
  const template = await call('/checklist-templates', { name: 'Live checklist' }, owner);
  await call(
    `/checklist-templates/${template.id}/items`,
    {
      expectedVersion: template.version,
      label: 'Inspect the live item',
      position: 0,
      isRequired: true,
    },
    owner,
  );
  await signIn(page, manager.email, owner.tenantId);
  const signInConnections = connections;
  const catchup = page.waitForResponse(
    (response) => response.url().includes(`${path}/comments?`) && response.ok(),
  );
  await page.goto(path);
  await catchup;
  await expect.poll(() => connections).toBeGreaterThan(signInConnections);
  await expect(page.getByText('No activity yet')).toBeVisible();
  await call(
    `${path}/comments`,
    {
      expectedVersion: order.version,
      body: 'Committed by the separate owner session',
      visibility: 'TechnicianVisible',
    },
    owner,
  );
  await expect(page.getByText('Committed by the separate owner session')).toBeVisible();

  let current = (await call(path, undefined, owner, 'GET')).order;
  const checklist = await call(
    `${path}/checklist/apply`,
    { expectedVersion: current.version, templateId: template.id },
    owner,
  );
  const checkbox = page.getByRole('checkbox', { name: /Inspect the live item/ });
  await expect(checkbox).toBeVisible();
  await page.getByRole('tab', { name: 'Audit history' }).click();
  const before = await call(
    `/audit-logs?resourceType=work_order&resourceId=${order.id}`,
    undefined,
    owner,
    'GET',
  );
  await expect(page.getByRole('tabpanel').locator('article')).toHaveCount(before.totalCount);
  await call(
    `${path}/checklist/${checklist.items[0].id}`,
    {
      expectedVersion: checklist.workOrderVersion,
      isCompleted: true,
    },
    owner,
    'PATCH',
  );
  await expect(checkbox).toBeChecked();
  await expect(page.getByRole('tabpanel').locator('article')).toHaveCount(before.totalCount + 1);

  await page.getByRole('tab', { name: 'Status history' }).click();
  await expect(page.getByRole('tabpanel').locator('article')).toHaveCount(1);
  const observer = await page.context().newPage();
  await observer.goto(path);
  await expect(observer.locator('.page-header .badge')).toHaveText('Draft');
  disconnected = true;
  const previousConnections = connections;
  await live!.close({ code: 1012 });
  current = (await call(path, undefined, owner, 'GET')).order;
  await call(
    `${path}/schedule`,
    {
      expectedVersion: current.version,
      plannedStartAt: '2027-01-15T10:00:00Z',
      plannedEndAt: '2027-01-15T11:00:00Z',
    },
    owner,
  );
  await expect(observer.locator('.page-header .badge')).toHaveText('Scheduled');
  await observer.close();
  await expect(page.getByRole('tabpanel').locator('article')).toHaveCount(1);
  disconnected = false;
  await expect.poll(() => connections).toBeGreaterThan(previousConnections);
  await expect(page.getByRole('tabpanel').locator('article')).toHaveCount(2);
  await expect(
    page.getByRole('tabpanel').getByText('Draft → Scheduled', { exact: true }),
  ).toBeVisible();
});

test('committed work order updates refresh automatically and private attachments round trip', async ({
  page,
}) => {
  const listing = await call('/work-orders?pageSize=1', undefined, owner, 'GET');
  const source = listing.items[0];
  const order = await call(
    '/work-orders',
    {
      customerId: source.customerId,
      serviceLocationId: source.serviceLocationId,
      title: 'Realtime draft',
      priority: 'Medium',
    },
    owner,
  );
  await signIn(page, accounts.owner!.email, owner.tenantId);
  const socket = page.waitForEvent('websocket', (event) => event.url().includes('/api/v1/live'));
  await page.goto(`/work-orders/${order.id}`);
  await expect(page.getByRole('heading', { name: order.title, exact: true })).toBeVisible();
  await socket;
  const title = `Live field update ${suffix}`;
  await call(`/work-orders/${order.id}`, { title, expectedVersion: order.version }, owner, 'PATCH');
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();

  const image = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=',
    'base64',
  );
  await page
    .getByLabel('Choose attachment')
    .setInputFiles({ name: 'site.png', mimeType: 'image/png', buffer: image });
  await page.getByRole('button', { name: 'Upload attachment' }).click();
  await expect(page.getByRole('button', { name: 'Preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Preview' }).click();
  await expect(page.getByRole('img', { name: 'site.png' })).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download' }).click();
  expect((await download).suggestedFilename()).toBe('site.png');
  const files = await call(`/work-orders/${order.id}/attachments`, undefined, owner, 'GET');
  expect(files).toHaveLength(1);
  const unsigned = await setup.get(
    `${apiOrigin}/api/v1/work-orders/${order.id}/attachments/${files[0].id}`,
  );
  expect(unsigned.status()).toBe(401);
});

test('work order edit restores focus after successful save, pending writes and errors', async ({
  page,
}) => {
  const source = (await call('/work-orders?pageSize=1', undefined, manager.tokens, 'GET')).items[0];
  let order = await call(
    '/work-orders',
    {
      customerId: source.customerId,
      serviceLocationId: source.serviceLocationId,
      assetId: source.assetId,
      title: 'Focus restoration regression',
      priority: 'Medium',
    },
    manager.tokens,
  );
  const path = `/work-orders/${order.id}`;
  await signIn(page, manager.email, owner.tenantId);
  const live = await controlOrderEvents(page);
  const pauseReads = await controlDetailResponses(page, path);
  await page.goto(path);
  await live.ready();
  const edit = page.getByRole('button', { name: 'Edit work order', exact: true });
  const modal = page.getByRole('dialog', { name: 'Edit work order', exact: true });
  await edit.click();
  await modal.getByLabel(/^Title/).fill('Saved focus regression');
  const saved = page.waitForResponse(
    (response) => response.url().endsWith(path) && response.request().method() === 'PATCH',
  );
  await modal.getByRole('button', { name: 'Save changes', exact: true }).click();
  expect((await saved).status()).toBe(200);
  await expect(modal).toHaveCount(0);
  await expect(edit).toBeEnabled();
  await expect(edit).toBeFocused();

  // Preserve the existing Escape and bidirectional keyboard behavior.
  await edit.click();
  const submit = modal.getByRole('button', { name: 'Save changes', exact: true });
  await submit.focus();
  await page.keyboard.press('Tab');
  await expect(modal.getByRole('button', { name: 'Close dialog' })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(submit).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);
  await expect(edit).toBeFocused();

  // Delay delivery of a real API 200 to inspect the disabled trigger while pending.
  let forwardedStatus = 0;
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(`**/api/v1${path}`, async (route) => {
    if (route.request().method() !== 'PATCH') {
      await route.continue();
      return;
    }
    const response = await route.fetch();
    forwardedStatus = response.status();
    await gate;
    await route.fulfill({ response });
  });
  try {
    await edit.click();
    await modal.getByLabel(/^Title/).fill('Saved after pending write');
    await submit.click();
    await expect.poll(() => forwardedStatus).toBe(200);
    await expect(edit).toBeDisabled();
    await expect(modal.locator('button[type="submit"]')).toBeDisabled();
    await expect(modal).toBeVisible();
    release();
    await expect(modal).toHaveCount(0);
    await expect(edit).toBeEnabled();
    await expect(edit).toBeFocused();
  } finally {
    release();
    await page.unroute(`**/api/v1${path}`);
  }

  // A genuine API conflict must retain the dialog and the user's unsaved input.
  await edit.click();
  await modal.getByLabel(/^Title/).fill('Unsaved input must survive');
  order = await call(path, undefined, owner, 'GET');
  // Keep the newer version out of the browser until the unchanged draft reaches the real API.
  const releaseConflict = live.hold(order.order);
  const resumeReads = pauseReads();
  try {
    await call(
      path,
      { expectedVersion: order.order.version, description: 'Updated by another actor' },
      owner,
      'PATCH',
    );
    const failed = page.waitForResponse(
      (response) => response.url().endsWith(path) && response.request().method() === 'PATCH',
    );
    await submit.click();
    expect((await failed).status()).toBe(409);
    await expect(modal.getByRole('alert')).toBeVisible();
    await expect(modal.getByLabel(/^Title/)).toHaveValue('Unsaved input must survive');
    await expect(modal).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(edit).toBeFocused();
    await releaseConflict();
  } finally {
    resumeReads();
  }

  // Completing the order actually unmounts its trigger: focus must reach the main landmark.
  order = (await call(path, undefined, owner, 'GET')).order;
  order = await call(
    `${path}/schedule`,
    {
      expectedVersion: order.version,
      plannedStartAt: '2027-02-10T10:00:00Z',
      plannedEndAt: '2027-02-10T11:00:00Z',
    },
    owner,
  );
  const technician = (await call('/technicians', undefined, owner, 'GET')).items[0];
  order = await call(
    `${path}/assign`,
    {
      expectedVersion: order.version,
      technicianUserIds: [technician.userId],
      primaryTechnicianUserId: technician.userId,
    },
    owner,
  );
  await call(`${path}/start`, { expectedVersion: order.version }, owner);
  await page.reload();
  const complete = page.getByRole('button', { name: 'Complete work order', exact: true });
  await complete.click();
  await page.getByRole('button', { name: 'Confirm complete', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(complete).toHaveCount(0);
  await expect(page.locator('main#main')).toBeFocused();
  await expect(page.locator('.page-header .badge')).toHaveText('Completed');
});

for (const conflict of [false, true]) {
  test(`Edit retains focus during a live detail read${conflict ? ' after a real conflict and Escape' : ''}`, async ({
    page,
  }) => {
    const source = (await call('/work-orders?pageSize=1', undefined, owner, 'GET')).items[0];
    const order = await call(
      '/work-orders',
      {
        customerId: source.customerId,
        serviceLocationId: source.serviceLocationId,
        title: 'Live focus regression',
        priority: 'Medium',
      },
      owner,
    );
    const path = `/work-orders/${order.id}`;
    const changedTitle = 'Updated by the independent owner session';
    let started = false;
    let forwardChange: (() => void) | undefined;
    await signIn(page, manager.email, owner.tenantId);
    const pauseReads = conflict ? await controlDetailResponses(page, path) : undefined;
    await page.routeWebSocket('**/api/v1/live?*', (socket) => {
      socket.connectToServer().onMessage((message) => {
        const frame = message.toString();
        if (
          frame.includes('"target":"OrderChanged"') &&
          frame.includes(order.id) &&
          frame.includes(`"version":${order.version + 1}`)
        ) {
          forwardChange = () => socket.send(message);
          return;
        }
        socket.send(message);
        if (frame === '{}\u001e') started = true;
      });
    });
    await page.goto(path);
    await expect.poll(() => started).toBe(true);
    const edit = page.getByRole('button', { name: 'Edit work order', exact: true });
    const modal = page.getByRole('dialog', { name: 'Edit work order', exact: true });
    await expect(edit).toBeEnabled();
    await edit.focus();
    if (conflict) {
      await page.keyboard.press('Enter');
      await modal.getByLabel(/^Title/).fill('Unsaved technician visit update');
    }
    let updatedVersion: number;
    const resumeReads = pauseReads?.();
    try {
      const updated = await call(
        path,
        { expectedVersion: order.version, title: changedTitle },
        owner,
        'PATCH',
      );
      updatedVersion = updated.version;
      await expect.poll(() => !!forwardChange).toBe(true);
      if (conflict) {
        const rejected = page.waitForResponse(
          (response) => response.url().endsWith(path) && response.request().method() === 'PATCH',
        );
        await modal.getByRole('button', { name: 'Save changes', exact: true }).click();
        expect((await rejected).status()).toBe(409);
        await expect(modal.getByRole('alert')).toBeVisible();
        await expect(modal.getByLabel(/^Title/)).toHaveValue('Unsaved technician visit update');
        await page.keyboard.press('Escape');
        await expect(modal).toHaveCount(0);
      }
    } finally {
      resumeReads?.();
    }
    await expect(edit).toBeFocused();

    // Proxy the real read and hold its response until the browser observes the pending GET.
    let releaseRead: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      releaseRead = resolve;
    });
    let responseStatus = 0;
    const routePath = `**/api/v1${path}`;
    await page.route(routePath, async (route) => {
      if (route.request().method() !== 'GET') return route.continue();
      const response = await route.fetch();
      responseStatus = response.status();
      await gate;
      await route.fulfill({ response });
    });
    try {
      forwardChange!();
      await expect.poll(() => responseStatus).toBe(200);
      expect(
        await edit.evaluate((button) => ({
          editFocused: document.activeElement === button,
          bodyFocused: document.activeElement === document.body,
        })),
      ).toEqual({ editFocused: true, bodyFocused: false });
      expect(await edit.isEnabled()).toBe(true);
      if (!conflict) {
        await page.keyboard.press('Enter');
        await expect(modal.getByLabel(/^Title/)).toBeEnabled();
        await expect(
          modal.getByRole('button', { name: 'Save changes', exact: true }),
        ).toBeEnabled();
        await page.keyboard.press('Escape');
        await expect(modal).toHaveCount(0);
        await expect(edit).toBeFocused();
      }
    } finally {
      releaseRead();
      await page.unrouteAll({ behavior: 'wait' });
    }
    await expect(page.getByRole('heading', { name: changedTitle, exact: true })).toBeVisible();
    await expect(edit).toBeFocused();
    if (conflict) {
      await call(
        `${path}/cancel`,
        { expectedVersion: updatedVersion, reason: 'Focus regression cleanup' },
        owner,
      );
      await expect(edit).toHaveCount(0);
      await expect(page.locator('main#main')).toBeFocused();
    } else {
      let releaseWrite: () => void = () => undefined;
      const writeGate = new Promise<void>((resolve) => {
        releaseWrite = resolve;
      });
      let written = 0;
      await page.route(routePath, async (route) => {
        if (route.request().method() !== 'PATCH') return route.continue();
        const response = await route.fetch();
        written = response.status();
        await writeGate;
        await route.fulfill({ response });
      });
      try {
        await page.keyboard.press('Enter');
        await modal.getByLabel(/^Title/).fill('Saved while keyboard focus moved on');
        await modal.getByRole('button', { name: 'Save changes', exact: true }).click();
        await expect.poll(() => written).toBe(200);
        await expect(edit).toBeDisabled();
        await page.keyboard.press('Escape');
        await expect(modal).toHaveCount(0);
        await expect(page.locator('main#main')).toBeFocused();
        await page.keyboard.press('Tab');
        const back = page.getByRole('link', { name: 'All work orders', exact: true });
        await expect(back).toBeFocused();
        releaseWrite();
        await expect(edit).toBeEnabled();
        await expect(
          page.getByRole('heading', { name: 'Saved while keyboard focus moved on' }),
        ).toBeVisible();
        await expect(back).toBeFocused();
      } finally {
        releaseWrite();
        await page.unrouteAll({ behavior: 'wait' });
      }
    }
  });
}

test('Save submits once when live updates arrive before, during and after the write', async ({
  page,
}) => {
  const source = (await call('/work-orders?pageSize=1', undefined, owner, 'GET')).items[0];
  const draft = (title: string) =>
    call(
      '/work-orders',
      {
        customerId: source.customerId,
        serviceLocationId: source.serviceLocationId,
        title,
        priority: 'Medium',
      },
      owner,
    );
  const order = await draft('Live save regression');
  const other = await draft('Independent order for background refetch');
  const path = `/work-orders/${order.id}`;
  await signIn(page, manager.email, owner.tenantId);
  const live = await controlOrderEvents(page);
  await page.goto(path);
  await live.ready();
  const edit = page.getByRole('button', { name: 'Edit work order', exact: true });
  const modal = page.getByRole('dialog', { name: 'Edit work order', exact: true });
  await edit.click();
  await modal.getByLabel(/^Title/).fill('Saved through live refetch');
  const submit = modal.locator('button[type="submit"]');
  await expect(submit).toBeEnabled();
  await submit.hover();
  const click = (await submit.boundingBox())!;
  const savePoint = { x: click.x + click.width - 12, y: click.y + click.height / 2 };
  await page.mouse.move(savePoint.x, savePoint.y);

  let releaseRead: () => void = () => undefined;
  const readGate = new Promise<void>((resolve) => {
    releaseRead = resolve;
  });
  let releaseWrite: () => void = () => undefined;
  const writeGate = new Promise<void>((resolve) => {
    releaseWrite = resolve;
  });
  let holdRead = false;
  let readStatus = 0;
  let writeStatus = 0;
  const versions: number[] = [];
  await page.route(`**/api/v1${path}`, async (route) => {
    const incoming = route.request();
    if (incoming.method() === 'GET' && holdRead) {
      holdRead = false;
      const response = await route.fetch();
      readStatus = response.status();
      await readGate;
      return route.fulfill({ response });
    }
    if (incoming.method() === 'PATCH') {
      versions.push(incoming.postDataJSON().expectedVersion);
      const response = await route.fetch();
      writeStatus = response.status();
      await writeGate;
      return route.fulfill({ response });
    }
    await route.continue();
  });
  try {
    const before = live.hold(other);
    await page.mouse.down();
    await call(
      `/work-orders/${other.id}`,
      { expectedVersion: other.version, title: 'Another order changed' },
      owner,
      'PATCH',
    );
    holdRead = true;
    await before();
    await expect.poll(() => readStatus).toBe(200);
    const enabledOnRelease = await submit.isEnabled();
    const ownCommit = live.hold(order);
    await page.mouse.up();
    await expect
      .poll(() => versions.length, {
        message: `Browser PATCH count after the real Save click (submit enabled on pointer release: ${enabledOnRelease})`,
      })
      .toBe(1);
    expect(enabledOnRelease).toBe(true);
    expect(versions).toEqual([order.version]);
    await expect.poll(() => writeStatus).toBe(200);
    releaseRead();
    await expect(submit).toBeDisabled();
    await page.mouse.dblclick(savePoint.x, savePoint.y);
    await ownCommit();

    const during = live.hold(order, order.version + 2);
    const newer = await call(
      path,
      {
        expectedVersion: order.version + 1,
        description: 'Owner updated while Save was pending',
      },
      owner,
      'PATCH',
    );
    await during();
    await expect(
      page.getByText('Owner updated while Save was pending', { exact: true }),
    ).toBeVisible();
    await expect(submit).toBeDisabled();
    releaseWrite();
    await expect(modal).toHaveCount(0);
    await expect(edit).toBeFocused();
    await expect(
      page.getByText('Owner updated while Save was pending', { exact: true }),
    ).toBeVisible();

    const after = live.hold(newer);
    await call(
      path,
      { expectedVersion: newer.version, title: 'Changed after Save completed' },
      owner,
      'PATCH',
    );
    await after();
    await expect(
      page.getByRole('heading', { name: 'Changed after Save completed', exact: true }),
    ).toBeVisible();
    await expect(edit).toBeFocused();
    expect(versions).toEqual([order.version]);
  } finally {
    releaseRead();
    releaseWrite();
    await page.unrouteAll({ behavior: 'wait' });
  }
});

test('manager completes real customer → asset → scheduled/assigned work and collaboration', async ({
  page,
}) => {
  await signIn(page, manager.email, owner.tenantId);
  await page.getByRole('link', { name: 'Customers', exact: true }).click();
  await page.getByRole('button', { name: 'Add customer', exact: true }).click();
  await page.getByLabel(/^Name/).fill(`Acme ${suffix}`);
  await page.getByLabel('Email', { exact: true }).fill('service@acme.test');
  await page.getByRole('button', { name: 'Create customer', exact: true }).click();
  await expect(page.getByRole('heading', { name: `Acme ${suffix}` })).toBeVisible();
  await page.getByRole('button', { name: 'Add service location', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Name/).fill('North facility');
  await dialog.getByLabel('Address', { exact: false }).fill('21 Service Street');
  await dialog.getByLabel('City', { exact: false }).fill('Istanbul');
  await dialog.getByLabel('Country', { exact: false }).fill('TR');
  await dialog.getByRole('button', { name: 'Create service location' }).click();
  await expect(page.getByRole('link', { name: /North facility/ }).first()).toBeVisible();
  await page.getByRole('link', { name: 'Assets', exact: true }).click();
  await page.getByRole('button', { name: 'Add asset', exact: true }).click();
  await dialog
    .getByRole('combobox', { name: /^Customer/ })
    .selectOption({ label: `Acme ${suffix}` });
  await dialog
    .getByRole('combobox', { name: /^Service location/ })
    .selectOption({ label: 'North facility' });
  await dialog.getByLabel('Asset code', { exact: false }).fill(`AHU-${suffix}`);
  await dialog.getByLabel(/^Name/).fill('Air handling unit');
  await dialog.getByRole('button', { name: 'Create asset' }).click();
  await expect(page.getByRole('heading', { name: 'Air handling unit' })).toBeVisible();
  await page.getByRole('link', { name: 'Work orders', exact: true }).click();
  await page.getByRole('button', { name: 'Create work order', exact: true }).click();
  await dialog.getByLabel('Title', { exact: false }).fill('Inspect ventilation system');
  await dialog
    .getByRole('combobox', { name: /^Customer/ })
    .selectOption({ label: `Acme ${suffix}` });
  await dialog
    .getByRole('combobox', { name: /^Service location/ })
    .selectOption({ label: 'North facility' });
  await dialog
    .getByRole('combobox', { name: /^Asset/ })
    .selectOption({ label: `AHU-${suffix} · Air handling unit` });
  await dialog.getByRole('button', { name: 'Create work order', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Inspect ventilation system' })).toBeVisible();
  await page.getByRole('button', { name: 'Schedule work order', exact: true }).click();
  await dialog.getByLabel('Planned start', { exact: false }).fill('2027-01-10T10:00');
  await dialog.getByLabel('Planned end', { exact: false }).fill('2027-01-10T11:00');
  await dialog.getByRole('button', { name: 'Save schedule' }).click();
  await page.getByRole('button', { name: 'Assign technicians', exact: true }).click();
  await dialog.getByLabel('Jamie Rivera', { exact: true }).check();
  await dialog.getByRole('button', { name: 'Save assignments' }).click();
  await expect(page.getByText('Primary technician', { exact: true })).toBeVisible();
  await expect(page.locator('.page-header .badge')).toHaveText('Assigned');
  await page
    .getByLabel('Add a comment', { exact: false })
    .fill('Check intake filters before testing.');
  await page.getByRole('button', { name: 'Post comment' }).click();
  await expect(
    page.getByText('Check intake filters before testing.', { exact: true }),
  ).toBeVisible();
  const detailUrl = page.url();
  await page.getByRole('link', { name: 'Checklist templates', exact: true }).click();
  await page.getByRole('button', { name: 'Create template', exact: true }).click();
  await dialog.getByLabel(/^Template name/).fill('Ventilation inspection');
  await dialog.getByRole('button', { name: 'Create template', exact: true }).click();
  await page.getByRole('button', { name: 'Add step', exact: true }).click();
  await dialog.getByLabel(/^Step label/).fill('Check filter condition');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('Check filter condition', { exact: true })).toBeVisible();
  await page.goto(detailUrl);
  const template = page.getByRole('combobox', { name: /^Checklist template/ });
  await template.selectOption({ label: 'Ventilation inspection' });
  const apply = page.locator('form').filter({ has: template }).locator('button[type="submit"]');
  const detailPath = new URL(detailUrl).pathname;
  let finishChecklistRead: () => void = () => undefined;
  const checklistRead = new Promise<void>((resolve) => {
    finishChecklistRead = resolve;
  });
  let detailStatus = 0;
  await page.route(`**/api/v1${detailPath}`, async (route) => {
    if (route.request().method() !== 'GET') return route.continue();
    const response = await route.fetch();
    detailStatus = response.status();
    await checklistRead;
    await route.fulfill({ response });
  });
  try {
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect.poll(() => detailStatus).toBe(200);
    await expect(apply).toBeEnabled();
    const applied = page.waitForResponse(
      (response) =>
        response.url().endsWith(`${detailPath}/checklist/apply`) &&
        response.request().method() === 'POST',
    );
    await apply.click();
    expect((await applied).status()).toBe(200);
  } finally {
    finishChecklistRead();
    await page.unrouteAll({ behavior: 'wait' });
  }
  await expect(page.getByRole('checkbox', { name: /Check filter condition/ })).toBeVisible();
  await page.getByRole('button', { name: 'Start work order', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm start', exact: true }).click();
  await page.getByRole('button', { name: 'Complete work order', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm complete', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('required checklist');
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  await page.getByLabel(/Check filter condition/).click();
  await expect(page.getByText('100% complete', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Complete work order', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm complete', exact: true }).click();
  await expect(page.locator('.page-header .badge')).toHaveText('Completed');
  await page.getByRole('tab', { name: 'Audit history' }).click();
  await expect(page.getByText(/planned Start At/i)).toBeVisible();
  await page.screenshot({ path: test.info().outputPath('work-order-desktop.png'), fullPage: true });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Inspect ventilation system' })).toBeVisible();
  const cookies = await page.context().cookies(`${apiOrigin}/api/v1/auth/web`);
  expect(cookies.find((cookie) => cookie.name === '__Secure-fieldops-refresh')).toMatchObject({
    secure: true,
    httpOnly: true,
    sameSite: 'Strict',
  });
  expect(
    await page.evaluate(() =>
      Object.keys(localStorage).filter((key) => /token|session/i.test(key)),
    ),
  ).toEqual([]);
});

test('schedule conflicts require an explicit override and retain before/after audit', async ({
  page,
}) => {
  const source = (await call('/work-orders?pageSize=1', undefined, manager.tokens, 'GET')).items[0];
  const tech = (await call('/technicians', undefined, manager.tokens, 'GET')).items[0];
  async function assigned(title: string, start: string, end: string) {
    let order = await call(
      '/work-orders',
      {
        customerId: source.customerId,
        serviceLocationId: source.serviceLocationId,
        assetId: source.assetId,
        title,
        priority: 'Medium',
      },
      manager.tokens,
    );
    order = await call(
      `/work-orders/${order.id}/schedule`,
      { expectedVersion: order.version, plannedStartAt: start, plannedEndAt: end },
      manager.tokens,
    );
    return call(
      `/work-orders/${order.id}/assign`,
      {
        expectedVersion: order.version,
        technicianUserIds: [tech.userId],
        primaryTechnicianUserId: tech.userId,
      },
      manager.tokens,
    );
  }
  const first = await assigned('Calibration A', '2027-01-11T10:00:00Z', '2027-01-11T11:00:00Z');
  await assigned('Calibration B', '2027-01-11T11:00:00Z', '2027-01-11T12:00:00Z');
  await signIn(page, manager.email, owner.tenantId);
  await page.goto(`/work-orders/${first.id}`);
  await page.getByRole('button', { name: 'Schedule work order', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Planned start/).fill('2027-01-11T11:30');
  await dialog.getByLabel(/^Planned end/).fill('2027-01-11T12:30');
  await dialog.getByRole('button', { name: 'Save schedule' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/overlap|conflict/i);
  await dialog
    .getByLabel('Availability override reason', { exact: true })
    .fill('Urgent equipment outage, coordinated with dispatch.');
  await dialog.getByRole('button', { name: 'Save schedule' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.page-header .badge')).toHaveText('Assigned');
  await page.getByRole('tab', { name: 'Audit history' }).click();
  await expect(
    page.getByText('Reason: Urgent equipment outage, coordinated with dispatch.').first(),
  ).toBeVisible();
  await expect(page.locator('.audit-changes').first()).toContainText('2027-01-11T10:00:00');
  await expect(page.locator('.audit-changes').first()).toContainText('2027-01-11T11:30:00');
});

test('registration, activation and password recovery use real email delivery', async ({ page }) => {
  const email = `browser-${suffix}@fieldops.test`;
  await page.goto('/register');
  await page.getByLabel(/^Your name/).fill('Browser Account');
  await page.getByLabel(/^Organization name/).fill('Browser Workspace');
  await page.getByLabel(/^Work email/).fill(email);
  await page.getByRole('button', { name: 'Create workspace', exact: true }).click();
  await expect(page.getByText('Check your inbox')).toBeVisible();
  const confirmation = await mail(email, 'Confirm');
  await page.getByRole('link', { name: 'Activate account', exact: true }).click();
  await page.getByLabel(/^User ID/).fill(confirmation.userId!);
  await page.getByLabel(/^Activation token/).fill(confirmation.token!);
  await page.getByLabel(/^New password/).fill(password);
  await page.getByRole('button', { name: 'Activate account', exact: true }).click();
  await expect(page.getByText('Request completed', { exact: true })).toBeVisible();
  await page.goto('/forgot-password');
  await page.getByLabel(/^Email address/).fill(email);
  await page.getByRole('button', { name: 'Send reset instructions', exact: true }).click();
  await expect(page.getByText('Request completed', { exact: true })).toBeVisible();
  const reset = await mail(email, 'password reset');
  await page.goto('/reset-password');
  await page.getByLabel(/^User ID/).fill(reset.userId!);
  await page.getByLabel(/^Reset token/).fill(reset.token!);
  await page.getByLabel(/^New password/).fill(password);
  await page.getByRole('button', { name: 'Reset password', exact: true }).click();
  await expect(page.getByText('Request completed', { exact: true })).toBeVisible();
  await signIn(page, email);
  await expect(
    page.getByRole('heading', { name: 'A clear view of the work ahead.' }),
  ).toBeVisible();
});

test('organization switch clears cached customer data and retains secure login', async ({
  page,
}) => {
  await signIn(page, manager.email, owner.tenantId);
  await page.getByRole('link', { name: 'Customers', exact: true }).click();
  await expect(
    page.getByRole('link', { name: new RegExp(`Acme ${suffix}`) }).first(),
  ).toBeVisible();
  await page.getByLabel('Active organization').selectOption({ label: managerHomeName });
  await expect(
    page.getByRole('heading', { name: 'A clear view of the work ahead.' }),
  ).toBeVisible();
  await page.getByRole('link', { name: 'Customers', exact: true }).click();
  await expect(page.getByText('No customers found')).toBeVisible();
  await expect(page.getByText(`Acme ${suffix}`)).toHaveCount(0);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Welcome back.' })).toBeVisible();
});

test('viewer cannot reach administrative routes or write API', async ({ page }) => {
  await signIn(page, viewer.email, owner.tenantId);
  await page.goto('/reports');
  await expect(page.getByText('Access restricted')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Reports', exact: true })).toHaveCount(0);
  await page.goto('/customers');
  await expect(page.getByRole('heading', { name: 'Customers', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add customer', exact: true })).toHaveCount(0);
  const denied = await setup.post(`${apiOrigin}/api/v1/customers`, {
    headers: { Authorization: `Bearer ${viewer.tokens.accessToken}` },
    data: { name: 'Denied', customerType: 'Business' },
  });
  expect(denied.status()).toBe(403);
});

test('responsive navigation, keyboard dialog and real reports/calendar', async ({ page }) => {
  await signIn(page, manager.email, owner.tenantId);
  await page.getByRole('link', { name: 'Reports', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Reports & insights' })).toBeVisible();
  await expect(page.getByText('Jamie Rivera', { exact: true }).first()).toBeVisible();
  await page.screenshot({ path: test.info().outputPath('reports-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Use dark theme' }).click();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Use light theme' })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath('reports-dark.png'), fullPage: true });
  await page.getByRole('button', { name: 'Use light theme' }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('link', { name: 'Schedule', exact: true }).click();
  await page.getByLabel('Schedule date').fill('2027-01-10');
  await expect(
    page.getByRole('link', { name: 'Inspect ventilation system', exact: true }),
  ).toBeVisible();
  await page.screenshot({ path: test.info().outputPath('schedule-mobile.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.getByRole('button', { name: 'Open navigation' }).click();
  await page.getByRole('link', { name: 'Customers', exact: true }).click();
  await page.getByRole('button', { name: 'Add customer', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add customer', exact: true })).toBeFocused();
});

test('owner manages categories, member roles, invitations and organization profile', async ({
  page,
}) => {
  await signIn(page, accounts.owner!.email, owner.tenantId);
  await page.getByRole('link', { name: 'Asset categories', exact: true }).click();
  await page.getByRole('button', { name: 'Add category', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Name/).fill('Climate equipment');
  await dialog.getByRole('button', { name: 'Create category', exact: true }).click();
  await page.getByRole('button', { name: 'Edit category', exact: true }).click();
  await dialog.getByLabel(/^Name/).fill('Climate systems');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Climate systems', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Archive', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.locator('.page-header .badge')).toHaveText('Archived');
  await page.getByRole('link', { name: 'Team', exact: true }).click();
  const member = page
    .getByRole('row')
    .filter({ hasText: manager.email })
    .filter({ has: page.getByRole('button', { name: 'Change role', exact: true }) });
  await member.getByRole('button', { name: 'Change role', exact: true }).click();
  await dialog.getByRole('combobox', { name: /^Role/ }).selectOption('Viewer');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(member).toContainText('Viewer');
  await member.getByRole('button', { name: 'Change role', exact: true }).click();
  await dialog.getByRole('combobox', { name: /^Role/ }).selectOption('Manager');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(member).toContainText('Manager');
  await page.getByRole('button', { name: 'Invite member', exact: true }).click();
  await dialog.getByLabel(/^Email address/).fill(`invited-${suffix}@fieldops.test`);
  await dialog.getByRole('button', { name: 'Send invitation', exact: true }).click();
  const invitation = page.getByRole('row').filter({ hasText: `invited-${suffix}@fieldops.test` });
  await expect(invitation).toContainText('Pending');
  await invitation.getByRole('button', { name: 'Revoke', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(invitation).toContainText('Revoked');
  await page.getByRole('link', { name: 'Settings', exact: true }).click();
  const organization = page
    .locator('section')
    .filter({ has: page.getByRole('heading', { name: 'Organization profile', exact: true }) });
  await organization.getByLabel(/^Organization name/).fill(`Updated workspace ${suffix}`);
  await organization.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByLabel('Active organization').locator('option:checked')).toHaveText(
    `Updated workspace ${suffix}`,
  );
  await expect(page.getByRole('heading', { name: 'Active sessions', exact: true })).toBeVisible();
});

test('two tabs serialize rotating cookies and clear tenant state on switch and logout', async ({
  page,
  context,
}) => {
  await signIn(page, manager.email, owner.tenantId);
  const other = await context.newPage();
  await other.goto('/overview');
  await expect(
    other.getByRole('heading', { name: 'A clear view of the work ahead.' }),
  ).toBeVisible();
  await Promise.all([page.reload(), other.reload()]);
  await expect(
    page.getByRole('heading', { name: 'A clear view of the work ahead.' }),
  ).toBeVisible();
  await expect(
    other.getByRole('heading', { name: 'A clear view of the work ahead.' }),
  ).toBeVisible();
  await other.getByRole('link', { name: 'Customers', exact: true }).click();
  await expect(
    other.getByRole('link', { name: new RegExp(`Acme ${suffix}`) }).first(),
  ).toBeVisible();
  await page.getByLabel('Active organization').selectOption({ label: managerHomeName });
  await expect(other.getByLabel('Active organization').locator('option:checked')).toHaveText(
    managerHomeName,
  );
  await other.getByRole('link', { name: 'Customers', exact: true }).click();
  await expect(other.getByText('No customers found')).toBeVisible();
  await expect(other.getByText(`Acme ${suffix}`)).toHaveCount(0);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(other.getByRole('heading', { name: 'Welcome back.' })).toBeVisible();
  await other.close();
});

async function seedWorkflow() {
  const customer = await call(
    '/customers',
    { customerType: 'Business', name: `Acme ${suffix}` },
    manager.tokens,
  );
  const location = await call(
    '/service-locations',
    {
      customerId: customer.id,
      name: 'North facility',
      addressLine: '21 Service Street',
      city: 'Istanbul',
      country: 'TR',
    },
    manager.tokens,
  );
  const asset = await call(
    '/assets',
    {
      customerId: customer.id,
      serviceLocationId: location.id,
      assetCode: `AHU-${suffix}`,
      name: 'Air handling unit',
      status: 'Active',
    },
    manager.tokens,
  );
  let order = await call(
    '/work-orders',
    {
      customerId: customer.id,
      serviceLocationId: location.id,
      assetId: asset.id,
      title: 'Inspect ventilation system',
      priority: 'Medium',
    },
    manager.tokens,
  );
  order = await call(
    `/work-orders/${order.id}/schedule`,
    {
      expectedVersion: order.version,
      plannedStartAt: '2027-01-10T10:00:00Z',
      plannedEndAt: '2027-01-10T11:00:00Z',
    },
    manager.tokens,
  );
  const technicians = await call('/technicians', undefined, manager.tokens, 'GET');
  order = await call(
    `/work-orders/${order.id}/assign`,
    {
      expectedVersion: order.version,
      technicianUserIds: [technicians.items[0].userId],
      primaryTechnicianUserId: technicians.items[0].userId,
    },
    manager.tokens,
  );
  order = await call(
    `/work-orders/${order.id}/start`,
    { expectedVersion: order.version },
    manager.tokens,
  );
  await call(
    `/work-orders/${order.id}/complete`,
    { expectedVersion: order.version },
    manager.tokens,
  );
}

test('checklist writes synchronize fast actions, preserve real conflicts and recover by explicit reload', async ({
  page,
}) => {
  const source = (await call('/work-orders?pageSize=1', undefined, manager.tokens, 'GET')).items[0];
  const tech = (await call('/technicians', undefined, manager.tokens, 'GET')).items[0];
  let order = await call(
    '/work-orders',
    {
      customerId: source.customerId,
      serviceLocationId: source.serviceLocationId,
      title: 'Version regression',
      priority: 'High',
    },
    manager.tokens,
  );
  order = await call(
    `/work-orders/${order.id}/schedule`,
    {
      expectedVersion: order.version,
      plannedStartAt: '2027-01-12T10:00:00Z',
      plannedEndAt: '2027-01-12T11:00:00Z',
    },
    manager.tokens,
  );
  order = await call(
    `/work-orders/${order.id}/assign`,
    {
      expectedVersion: order.version,
      technicianUserIds: [tech.userId],
      primaryTechnicianUserId: tech.userId,
    },
    manager.tokens,
  );
  await call(
    `/work-orders/${order.id}/comments`,
    { expectedVersion: order.version, body: 'Ready to inspect', visibility: 'TechnicianVisible' },
    manager.tokens,
  );
  let template = await call('/checklist-templates', { name: 'Version checklist' }, manager.tokens);
  template = await call(
    `/checklist-templates/${template.id}/items`,
    {
      expectedVersion: template.version,
      label: 'Check filter condition',
      position: 0,
      isRequired: true,
    },
    manager.tokens,
  );
  await signIn(page, manager.email, owner.tenantId);
  await page.goto(`/work-orders/${order.id}`);
  let hold = false;
  let waiting = false;
  let release: () => void = () => undefined;
  let gate: Promise<void> = Promise.resolve();
  const defer = () => {
    waiting = false;
    hold = true;
    gate = new Promise<void>((resolve) => {
      release = resolve;
    });
  };
  await page.route(`**/api/v1/work-orders/${order.id}`, async (route) => {
    if (route.request().method() === 'GET' && hold) {
      waiting = true;
      await gate;
    }
    await route.continue();
  });
  const writes: { path: string; expectedVersion: number }[] = [];
  page.on('request', (request) => {
    if (
      request.url().includes(`/work-orders/${order.id}/`) &&
      ['POST', 'PATCH'].includes(request.method())
    )
      writes.push({
        path: new URL(request.url()).pathname,
        expectedVersion: request.postDataJSON().expectedVersion,
      });
  });
  await page.getByRole('combobox', { name: /^Checklist template/ }).selectOption(template.id);
  defer();
  await page.getByRole('button', { name: 'Apply checklist' }).click();
  await expect.poll(() => waiting).toBe(true);
  const start = page.getByRole('button', { name: 'Start work order', exact: true });
  await expect(start).toBeDisabled();
  await expect(page.getByText(/Version 5/)).toBeVisible();
  hold = false;
  release();
  await expect(start).toBeEnabled();
  const checkbox = page.getByRole('checkbox', { name: /Check filter condition/ });
  defer();
  await checkbox.click();
  await expect.poll(() => waiting).toBe(true);
  await expect(start).toBeDisabled();
  await expect(checkbox).toBeDisabled();
  await checkbox.evaluate((element) => (element as HTMLInputElement).click());
  hold = false;
  release();
  await expect(start).toBeEnabled();
  await checkbox.click();
  await expect(start).toBeEnabled();
  await expect(checkbox).not.toBeChecked();
  await checkbox.click();
  await expect(start).toBeEnabled();
  await expect(checkbox).toBeChecked();
  expect(
    writes
      .filter((write) => write.path.includes('/checklist/') && !write.path.endsWith('/apply'))
      .map((write) => write.expectedVersion),
  ).toEqual([5, 6, 7]);
  await start.click();
  await page.getByRole('button', { name: 'Confirm start' }).click();
  await expect(page.locator('.page-header .badge')).toHaveText('In Progress');
  expect(writes.find((write) => write.path.endsWith('/start'))?.expectedVersion).toBe(8);
  await page.getByRole('button', { name: 'Pause work order', exact: true }).click();
  await call(
    `/work-orders/${order.id}`,
    { expectedVersion: 9, description: 'Changed by organization owner' },
    owner,
    'PATCH',
  );
  await page.getByRole('button', { name: 'Confirm pause' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText(/changed|version/i);
  expect(writes.filter((write) => write.path.endsWith('/pause'))).toHaveLength(1);
  await page.getByRole('button', { name: 'Reload current work order' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText(/Version 10/)).toBeVisible();
  await expect(page.getByText('Changed by organization owner', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Pause work order', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm pause' }).click();
  await expect(page.locator('.page-header .badge')).toHaveText('On Hold');
  expect(
    writes.filter((write) => write.path.endsWith('/pause')).map((write) => write.expectedVersion),
  ).toEqual([9, 10]);
  await page.reload();
  await expect(page.getByText(/Version 11/)).toBeVisible();
});

test('invitation pagination handles the exact twenty boundary and revocation on the last page', async ({
  page,
}) => {
  const emptyOrganization = await call(
    '/organizations',
    { name: `Boundary workspace ${suffix}`, timeZoneId: 'UTC' },
    owner,
  );
  owner = (await call(
    `/organizations/${emptyOrganization.id}/select`,
    { refreshToken: owner.refreshToken },
    owner,
  )) as Tokens;
  for (let index = 0; index < 20; index++)
    await call(
      `/organizations/${owner.tenantId}/invitations`,
      { email: `boundary-${index}-${suffix}@fieldops.test`, role: 'Viewer' },
      owner,
    );
  await signIn(page, accounts.owner!.email, owner.tenantId);
  await page.getByRole('link', { name: 'Team', exact: true }).click();
  const panel = page
    .locator('section')
    .filter({ has: page.getByRole('heading', { name: 'Invitations', exact: true }) });
  await expect(panel.getByText(/Page 1 \u00b7 20 records shown/)).toBeVisible();
  await expect(panel.getByRole('button', { name: 'Next page' })).toBeDisabled();
  await call(
    `/organizations/${owner.tenantId}/invitations`,
    { email: `boundary-last-${suffix}@fieldops.test`, role: 'Viewer' },
    owner,
  );
  await page.reload();
  await panel.getByRole('button', { name: 'Next page' }).click();
  await expect(panel.getByText(/Page 2 \u00b7 1 records shown/)).toBeVisible();
  // Invitations are retained on revoke; the client must honor this API contract.
  const revoke = panel.getByRole('button', { name: 'Revoke', exact: true });
  {
    await expect(revoke).toHaveCount(1);
    await revoke.click();
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm', exact: true }).click();
    await expect(panel.getByText('Revoked', { exact: true })).toBeVisible();
  }
  await expect(panel.getByRole('button', { name: 'Previous page' })).toBeEnabled();
  await panel.getByRole('button', { name: 'Previous page' }).click();
  await expect(panel.getByText(/Page 1 \u00b7 20 records shown/)).toBeVisible();
});

test('modal, closed-menu focus, contrast and archived templates work at 375, 768 and 1440 pixels', async ({
  page,
}, info) => {
  await signIn(page, accounts.owner!.email, owner.tenantId);
  const measurements: unknown[] = [];
  for (const width of [1440, 768, 375]) {
    const height = width === 375 ? 667 : 900;
    await page.setViewportSize({ width, height });
    await page.goto('/service-locations');
    if (width === 375) {
      const sidebar = page.locator('#workspace-navigation');
      await expect(sidebar).toHaveAttribute('inert', '');
      await expect(page.getByRole('link', { name: 'Customers', exact: true })).toHaveCount(0);
      await page.locator('body').evaluate((element) => {
        element.tabIndex = -1;
        element.focus();
      });
      for (let index = 0; index < 12; index++) {
        await page.keyboard.press('Tab');
        expect(await sidebar.evaluate((element) => element.contains(document.activeElement))).toBe(
          false,
        );
      }
      for (let index = 0; index < 4; index++) {
        await page.keyboard.press('Shift+Tab');
        expect(await sidebar.evaluate((element) => element.contains(document.activeElement))).toBe(
          false,
        );
      }
      await page.getByRole('button', { name: 'Open navigation' }).click();
      await expect(sidebar.getByRole('button', { name: 'Close navigation' })).toBeFocused();
      await sidebar.getByRole('button', { name: 'Sign out' }).focus();
      await page.keyboard.press('Tab');
      expect(
        await sidebar.evaluate((element) => element.querySelector('a') === document.activeElement),
      ).toBe(true);
      await page.keyboard.press('Shift+Tab');
      await expect(sidebar.getByRole('button', { name: 'Sign out' })).toBeFocused();
      await page.keyboard.press('Escape');
      await expect(page.getByRole('button', { name: 'Open navigation' })).toBeFocused();
      await page.getByRole('button', { name: 'Open navigation' }).click();
      await page.setViewportSize({ width: 768, height });
      await expect(sidebar).not.toHaveAttribute('inert', '');
      await expect(page.locator('.app-body')).not.toHaveAttribute('inert', '');
      await page.setViewportSize({ width, height });
      await expect(sidebar).toHaveAttribute('inert', '');
    }
    const trigger = page.getByRole('button', { name: 'Add service location', exact: true });
    await trigger.click();
    const modal = page.getByRole('dialog', { name: 'Add service location', exact: true });
    await expect(modal).toBeVisible();
    await expect(modal.getByRole('combobox', { name: /^Customer/ }).locator('option')).toHaveCount(
      2,
    );
    const bounds = (await modal.boundingBox())!;
    expect(Math.abs(bounds.x - (width - bounds.width) / 2)).toBeLessThan(2);
    expect(Math.abs(bounds.y - (height - bounds.height) / 2)).toBeLessThan(2);
    expect(await page.locator('body').evaluate((element) => element.style.overflow)).toBe('hidden');
    const submit = modal.getByRole('button', { name: 'Create service location', exact: true });
    await submit.focus();
    await page.keyboard.press('Tab');
    await expect(modal.getByRole('button', { name: 'Close dialog' })).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(submit).toBeFocused();
    const submitBounds = (await submit.boundingBox())!;
    expect(submitBounds.y + submitBounds.height).toBeLessThanOrEqual(bounds.y + bounds.height);
    await page.screenshot({ path: info.outputPath(`modal-${width}.png`) });
    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0);
    await expect(trigger).toBeFocused();
    expect(await page.locator('body').evaluate((element) => element.style.overflow)).not.toBe(
      'hidden',
    );
    await page.goto('/overview');
    await expect(page.getByText('Total work orders', { exact: true })).toBeVisible();
    for (const theme of ['light', 'dark']) {
      if (theme === 'dark') await page.getByRole('button', { name: 'Use dark theme' }).click();
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
      const ratios = await page
        .locator('.stat-card small, .page-header p, .live-tag')
        .evaluateAll((elements) => {
          const rgb = (value: string) => value.match(/[\d.]+/g)!.map(Number);
          const lum = (color: number[]) =>
            color
              .slice(0, 3)
              .map((value) => {
                const channel = value / 255;
                return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
              })
              .reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index]!, 0);
          return elements.map((element) => {
            let ancestor: Element | null = element;
            let background = [255, 255, 255];
            while (ancestor) {
              const candidate = rgb(getComputedStyle(ancestor).backgroundColor);
              if (candidate.length === 3 || candidate[3] === 1) {
                background = candidate;
                break;
              }
              ancestor = ancestor.parentElement;
            }
            const a = lum(rgb(getComputedStyle(element).color));
            const b = lum(background);
            return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
          });
        });
      expect(ratios.length).toBeGreaterThan(4);
      ratios.forEach((ratio) => expect(ratio).toBeGreaterThanOrEqual(4.5));
      measurements.push({ width, theme, bounds, ratios });
    }
    await page.getByRole('button', { name: 'Use light theme' }).click();
  }
  await page.goto('/checklist-templates');
  await page.getByRole('button', { name: 'Create template', exact: true }).click();
  await page
    .getByRole('dialog')
    .getByLabel(/^Template name/)
    .fill('Archive regression');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create template', exact: true })
    .click();
  await page.getByRole('button', { name: 'Add step' }).click();
  await page
    .getByRole('dialog')
    .getByLabel(/^Step label/)
    .fill('Read-only step');
  await page.getByRole('dialog').getByRole('button', { name: 'Save changes' }).click();
  await page.getByRole('button', { name: 'Archive template' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.getByText(/archived and read-only/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save changes' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add step' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Edit step' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Remove', exact: true })).toBeDisabled();
  await info.attach('viewport-and-contrast', {
    body: JSON.stringify(measurements, null, 2),
    contentType: 'application/json',
  });
});
