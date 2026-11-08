import { useWorkOrderAction } from '../api/work-order-state';
import { WorkOrderError } from './WorkOrderError';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, queryString } from '../api/client';
import type { Assignment, Page, Technician, WorkOrder } from '../api/contracts';
import { localDateTime } from '../api/hooks';
import { useSession } from '../api/session';
import { Form } from './Form';
import { Empty, ErrorBox, Modal, Pagination } from './ui';

export function ScheduleDialog({ order, onClose }: { order: WorkOrder; onClose: () => void }) {
  const [snapshot] = useState(order);
  const action = useWorkOrderAction(order.id, onClose);
  const reload = () => {
    onClose();
    void action.reload().catch(() => undefined);
  };
  return (
    <Modal title="Schedule work order" onClose={onClose}>
      <p className="muted">
        Times use your browser’s local time zone ({Intl.DateTimeFormat().resolvedOptions().timeZone}
        ). Availability is checked by the server.
      </p>
      <Form
        fields={[
          {
            name: 'plannedStartAt',
            label: 'Planned start',
            type: 'datetime-local',
            required: true,
          },
          { name: 'plannedEndAt', label: 'Planned end', type: 'datetime-local', required: true },
          {
            name: 'overrideReason',
            label: 'Availability override reason',
            type: 'textarea',
            max: 500,
            help: 'Only use an override after reviewing a conflict. The reason is recorded in audit history.',
          },
        ]}
        initial={{
          plannedStartAt: localDateTime(order.plannedStartAt),
          plannedEndAt: localDateTime(order.plannedEndAt),
        }}
        onSubmit={(values) =>
          action.mutateAsync({
            path: `/work-orders/${order.id}/schedule`,
            body: {
              expectedVersion: snapshot.version,
              plannedStartAt: new Date(values.plannedStartAt!).toISOString(),
              plannedEndAt: new Date(values.plannedEndAt!).toISOString(),
              overrideReason: values.overrideReason || undefined,
            },
          })
        }
        error={undefined}
        pending={action.busy}
        submitLabel="Save schedule"
      />
      <WorkOrderError error={action.error} reload={reload} />
    </Modal>
  );
}
export function AssignDialog({
  order,
  assignments = [],
  onClose,
}: {
  order: WorkOrder;
  assignments?: Assignment[];
  onClose: () => void;
}) {
  const session = useSession();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Record<string, string>>(() =>
    Object.fromEntries(assignments.map((item) => [item.technicianUserId, item.displayName])),
  );
  const [primary, setPrimary] = useState(
    assignments.find((item) => item.isPrimary)?.technicianUserId || '',
  );
  const [reason, setReason] = useState('');
  const [validation, setValidation] = useState('');
  const [snapshot] = useState(order);
  const action = useWorkOrderAction(order.id, onClose);
  const reload = () => {
    onClose();
    void action.reload().catch(() => undefined);
  };
  const params = queryString({ page, pageSize: 10, search });
  const technicians = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'technicians',
      params,
    ],
    queryFn: ({ signal }) => api<Page<Technician>>(`/technicians?${params}`, { signal }),
  });
  return (
    <Modal title="Assign technicians" onClose={onClose}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!Object.keys(selected).length || !selected[primary]) {
            setValidation('Select at least one technician and a primary technician.');
            return;
          }
          if (Object.keys(selected).length > 20) {
            setValidation('A maximum of 20 technicians can be assigned.');
            return;
          }
          void action
            .mutateAsync({
              path: `/work-orders/${order.id}/assign`,
              body: {
                expectedVersion: snapshot.version,
                technicianUserIds: Object.keys(selected),
                primaryTechnicianUserId: primary,
                overrideReason: reason || undefined,
              },
            })
            .catch(() => undefined);
        }}
      >
        <label htmlFor="technician-search">Find technicians</label>
        <input
          id="technician-search"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value);
            setPage(1);
          }}
          placeholder="Search by name…"
        />
        {technicians.isError && <ErrorBox error={technicians.error} />}
        {technicians.isPending && <p role="status">Loading technicians…</p>}
        <fieldset className="technician-options">
          <legend>Technicians</legend>
          {technicians.data?.items.map((technician) => (
            <label key={technician.userId} className="checkbox-label">
              <input
                type="checkbox"
                checked={!!selected[technician.userId]}
                onChange={(event) => {
                  const next = { ...selected };
                  if (event.target.checked) next[technician.userId] = technician.displayName;
                  else delete next[technician.userId];
                  setSelected(next);
                  if (!Object.keys(next).includes(primary)) setPrimary(Object.keys(next)[0] || '');
                }}
              />
              {technician.displayName}
            </label>
          ))}
          {technicians.data?.totalCount === 0 && (
            <Empty
              title="No technicians found"
              text="An owner can invite a technician from Team."
            />
          )}
        </fieldset>
        {technicians.data && (
          <Pagination
            page={page}
            total={technicians.data.totalCount}
            size={10}
            onChange={setPage}
          />
        )}
        <div className="field">
          <label htmlFor="primary-technician">Primary technician</label>
          <select
            id="primary-technician"
            value={primary}
            onChange={(event) => setPrimary(event.target.value)}
          >
            <option value="">Select primary technician</option>
            {Object.entries(selected).map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
          </select>
          <small>{Object.keys(selected).length} selected</small>
        </div>
        <div className="field">
          <label htmlFor="assignment-override">Availability override reason</label>
          <textarea
            id="assignment-override"
            value={reason}
            maxLength={500}
            onChange={(event) => setReason(event.target.value)}
          />
          <small>Provide a reason only after reviewing the server’s conflict warning.</small>
        </div>
        {validation && (
          <p role="alert" className="field-error">
            {validation}
          </p>
        )}
        <WorkOrderError error={action.error} reload={reload} />
        <div className="form-actions">
          <button className="btn primary" disabled={action.busy}>
            Save assignments
          </button>
        </div>
      </form>
    </Modal>
  );
}
