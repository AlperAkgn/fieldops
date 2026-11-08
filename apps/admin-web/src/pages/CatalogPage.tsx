import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, ArrowUpRight, Plus, Search } from 'lucide-react';
import { api, queryString } from '../api/client';
import type { Asset, Category, Customer, Page, ServiceLocation, WorkOrder } from '../api/contracts';
import { canManage, useSession } from '../api/session';
import { dateTime, initialValues, useAction } from '../api/hooks';
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
  Panel,
  Skeleton,
} from '../components/ui';
import { catalogBody, catalogFields, catalogNames, type CatalogKind } from './catalog-fields';
type CatalogRecord = Customer | ServiceLocation | Asset | Category;

export function CatalogPage({
  kind,
  fixed = {},
  embedded = false,
}: {
  kind: CatalogKind;
  fixed?: Record<string, string>;
  embedded?: boolean;
}) {
  const session = useSession();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState('-createdAt');
  const [archived, setArchived] = useState(false);
  const [creating, setCreating] = useState(false);
  const action = useAction((result) => {
    setCreating(false);
    if (!embedded) navigate(`/${kind}/${(result as CatalogRecord).id}`);
  });
  const params = queryString({
    page,
    pageSize: 20,
    search,
    sort,
    includeArchived: archived,
    ...fixed,
  });
  const query = useQuery({
    queryKey: [session?.tenantId, session?.userId, session?.role, session?.sessionId, kind, params],
    queryFn: ({ signal }) => api<Page<CatalogRecord>>(`/${kind}?${params}`, { signal }),
    enabled: session?.role !== 'Technician',
  });
  const names = catalogNames[kind];
  if (session?.role === 'Technician')
    return (
      <Empty
        title="Access restricted"
        text="Catalog access requires an administrative or viewer role."
      />
    );
  return (
    <>
      {!embedded && (
        <PageHeader
          eyebrow="CUSTOMER OPERATIONS"
          title={names.title}
          description={names.description}
        >
          {canManage(session?.role) && (
            <button className="btn primary" onClick={() => setCreating(true)}>
              <Plus size={17} />
              Add {names.singular}
            </button>
          )}
        </PageHeader>
      )}
      <Panel
        title={embedded ? names.title : undefined}
        action={
          embedded && canManage(session?.role) ? (
            <button className="btn secondary" onClick={() => setCreating(true)}>
              <Plus size={15} />
              Add {names.singular}
            </button>
          ) : undefined
        }
      >
        <div className="toolbar">
          <div className="search-input">
            <Search size={17} />
            <input
              aria-label={`Search ${names.title.toLowerCase()}`}
              placeholder={`Search ${names.title.toLowerCase()}…`}
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
            />
          </div>
          <select
            aria-label="Sort records"
            value={sort}
            onChange={(event) => {
              setSort(event.target.value);
              setPage(1);
            }}
          >
            <option value="-createdAt">Newest first</option>
            <option value="createdAt">Oldest first</option>
            <option value="name">Name A–Z</option>
            <option value="-name">Name Z–A</option>
          </select>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={archived}
              onChange={(event) => {
                setArchived(event.target.checked);
                setPage(1);
              }}
            />
            Include archived
          </label>
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
            title={`No ${names.title.toLowerCase()} found`}
            text="Adjust your search or add a record to get started."
          />
        ) : (
          <DataTable
            headings={[
              'Name',
              kind === 'customers'
                ? 'Contact'
                : kind === 'service-locations'
                  ? 'Address'
                  : kind === 'assets'
                    ? 'Asset code'
                    : 'Description',
              'Status',
              'Created',
              '',
            ]}
          >
            {query.data.items.map((item) => (
              <tr key={item.id}>
                <td>
                  <Link className="record-link" to={`/${kind}/${item.id}`}>
                    <span className="record-avatar">{item.name.slice(0, 1)}</span>
                    <span>
                      {item.name}
                      <small>
                        {'customerType' in item
                          ? item.customerType
                          : 'brand' in item
                            ? [item.brand, item.model].filter(Boolean).join(' · ')
                            : ''}
                      </small>
                    </span>
                  </Link>
                </td>
                <td>
                  {'email' in item
                    ? item.email || item.phone || '—'
                    : 'addressLine' in item
                      ? `${item.addressLine}, ${item.city}`
                      : 'assetCode' in item
                        ? item.assetCode
                        : item.description || '—'}
                </td>
                <td>
                  <Badge>
                    {!item.isActive ? 'Archived' : 'status' in item ? item.status : 'Active'}
                  </Badge>
                </td>
                <td className="muted">{dateTime(item.createdAt)}</td>
                <td>
                  <Link
                    aria-label={`View ${item.name}`}
                    className="btn secondary icon"
                    to={`/${kind}/${item.id}`}
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
        <Modal title={`Add ${names.singular}`} onClose={() => setCreating(false)}>
          <Form
            fields={catalogFields(kind)}
            initial={{ customerType: 'Business', status: 'Active', ...fixed }}
            onSubmit={(values) =>
              action.mutateAsync({ path: `/${kind}`, body: catalogBody(values, kind) })
            }
            error={action.error}
            pending={action.isPending}
            submitLabel={`Create ${names.singular}`}
          />
        </Modal>
      )}
    </>
  );
}

export function RelatedOrders({
  filters,
  title = 'Service history',
}: {
  filters: Record<string, string>;
  title?: string;
}) {
  const session = useSession();
  const [page, setPage] = useState(1);
  const params = queryString({ page, pageSize: 10, ...filters });
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
    <Panel title={title}>
      {query.isPending ? (
        <Skeleton />
      ) : query.isError ? (
        <ErrorBox error={query.error} />
      ) : !query.data.items.length ? (
        <Empty title="No work orders yet" text="Work linked to this record will appear here." />
      ) : (
        <>
          <DataTable headings={['Work order', 'Status', 'Priority', 'Planned start']}>
            {query.data.items.map((order) => (
              <tr key={order.id}>
                <td>
                  <Link className="text-link" to={`/work-orders/${order.id}`}>
                    {order.title}
                  </Link>
                </td>
                <td>
                  <Badge>{order.status}</Badge>
                </td>
                <td>
                  <Badge>{order.priority}</Badge>
                </td>
                <td>{dateTime(order.plannedStartAt)}</td>
              </tr>
            ))}
          </DataTable>
          <Pagination page={page} total={query.data.totalCount} size={10} onChange={setPage} />
        </>
      )}
    </Panel>
  );
}

