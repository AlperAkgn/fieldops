import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { Plus, ShieldCheck, Users } from 'lucide-react';
import { api, queryString } from '../api/client';
import {
  roles,
  type Invitation,
  type Member,
  type Organization,
  type Page,
  type Profile,
  type Session,
  type Technician,
  type Template,
} from '../api/contracts';
import { dateTime, useAction } from '../api/hooks';
import { useSession } from '../api/session';
import { Form } from '../components/Form';
import {
  Badge,
  Confirm,
  DataTable,
  Empty,
  ErrorBox,
  Modal,
  PageHeader,
  Pagination,
  OffsetPagination,
  Panel,
  Skeleton,
} from '../components/ui';
import { RelatedOrders } from './CatalogPage';

export function TeamPage() {
  const session = useSession();
  const [page, setPage] = useState(1);
  const [invitePage, setInvitePage] = useState(1);
  const [memberPage, setMemberPage] = useState(1);
  const [now] = useState(() => Date.now());
  const [search, setSearch] = useState('');
  const [invite, setInvite] = useState(false);
  const [editing, setEditing] = useState<Member | null>(null);
  const [confirm, setConfirm] = useState<{ title: string; path: string; method?: string } | null>(
    null,
  );
  const [technician, setTechnician] = useState<Technician | null>(null);
  const action = useAction(() => {
    setInvite(false);
    setEditing(null);
    setConfirm(null);
  });
  const owner = session?.role === 'OrganizationOwner';
  const root = `/organizations/${session?.tenantId}`;
  const members = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'members',
      memberPage,
    ],
    queryFn: ({ signal }) =>
      api<Member[]>(`${root}/members?offset=${(memberPage - 1) * 20}&limit=21`, { signal }),
    enabled: owner,
  });
  const invitations = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'invitations',
      invitePage,
    ],
    queryFn: ({ signal }) =>
      api<Invitation[]>(`${root}/invitations?offset=${(invitePage - 1) * 20}&limit=21`, { signal }),
    enabled: owner,
  });
  const technicians = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'technicians',
      page,
      search,
    ],
    queryFn: ({ signal }) =>
      api<Page<Technician>>(`/technicians?${queryString({ page, pageSize: 20, search })}`, {
        signal,
      }),
  });
  return (
    <>
      <PageHeader
        eyebrow="BETTER TOGETHER"
        title="Your team"
        description="The right people, connected to the right work."
      >
        {owner && (
          <button className="btn primary" onClick={() => setInvite(true)}>
            <Plus size={17} />
            Invite member
          </button>
        )}
      </PageHeader>
      {owner && (
        <Panel title="Organization members">
          {members.isPending ? (
            <Skeleton />
          ) : members.isError ? (
            <ErrorBox error={members.error} />
          ) : (
            <>
              <DataTable headings={['Member', 'Role', 'Account status', 'Actions']}>
                {members.data.slice(0, 20).map((member) => (
                  <tr key={member.id}>
                    <td>
                      <span className="record-link">
                        <span className="avatar">{member.email.slice(0, 1).toUpperCase()}</span>
                        {member.email}
                      </span>
                    </td>
                    <td>
                      <Badge>{member.role}</Badge>
                    </td>
                    <td>
                      <Badge>{member.isActive ? 'Active' : 'Inactive'}</Badge>
                    </td>
                    <td>
                      <div className="row-actions">
                        <button className="btn secondary" onClick={() => setEditing(member)}>
                          Change role
                        </button>
                        <button
                          className="btn secondary"
                          onClick={() =>
                            setConfirm({
                              title: `${member.isActive ? 'Deactivate' : 'Reactivate'} ${member.email}?`,
                              path: `${root}/members/${member.id}/${member.isActive ? 'deactivate' : 'reactivate'}`,
                            })
                          }
                        >
                          {member.isActive ? 'Deactivate' : 'Reactivate'}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </DataTable>
            </>
          )}
          {members.data && (
            <OffsetPagination
              page={memberPage}
              shown={Math.min(20, members.data.length)}
              hasNext={members.data.length > 20}
              onChange={setMemberPage}
            />
          )}
        </Panel>
      )}
      {owner && (
        <Panel title="Invitations">
          {invitations.isPending ? (
            <Skeleton />
          ) : invitations.isError ? (
            <ErrorBox error={invitations.error} />
          ) : !invitations.data.length ? (
            <Empty title="No invitations" text="Invite people to collaborate in your workspace." />
          ) : (
            <>
              <DataTable headings={['Email', 'Role', 'Status', 'Expires', '']}>
                {invitations.data.slice(0, 20).map((item) => (
                  <tr key={item.id}>
                    <td>{item.email}</td>
                    <td>
                      <Badge>{item.role}</Badge>
                    </td>
                    <td>
                      {item.acceptedAt
                        ? 'Accepted'
                        : item.revokedAt
                          ? 'Revoked'
                          : Date.parse(item.expiresAt) < now
                            ? 'Expired'
                            : 'Pending'}
                    </td>
                    <td>{dateTime(item.expiresAt)}</td>
                    <td>
                      {!item.acceptedAt && !item.revokedAt && (
                        <button
                          className="btn secondary"
                          onClick={() =>
                            setConfirm({
                              title: `Revoke invitation for ${item.email}?`,
                              path: `${root}/invitations/${item.id}`,
                              method: 'DELETE',
                            })
                          }
                        >
                          Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </DataTable>
            </>
          )}
          {invitations.data && (
            <OffsetPagination
              page={invitePage}
              shown={Math.min(20, invitations.data.length)}
              hasNext={invitations.data.length > 20}
              onChange={setInvitePage}
            />
          )}
        </Panel>
      )}
      <Panel title="Technician directory" action={<Users size={18} />}>
        <div className="toolbar">
          <input
            aria-label="Search technician directory"
            placeholder="Search technician name…"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
          {!owner && (
            <span className="muted">
              Membership and invitation administration requires an owner.
            </span>
          )}
        </div>
        {technicians.isPending ? (
          <Skeleton />
        ) : technicians.isError ? (
          <ErrorBox error={technicians.error} />
        ) : !technicians.data.items.length ? (
          <Empty
            title="No technicians found"
            text="Technicians appear after accepting an invitation and activating their account."
          />
        ) : (
          <>
            <DataTable headings={['Technician', 'Work assignments']}>
              {technicians.data.items.map((item) => (
                <tr key={item.userId}>
                  <td>{item.displayName}</td>
                  <td>
                    <button className="btn secondary" onClick={() => setTechnician(item)}>
                      View assignments
                    </button>
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination page={page} total={technicians.data.totalCount} onChange={setPage} />
          </>
        )}
      </Panel>
      {technician && (
        <RelatedOrders
          key={technician.userId}
          title={`${technician.displayName} · Assignments`}
          filters={{ technicianUserId: technician.userId }}
        />
      )}
      {invite && (
        <Modal title="Invite a team member" onClose={() => setInvite(false)}>
          <p className="muted">
            The recipient receives an invitation token by email. They must sign in to a verified
            account before accepting.
          </p>
          <Form
            fields={[
              { name: 'email', label: 'Email address', type: 'email', required: true, max: 254 },
              { name: 'role', label: 'Role', type: 'select', required: true, options: roles },
            ]}
            initial={{ role: 'Technician' }}
            onSubmit={(values) => action.mutateAsync({ path: `${root}/invitations`, body: values })}
            pending={action.isPending}
            error={action.error}
            submitLabel="Send invitation"
          />
        </Modal>
      )}
      {editing && (
        <Modal title="Change membership role" onClose={() => setEditing(null)}>
          <p>{editing.email}. Last-owner protection is enforced by the server.</p>
          <Form
            fields={[
              { name: 'role', label: 'Role', type: 'select', required: true, options: roles },
            ]}
            initial={{ role: editing.role }}
            onSubmit={(values) =>
              action.mutateAsync({
                path: `${root}/members/${editing.id}/role`,
                method: 'PATCH',
                body: values,
              })
            }
            pending={action.isPending}
            error={action.error}
          />
        </Modal>
      )}
      {confirm && (
        <Confirm
          error={action.error}
          title={confirm.title}
          text="This changes access to the current organization. Existing data is preserved."
          pending={action.isPending}
          onClose={() => setConfirm(null)}
          onConfirm={() => action.mutate({ path: confirm.path, method: confirm.method || 'POST' })}
        />
      )}
      {action.isError && !invite && !editing && <ErrorBox error={action.error} />}
    </>
  );
}

export function SettingsPage() {
  const session = useSession();
  const client = useQueryClient();
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState<string | null>(null);
  const action = useAction(() => setConfirm(null));
  const root = `/organizations/${session?.tenantId}`;
  const profile = useQuery({
    queryKey: [session?.tenantId, session?.userId, session?.role, session?.sessionId, 'me'],
    queryFn: ({ signal }) => api<Profile>('/auth/me', { signal }),
  });
  const organization = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'organization',
    ],
    queryFn: ({ signal }) => api<Organization>(root, { signal }),
  });
  const sessions = useQuery({
    queryKey: [session?.tenantId, session?.userId, session?.role, session?.sessionId, 'sessions'],
    queryFn: ({ signal }) => api<Session[]>('/auth/sessions', { signal }),
  });
  return (
    <>
      <PageHeader
        eyebrow="MAKE IT YOURS"
        title="Workspace settings"
        description="Your organization, your account, and the way you work."
      />
      <div className="settings-grid">
        <Panel title="Your profile" action={<ShieldCheck size={18} />}>
          {profile.isPending ? (
            <Skeleton />
          ) : profile.isError ? (
            <ErrorBox error={profile.error} />
          ) : (
            <dl className="details-grid">
              <div>
                <dt>Name</dt>
                <dd>{profile.data.displayName}</dd>
              </div>
              <div>
                <dt>Email</dt>
                <dd>
                  {profile.data.email}
                  <small>{profile.data.emailConfirmed ? 'Verified email' : 'Not verified'}</small>
                </dd>
              </div>
              <div>
                <dt>Workspace role</dt>
                <dd>
                  <Badge>{session?.role}</Badge>
                </dd>
              </div>
              <div>
                <dt>Password</dt>
                <dd>
                  <Link className="text-link" to="/forgot-password">
                    Request a password reset ↗
                  </Link>
                </dd>
              </div>
            </dl>
          )}
          <p className="muted">
            Use the theme button in the top bar to change appearance. Switch organizations from the
            workspace menu.
          </p>
        </Panel>
        <Panel title="Organization profile">
          {organization.isPending ? (
            <Skeleton />
          ) : organization.isError ? (
            <ErrorBox error={organization.error} />
          ) : session?.role === 'OrganizationOwner' ? (
            <Form
              key={organization.data.name}
              fields={[
                { name: 'name', label: 'Organization name', required: true, max: 160 },
                {
                  name: 'timeZoneId',
                  label: 'Time zone',
                  required: true,
                  help: 'Use a valid IANA time zone.',
                },
              ]}
              initial={{ name: organization.data.name, timeZoneId: organization.data.timeZoneId }}
              onSubmit={(values) =>
                action.mutateAsync({ path: root, method: 'PATCH', body: values })
              }
              error={action.error}
              pending={action.isPending}
            />
          ) : (
            <dl>
              <dt>Organization</dt>
              <dd>{organization.data.name}</dd>
              <dt>Time zone</dt>
              <dd>{organization.data.timeZoneId}</dd>
            </dl>
          )}
        </Panel>
        <Panel title="Create another organization">
          <Form
            fields={[
              { name: 'name', label: 'Organization name', required: true, max: 160 },
              { name: 'timeZoneId', label: 'Time zone', required: true },
            ]}
            initial={{ timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone }}
            onSubmit={(values) => action.mutateAsync({ path: '/organizations', body: values })}
            error={action.error}
            pending={action.isPending}
            submitLabel="Create organization"
          />
          <p className="muted">
            Your current workspace stays active. Select the new organization in the workspace menu.
          </p>
        </Panel>
        <Panel title="Accept an invitation">
          <Form
            fields={[
              {
                name: 'token',
                label: 'Invitation token',
                type: 'textarea',
                required: true,
                max: 4000,
              },
            ]}
            onSubmit={(values) => action.mutateAsync({ path: '/invitations/accept', body: values })}
            error={action.error}
            pending={action.isPending}
            submitLabel="Accept invitation"
          />
        </Panel>
      </div>
      <Panel
        title="Active sessions"
        action={
          <button className="btn danger" onClick={() => setConfirm('/auth/logout-all')}>
            Revoke all sessions
          </button>
        }
      >
        {sessions.isPending ? (
          <Skeleton />
        ) : sessions.isError ? (
          <ErrorBox error={sessions.error} />
        ) : (
          <DataTable headings={['Session', 'Organization', 'Last used', 'Expires', '']}>
            {sessions.data.map((item) => (
              <tr key={item.id}>
                <td>
                  {item.transport} {item.isCurrent && <Badge>Current</Badge>}
                </td>
                <td className="mono">{item.tenantId.slice(0, 8)}</td>
                <td>{dateTime(item.lastUsedAt)}</td>
                <td>{dateTime(item.expiresAt)}</td>
                <td>
                  <button
                    className="btn secondary"
                    onClick={() => setConfirm(`/auth/sessions/${item.id}`)}
                  >
                    Revoke
                  </button>
                </td>
              </tr>
            ))}
          </DataTable>
        )}
      </Panel>
      {confirm && (
        <Confirm
          error={action.error}
          title="Revoke session access?"
          text="Affected sessions stop working immediately. Revoking your current session returns you to sign in."
          pending={action.isPending}
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            const current =
              confirm === '/auth/logout-all' || confirm.endsWith(session?.sessionId || 'none');
            action.mutate(
              { path: confirm, method: confirm === '/auth/logout-all' ? 'POST' : 'DELETE' },
              {
                onSuccess: () => {
                  if (current) {
                    client.clear();
                    navigate('/login');
                    window.location.reload();
                  }
                },
              },
            );
          }}
        />
      )}
    </>
  );
}

export function TemplatesPage() {
  const session = useSession();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState('');
  const [creating, setCreating] = useState(false);
  const [editingItem, setEditingItem] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<{ path: string; label: string } | null>(null);
  const action = useAction((result) => {
    setCreating(false);
    setEditingItem(null);
    setConfirm(null);
    if ((result as Template | undefined)?.id) setSelected((result as Template).id);
  });
  const query = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'checklist-templates',
      page,
      search,
    ],
    queryFn: ({ signal }) =>
      api<Page<Template>>(`/checklist-templates?${queryString({ page, pageSize: 20, search })}`, {
        signal,
      }),
  });
  const detail = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'checklist-templates',
      selected,
    ],
    queryFn: ({ signal }) => api<Template>(`/checklist-templates/${selected}`, { signal }),
    enabled: !!selected,
  });
  const template = detail.data;
  const item = template?.items.find((value) => value.id === editingItem);
  return (
    <>
      <PageHeader
        eyebrow="CONSISTENT SERVICE"
        title="Checklist templates"
        description="Turn your service standards into clear, repeatable steps."
      >
        <button className="btn primary" onClick={() => setCreating(true)}>
          <Plus size={17} />
          Create template
        </button>
      </PageHeader>
      <Panel>
        <div className="toolbar">
          <input
            aria-label="Search templates"
            placeholder="Search templates…"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
        </div>
        {query.isPending ? (
          <Skeleton />
        ) : query.isError ? (
          <ErrorBox error={query.error} />
        ) : !query.data.items.length ? (
          <Empty
            title="No templates yet"
            text="Create a template and add required or optional service steps."
          />
        ) : (
          <>
            <DataTable headings={['Template', 'Status', 'Actions']}>
              {query.data.items.map((value) => (
                <tr key={value.id}>
                  <td>{value.name}</td>
                  <td>
                    <Badge>{value.isActive ? 'Active' : 'Archived'}</Badge>
                  </td>
                  <td>
                    <button className="btn secondary" onClick={() => setSelected(value.id)}>
                      Manage steps
                    </button>
                  </td>
                </tr>
              ))}
            </DataTable>
            <Pagination page={page} total={query.data.totalCount} onChange={setPage} />
          </>
        )}
      </Panel>
      {selected &&
        (detail.isPending ? (
          <Skeleton />
        ) : detail.isError ? (
          <ErrorBox error={detail.error} />
        ) : (
          template && (
            <Panel
              title={template.name}
              action={
                template.isActive && (
                  <button
                    className="btn secondary"
                    disabled={action.isPending}
                    onClick={() =>
                      setConfirm({
                        path: `/checklist-templates/${template.id}?expectedVersion=${template.version}`,
                        label: template.name,
                      })
                    }
                  >
                    Archive template
                  </button>
                )
              }
            >
              {template.isActive ? (
                <Form
                  key={`${template.id}-${template.version}`}
                  fields={[{ name: 'name', label: 'Template name', required: true, max: 120 }]}
                  initial={{ name: template.name }}
                  onSubmit={(values) =>
                    action.mutateAsync({
                      path: `/checklist-templates/${template.id}`,
                      method: 'PATCH',
                      body: { ...values, expectedVersion: template.version },
                    })
                  }
                  error={action.error}
                  pending={action.isPending}
                />
              ) : (
                <p role="status">
                  This template is archived and read-only. Existing work order checklists are
                  preserved.
                </p>
              )}
              {template.items.length ? (
                <DataTable headings={['Position', 'Step', 'Required', 'Actions']}>
                  {template.items.map((value) => (
                    <tr key={value.id}>
                      <td>{value.position}</td>
                      <td>{value.label}</td>
                      <td>{value.isRequired ? 'Required' : 'Optional'}</td>
                      <td>
                        <div className="row-actions">
                          <button
                            className="btn secondary"
                            disabled={!template.isActive || action.isPending}
                            onClick={() => setEditingItem(value.id)}
                          >
                            Edit step
                          </button>
                          <button
                            className="btn secondary"
                            disabled={!template.isActive || action.isPending}
                            onClick={() =>
                              setConfirm({
                                path: `/checklist-templates/${template.id}/items/${value.id}?expectedVersion=${template.version}`,
                                label: value.label,
                              })
                            }
                          >
                            Remove
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </DataTable>
              ) : (
                <p className="muted">Add a step before applying this template to work.</p>
              )}
              <button
                className="btn secondary"
                disabled={!template.isActive || action.isPending}
                onClick={() => setEditingItem('new')}
              >
                <Plus size={16} />
                Add step
              </button>
            </Panel>
          )
        ))}
      {creating && (
        <Modal title="Create checklist template" onClose={() => setCreating(false)}>
          <Form
            fields={[{ name: 'name', label: 'Template name', required: true, max: 120 }]}
            onSubmit={(values) =>
              action.mutateAsync({ path: '/checklist-templates', body: values })
            }
            error={action.error}
            pending={action.isPending}
            submitLabel="Create template"
          />
        </Modal>
      )}
      {editingItem && template?.isActive && (
        <Modal
          title={item ? 'Edit checklist step' : 'Add checklist step'}
          onClose={() => setEditingItem(null)}
        >
          <Form
            fields={[
              { name: 'label', label: 'Step label', required: true, max: 240 },
              { name: 'position', label: 'Position', type: 'number', required: true },
              {
                name: 'isRequired',
                label: 'Requirement',
                type: 'select',
                options: [
                  { value: 'true', label: 'Required' },
                  { value: 'false', label: 'Optional' },
                ],
                required: true,
              },
            ]}
            initial={{
              label: item?.label || '',
              position: String(item?.position ?? template.items.length),
              isRequired: String(item?.isRequired ?? true),
            }}
            onSubmit={(values) =>
              action.mutateAsync({
                path: `/checklist-templates/${template.id}/items${item ? `/${item.id}` : ''}`,
                method: item ? 'PATCH' : 'POST',
                body: {
                  label: values.label,
                  position: Number(values.position),
                  isRequired: values.isRequired === 'true',
                  expectedVersion: template.version,
                },
              })
            }
            pending={action.isPending}
            error={action.error}
          />
        </Modal>
      )}
      {confirm && (
        <Confirm
          error={action.error}
          title={`Remove ${confirm.label}?`}
          text="Existing work order checklist snapshots are preserved."
          pending={action.isPending}
          onClose={() => setConfirm(null)}
          onConfirm={() => action.mutate({ path: confirm.path, method: 'DELETE' })}
        />
      )}
    </>
  );
}
