import { copy } from '../../i18n/en';
import { useState } from 'react';
import { Text, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useApi, useWorkspace } from '../../features/queries';
import { message, statusLabel } from '../../features/orders';
import {
  Button,
  Card,
  Confirm,
  ErrorBox,
  Icon,
  Loading,
  Screen,
  styles,
} from '../../components/ui';
export default function Profile() {
  const { session, state, scope } = useApi();
  const workspace = useWorkspace();
  const [error, setError] = useState('');
  const [signOut, setSignOut] = useState(false);
  const profile = useQuery({
    queryKey: [...scope, 'profile'],
    queryFn: ({ signal }) => session.profile(signal),
    enabled: !state.busy,
  });
  return (
    <Screen>
      <Text style={styles.label}>{copy.fieldopsAccount}</Text>
      <Text style={styles.title}>{copy.yourProfile}</Text>
      {profile.isLoading && <Loading />}
      {profile.isError && (
        <ErrorBox
          text={message(profile.error)}
          retry={() => {
            void profile.refetch();
          }}
        />
      )}
      {error !== '' && <ErrorBox text={error} />}
      {profile.data && (
        <Card>
          <Icon name="person-circle-outline" />
          <Text style={styles.title}>{profile.data.displayName}</Text>
          <Text style={styles.text}>{profile.data.email}</Text>
          <Text style={styles.subtitle}>{statusLabel(state.access?.role || '')}</Text>
        </Card>
      )}
      <Card>
        <Text accessibilityRole="header" style={styles.label}>
          {copy.workspace}
        </Text>
        <Text style={styles.text}>{workspace.data?.current?.name}</Text>
        <Text style={styles.subtitle}>{workspace.data?.current?.timeZoneId}</Text>
        {workspace.isError && (
          <ErrorBox
            text={message(workspace.error)}
            retry={() => {
              void workspace.refetch();
            }}
          />
        )}
        {workspace.data?.rows
          .filter((row) => row.id !== state.access?.tenantId)
          .map((row) => (
            <View key={row.id} style={{ gap: 6 }}>
              <Text style={styles.subtitle}>
                {row.name} {copy.separator}
                {statusLabel(row.role)}
              </Text>
              <Button
                title={`Switch to ${row.name}`}
                disabled={state.busy}
                secondary
                onPress={() => {
                  setError('');
                  void session.selectOrganization(row.id).catch((err) => setError(message(err)));
                }}
              />
            </View>
          ))}
      </Card>
      <Card>
        <Text style={styles.label}>{copy.onlineService}</Text>
        <Text style={styles.subtitle}>{copy.onlineNotice}</Text>
        <Button
          title={copy.signOut}
          danger
          disabled={state.busy}
          onPress={() => setSignOut(true)}
        />
      </Card>
      <Confirm
        visible={signOut}
        busy={state.busy}
        title={copy.confirmSignOut}
        body={copy.signOutNotice}
        onCancel={() => setSignOut(false)}
        onConfirm={() => {
          void session.logout().catch((err) => setError(message(err)));
        }}
      />
    </Screen>
  );
}
