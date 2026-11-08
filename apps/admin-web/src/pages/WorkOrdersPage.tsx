import { useWorkOrderAction } from '../api/work-order-state';
import { WorkOrderError } from '../components/WorkOrderError';
import { useLayoutEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ArrowLeft,
  ArrowUpRight,
  CalendarDays,
  CheckCircle2,
  Clock3,
  Plus,
  Search,
  Users,
} from 'lucide-react';
import { api, queryString } from '../api/client';
import {
  priorities,
  statuses,
  type OrderDetail,
  type Page,
  type WorkOrder,
} from '../api/contracts';
import { dateTime, useAction } from '../api/hooks';
import { canManage, useSession } from '../api/session';
import { Form } from '../components/Form';
import type { Field } from '../components/form-schema';
import {
  Badge,
  DataTable,
  Empty,
  ErrorBox,
  Modal,
  PageHeader,
  Pagination,
  Panel,
  Skeleton,
} from '../components/ui';
import { AssignDialog, ScheduleDialog } from '../components/OrderActions';
import { OrderCollaboration } from './OrderCollaboration';
const orderFields: Field[] = [
  { name: 'title', label: 'Title', required: true, max: 160 },
  { name: 'priority', label: 'Priority', type: 'select', options: priorities, required: true },
  { name: 'customerId', label: 'Customer', lookup: 'customers', required: true },
  {
    name: 'serviceLocationId',
    label: 'Service location',
    lookup: 'service-locations',
    dependsOn: 'customerId',
    param: 'customerId',
    required: true,
  },
  {
    name: 'assetId',
    label: 'Asset',
    lookup: 'assets',
    dependsOn: 'serviceLocationId',
    param: 'serviceLocationId',
  },
  { name: 'description', label: 'Description', type: 'textarea' },
];

export function WorkOrdersPage() {
  const session = useSession();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [priority, setPriority] = useState('');
  const [sort, setSort] = useState('createdAt');
  const [descending, setDescending] = useState(true);
  const [creating, setCreating] = useState(false);
  const action = useAction((result) => {
    setCreating(false);
    navigate(`/work-orders/${(result as WorkOrder).id}`);
  });
  const params = queryString({
    page,
    pageSize: 20,
    search,
    status,
    priority,
    sortBy: sort,
    descending,
  });
  const query = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'work-orders',
      params,
    ],
    queryFn: ({ signal }) => api<Page<WorkOrder>>(`/work-orders?${params}`, { signal }),
  });
  return (
    <>
      <PageHeader
        eyebrow="SERVICE OPERATIONS"
        title="Work orders"
        description="From first request to a job well done."
      >
        {canManage(session?.role) && (
          <button className="btn primary" onClick={() => setCreating(true)}>
            <Plus size={17} />
            Create work order
          </button>
        )}
      </PageHeader>
      <Panel>
        <div className="list-summary">
          <strong>
            All work orders <span className="count-pill">{query.data?.totalCount ?? '…'}</span>
          </strong>
          <span className="muted">Live from your workspace</span>
        </div>
        <div className="toolbar">
          <div className="search-input">
            <Search size={17} />
            <input
              aria-label="Search work orders"
              placeholder="Search work orders…"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
            />
          </div>
          <select
            aria-label="Filter by status"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value);
              setPage(1);
            }}
          >
            <option value="">All statuses</option>
            {statuses.map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
          <select
            aria-label="Filter by priority"
            value={priority}
            onChange={(event) => {
              setPriority(event.target.value);
              setPage(1);
            }}
          >
            <option value="">All priorities</option>
            {priorities.map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
          <select
            aria-label="Sort work orders"
            value={sort}
            onChange={(event) => {
              setSort(event.target.value);
              setPage(1);
            }}
          >
            <option value="createdAt">Created</option>
            <option value="updatedAt">Updated</option>
            <option value="title">Title</option>
            <option value="plannedStartAt">Planned start</option>
          </select>
          <button
            className="btn secondary"
            aria-label="Toggle sort direction"
            onClick={() => setDescending(!descending)}
          >
            {descending ? '↓ Desc' : '↑ Asc'}
          </button>
        </div>
        {query.isPending ? (
          <Skeleton />
        ) : query.isError ? (
          <ErrorBox
            error={query.error}
            retry={() => {
              void query.refetch();
            }}
          />
        ) : !query.data.items.length ? (
          <Empty
            title="No work orders found"
            text="Adjust the filters or create a work order to start planning."
          />
        ) : (
          <DataTable
            headings={['Work order', 'Status', 'Priority', 'Planned start', 'Updated', '']}
          >
            {query.data.items.map((order) => (
              <tr key={order.id}>
                <td>
                  <Link to={`/work-orders/${order.id}`} className="record-link">
                    <span className="order-indicator" />
                    <span>
                      {order.title}
                      <small>{order.workOrderNumber.slice(0, 14).toUpperCase()}</small>
                    </span>
                  </Link>
                </td>
                <td>
                  <Badge>{order.status}</Badge>
                </td>
                <td>
                  <Badge>{order.priority}</Badge>
                </td>
                <td>{dateTime(order.plannedStartAt)}</td>
                <td className="muted">{dateTime(order.updatedAt)}</td>
                <td>
                  <Link
                    className="btn secondary icon"
                    aria-label={`View ${order.title}`}
                    to={`/work-orders/${order.id}`}
                  >
                    <ArrowUpRight size={16} />
                  </Link>
                </td>
              </tr>
            ))}
          </DataTable>
        )}
        {query.data && <Pagination page={page} total={query.data.totalCount} onChange={setPage} />}
      </Panel>
      {creating && (
        <Modal title="Create work order" onClose={() => setCreating(false)}>
          <Form
            fields={orderFields}
            initial={{ priority: 'Medium' }}
            onSubmit={(values) =>
              action.mutateAsync({
                path: '/work-orders',
                body: {
                  ...values,
                  assetId: values.assetId || null,
                  description: values.description || null,
                },
              })
            }
            error={action.error}
            pending={action.isPending}
            submitLabel="Create work order"
          />
        </Modal>
      )}
    </>
  );
}

