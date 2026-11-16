import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, CalendarDays } from 'lucide-react';
import { api, queryString } from '../api/client';
import type { Organization, Page, Technician, WorkOrder } from '../api/contracts';
import { useSession } from '../api/session';
import { calendarDay, calendarPeriod, displayScheduleTime, moveSchedule } from '../api/calendar';
import { ScheduleDialog } from '../components/OrderActions';
import { Badge, Empty, ErrorBox, PageHeader, Pagination, Panel, Skeleton } from '../components/ui';

export function SchedulingPage() {
  const session = useSession();
  const scope = [session?.tenantId, session?.userId, session?.role, session?.sessionId];
  const organizations = useQuery({
    queryKey: [...scope, 'organizations'],
    queryFn: ({ signal }) => api<Organization[]>('/organizations', { signal }),
  });
  const timeZone = organizations.data?.find((row) => row.id === session?.tenantId)?.timeZoneId;
  if (!timeZone)
    return organizations.error ? <ErrorBox error={organizations.error} /> : <Skeleton />;
  return <WorkspaceSchedule key={session?.sessionId} timeZone={timeZone} />;
}

function WorkspaceSchedule({ timeZone }: { timeZone: string }) {
  const session = useSession();
  const [anchor, setAnchor] = useState(() => calendarDay(timeZone).toString());
  const [weekly, setWeekly] = useState(() => window.matchMedia('(min-width: 760px)').matches);
  const [technician, setTechnician] = useState('');
  const [techSearch, setTechSearch] = useState('');
  const [techPage, setTechPage] = useState(1);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<WorkOrder | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);
  const [move, setMove] = useState<{ start: string; end: string } | null>(null);
  const { days, from, to } = calendarPeriod(anchor, timeZone, weekly);
  const params = queryString({
    page,
    pageSize: 100,
    plannedFrom: from,
    plannedTo: to,
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
    const date = calendarPeriod(anchor, timeZone, false).days[0]!.date;
    setAnchor(date.add({ days: amount * (weekly ? 7 : 1) }).toString());
    setPage(1);
  }
  return (
    <>
      <PageHeader
        eyebrow="TIME, WELL PLANNED"
        title="Team schedule"
        description={`Times shown in ${timeZone}.`}
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
              setAnchor(calendarDay(timeZone).toString());
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
              {days.map(({ date, from: dayFrom, to: dayTo }) => (
                <section
                  className={`calendar-day ${date.equals(calendarDay(timeZone)) ? 'today' : ''}`}
                  key={date.toString()}
                  onDragOver={(event) => {
                    if (dragging) event.preventDefault();
                  }}
                  onDrop={(event) => {
                    event.preventDefault();
                    const item = query.data.items.find((entry) => entry.id === dragging);
                    setDragging(null);
                    if (!item?.plannedStartAt || !item.plannedEndAt) return;
                    setMove(moveSchedule(item.plannedStartAt, item.plannedEndAt, date, timeZone));
                    setSelected(item);
                  }}
                >
                  <div className="calendar-day-heading">
                    <span>{date.toLocaleString('en', { weekday: 'short' })}</span>
                    <strong>{date.day}</strong>
                  </div>
                  <div className="calendar-day-body">
                    {query.data.items
                      .filter(
                        (order) =>
                          order.plannedStartAt &&
                          order.plannedEndAt &&
                          Date.parse(order.plannedStartAt) < Date.parse(dayTo) &&
                          Date.parse(order.plannedEndAt) > Date.parse(dayFrom),
                      )
                      .map((order) => (
                        <article
                          className={`calendar-event priority-${order.priority.toLowerCase()}`}
                          key={order.id}
                          draggable={['Scheduled', 'Assigned'].includes(order.status)}
                          onDragStart={(event) => {
                            event.dataTransfer.setData('text/plain', order.id);
                            setDragging(order.id);
                          }}
                          onDragEnd={() => setDragging(null)}
                        >
                          <small>{displayScheduleTime(order.plannedStartAt, timeZone)}</small>
                          <Link to={`/work-orders/${order.id}`}>{order.title}</Link>
                          <Badge>{order.status}</Badge>
                          {['Scheduled', 'Assigned'].includes(order.status) && (
                            <button
                              onClick={() => {
                                setMove(null);
                                setSelected(order);
                              }}
                            >
                              Reschedule
                            </button>
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
      {selected && (
        <ScheduleDialog order={selected} proposal={move} onClose={() => setSelected(null)} />
      )}
    </>
  );
}
