import { expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { TeamPage, TemplatesPage } from './ManagementPages';
const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('../api/client', async (original) => ({
  ...(await original()),
  api: (...args: unknown[]) => mocks.api(...args),
}));
vi.mock('../api/session', () => ({
  useSession: () => ({
    tenantId: 'tenant',
    userId: 'user',
    role: 'OrganizationOwner',
    sessionId: 'session',
  }),
}));
const empty = { items: [], totalCount: 0, page: 1, pageSize: 20 };
it('resets a filtered template list to page one when its previous page becomes invalid', async () => {
  mocks.api.mockImplementation(async (path: string) => {
    const params = new URL(path, 'https://test').searchParams;
    if (params.get('search'))
      return { ...empty, items: [{ id: 'match', name: 'Matched', isActive: true }], totalCount: 1 };
    return {
      ...empty,
      items: [{ id: 'initial', name: 'Initial', isActive: true }],
      totalCount: 21,
    };
  });
  setup(<TemplatesPage />);
  await userEvent.click(await screen.findByRole('button', { name: 'Next page' }));
  expect(await screen.findByText('Page 2')).toBeTruthy();
  await userEvent.type(screen.getByRole('textbox', { name: 'Search templates' }), 'Matched');
  expect(await screen.findByText('Page 1')).toBeTruthy();
  expect(await screen.findByRole('cell', { name: 'Matched' })).toBeTruthy();
});
function setup(component: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{component}</MemoryRouter>
    </QueryClientProvider>,
  );
  return client;
}
const invitations = (count: number) =>
  Array.from({ length: count }, (_, id) => ({
    id: String(id),
    email: `invite${id}@example.test`,
    role: 'Viewer',
    expiresAt: '2090-01-01T00:00:00Z',
    acceptedAt: null,
    revokedAt: null,
  }));
it.each([0, 1, 20, 21])(
  'keeps valid invitation navigation for %i records without inventing a total',
  async (count) => {
    mocks.api.mockImplementation(async (path: string) =>
      path.includes('/invitations')
        ? invitations(count).slice(
            Number(new URL(path, 'https://test').searchParams.get('offset')),
            Number(new URL(path, 'https://test').searchParams.get('offset')) + 21,
          )
        : path.includes('/members')
          ? []
          : empty,
    );
    setup(<TeamPage />);
    const panel = within(screen.getByRole('heading', { name: 'Invitations' }).closest('section')!);
    const next = await panel.findByRole('button', { name: 'Next page' });
    expect((next as HTMLButtonElement).disabled).toBe(count <= 20);
    expect(
      (panel.getByRole('button', { name: 'Previous page' }) as HTMLButtonElement).disabled,
    ).toBe(true);
    if (count === 21) {
      await userEvent.click(next);
      expect(await panel.findByText(/Page 2 · 1 records/)).toBeTruthy();
      expect((panel.getByRole('button', { name: 'Next page' }) as HTMLButtonElement).disabled).toBe(
        true,
      );
      await userEvent.click(panel.getByRole('button', { name: 'Previous page' }));
      expect(await panel.findByText(/Page 1 · 20 records/)).toBeTruthy();
    }
  },
);
it('retains Previous on an empty last page after the list shrinks', async () => {
  let records = invitations(21);
  mocks.api.mockImplementation(async (path: string) =>
    path.includes('/invitations')
      ? records.slice(
          Number(new URL(path, 'https://test').searchParams.get('offset')),
          Number(new URL(path, 'https://test').searchParams.get('offset')) + 21,
        )
      : path.includes('/members')
        ? []
        : empty,
  );
  const client = setup(<TeamPage />);
  const panel = within(screen.getByRole('heading', { name: 'Invitations' }).closest('section')!);
  await userEvent.click(await panel.findByRole('button', { name: 'Next page' }));
  await panel.findByText('invite20@example.test');
  records = invitations(20);
  await client.invalidateQueries();
  expect(await panel.findByText('No invitations')).toBeTruthy();
  expect((panel.getByRole('button', { name: 'Previous page' }) as HTMLButtonElement).disabled).toBe(
    false,
  );
  await userEvent.click(panel.getByRole('button', { name: 'Previous page' }));
  expect(await panel.findByText(/Page 1 · 20 records/)).toBeTruthy();
});
it.each([true, false])('offers template edits only when active=%s', async (isActive) => {
  const template = {
    id: 'template',
    name: 'Inspection',
    version: 2,
    isActive,
    items: [{ id: 'item', label: 'Check filter', position: 0, isRequired: true }],
  };
  mocks.api.mockImplementation(async (path: string) =>
    path.includes('/template?')
      ? template
      : path.startsWith('/checklist-templates?')
        ? { ...empty, items: [template], totalCount: 1 }
        : template,
  );
  setup(<TemplatesPage />);
  await userEvent.click(await screen.findByRole('button', { name: 'Manage steps' }));
  const add = await screen.findByRole('button', { name: 'Add step' });
  expect((add as HTMLButtonElement).disabled).toBe(!isActive);
  expect((screen.getByRole('button', { name: 'Edit step' }) as HTMLButtonElement).disabled).toBe(
    !isActive,
  );
  expect((screen.getByRole('button', { name: 'Remove' }) as HTMLButtonElement).disabled).toBe(
    !isActive,
  );
  expect(screen.queryByRole('button', { name: 'Save changes' }) !== null).toBe(isActive);
  expect(screen.queryByRole('button', { name: 'Archive template' }) !== null).toBe(isActive);
  if (!isActive) expect(screen.getByText(/archived and read-only/)).toBeTruthy();
});
