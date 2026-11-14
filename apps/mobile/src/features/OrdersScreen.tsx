import { copy } from '../i18n/en';
import { useEffect, useMemo, useState } from 'react';
import { AppState, FlatList, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { priorities, statuses } from '../api/contracts';
import {
  Button,
  Card,
  ChoiceDialog,
  colors,
  ErrorBox,
  Field,
  Loading,
  Pill,
  styles,
} from '../components/ui';
import { dateTime, message, statusLabel, todayRange } from './orders';
import { useApi, useWorkspace } from './queries';
import { useOffline } from '../offline/runtime';
import { OfflineOrders } from '../offline/screens';
export function OrdersScreen({ today = false }: { today?: boolean }) {
  const { offline, snapshot } = useOffline();
  if (offline || snapshot?.rebuilding) return <OfflineOrders />;
  return <OnlineOrders today={today} />;
}
function OnlineOrders({ today }: { today: boolean }) {
  const { api, state, scope } = useApi();
  const workspace = useWorkspace();
  const [search, setSearch] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [status, setStatus] = useState('');
  const [priority, setPriority] = useState('');
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<'status' | 'priority' | null>(null);
  const zone = workspace.data?.current?.timeZoneId;
  const [clock, setClock] = useState(() => new Date());
  useEffect(() => {
    if (!today) return;
    const update = () => setClock(new Date());
    const timer = setInterval(update, 60_000);
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') update();
    });
    return () => {
      clearInterval(timer);
      subscription.remove();
    };
  }, [today]);
  const range = useMemo(() => (today && zone ? todayRange(zone, clock) : {}), [today, zone, clock]);
  const filters = { search: submitted, status, priority, page, ...range };
  const orders = useQuery({
    queryKey: [...scope, 'orders', filters],
    enabled: state.access?.role === 'Technician' && !state.busy && !!zone,
    queryFn: ({ signal }) => api.list(filters, signal),
  });
  if (state.access?.role !== 'Technician')
    return (
      <SafeAreaView style={styles.screen}>
        <View style={styles.content}>
          <Text style={styles.title}>{copy.chooseYourWorkspace}</Text>
          <Text style={styles.subtitle}>{copy.technicianWorkspaceHelp}</Text>
          <Button title={copy.openProfile} onPress={() => router.navigate('/(tabs)/profile')} />
        </View>
      </SafeAreaView>
    );
  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      <FlatList
        contentContainerStyle={styles.content}
        data={orders.data?.items || []}
        keyExtractor={(item) => item.id}
        keyboardShouldPersistTaps="handled"
        refreshControl={
          <RefreshControl
            refreshing={orders.isRefetching || workspace.isRefetching}
            onRefresh={() => {
              setClock(new Date());
              void workspace.refetch();
              void orders.refetch();
            }}
            tintColor={colors.primary}
          />
        }
        ListHeaderComponent={
          <View style={{ gap: 16 }}>
            <Text style={styles.label}>
              {copy.fieldops}
              {workspace.data?.current?.name || 'YOUR WORKSPACE'}
            </Text>
            <Text style={styles.title}>{today ? copy.yourDayInTheField : copy.yourWorkOrders}</Text>
            <Text style={styles.subtitle}>
              {today ? copy.scheduledToday : copy.assignedToYou}
              {zone || 'Loading timezone…'}
            </Text>
            <Field
              label={copy.searchWorkOrders}
              placeholder={copy.numberOrTitle}
              value={search}
              onChangeText={setSearch}
              maxLength={200}
              returnKeyType="search"
              onSubmitEditing={() => {
                setSubmitted(search.trim());
                setPage(1);
              }}
            />
            <Button
              title={copy.search}
              secondary
              onPress={() => {
                setSubmitted(search.trim());
                setPage(1);
              }}
            />
            <View style={styles.row}>
              <Button
                title={`Status: ${statusLabel(status) || copy.all}`}
                secondary
                onPress={() => setFilter('status')}
              />
              <Button
                title={`Priority: ${priority || copy.all}`}
                secondary
                onPress={() => setFilter('priority')}
              />
            </View>
            {workspace.isError && (
              <ErrorBox
                text={message(workspace.error)}
                retry={() => {
                  void workspace.refetch();
                }}
              />
            )}
            {orders.isError && (
              <ErrorBox
                text={message(orders.error)}
                retry={() => {
                  void orders.refetch();
                }}
              />
            )}
            {(orders.isLoading || workspace.isLoading) && <Loading />}
          </View>
        }
        renderItem={({ item }) => (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={`${item.workOrderNumber}, ${item.title}, ${statusLabel(item.status)}`}
            onPress={() =>
              router.push({
                pathname: '/work-orders/[id]',
                params: { id: item.id },
              })
            }
          >
            <Card>
              <View style={styles.row}>
                <Text style={styles.label}>{item.workOrderNumber}</Text>
                <Pill text={item.priority} />
              </View>
              <Text style={[styles.text, { fontWeight: '700', fontSize: 19 }]}>{item.title}</Text>
              <Text style={styles.subtitle}>{dateTime(item.plannedStartAt, zone || 'UTC')}</Text>
              <Pill text={statusLabel(item.status)} />
            </Card>
          </Pressable>
        )}
        ListEmptyComponent={
          !orders.isLoading && !orders.isError && !workspace.isError ? (
            <Card>
              <Text style={styles.text}>{copy.noAssignedWorkOrdersMatchThisView}</Text>
              <Text style={styles.subtitle}>{copy.pullToRefreshOrAdjustYourFilters}</Text>
            </Card>
          ) : undefined
        }
        ListFooterComponent={
          orders.data ? (
            <View style={{ gap: 12 }}>
              <Text style={styles.subtitle}>
                {orders.data.totalCount} {copy.workOrdersPage}
                {page}
              </Text>
              <View style={styles.row}>
                <Button
                  title={copy.previous}
                  disabled={page === 1 || orders.isFetching}
                  secondary
                  onPress={() => setPage(page - 1)}
                />
                <Button
                  title={copy.next}
                  disabled={page * 20 >= orders.data.totalCount || orders.isFetching}
                  secondary
                  onPress={() => setPage(page + 1)}
                />
              </View>
            </View>
          ) : undefined
        }
      />
      <ChoiceDialog
        title={filter === 'priority' ? 'Filter by priority' : 'Filter by status'}
        visible={!!filter}
        selected={filter === 'priority' ? priority : status}
        choices={['', ...(filter === 'priority' ? priorities : statuses)].map((value) => ({
          value,
          label: statusLabel(value) || copy.all,
        }))}
        onClose={() => setFilter(null)}
        onSelect={(value) => {
          if (filter === 'priority') setPriority(value);
          else setStatus(value);
          setPage(1);
          setFilter(null);
        }}
      />
    </SafeAreaView>
  );
}
