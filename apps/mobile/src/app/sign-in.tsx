import { copy } from '../i18n/en';
import { useState } from 'react';
import { Text, View } from 'react-native';
import { Redirect } from 'expo-router';
import { useSession } from '../auth/runtime';
import { message } from '../features/orders';
import { Button, Card, ErrorBox, Field, Icon, Loading, Screen, styles } from '../components/ui';
export default function SignIn() {
  const { session, state } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [recovery, setRecovery] = useState(false);
  const [notice, setNotice] = useState('');
  const [pending, setPending] = useState(false);
  if (state.status === 'authenticated') return <Redirect href="/(tabs)" />;
  if (state.status === 'loading')
    return (
      <Screen>
        <Loading />
      </Screen>
    );
  async function submit() {
    setError('');
    setNotice('');
    setPending(true);
    try {
      if (recovery) {
        await session.transport.request('/auth/forgot-password', {
          method: 'POST',
          body: { email: email.trim() },
        });
        setNotice(
          'If this account is eligible, recovery instructions have been sent. Open the recovery link in your browser.',
        );
      } else {
        await session.login(email, password);
        setPassword('');
      }
    } catch (err) {
      setError(message(err));
    } finally {
      setPending(false);
    }
  }
  return (
    <Screen>
      <View style={{ paddingTop: 32, gap: 12 }}>
        <Icon name="construct" />
        <Text style={styles.label}>{copy.fieldopsTechnician}</Text>
        <Text style={styles.title}>
          {recovery ? copy.recoverYourAccount : copy.readyForTheField}
        </Text>
        <Text style={styles.subtitle}>{copy.intro}</Text>
      </View>
      <Card>
        {state.message && <Text style={styles.subtitle}>{state.message}</Text>}
        {state.status === 'locked' ? (
          <ErrorBox
            text={state.message || 'Protected storage unavailable.'}
            retry={() => {
              void session.clearLocal().catch((err) => setError(message(err)));
            }}
          />
        ) : (
          <>
            <Field
              label={copy.email}
              value={email}
              onChangeText={setEmail}
              autoComplete="email"
              keyboardType="email-address"
              autoCapitalize="none"
              editable={!pending && !state.busy}
            />
            {!recovery && (
              <Field
                label={copy.password}
                value={password}
                onChangeText={setPassword}
                secureTextEntry
                autoComplete="current-password"
                autoCapitalize="none"
                editable={!pending && !state.busy}
              />
            )}
            {error !== '' && <ErrorBox text={error} />}
            {notice !== '' && (
              <Text accessibilityLiveRegion="polite" style={styles.text}>
                {notice}
              </Text>
            )}
            <Button
              title={
                pending || state.busy
                  ? copy.pleaseWait
                  : recovery
                    ? copy.sendRecoveryInstructions
                    : copy.signIn
              }
              onPress={() => {
                void submit();
              }}
              disabled={pending || state.busy || !email.trim() || (!recovery && !password)}
            />
            <Button
              title={recovery ? copy.backToSignIn : copy.forgotPassword}
              secondary
              disabled={pending || state.busy}
              onPress={() => {
                setRecovery(!recovery);
                setError('');
                setNotice('');
                setPassword('');
              }}
            />
          </>
        )}
      </Card>
      <Text style={styles.subtitle}>{copy.invitationHelp}</Text>
    </Screen>
  );
}