export function CatalogDetail({ kind }: { kind: CatalogKind }) {
  const { id } = useParams();
  const session = useSession();
  const [editing, setEditing] = useState(false);
  const [archiving, setArchiving] = useState(false);
  const action = useAction(() => {
    setEditing(false);
    setArchiving(false);
  });
  const query = useQuery({
    queryKey: [session?.tenantId, session?.userId, session?.role, session?.sessionId, kind, id],
    queryFn: ({ signal }) => api<CatalogRecord>(`/${kind}/${id}`, { signal }),
    enabled: session?.role !== 'Technician',
  });
  if (session?.role === 'Technician') return <Empty title="Access restricted" />;
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
  const record = query.data;
  const names = catalogNames[kind];
  return (
    <>
      <Link className="back-link" to={`/${kind}`}>
        <ArrowLeft size={15} />
        Back to {names.title.toLowerCase()}
      </Link>
      <PageHeader
        eyebrow={names.singular.toUpperCase()}
        title={record.name}
        description={`Created ${dateTime(record.createdAt)} · Version ${record.version}`}
      >
        <Badge>{record.isActive ? 'Active' : 'Archived'}</Badge>
        {canManage(session?.role) && record.isActive && (
          <>
            <button className="btn secondary" onClick={() => setArchiving(true)}>
              Archive
            </button>
            <button className="btn primary" onClick={() => setEditing(true)}>
              Edit {names.singular}
            </button>
          </>
        )}
      </PageHeader>
      <Panel title="Record details">
        <dl className="details-grid">
          {catalogFields(kind).map((field) => {
            const value = (record as unknown as Record<string, unknown>)[field.name];
            return (
              <div key={field.name}>
                <dt>{field.label}</dt>
                <dd>
                  {value === null || value === undefined || value === '' ? (
                    '—'
                  ) : field.lookup ? (
                    <Link className="text-link" to={`/${field.lookup}/${String(value)}`}>
                      View {field.label.toLowerCase()} ↗
                    </Link>
                  ) : (
                    String(value)
                  )}
                </dd>
              </div>
            );
          })}
        </dl>
      </Panel>
      {kind === 'customers' && (
        <div className="section-stack">
          <CatalogPage
            key={`locations-${id}`}
            kind="service-locations"
            fixed={{ customerId: record.id }}
            embedded
          />
          <CatalogPage
            key={`assets-${id}`}
            kind="assets"
            fixed={{ customerId: record.id }}
            embedded
          />
          <RelatedOrders filters={{ customerId: record.id, status: 'Completed' }} />
        </div>
      )}
      {kind === 'service-locations' && (
        <div className="section-stack">
          <CatalogPage
            key={`assets-${id}`}
            kind="assets"
            fixed={{
              customerId: (record as ServiceLocation).customerId,
              serviceLocationId: record.id,
            }}
            embedded
          />
          <RelatedOrders filters={{ serviceLocationId: record.id, status: 'Completed' }} />
        </div>
      )}
      {kind === 'assets' && <RelatedOrders filters={{ assetId: record.id, status: 'Completed' }} />}
      {editing && (
        <Modal title={`Edit ${names.singular}`} onClose={() => setEditing(false)}>
          <Form
            fields={catalogFields(kind, true)}
            initial={initialValues(record)}
            onSubmit={(values) =>
              action.mutateAsync({
                path: `/${kind}/${id}`,
                method: 'PATCH',
                body: catalogBody(values, kind, record.version),
              })
            }
            pending={action.isPending}
            error={action.error}
          />
        </Modal>
      )}
      {archiving && (
        <Confirm
          error={action.error}
          title={`Archive ${record.name}?`}
          text="This record will be unavailable for new work. Existing service history is retained."
          pending={action.isPending}
          onClose={() => setArchiving(false)}
          onConfirm={() => {
            void action
              .mutateAsync({
                path: `/${kind}/${id}?expectedVersion=${record.version}`,
                method: 'DELETE',
              })
              .catch(() => undefined);
          }}
        />
      )}
      {action.isError && !editing && <ErrorBox error={action.error} />}
    </>
  );
}
