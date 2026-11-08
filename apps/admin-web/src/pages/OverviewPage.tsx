import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  ArrowDownRight,
  ArrowUpRight,
  CalendarDays,
  CheckCircle2,
  ClipboardList,
  Clock3,
  Users,
} from 'lucide-react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { api, queryString } from '../api/client';
import type { Report } from '../api/contracts';
import { dateTime } from '../api/hooks';
import { useSession } from '../api/session';
import { Badge, DataTable, Empty, ErrorBox, PageHeader, Panel, Skeleton } from '../components/ui';
const colors = ['#5865df', '#7b8bf2', '#9cb1ff', '#37ac8b', '#efb34b', '#dc7981', '#a9b3c5'];
export function OverviewPage({ reports = false }: { reports?: boolean }) {
  const session = useSession();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const invalid = !!from && !!to && to <= from;
  const params = queryString({
    from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
    to: to ? new Date(`${to}T00:00:00`).toISOString() : undefined,
  });
  const query = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'report',
      params,
    ],
    queryFn: ({ signal }) => api<Report>(`/reports/work-orders?${params}`, { signal }),
    enabled: !invalid,
  });
  const data = query.data;
  const tiles = [
    {
      label: 'Total work orders',
      value: data?.totalOrders,
      icon: ClipboardList,
      note: 'All orders in this period',
      color: 'indigo',
    },
    {
      label: 'Active work orders',
      value: data?.activeOrders,
      icon: Clock3,
      note: 'From draft to in progress',
      color: 'blue',
    },
    {
      label: 'Completed',
      value: data?.completedOrders,
      icon: CheckCircle2,
      note: `${data?.completionRate ?? 0}% completion rate`,
      color: 'green',
    },
    {
      label: 'Overdue',
      value: data?.overdueOrders,
      icon: ArrowDownRight,
      note: 'Active, past planned end',
      color: 'amber',
    },
  ];
  return (
    <>
      <PageHeader
        eyebrow={reports ? 'OPERATIONAL INTELLIGENCE' : 'YOUR OPERATIONS, AT A GLANCE'}
        title={reports ? 'Reports & insights' : 'A clear view of the work ahead.'}
        description={
          reports
            ? 'Understand performance across your organization.'
            : new Intl.DateTimeFormat('en', {
                weekday: 'long',
                month: 'long',
                day: 'numeric',
                year: 'numeric',
              }).format(new Date())
        }
      >
        <Link className="btn secondary" to="/scheduling">
          <CalendarDays size={17} />
          View schedule
        </Link>
        <Link className="btn primary" to="/work-orders">
          Manage work orders
          <ArrowUpRight size={16} />
        </Link>
      </PageHeader>
      {reports && (
        <Panel>
          <div className="toolbar">
            <div className="field">
              <label htmlFor="report-from">Created from</label>
              <input
                id="report-from"
                type="date"
                value={from}
                onChange={(event) => setFrom(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="report-to">Created before</label>
              <input
                id="report-to"
                type="date"
                value={to}
                onChange={(event) => setTo(event.target.value)}
              />
            </div>
            <button
              className="btn secondary"
              onClick={() => {
                setFrom('');
                setTo('');
              }}
            >
              All time
            </button>
            <small>Start inclusive, end exclusive · Local midnight converted to UTC</small>
          </div>
        </Panel>
      )}
      {invalid ? (
        <ErrorBox error={new Error('Choose an end date after the start date.')} />
      ) : query.isPending ? (
        <Skeleton />
      ) : query.isError ? (
        <ErrorBox
          error={query.error}
          retry={() => {
            void query.refetch();
          }}
        />
      ) : (
        data && (
          <>
            <div className="stat-grid">
              {tiles.map((tile) => (
                <div className="stat-card" key={tile.label}>
                  <div className="stat-top">
                    <span>{tile.label}</span>
                    <span className={`stat-icon ${tile.color}`}>
                      <tile.icon size={18} />
                    </span>
                  </div>
                  <strong>{tile.value?.toLocaleString()}</strong>
                  <small>{tile.note}</small>
                </div>
              ))}
            </div>
            <div className="overview-grid">
              <Panel
                title={reports ? 'Priority distribution' : 'Work order distribution'}
                action={<span className="muted">{data.totalOrders} orders</span>}
              >
                <div className="chart-layout">
                  <div
                    className="donut-chart"
                    role="img"
                    aria-label={`${reports ? 'Priority' : 'Status'} distribution: ${(reports ? data.priorityDistribution : data.statusDistribution).map((item) => `${item.name} ${item.count}`).join(', ') || 'No data'}`}
                  >
                    {data.totalOrders ? (
                      <ResponsiveContainer width="100%" height={235}>
                        <PieChart>
                          <Pie
                            isAnimationActive={false}
                            data={reports ? data.priorityDistribution : data.statusDistribution}
                            dataKey="count"
                            nameKey="name"
                            innerRadius={68}
                            outerRadius={96}
                            paddingAngle={4}
                            stroke="none"
                          >
                            {(reports ? data.priorityDistribution : data.statusDistribution).map(
                              (item, i) => (
                                <Cell key={item.name} fill={colors[i % colors.length]!} />
                              ),
                            )}
                          </Pie>
                          <Tooltip />
                        </PieChart>
                      </ResponsiveContainer>
                    ) : (
                      <div className="empty-donut" />
                    )}
                    <div className="donut-label">
                      <strong>{data.totalOrders}</strong>
                      <span>Total orders</span>
                    </div>
                  </div>
                  <ul className="chart-legend">
                    {(reports ? data.priorityDistribution : data.statusDistribution).map(
                      (item, i) => (
                        <li key={item.name}>
                          <i style={{ background: colors[i % colors.length] }} />
                          <span>{item.name.replace(/([a-z])([A-Z])/g, '$1 $2')}</span>
                          <strong>{item.count}</strong>
                        </li>
                      ),
                    )}
                    {!data.totalOrders && (
                      <li className="muted">Your first work order starts the story.</li>
                    )}
                  </ul>
                </div>
              </Panel>
              <Panel title="Team capacity" action={<Users size={18} />}>
                <div className="capacity-number">
                  <strong>{data.activeTechnicians}</strong>
                  <span>active technicians</span>
                  <span className="live-tag">LIVE</span>
                </div>
                <p className="muted">Verified, active technician memberships in this workspace.</p>
                <div className="capacity-divider" />
                <div className="capacity-metric">
                  <span>Completion rate</span>
                  <strong>{data.completionRate}%</strong>
                </div>
                <div className="progress-track">
                  <div style={{ width: `${data.completionRate}%` }} />
                </div>
                <Link to="/team" className="text-link capacity-link">
                  Explore your team <ArrowUpRight size={15} />
                </Link>
              </Panel>
            </div>
            {reports && (
              <Panel
                title="Technician workload"
                action={<small>Top 50 · Active orders in this period</small>}
              >
                {data.technicianWorkload.length ? (
                  <>
                    <div
                      style={{ height: 260 }}
                      role="img"
                      aria-label={data.technicianWorkload
                        .map((item) => `${item.displayName}: ${item.activeOrders}`)
                        .join(', ')}
                    >
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart data={data.technicianWorkload}>
                          <CartesianGrid vertical={false} strokeDasharray="3 3" />
                          <XAxis dataKey="displayName" fontSize={11} />
                          <YAxis allowDecimals={false} />
                          <Tooltip />
                          <Bar
                            isAnimationActive={false}
                            dataKey="activeOrders"
                            name="Active orders"
                            fill="#6673e4"
                            radius={[5, 5, 0, 0]}
                          />
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                    <DataTable headings={['Technician', 'Active orders']}>
                      {data.technicianWorkload.map((item) => (
                        <tr key={item.userId}>
                          <td>{item.displayName}</td>
                          <td>{item.activeOrders}</td>
                        </tr>
                      ))}
                    </DataTable>
                  </>
                ) : (
                  <Empty
                    title="No active technicians"
                    text="Invite your team to start distributing work."
                  />
                )}
              </Panel>
            )}
            <div className="overview-grid">
              <Panel
                title="Upcoming work orders"
                action={
                  <Link className="text-link" to="/scheduling">
                    View schedule ↗
                  </Link>
                }
              >
                {data.upcomingOrders.length ? (
                  <DataTable headings={['Work order', 'Status', 'Planned start']}>
                    {data.upcomingOrders.map((order) => (
                      <tr key={order.id}>
                        <td>
                          <Link className="text-link" to={`/work-orders/${order.id}`}>
                            {order.title}
                          </Link>
                        </td>
                        <td>
                          <Badge>{order.status}</Badge>
                        </td>
                        <td>{dateTime(order.plannedStartAt)}</td>
                      </tr>
                    ))}
                  </DataTable>
                ) : (
                  <Empty
                    title="A little breathing room"
                    text="Upcoming scheduled work will appear here."
                  />
                )}
              </Panel>
              <Panel title="Recent activity">
                <div className="activity-list">
                  {data.recentActivities.length ? (
                    data.recentActivities.map((item) => (
                      <div className="activity-row" key={item.id}>
                        <span className="activity-icon">
                          <ClipboardList size={16} />
                        </span>
                        <div>
                          <strong>{item.action.replaceAll('_', ' ')}</strong>
                          <span>
                            {item.resourceType}
                            {item.resourceType === 'work_order' && (
                              <>
                                {' '}
                                ·{' '}
                                <Link to={`/work-orders/${item.resourceId}`} className="text-link">
                                  Open order
                                </Link>
                              </>
                            )}
                          </span>
                          <small>{dateTime(item.occurredAt)}</small>
                        </div>
                      </div>
                    ))
                  ) : (
                    <Empty
                      title="Your workspace is ready"
                      text="Team activity will appear as work gets underway."
                    />
                  )}
                </div>
              </Panel>
            </div>
          </>
        )
      )}
    </>
  );
}
