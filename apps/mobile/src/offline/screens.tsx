import { useState } from 'react';
import { Text } from 'react-native';
import { router } from 'expo-router';
import { Button, Card, Confirm, ErrorBox, Field, Pill, Screen, styles } from '../components/ui';
import { message, statusLabel } from '../features/orders';
import { useOffline } from './runtime';
import type { Intent, QueueRow } from './store';

export function OfflineOrders() {
  const { snapshot, busy, error, workspace, offline } = useOffline();
  return (
    <Screen>
      <Text style={styles.title}>Downloaded work orders</Text>
      <Pill text={offline ? 'Offline' : 'Synchronizing'} />
      {error && <ErrorBox text={error} />}
      {busy && <Text style={styles.subtitle}>Checking available work orders…</Text>}
      {!snapshot?.orders.length && (
        <Text style={styles.text}>No downloaded work orders are available.</Text>
      )}
      {snapshot?.orders.map(({ order }) => (
        <Card key={order.id}>
          <Text style={styles.text}>
            {order.workOrderNumber} · {order.title}
          </Text>
          <Text style={styles.subtitle}>{statusLabel(order.status)}</Text>
          {order.plannedStartAt && (
            <Text style={styles.subtitle}>{new Date(order.plannedStartAt).toLocaleString()}</Text>
          )}
          <Button
            title="Open work order"
            onPress={() => router.push({ pathname: '/work-orders/[id]', params: { id: order.id } })}
          />
        </Card>
      ))}
      <Button title="Sync center" secondary onPress={() => router.navigate('/(tabs)/sync')} />
      {!offline && (
        <Button
          title="Synchronize"
          secondary
          disabled={busy}
          onPress={() => void workspace?.sync()}
        />
      )}
    </Screen>
  );
}

export function OfflineOrder({
  id,
  noteDraft,
}: {
  id: string;
  noteDraft: [string, (value: string) => void];
}) {
  const { workspace, snapshot, busy, error: syncError, offline } = useOffline();
  const [note, setNote] = noteDraft;
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [confirmation, setConfirmation] = useState<{ intent: Intent; version: number } | null>(
    null,
  );
  const entity = snapshot?.orders.find(({ order }) => order.id === id);
  const pending = snapshot?.operations.filter((row) => row.orderId === id) ?? [];
  const blocked =
    busy || saving || pending.some((row) => row.state === 'conflict' || row.state === 'rejected');

  async function save(intent: Intent, version: number) {
    if (blocked || !workspace) return;
    setSaving(true);
    setError('');
    try {
      await workspace.enqueue(id, version, intent);
      if (intent.kind === 'note') setNote('');
      setConfirmation(null);
    } catch (failure) {
      setError(message(failure));
    } finally {
      setSaving(false);
    }
  }

  const action =
    entity?.order.status === 'Assigned'
      ? 'start'
      : entity?.order.status === 'OnHold'
        ? 'resume'
        : null;
  const editable = entity && !['Completed', 'Cancelled'].includes(entity.order.status);
  const version = entity?.order.version ?? 0;
  return (
    <Screen>
      <Button
        title="Back to work orders"
        secondary
        onPress={() => router.replace('/(tabs)/orders')}
      />
      {entity && <Text style={styles.title}>{entity.order.title}</Text>}
      <Pill text={offline ? 'Offline' : 'Saved workspace'} />
      {error && <ErrorBox text={error} />}
      {syncError && <ErrorBox text={syncError} />}
      {!entity && (
        <Text style={styles.text}>This work order is unavailable in the downloaded workspace.</Text>
      )}
      {!!pending.length && (
        <Card>
          <Text style={styles.text}>
            {pending.length} saved {pending.length === 1 ? 'edit' : 'edits'}
          </Text>
          <Button
            title="Review in Sync"
            secondary
            onPress={() => router.navigate('/(tabs)/sync')}
          />
        </Card>
      )}
      {entity && (
        <>
          <Card>
            <Text style={styles.text}>
              {entity.order.workOrderNumber} · {statusLabel(entity.order.status)}
            </Text>
            <Text style={styles.subtitle}>
              Version {version}
              {pending.length ? ' · includes local changes' : ''}
            </Text>
            {entity.order.description && (
              <Text style={styles.text}>{entity.order.description}</Text>
            )}
            <Text style={styles.subtitle}>{statusLabel(entity.order.priority)} priority</Text>
            {action && (
              <Button
                title={action === 'start' ? 'Start work' : 'Resume work'}
                disabled={blocked}
                onPress={() => setConfirmation({ intent: { kind: action }, version })}
              />
            )}
            {entity.order.status === 'InProgress' && (
              <>
                <Button
                  title="Pause work"
                  secondary
                  disabled={blocked}
                  onPress={() => setConfirmation({ intent: { kind: 'pause' }, version })}
                />
                <Button
                  title="Complete work"
                  disabled={blocked || !entity.checklist.requiredItemsComplete}
                  onPress={() => setConfirmation({ intent: { kind: 'complete' }, version })}
                />
              </>
            )}
          </Card>
          <Card>
            <Text style={styles.label}>Checklist · {entity.checklist.progressPercent}%</Text>
            {entity.checklist.items.map((item) => (
              <Button
                key={item.id}
                secondary
                disabled={blocked || !editable}
                title={`${item.isCompleted ? 'Checked' : 'Unchecked'} · ${item.label}${item.isRequired ? ' (required)' : ''}`}
                onPress={() =>
                  void save(
                    { kind: 'checklist', itemId: item.id, isCompleted: !item.isCompleted },
                    version,
                  )
                }
              />
            ))}
          </Card>
          <Card>
            <Text style={styles.label}>Service notes</Text>
            {entity.notes.map((item) => (
              <Text key={item.id} style={styles.text}>
                {item.authorDisplayName} · {item.body}
              </Text>
            ))}
            {editable && (
              <>
                <Field
                  label="Service note"
                  value={note}
                  onChangeText={setNote}
                  multiline
                  maxLength={4000}
                  editable={!blocked}
                />
                <Button
                  title="Save note"
                  disabled={blocked || !note.trim()}
                  onPress={() => void save({ kind: 'note', body: note.trim() }, version)}
                />
              </>
            )}
          </Card>
        </>
      )}
      <Confirm
        visible={!!entity && !!confirmation}
        busy={saving}
        title="Save work order action?"
        body="The server applies the action and records its time when the change synchronizes."
        onCancel={() => setConfirmation(null)}
        onConfirm={() => {
          if (confirmation) void save(confirmation.intent, confirmation.version);
        }}
      />
    </Screen>
  );
}

