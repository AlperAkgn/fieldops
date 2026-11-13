import { useLocalSearchParams } from 'expo-router';
import { WorkOrderScreen } from '../../features/WorkOrderScreen';
export default function WorkOrderRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <WorkOrderScreen key={id} id={id} />;
}
