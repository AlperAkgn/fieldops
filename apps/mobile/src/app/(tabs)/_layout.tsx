import { Tabs } from 'expo-router';
import Ionicons from '@expo/vector-icons/Ionicons';
import { colors } from '../../components/ui';
import { useSession } from '../../auth/runtime';
export default function TabLayout() {
  const { state } = useSession();
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.muted,
        tabBarStyle: { minHeight: 66, paddingTop: 6 },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Today',
          tabBarIcon: ({ color, size }) => (
            <Ionicons
              name="today-outline"
              color={typeof color === 'string' ? color : colors.primary}
              size={size}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="orders"
        options={{
          title: 'Work orders',
          tabBarIcon: ({ color, size }) => (
            <Ionicons
              name="clipboard-outline"
              color={typeof color === 'string' ? color : colors.primary}
              size={size}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="sync"
        options={{
          title: 'Sync',
          href:
            (state.access?.role ?? state.offline?.role) === 'Technician' ? '/(tabs)/sync' : null,
          tabBarIcon: ({ color, size }) => (
            <Ionicons
              name="sync-outline"
              color={typeof color === 'string' ? color : colors.primary}
              size={size}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          title: 'Profile',
          tabBarIcon: ({ color, size }) => (
            <Ionicons
              name="person-circle-outline"
              color={typeof color === 'string' ? color : colors.primary}
              size={size}
            />
          ),
        }}
      />
    </Tabs>
  );
}
