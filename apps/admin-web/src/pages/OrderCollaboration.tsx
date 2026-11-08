import { useWorkOrderAction } from '../api/work-order-state';
import { WorkOrderError } from '../components/WorkOrderError';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, queryString } from '../api/client';
import type { Audit, Checklist, Comment, History, Page, WorkOrder } from '../api/contracts';
import { dateTime } from '../api/hooks';
import { useSession } from '../api/session';
import { Form } from '../components/Form';
import { Badge, Empty, ErrorBox, Pagination, Panel, Skeleton } from '../components/ui';

export function OrderCollaboration({
  order,
  execute,
  manage,
}: {
  order: WorkOrder;
  execute: boolean;
  manage: boolean;
}) {
  const session = useSession();
  const [tab, setTab] = useState('comments');
  const [page, setPage] = useState(1);
  const action = useWorkOrderAction(order.id);
  const reload = () => {
    action.reset();
    void action.reload().catch(() => undefined);
  };
  const terminal = ['Completed', 'Cancelled'].includes(order.status);
  const root = `/work-orders/${order.id}`;
  const checklist = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'checklist',
      order.id,
    ],
    queryFn: ({ signal }) => api<Checklist>(`${root}/checklist`, { signal }),
  });
  const params = queryString({ page, pageSize: 10 });
  const comments = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'comments',
      order.id,
      page,
    ],
    queryFn: ({ signal }) => api<Page<Comment>>(`${root}/comments?${params}`, { signal }),
    enabled: tab === 'comments',
  });
  const history = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'history',
      order.id,
      page,
    ],
    queryFn: ({ signal }) => api<Page<History>>(`${root}/history?${params}`, { signal }),
    enabled: tab === 'history',
  });
  const audit = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'audit',
      order.id,
      page,
    ],
    queryFn: ({ signal }) =>
      api<Page<Audit>>(
        `/audit-logs?${queryString({ page, pageSize: 10, resourceType: 'work_order', resourceId: order.id })}`,
        { signal },
      ),
    enabled: manage && tab === 'audit',
  });
  const query = tab === 'comments' ? comments : tab === 'history' ? history : audit;
  return (
    <>
      <Panel
        title="Service checklist"
        action={
          checklist.data ? (
            <span className="count-pill">{checklist.data.progressPercent}% complete</span>
          ) : undefined
        }
      >
        {checklist.isPending ? (
          <Skeleton />
        ) : checklist.isError ? (
          <ErrorBox error={checklist.error} />
        ) : checklist.data.items.length ? (
          <>
            <div className="progress-track">
              <div style={{ width: `${checklist.data.progressPercent}%` }} />
            </div>
            <div className="checklist-items">
              {checklist.data.items.map((item) => (
                <label key={item.id} className="checklist-row">
                  <input
                    type="checkbox"
                    checked={item.isCompleted}
                    disabled={!execute || terminal || action.busy}
                    onChange={(event) =>
                      action.mutate({
                        path: `${root}/checklist/${item.id}`,
                        method: 'PATCH',
                        body: { expectedVersion: order.version, isCompleted: event.target.checked },
                      })
                    }
                  />
                  <span>
                    {item.label}
                    {item.isRequired && <small>Required to complete work</small>}
                  </span>
                  {item.isCompleted && <Badge>Completed</Badge>}
                </label>
              ))}
            </div>
          </>
        ) : (
          <>
            <p className="muted">No checklist has been applied.</p>
            {manage && !terminal && (
              <Form
                fields={[
                  {
                    name: 'templateId',
                    label: 'Checklist template',
                    lookup: 'checklist-templates',
                    required: true,
                  },
                ]}
                onSubmit={(values) =>
                  action.mutateAsync({
                    path: `${root}/checklist/apply`,
                    body: { templateId: values.templateId, expectedVersion: order.version },
                  })
                }
                submitLabel="Apply checklist"
                error={undefined}
                pending={action.busy}
              />
            )}
          </>
        )}
        <WorkOrderError error={action.error} reload={reload} />
      </Panel>
      <Panel>
        <div className="tabs" role="tablist" aria-label="Work order activity">
          {['comments', 'history', ...(manage ? ['audit'] : [])].map((name) => (
            <button
              key={name}
              id={`tab-${name}`}
              role="tab"
              aria-selected={tab === name}
              tabIndex={tab === name ? 0 : -1}
              aria-controls="activity-content"
              onKeyDown={(event) => {
                if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
                event.preventDefault();
                const tabs = ['comments', 'history', ...(manage ? ['audit'] : [])];
                const next =
                  tabs[
                    (tabs.indexOf(name) + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) %
                      tabs.length
                  ]!;
                setTab(next);
                setPage(1);
                document.getElementById(`tab-${next}`)?.focus();
              }}
              onClick={() => {
                setTab(name);
                setPage(1);
              }}
            >
              {name === 'history'
                ? 'Status history'
                : name === 'audit'
                  ? 'Audit history'
                  : 'Comments'}
            </button>
          ))}
        </div>
        <div id="activity-content" role="tabpanel" aria-labelledby={`tab-${tab}`}>
          {query.isPending ? (
            <Skeleton />
          ) : query.isError ? (
            <ErrorBox
              error={query.error}
              retry={() => {
                void query.refetch();
              }}
            />
          ) : !query.data?.items.length ? (
            <Empty title="No activity yet" text="Updates to this work order will appear here." />
          ) : (
            <div className="timeline">
              {tab === 'comments'
                ? comments.data?.items.map((item) => (
                    <article className="timeline-item" key={item.id}>
                      <span className="avatar small">{item.authorDisplayName.slice(0, 1)}</span>
                      <div>
                        <strong>{item.authorDisplayName}</strong> <Badge>{item.visibility}</Badge>
                        <small>{dateTime(item.createdAt)}</small>
                        <p className="pre-wrap">{item.body}</p>
                      </div>
                    </article>
                  ))
                : tab === 'history'
                  ? history.data?.items.map((item) => (
                      <article className="timeline-item" key={item.id}>
                        <span className="timeline-dot" />
                        <div>
                          <strong>
                            {item.previousStatus || 'Created'} → {item.status}
                          </strong>
                          <small>
                            {item.displayName} · {dateTime(item.changedAt)} · v{item.version}
                          </small>
                          {item.reason && <p>{item.reason}</p>}
                        </div>
                      </article>
                    ))
                  : audit.data?.items.map((item) => (
                      <article className="timeline-item" key={item.id}>
                        <span className="timeline-dot" />
                        <div>
                          <strong>{item.action.replaceAll('_', ' ')}</strong>
                          <small>
                            {dateTime(item.occurredAt)} · Actor {item.actorUserId}
                          </small>
                          {item.reason && <p>Reason: {item.reason}</p>}
                          {item.metadata && (
                            <>
                              <small>
                                Version {item.metadata.previousVersion} →{' '}
                                {item.metadata.entityVersion}
                              </small>
                              <dl className="audit-changes">
                                {Object.entries(item.metadata.changes)
                                  .filter(([, change]) => change !== null)
                                  .map(([key, change]) => (
                                    <div key={key}>
                                      <dt>{key.replace(/([A-Z])/g, ' $1')}</dt>
                                      <dd>
                                        <del>
                                          {Array.isArray(change?.previous)
                                            ? change.previous.join(', ') || 'None'
                                            : change?.previous || 'None'}
                                        </del>
                                        <span> → </span>
                                        <ins>
                                          {Array.isArray(change?.current)
                                            ? change.current.join(', ') || 'None'
                                            : change?.current || 'None'}
                                        </ins>
                                      </dd>
                                    </div>
                                  ))}
                              </dl>
                            </>
                          )}
                        </div>
                      </article>
                    ))}
            </div>
          )}
          {query.data && (
            <Pagination page={page} size={10} total={query.data.totalCount} onChange={setPage} />
          )}
          {tab === 'comments' && execute && !terminal && (
            <div className="comment-compose">
              <Form
                key={
                  action.submittedAt && !action.busy && action.isSuccess
                    ? action.submittedAt
                    : 'compose'
                }
                fields={[
                  {
                    name: 'body',
                    label: 'Add a comment',
                    type: 'textarea',
                    required: true,
                    max: 4000,
                  },
                  {
                    name: 'visibility',
                    label: 'Visibility',
                    type: 'select',
                    required: true,
                    options: manage ? ['TechnicianVisible', 'Internal'] : ['TechnicianVisible'],
                  },
                ]}
                initial={{ visibility: 'TechnicianVisible' }}
                onSubmit={(values) =>
                  action.mutateAsync({
                    path: `${root}/comments`,
                    body: { ...values, expectedVersion: order.version },
                  })
                }
                error={undefined}
                pending={action.busy}
                submitLabel="Post comment"
              />
            </div>
          )}
        </div>
      </Panel>
    </>
  );
}
