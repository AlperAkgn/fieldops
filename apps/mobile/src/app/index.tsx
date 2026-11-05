import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { fetchHealth, getApiOrigin } from '../api/health';
import type { HealthEndpoint } from '../api/health';
import { messages as t } from '../i18n/en';

const apiBaseUrl = (() => {
  try { return getApiOrigin(process.env.EXPO_PUBLIC_API_BASE_URL || 'http://localhost:5080'); }
  catch { return ''; }
})();

function ServiceStatus({ endpoint, label }: { endpoint: HealthEndpoint; label: string }) {
  const query = useQuery({
    queryKey: ['health', apiBaseUrl, endpoint],
    queryFn: ({ signal }) => fetchHealth(apiBaseUrl, endpoint, signal),
  });
  const healthy = !query.isPending && !query.isError && query.data.status === 'Healthy';
  return (
    <View style={styles.service}>
      <View style={styles.serviceTop}><Text style={styles.serviceTitle}>{label}</Text><Text accessibilityLiveRegion="polite" style={[styles.status, healthy ? styles.healthy : styles.neutral]}>{query.isPending ? t.checking : query.isError ? t.unavailable : query.data.status}</Text></View>
      <Text style={styles.endpoint}>GET /health/{endpoint}</Text>
      {query.isError && <Text accessibilityRole="alert" style={styles.error}>{apiBaseUrl ? t.error : t.configurationError}</Text>}
    </View>
  );
}

export default function FoundationScreen() {
  const live = useQuery({ queryKey: ['health', apiBaseUrl, 'live'], queryFn: ({ signal }) => fetchHealth(apiBaseUrl, 'live', signal) });
  const ready = useQuery({ queryKey: ['health', apiBaseUrl, 'ready'], queryFn: ({ signal }) => fetchHealth(apiBaseUrl, 'ready', signal) });
  const checking = live.isFetching || ready.isFetching;

  return (
    <SafeAreaView style={styles.safe}>
      <ScrollView contentContainerStyle={styles.container}>
        <View style={styles.brand}><View style={styles.brandMark}><Text style={styles.brandLetter}>F</Text></View><Text style={styles.brandName}>{t.productName}</Text></View>
        <Text style={styles.eyebrow}>{t.phase}</Text>
        <Text style={styles.heading} accessibilityRole="header">{t.heading}</Text>
        <Text style={styles.description}>{t.description}</Text>
        <Text style={styles.sectionHeading} accessibilityRole="header">{t.connectivity}</Text>
        <View style={styles.services}><ServiceStatus endpoint="live" label={t.api} /><ServiceStatus endpoint="ready" label={t.database} /></View>
        <Pressable accessibilityRole="button" disabled={checking} accessibilityState={{ disabled: checking, busy: checking }} onPress={() => { void live.refetch(); void ready.refetch(); }} style={({ pressed }) => [styles.button, (checking || pressed) && styles.buttonMuted]}><Text style={styles.buttonText}>{checking ? t.checking : t.refresh}</Text></Pressable>
        <View style={styles.next}><Text style={styles.nextLabel}>{t.nextLabel}</Text><Text style={styles.nextTitle}>{t.nextTitle}</Text><Text style={styles.nextDescription}>{t.nextDescription}</Text></View>
        <Text style={styles.apiLabel}>{t.endpointLabel}</Text><Text selectable style={styles.apiAddress}>{apiBaseUrl || t.invalidConfiguration}</Text><Text style={styles.help}>{t.deviceHelp}</Text>
        <Text style={styles.scope}>{t.scope}</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: '#f5f7f8' },
  container: { padding: 25, paddingBottom: 40, width: '100%', maxWidth: 640, alignSelf: 'center' },
  brand: { flexDirection: 'row', alignItems: 'center', gap: 10, marginBottom: 38 },
  brandMark: { width: 35, height: 37, borderRadius: 10, backgroundColor: '#1a7479', alignItems: 'center', justifyContent: 'center' },
  brandLetter: { color: '#d4f7ef', fontSize: 23, fontWeight: '800' },
  brandName: { color: '#19343f', fontSize: 24, fontWeight: '700', letterSpacing: -1 },
  eyebrow: { fontSize: 9, color: '#397d7b', letterSpacing: 1.4, fontWeight: '700', marginBottom: 14 },
  heading: { fontSize: 30, lineHeight: 36, fontWeight: '700', color: '#19343f', letterSpacing: -1, marginBottom: 15 },
  description: { fontSize: 13, lineHeight: 22, color: '#71858d', marginBottom: 32 },
  sectionHeading: { color: '#243d47', fontSize: 15, fontWeight: '600', marginBottom: 14 },
  services: { gap: 11 },
  service: { borderWidth: 1, borderColor: '#e0e7ea', borderRadius: 10, padding: 19, backgroundColor: '#fff' },
  serviceTop: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 10, marginBottom: 16 },
  serviceTitle: { color: '#314d58', fontSize: 13, fontWeight: '600', flexShrink: 1 },
  status: { fontSize: 10, paddingVertical: 5, paddingHorizontal: 8, borderRadius: 5, overflow: 'hidden' },
  healthy: { color: '#267962', backgroundColor: '#ecf7f0' },
  neutral: { color: '#607b89', backgroundColor: '#edf3f7' },
  endpoint: { fontSize: 10, color: '#8b9da4' },
  error: { marginTop: 12, fontSize: 11, lineHeight: 18, color: '#a34d47' },
  button: { backgroundColor: '#1b666b', borderRadius: 7, alignItems: 'center', padding: 15, marginTop: 14, minHeight: 48 },
  buttonMuted: { opacity: 0.6 },
  buttonText: { color: '#fff', fontSize: 12, fontWeight: '600' },
  next: { backgroundColor: '#eaf1f1', borderWidth: 1, borderColor: '#dce6e8', borderRadius: 9, marginTop: 28, padding: 20 },
  nextLabel: { fontSize: 8, fontWeight: '700', color: '#6f8d8e', letterSpacing: 1.2, marginBottom: 10 },
  nextTitle: { fontSize: 14, fontWeight: '600', color: '#314d58', marginBottom: 7 },
  nextDescription: { fontSize: 11, lineHeight: 19, color: '#758b90' },
  apiLabel: { marginTop: 25, marginBottom: 5, fontSize: 10, color: '#8a9ba2' },
  apiAddress: { fontSize: 11, color: '#617f8b' },
  help: { fontSize: 10, lineHeight: 17, color: '#8699a1', marginTop: 9 },
  scope: { marginTop: 25, paddingTop: 20, borderTopWidth: 1, borderTopColor: '#e0e7ea', fontSize: 10, lineHeight: 17, color: '#8b9da4' },
});
