import { copy } from '../i18n/en';
import { useState } from 'react';
import {
  Linking,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  RefreshControl,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { router } from 'expo-router';
import { useIsFetching, useIsMutating, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/transport';
import {
  Button,
  Card,
  colors,
  Confirm,
  ErrorBox,
  Field,
  Icon,
  Loading,
  Pill,
  styles,
} from '../components/ui';
import { dateTime, mapUrl, message, statusLabel } from './orders';
import { useOrder, useOrderWrite, useWorkspace } from './queries';

export function WorkOrderScreen({
  id,
  noteDraft,
}: {
  id: string;
  noteDraft?: [string, (value: string) => void];
}) {
  const { detail, checklist, key, api, sessionBusy } = useOrder(id);
  const workspace = useWorkspace();
  const write = useOrderWrite(id);
  const client = useQueryClient();
  const [tab, setTab] = useState<'Checklist' | 'Notes' | 'History'>('Checklist');
  const localNote = useState('');
  const [note, setNote] = noteDraft ?? localNote;
  const [error, setError] = useState('');
  const [needsReload, setNeedsReload] = useState(false);
  const [notePage, setNotePage] = useState(1);
  const [historyPage, setHistoryPage] = useState(1);
  const [confirm, setConfirm] = useState<{
    action: 'start' | 'pause' | 'resume' | 'complete';
    version: number;
  } | null>(null);
  const mutations = useIsMutating({ mutationKey: key });
  const fetching = useIsFetching({ queryKey: key });
  const busy = sessionBusy || mutations > 0 || fetching > 0;
  const notes = useQuery({
    queryKey: [...key, 'notes', notePage],
    queryFn: ({ signal }) => api.comments(id, notePage, signal),
    enabled: tab === 'Notes' && !!detail.data && !detail.isError,
  });
  const history = useQuery({
    queryKey: [...key, 'history', historyPage],
    queryFn: ({ signal }) => api.history(id, historyPage, signal),
    enabled: tab === 'History' && !!detail.data && !detail.isError,
  });
  const denied = [detail.error, checklist.error, notes.error, history.error, write.error].some(
    (err) => err instanceof ApiError && [403, 404].includes(err.status),
  );
  async function reload() {
    setConfirm(null);
    await client.invalidateQueries({ queryKey: key });
    const current = client.getQueryState([...key, 'detail']);
    const items = client.getQueryState([...key, 'checklist']);
    if (current?.status === 'success' && items?.status === 'success') {
      setNeedsReload(false);
      setError('');
    }
  }
  async function change(
    operation: Parameters<typeof write.mutateAsync>[0],
    onSuccess?: () => void,
  ) {
    if (busy || needsReload) return;
    setError('');
    try {
      await write.mutateAsync(operation);
      onSuccess?.();
    } catch (err) {
      if (err instanceof ApiError && err.code === 'write_pending') return;
      setError(message(err) + ' Reload current data before retrying.');
      setNeedsReload(true);
      setConfirm(null);
    }
  }
  const back = (
    <Button
      title={copy.backToWorkOrders}
      secondary
      onPress={() => {
        if (router.canGoBack()) router.back();
        else router.replace('/(tabs)/orders');
      }}
    />
  );
  if (denied)
    return (
      <SafeAreaView style={styles.screen}>
        <View style={styles.content}>
          {back}
          <ErrorBox text="This work order is unavailable or no longer assigned to you." />
        </View>
      </SafeAreaView>
    );
  if (!detail.data)
    return (
      <SafeAreaView style={styles.screen}>
        <View style={styles.content}>
          {back}
          {detail.isError ? (
            <ErrorBox
              text={message(detail.error)}
              retry={() => {
                void detail.refetch();
              }}
            />
          ) : (
            <Loading />
          )}
        </View>
      </SafeAreaView>
    );
  const { order, customer, serviceLocation: location, asset, assignments } = detail.data;
  const zone = workspace.data?.current?.timeZoneId;
  const version = order.version;
  const mismatch = checklist.data && checklist.data.workOrderVersion !== version;
  const blocked =
    busy ||
    needsReload ||
    !!mismatch ||
    detail.isError ||
    checklist.isError ||
    !checklist.data ||
    !zone;
  const editable = !['Completed', 'Cancelled'].includes(order.status);
  const action =
    order.status === 'Assigned' ? 'start' : order.status === 'OnHold' ? 'resume' : null;
  return (
    <SafeAreaView style={styles.screen}>
      <KeyboardAvoidingView
        style={{ flex: 1 }}
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <ScrollView
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
          refreshControl={
            <RefreshControl
              refreshing={detail.isRefetching || checklist.isRefetching}
              onRefresh={() => {
                void reload();
              }}
              tintColor={colors.primary}
            />
          }
        >
          {back}
          <View style={styles.row}>
            <Text style={styles.label}>{order.workOrderNumber}</Text>
            <Pill text={order.priority} />
          </View>
          <Text accessibilityRole="header" style={styles.title}>
            {order.title}
          </Text>
          <View style={styles.row}>
            <Pill text={statusLabel(order.status)} />
            <Text style={styles.subtitle}>
              {copy.version}
              {version}
            </Text>
          </View>
          {order.description && <Text style={styles.text}>{order.description}</Text>}
          {error !== '' && <ErrorBox text={error} />}
          {(detail.isError || checklist.isError || workspace.isError) && (
            <ErrorBox text={message(detail.error || checklist.error || workspace.error)} />
          )}
          {(needsReload ||
            mismatch ||
            detail.isError ||
            checklist.isError ||
            workspace.isError) && (
            <Button
              title={copy.reloadCurrentData}
              secondary
              disabled={busy}
              onPress={() => {
                void reload();
                void workspace.refetch();
              }}
            />
          )}
          <Card>
            <Text style={styles.label}>
              {copy.serviceVisit}
              {zone || 'LOADING TIMEZONE'}
            </Text>
            <Text style={styles.text}>
              {copy.planned}
              {zone ? dateTime(order.plannedStartAt, zone) : '…'}
            </Text>
            <Text style={styles.subtitle}>
              {copy.until}
              {zone ? dateTime(order.plannedEndAt, zone) : '…'}
            </Text>
            <Text style={styles.subtitle}>
              {copy.started}{' '}
              {order.actualStartAt && zone ? dateTime(order.actualStartAt, zone) : copy.notStarted}
            </Text>
            <Text style={styles.subtitle}>
              {copy.finished}{' '}
              {order.actualEndAt && zone ? dateTime(order.actualEndAt, zone) : copy.notFinished}
            </Text>
            <Text style={styles.subtitle}>{copy.serverTimesHelp}</Text>
            {action && (
              <Button
                title={action === 'start' ? copy.startWork : copy.resumeWork}
                disabled={blocked}
                onPress={() => setConfirm({ action, version })}
              />
            )}
            {order.status === 'InProgress' && (
              <>
                <Button
                  title={copy.pauseWork}
                  secondary
                  disabled={blocked}
                  onPress={() => setConfirm({ action: 'pause', version })}
                />
                <Button
                  title={copy.completeWork}
                  disabled={blocked || !checklist.data?.requiredItemsComplete}
                  onPress={() => setConfirm({ action: 'complete', version })}
                />
                {!checklist.data?.requiredItemsComplete && (
                  <Text style={styles.subtitle}>{copy.requiredChecklistHelp}</Text>
                )}
              </>
            )}
          </Card>
          <Card>
            <View style={styles.row}>
              <Icon name="location-outline" />
              <Text style={styles.label}>{copy.customerLocation}</Text>
            </View>
            <Text style={styles.text}>{customer.name}</Text>
            <Text style={styles.text}>{location.name}</Text>
            <Text style={styles.subtitle}>
              {[
                location.addressLine,
                location.city,
                location.stateOrProvince,
                location.postalCode,
                location.country,
              ]
                .filter(Boolean)
                .join(', ')}
            </Text>
            {location.contactName && (
              <Text style={styles.text}>
                {copy.contact}
                {location.contactName}
              </Text>
            )}
            {location.contactPhone && (
              <Text selectable style={styles.text}>
                {location.contactPhone}
              </Text>
            )}
            <Button
              title={copy.openInMaps}
              secondary
              onPress={() => {
                void Linking.openURL(mapUrl(location)).catch(() =>
                  setError('Maps could not be opened. Use the address above.'),
                );
              }}
            />
          </Card>
          {asset && (
            <Card>
              <Text style={styles.label}>
                {copy.asset}
                {asset.assetCode}
              </Text>
              <Text style={styles.text}>{asset.name}</Text>
              <Text style={styles.subtitle}>
                {[asset.brand, asset.model].filter(Boolean).join(' · ') || 'No model details'}
              </Text>
              {asset.serialNumber && (
                <Text selectable style={styles.subtitle}>
                  {copy.serial}
                  {asset.serialNumber}
                </Text>
              )}
            </Card>
          )}
          <Card>
            <Text style={styles.label}>{copy.serviceTeam}</Text>
            {assignments.map((member) => (
              <Text key={member.id} style={styles.text}>
                {member.displayName}
                {member.isPrimary ? ' · Primary technician' : ''}
              </Text>
            ))}
          </Card>
          <View style={styles.row}>
            {(['Checklist', 'Notes', 'History'] as const).map((item) => (
              <Pressable
                key={item}
                accessibilityRole="tab"
                accessibilityState={{ selected: tab === item }}
                onPress={() => setTab(item)}
                style={[
                  styles.chip,
                  tab === item && {
                    backgroundColor: colors.primary,
                    paddingVertical: 14,
                  },
                ]}
              >
                <Text
                  style={{
                    color: tab === item ? '#FFFFFF' : colors.primary,
                    fontWeight: '700',
                  }}
                >
                  {item}
                </Text>
              </Pressable>
            ))}
          </View>
          {tab === 'Checklist' && (
            <Card>
              <Text accessibilityRole="header" style={styles.label}>
                {copy.checklist}
                {checklist.data?.progressPercent ?? 0}
                {copy.separator2}
              </Text>
              {checklist.isLoading && <Loading />}
              {checklist.data?.items.length === 0 && (
                <Text style={styles.subtitle}>{copy.noChecklistIsAttachedToThisWorkOrder}</Text>
              )}
              {checklist.data?.items.map((item) => (
                <Pressable
                  key={item.id}
                  accessibilityRole="checkbox"
                  accessibilityLabel={`${item.label}${item.isRequired ? copy.required : ''}`}
                  accessibilityState={{
                    checked: item.isCompleted,
                    disabled: blocked || !editable,
                  }}
                  disabled={blocked || !editable}
                  onPress={() => {
                    void change(() => api.check(id, item.id, !item.isCompleted, version));
                  }}
                  style={[styles.row, { minHeight: 52 }, (blocked || !editable) && styles.disabled]}
                >
                  <Icon name={item.isCompleted ? 'checkbox' : 'square-outline'} />
                  <Text style={[styles.text, { flex: 1 }]}>
                    {item.label}
                    {item.isRequired ? ' *' : ''}
                  </Text>
                </Pressable>
              ))}
            </Card>
          )}
          {tab === 'Notes' && (
            <Card>
              <Text accessibilityRole="header" style={styles.label}>
                {copy.serviceNotes}
              </Text>
              {editable && (
                <>
                  <Field
                    label={copy.serviceNote}
                    value={note}
                    onChangeText={setNote}
                    multiline
                    maxLength={4000}
                    editable={!busy}
                    placeholder={copy.recordFindingsAndWorkPerformed}
                  />
                  <Text style={styles.subtitle}>
                    {note.length}
                    {copy.text4000VisibleToTheServiceTeam}
                  </Text>
                  <Button
                    title={copy.addServiceNote}
                    disabled={blocked || !note.trim()}
                    onPress={() => {
                      void change(
                        () => api.note(id, note.trim(), version),
                        () => {
                          setNote('');
                          setNotePage(1);
                        },
                      );
                    }}
                  />
                </>
              )}
              {notes.isLoading && <Loading />}
              {notes.isError && (
                <ErrorBox
                  text={message(notes.error)}
                  retry={() => {
                    void notes.refetch();
                  }}
                />
              )}
              {notes.data?.items.map((item) => (
                <View key={item.id} style={{ gap: 8 }}>
                  <View style={styles.separator} />
                  <Text style={styles.text}>{item.body}</Text>
                  <Text style={styles.subtitle}>
                    {item.authorDisplayName} {copy.separator4}{' '}
                    {zone ? dateTime(item.createdAt, zone) : '…'}
                  </Text>
                </View>
              ))}
              {notes.data?.items.length === 0 && (
                <Text style={styles.subtitle}>{copy.noServiceNotesYet}</Text>
              )}
              {notes.data && (
                <Pager
                  page={notePage}
                  count={notes.data.totalCount}
                  busy={notes.isFetching}
                  onPage={setNotePage}
                />
              )}
            </Card>
          )}
          {tab === 'History' && (
            <Card>
              <Text accessibilityRole="header" style={styles.label}>
                {copy.workHistory}
              </Text>
              {history.isLoading && <Loading />}
              {history.isError && (
                <ErrorBox
                  text={message(history.error)}
                  retry={() => {
                    void history.refetch();
                  }}
                />
              )}
              {history.data?.items.map((item) => (
                <View key={item.id} style={{ gap: 8 }}>
                  <Text style={styles.text}>
                    {item.previousStatus ? statusLabel(item.previousStatus) + ' → ' : ''}
                    {statusLabel(item.status)}
                  </Text>
                  <Text style={styles.subtitle}>
                    {item.displayName} {copy.separator4}{' '}
                    {zone ? dateTime(item.changedAt, zone) : '…'} {copy.v}
                    {item.version}
                  </Text>
                  <View style={styles.separator} />
                </View>
              ))}
              {history.data && (
                <Pager
                  page={historyPage}
                  count={history.data.totalCount}
                  busy={history.isFetching}
                  onPage={setHistoryPage}
                />
              )}
            </Card>
          )}
          <Confirm
            visible={!!confirm}
            busy={write.isPending || sessionBusy}
            title={`${statusLabel(confirm?.action || '')} work?`}
            body={`Save this action using work order version ${confirm?.version ?? version}?`}
            onCancel={() => setConfirm(null)}
            onConfirm={() => {
              if (confirm) {
                const snapshot = confirm;
                void change(
                  () => api.action(id, snapshot.action, snapshot.version),
                  () => setConfirm(null),
                );
              }
            }}
          />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
function Pager({
  page,
  count,
  busy,
  onPage,
}: {
  page: number;
  count: number;
  busy: boolean;
  onPage(page: number): void;
}) {
  return (
    <View style={styles.row}>
      <Button
        title={copy.previousPage}
        disabled={page === 1 || busy}
        secondary
        onPress={() => onPage(page - 1)}
      />
      <Text style={styles.subtitle}>
        {copy.page}
        {page}
      </Text>
      <Button
        title={copy.nextPage}
        disabled={page * 20 >= count || busy}
        secondary
        onPress={() => onPage(page + 1)}
      />
    </View>
  );
}
