import { copy } from '../i18n/en';
import { Redirect } from 'expo-router';
import { useSession } from '../auth/runtime';
import { Loading, Screen } from '../components/ui';
export default function Entry() {
  const { state } = useSession();
  if (state.status === 'loading')
    return (
      <Screen>
        <Loading label={copy.restoringProtectedSession} />
      </Screen>
    );
  return <Redirect href={state.status === 'authenticated' ? '/(tabs)' : '/sign-in'} />;
}