export function SyncCenter() {
  const { snapshot, workspace, offline, busy, error } = useOffline();
  const [failure, setFailure] = useState('');
  const [review, setReview] = useState<{
    row: QueueRow;
    version: number;
    serverState: string;
  } | null>(null);
  const [discard, setDiscard] = useState<QueueRow | null>(null);
  const [saving, setSaving] = useState(false);
  async function perform(action: () => Promise<void>) {
    setSaving(true);
    setFailure('');
    try {
      await action();
      setReview(null);
      setDiscard(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setSaving(false);
    }
  }
  return (
    <Screen>
      <Text style={styles.title}>Sync</Text>
      <Pill text={busy ? 'Synchronizing' : offline ? 'Offline' : 'Online'} />
      <Text style={styles.text}>{snapshot?.operations.length ?? 0} saved edits</Text>
      <Text style={styles.subtitle}>
        {snapshot?.lastSync
          ? `Last download: ${new Date(snapshot.lastSync).toLocaleString()}`
          : 'No authorized download yet.'}
      </Text>
      {!!snapshot?.removed && (
        <Text style={styles.subtitle}>
          {snapshot.removed} saved edits were removed after order access changed.
        </Text>
      )}
      {error && <ErrorBox text={error} />}
      {failure && <ErrorBox text={failure} />}
      <Button
        title="Synchronize now"
        disabled={!workspace || busy}
        onPress={() => void workspace?.sync()}
      />
      {snapshot?.operations.map((row) => {
        const target = snapshot.orders.find(({ order }) => order.id === row.orderId);
        const actionable = row.state === 'conflict' || row.state === 'rejected';
        const canApply =
          row.state === 'conflict' &&
          ['work_order_version_conflict', 'sync_predecessor_review'].includes(row.code ?? '');
        const hasPredecessor = snapshot.operations.some(
          (item) =>
            item.orderId === row.orderId &&
            item.sequence < row.sequence &&
            item.state !== 'acknowledged',
        );
        const checklistItem =
          row.command.kind === 'checklist'
            ? target?.checklist.items.find(
                (item) => row.command.kind === 'checklist' && item.id === row.command.itemId,
              )
            : undefined;
        return (
          <Card key={row.id}>
            <Text style={styles.text}>
              {target?.order.workOrderNumber ?? 'Work order'} · {statusLabel(row.command.kind)}
            </Text>
            <Text style={styles.subtitle}>
              {row.state === 'acknowledged'
                ? 'Saved on server · awaiting download'
                : row.state === 'retry'
                  ? `Retry scheduled for ${new Date(row.due).toLocaleTimeString()}`
                  : statusLabel(row.state)}
            </Text>
            {row.command.kind === 'note' && <Text style={styles.text}>{row.command.body}</Text>}
            {row.command.kind === 'checklist' && (
              <Text style={styles.text}>
                {checklistItem?.label ?? 'Checklist item'}
                {' · '}
                {row.command.isCompleted ? 'Mark complete' : 'Reopen'}
              </Text>
            )}
            {actionable && <Text style={styles.subtitle}>{row.code?.replaceAll('_', ' ')}</Text>}
            {canApply && target && !hasPredecessor && (
              <Button
                title="Review and apply to current version"
                secondary
                disabled={offline || busy || saving}
                onPress={() =>
                  setReview({
                    row,
                    version: target.order.version,
                    serverState: checklistItem
                      ? `The server checklist item is ${checklistItem.isCompleted ? 'complete' : 'open'}.`
                      : `The server work order is ${statusLabel(target.order.status)}.`,
                  })
                }
              />
            )}
            {actionable && (
              <Button
                title="Discard edit and later edits for this order"
                danger
                disabled={busy || saving}
                onPress={() => setDiscard(row)}
              />
            )}
          </Card>
        );
      })}
      <Confirm
        visible={!!review && !!snapshot?.operations.some((row) => row.id === review.row.id)}
        busy={saving}
        title="Apply this edit again?"
        body={`${review?.serverState ?? ''} Apply the saved intent to version ${review?.version ?? ''}? Later edits will require their own review.`}
        onCancel={() => setReview(null)}
        onConfirm={() => {
          if (workspace && review)
            void perform(() => workspace.resolve(review.row.id, review.version));
        }}
      />
      <Confirm
        visible={!!discard && !!snapshot?.operations.some((row) => row.id === discard.id)}
        busy={saving}
        title="Discard saved edits?"
        body="This removes the selected edit and all later local edits for this work order."
        onCancel={() => setDiscard(null)}
        onConfirm={() => {
          if (workspace && discard) void perform(() => workspace.discard(discard.id));
        }}
      />
    </Screen>
  );
}
