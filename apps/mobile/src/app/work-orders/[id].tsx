import { useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { WorkOrderScreen } from '../../features/WorkOrderScreen';
import { useOffline } from '../../offline/runtime';
import { OfflineOrder } from '../../offline/screens';
export default function WorkOrderRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <WorkOrderView key={id} id={id} />;
}

function WorkOrderView({ id }: { id: string }) {
  const { offline, snapshot } = useOffline();
  const noteDraft = useState('');
  if (offline || snapshot?.rebuilding || snapshot?.operations.some((row) => row.orderId === id))
    return <OfflineOrder id={id} noteDraft={noteDraft} />;
  return <WorkOrderScreen id={id} noteDraft={noteDraft} />;
}
