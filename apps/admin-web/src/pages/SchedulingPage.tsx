import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, CalendarDays } from 'lucide-react';
import { api, queryString } from '../api/client';
import type { Page, Technician, WorkOrder } from '../api/contracts';
import { useSession } from '../api/session';
import { dateTime } from '../api/hooks';
import { ScheduleDialog } from '../components/OrderActions';
import { Badge, Empty, ErrorBox, PageHeader, Pagination, Panel, Skeleton } from '../components/ui';

function dateKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
export function SchedulingPage() {
  const session = useSession();
  const [anchor, setAnchor] = useState(dateKey(new Date()));
  const [weekly, setWeekly] = useState(() => window.matchMedia('(min-width: 760px)').matches);
  const [technician, setTechnician] = useState('');
  const [techSearch, setTechSearch] = useState('');
  const [techPage, setTechPage] = useState(1);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<WorkOrder | null>(null);
  const start = new Date(`${anchor}T00:00:00`);
  if (weekly) start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
  const end = new Date(start);
  end.setDate(end.getDate() + (weekly ? 7 : 1));
  const days = Array.from({ length: weekly ? 7 : 1 }, (_, index) => {
    const value = new Date(start);
    value.setDate(value.getDate() + index);
    return value;
  });
  const params = queryString({
    page,
    pageSize: 100,
    plannedFrom: start.toISOString(),
    plannedTo: end.toISOString(),
    technicianUserId: technician,
    sortBy: 'plannedStartAt',
    descending: false,
  });
  const query = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'work-orders',
      'calendar',
      params,
    ],
    queryFn: ({ signal }) => api<Page<WorkOrder>>(`/work-orders?${params}`, { signal }),
  });
  const technicians = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'technicians',
      techSearch,
      techPage,
    ],
    queryFn: ({ signal }) =>
      api<Page<Technician>>(
        `/technicians?${queryString({ search: techSearch, page: techPage, pageSize: 20 })}`,
        { signal },
      ),
  });
  function shift(amount: number) {
    const value = new Date(`${anchor}T00:00:00`);
    value.setDate(value.getDate() + amount * (weekly ? 7 : 1));
    setAnchor(dateKey(value));
    setPage(1);
  }
  return (
    <>
      <PageHeader
        eyebrow="TIME, WELL PLANNED"
        title="Team schedule"
        description={`Plan with confidence. Times shown in ${Intl.DateTimeFormat().resolvedOptions().timeZone}.`}
      >
        <Link className="btn primary" to="/work-orders">
          <CalendarDays size={17} />
          Find unscheduled work
        </Link>
      </PageHeader>
      <Panel>
        <div className="toolbar calendar-toolbar">
          <div className="segmented">
            <button
              aria-pressed={!weekly}
              onClick={() => {
                setWeekly(false);
                setPage(1);
              }}
            >
              Day
            </button>
            <button
              aria-pressed={weekly}
              onClick={() => {
                setWeekly(true);
                setPage(1);
              }}
            >
              Week
            </button>
          </div>
          <button
            className="btn secondary icon"
            aria-label="Previous period"
            onClick={() => shift(-1)}
          >
            <ArrowLeft size={16} />
          </button>
          <input
            type="date"
            aria-label="Schedule date"
            value={anchor}
            onChange={(event) => {
              if (event.target.value) {
                setAnchor(event.target.value);
                setPage(1);
              }
            }}
          />
          <button className="btn secondary icon" aria-label="Next period" onClick={() => shift(1)}>
            <ArrowRight size={16} />
          </button>
          <button
            className="btn secondary"
            onClick={() => {
              setAnchor(dateKey(new Date()));
              setPage(1);
            }}
          >
            Today
          </button>
          <input
            aria-label="Search technicians"
            placeholder="Find technician…"
            value={techSearch}
            onChange={(event) => {
              setTechSearch(event.target.value);
              setTechPage(1);
            }}
          />
          <select
            aria-label="Filter schedule by technician"
            value={technician}
            onChange={(event) => {
              setTechnician(event.target.value);
              setPage(1);
            }}
          >
            <option value="">All technicians</option>
            {technician && !technicians.data?.items.some((item) => item.userId === technician) && (
              <option value={technician}>Selected technician</option>
            )}
            {technicians.data?.items.map((item) => (
              <option key={item.userId} value={item.userId}>
                {item.displayName}
              </option>
            ))}
          </select>
        </div>
        {technicians.isError && <ErrorBox error={technicians.error} />}
        {(technicians.data?.totalCount ?? 0) > 20 && (
          <Pagination
            page={techPage}
            total={technicians.data?.totalCount ?? 0}
            onChange={setTechPage}
          />
        )}
        {query.isPending ? (
          <Skeleton />
        ) : query.isError ? (
          <ErrorBox
            error={query.error}
            retry={() => {
              void query.refetch();
            }}
          />
        ) : (
          <>
            <div className={`calendar ${weekly ? 'week' : 'day'}`}>
              {days.map((day) => (
                <section
                  className={`calendar-day ${dateKey(day) === dateKey(new Date()) ? 'today' : ''}`}
                  key={dateKey(day)}
                >
                  <div className="calendar-day-heading">
                    <span>{new Intl.DateTimeFormat('en', { weekday: 'short' }).format(day)}</span>
                    <strong>{day.getDate()}</strong>
                  </div>
                  <div className="calendar-day-body">
                    {query.data.items
                      .filter(
                        (order) =>
                          order.plannedStartAt &&
                          order.plannedEndAt &&
                          new Date(order.plannedStartAt) <
                            new Date(new Date(day).setDate(day.getDate() + 1)) &&
                          new Date(order.plannedEndAt) > day,
                      )
                      .map((order) => (
                        <article
                          className={`calendar-event priority-${order.priority.toLowerCase()}`}
                          key={order.id}
                        >
                          <small>{dateTime(order.plannedStartAt)}</small>
                          <Link to={`/work-orders/${order.id}`}>{order.title}</Link>
                          <Badge>{order.status}</Badge>
                          {['Scheduled', 'Assigned'].includes(order.status) && (
                            <button onClick={() => setSelected(order)}>Reschedule</button>
                          )}
                        </article>
                      ))}
                  </div>
                </section>
              ))}
            </div>
            {!query.data.items.length && (
              <Empty
                title="No work planned for this period"
                text="Schedule a work order from its detail page to see it here."
              />
            )}
            <Pagination page={page} total={query.data.totalCount} size={100} onChange={setPage} />
          </>
        )}
      </Panel>
      {selected && <ScheduleDialog order={selected} onClose={() => setSelected(null)} />}
    </>
  );
}