export function WorkOrderDetailPage() {
  const { id } = useParams();
  const session = useSession();
  const client = useQueryClient();
  const [dialog, setDialog] = useState('');
  const [snapshot, setSnapshot] = useState<WorkOrder | null>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const action = useWorkOrderAction(id || '', () => setDialog(''));
  useLayoutEffect(() => {
    if (dialog || action.busy || !returnFocus.current) return;
    const target = returnFocus.current;
    returnFocus.current = null;
    const main = document.getElementById('main');
    // Layout effects run after the trigger's disabled attribute has been committed.
    // Do not steal focus if the user has already moved away from the fallback.
    if (![main, document.body, target].includes(document.activeElement as HTMLElement)) return;
    if (target.isConnected && target !== document.body) target.focus();
    if (document.activeElement !== target || target === document.body) main?.focus();
  }, [dialog, action.busy]);
  const reload = () => {
    setDialog('');
    action.reset();
    void action.reload().catch(() => undefined);
  };
  const query = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'work-orders',
      id,
    ],
    queryFn: ({ signal }) => api<OrderDetail>(`/work-orders/${id}`, { signal }),
  });
  if (query.isPending) return <Skeleton />;
  if (query.isError)
    return (
      <ErrorBox
        error={query.error}
        retry={() => {
          void query.refetch();
        }}
      />
    );
  const { order, customer, serviceLocation, asset, assignments } = query.data;
  const openDialog = (name: string) => {
    if (!action.busy) {
      setSnapshot(order);
      setDialog(name);
      action.reset();
    }
  };
  const command = snapshot || order;
  const manage = canManage(session?.role);
  const execute =
    manage ||
    (session?.role === 'Technician' &&
      assignments.some((item) => item.technicianUserId === session.userId));
  const terminal = ['Completed', 'Cancelled'].includes(order.status);
  const transitions: Record<string, string[]> = {
    Assigned: ['start'],
    InProgress: ['pause', 'complete'],
    OnHold: ['resume'],
  };
  const actions = execute ? [...(transitions[order.status] ?? [])] : [];
  if (manage && ['Draft', 'Scheduled', 'Assigned', 'OnHold'].includes(order.status))
    actions.push('cancel');
  return (
    <>
      <Link className="back-link" to="/work-orders">
        <ArrowLeft size={15} />
        All work orders
      </Link>
      <PageHeader
        eyebrow={order.workOrderNumber.toUpperCase()}
        title={order.title}
        description={`Updated ${dateTime(order.updatedAt)} · Version ${order.version}`}
      >
        <button
          className="btn secondary"
          onClick={() => {
            void client.invalidateQueries();
          }}
        >
          Refresh
        </button>
        {manage && !terminal && (
          <button
            className="btn secondary"
            disabled={action.busy}
            onClick={() => openDialog('edit')}
          >
            Edit work order
          </button>
        )}
        <Badge>{order.status}</Badge>
      </PageHeader>
      <div className="detail-layout">
        <div className="section-stack">
          <Panel title="Job overview">
            <p className="description-text">{order.description || 'No description provided.'}</p>
            <dl className="details-grid">
              <div>
                <dt>Customer</dt>
                <dd>
                  {session?.role === 'Technician' ? (
                    customer.name
                  ) : (
                    <Link className="text-link" to={`/customers/${customer.id}`}>
                      {customer.name}
                    </Link>
                  )}
                </dd>
              </div>
              <div>
                <dt>Service location</dt>
                <dd>
                  {serviceLocation.name}
                  <small>
                    {serviceLocation.addressLine}, {serviceLocation.city}
                  </small>
                </dd>
              </div>
              <div>
                <dt>Asset</dt>
                <dd>
                  {asset ? (
                    session?.role === 'Technician' ? (
                      asset.name
                    ) : (
                      <Link className="text-link" to={`/assets/${asset.id}`}>
                        {asset.name} · {asset.assetCode}
                      </Link>
                    )
                  ) : (
                    'No asset linked'
                  )}
                </dd>
              </div>
              <div>
                <dt>Priority</dt>
                <dd>
                  <Badge>{order.priority}</Badge>
                </dd>
              </div>
            </dl>
          </Panel>
          <OrderCollaboration order={order} execute={execute} manage={manage} />
        </div>
        <div className="section-stack">
          <Panel title="Schedule" action={<CalendarDays size={17} />}>
            <div className="info-block">
              <small>PLANNED START</small>
              <strong>{dateTime(order.plannedStartAt)}</strong>
              <small>PLANNED END</small>
              <strong>{dateTime(order.plannedEndAt)}</strong>
            </div>
            {manage && ['Draft', 'Scheduled', 'Assigned'].includes(order.status) && (
              <button
                className="btn secondary wide"
                disabled={action.busy}
                onClick={() => openDialog('schedule')}
              >
                Schedule work order
              </button>
            )}
            <div className="actual-times">
              <Clock3 size={16} />
              <span>
                Started: {order.actualStartAt ? dateTime(order.actualStartAt) : 'Not started'}
                <br />
                Ended: {order.actualEndAt ? dateTime(order.actualEndAt) : 'Not ended'}
              </span>
            </div>
          </Panel>
          <Panel title="Assigned team" action={<Users size={17} />}>
            {assignments.length ? (
              assignments.map((item) => (
                <div className="person-row" key={item.id}>
                  <span className="avatar">{item.displayName.slice(0, 1)}</span>
                  <div>
                    <strong>{item.displayName}</strong>
                    <small>{item.isPrimary ? 'Primary technician' : 'Technician'}</small>
                  </div>
                </div>
              ))
            ) : (
              <p className="muted">No technicians assigned.</p>
            )}
            {manage && ['Scheduled', 'Assigned'].includes(order.status) && (
              <button
                className="btn secondary wide"
                disabled={action.busy}
                onClick={() => openDialog('assign')}
              >
                Assign technicians
              </button>
            )}
          </Panel>
          {actions.length > 0 && (
            <Panel title="Move work forward">
              <div className="action-stack">
                {actions.map((name) => (
                  <button
                    className={`btn ${name === 'cancel' ? 'danger' : 'primary'}`}
                    key={name}
                    disabled={action.busy}
                    onClick={() => openDialog(name)}
                  >
                    <CheckCircle2 size={16} />
                    {name[0]!.toUpperCase() + name.slice(1)} work order
                  </button>
                ))}
              </div>
            </Panel>
          )}
        </div>
      </div>
      {dialog === 'schedule' && <ScheduleDialog order={command} onClose={() => setDialog('')} />}
      {dialog === 'assign' && (
        <AssignDialog order={command} assignments={assignments} onClose={() => setDialog('')} />
      )}
      {dialog === 'edit' && (
        <Modal title="Edit work order" onClose={() => setDialog('')} returnFocusRef={returnFocus}>
          <Form
            fields={orderFields.map((field) => ({
              ...field,
              disabled: !!field.lookup && order.status !== 'Draft',
            }))}
            initial={{
              title: order.title,
              description: order.description || '',
              priority: order.priority,
              customerId: order.customerId,
              serviceLocationId: order.serviceLocationId,
              assetId: order.assetId || '',
            }}
            onSubmit={(values) =>
              action.mutateAsync({
                path: `/work-orders/${id}`,
                method: 'PATCH',
                body: {
                  ...values,
                  expectedVersion: command.version,
                  assetId: values.assetId || undefined,
                  clearAsset: !values.assetId,
                },
              })
            }
            error={undefined}
            pending={action.busy}
          />
          <WorkOrderError error={action.error} reload={reload} />
        </Modal>
      )}
      {actions.includes(dialog) && (
        <Modal
          title={`${dialog[0]!.toUpperCase() + dialog.slice(1)} work order?`}
          onClose={() => setDialog('')}
        >
          <p>This action updates the work order and its audit history.</p>
          <Form
            fields={[
              {
                name: 'reason',
                label: 'Reason',
                type: 'textarea',
                max: 500,
                required: dialog === 'cancel',
              },
            ]}
            onSubmit={(values) =>
              action.mutateAsync({
                path: `/work-orders/${id}/${dialog}`,
                body: { expectedVersion: command.version, reason: values.reason || undefined },
              })
            }
            error={undefined}
            pending={action.busy}
            submitLabel={`Confirm ${dialog}`}
          />
          <WorkOrderError error={action.error} reload={reload} />
        </Modal>
      )}
    </>
  );
}
