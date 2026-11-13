import { Tabs } from 'expo-router';
import Ionicons from '@expo/vector-icons/Ionicons';
import { colors } from '../../components/ui';
export default function TabLayout() {
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
