import { copy } from '../i18n/en';
import type { PropsWithChildren } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type TextInputProps,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import Ionicons from '@expo/vector-icons/Ionicons';
export const colors = {
  ink: '#15253B',
  muted: '#52647B',
  line: '#D8E1EC',
  surface: '#FFFFFF',
  canvas: '#F4F7FB',
  primary: '#3559D5',
  pale: '#EAF0FF',
  danger: '#AE2438',
  good: '#16634B',
};
export const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.canvas },
  content: {
    padding: 20,
    gap: 18,
    width: '100%',
    maxWidth: 760,
    alignSelf: 'center',
  },
  title: { fontSize: 28, fontWeight: '700', color: colors.ink },
  subtitle: { fontSize: 16, color: colors.muted, lineHeight: 23 },
  text: { fontSize: 16, color: colors.ink, lineHeight: 24 },
  label: {
    fontSize: 13,
    fontWeight: '700',
    color: colors.muted,
    textTransform: 'uppercase',
    letterSpacing: 0.8,
  },
  card: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: 18,
    padding: 18,
    gap: 12,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    flexWrap: 'wrap',
  },
  input: {
    minHeight: 50,
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: 12,
    padding: 13,
    backgroundColor: colors.surface,
    color: colors.ink,
    fontSize: 16,
  },
  button: {
    minHeight: 50,
    paddingHorizontal: 18,
    paddingVertical: 13,
    backgroundColor: colors.primary,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  buttonText: { color: '#FFFFFF', fontSize: 16, fontWeight: '600' },
  disabled: { opacity: 0.45 },
  chip: {
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 7,
    backgroundColor: colors.pale,
  },
  error: {
    backgroundColor: '#FFF0F1',
    borderColor: '#E8B7C0',
    borderWidth: 1,
    padding: 14,
    borderRadius: 12,
    gap: 10,
  },
  separator: { height: 1, backgroundColor: colors.line },
});
export function Screen({ children }: PropsWithChildren) {
  return (
    <SafeAreaView style={styles.screen} edges={['top', 'left', 'right']}>
      <KeyboardAvoidingView
        style={{ flex: 1 }}
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
export function Card({ children }: PropsWithChildren) {
  return <View style={styles.card}>{children}</View>;
}
export function Button({
  title,
  onPress,
  disabled = false,
  secondary = false,
  danger = false,
}: {
  title: string;
  onPress(): void;
  disabled?: boolean;
  secondary?: boolean;
  danger?: boolean;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={[
        styles.button,
        secondary && { backgroundColor: colors.pale },
        danger && { backgroundColor: colors.danger },
        disabled && styles.disabled,
      ]}
    >
      <Text style={[styles.buttonText, secondary && { color: colors.primary }]}>{title}</Text>
    </Pressable>
  );
}
export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return (
    <View style={{ gap: 8 }}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        placeholderTextColor={colors.muted}
        style={styles.input}
        {...props}
      />
    </View>
  );
}
export function ErrorBox({ text, retry }: { text: string; retry?: () => void }) {
  return (
    <View style={styles.error} accessibilityLiveRegion="polite">
      <Text style={[styles.text, { color: colors.danger }]}>{text}</Text>
      {retry && <Button title={copy.tryAgain} onPress={retry} secondary />}
    </View>
  );
}
export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <View style={{ padding: 24, gap: 14, alignItems: 'center' }} accessibilityLiveRegion="polite">
      <ActivityIndicator color={colors.primary} />
      <Text style={styles.subtitle}>{label}</Text>
    </View>
  );
}
export function Pill({ text }: { text: string }) {
  return (
    <View style={styles.chip}>
      <Text style={{ color: colors.primary, fontWeight: '600' }}>{text}</Text>
    </View>
  );
}
export function Icon({ name }: { name: React.ComponentProps<typeof Ionicons>['name'] }) {
  return <Ionicons name={name} size={22} color={colors.primary} accessible={false} />;
}
export function ChoiceDialog({
  title,
  visible,
  selected,
  choices,
  onSelect,
  onClose,
}: {
  title: string;
  visible: boolean;
  selected: string;
  choices: { value: string; label: string }[];
  onSelect(value: string): void;
  onClose(): void;
}) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <SafeAreaView
        style={{ flex: 1, backgroundColor: '#15253BBB', justifyContent: 'center', padding: 24 }}
      >
        <View accessibilityViewIsModal style={[styles.card, { maxHeight: '90%' }]}>
          <Text accessibilityRole="header" style={styles.title}>
            {title}
          </Text>
          <ScrollView>
            {choices.map((choice) => (
              <Pressable
                key={choice.value}
                accessibilityRole="radio"
                accessibilityState={{ checked: selected === choice.value }}
                onPress={() => onSelect(choice.value)}
                style={[styles.row, { minHeight: 50 }]}
              >
                <Icon name={selected === choice.value ? 'radio-button-on' : 'radio-button-off'} />
                <Text style={styles.text}>{choice.label}</Text>
              </Pressable>
            ))}
          </ScrollView>
          <Button title={copy.cancel} onPress={onClose} secondary />
        </View>
      </SafeAreaView>
    </Modal>
  );
}
export function Confirm({
  title,
  body,
  visible,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  body: string;
  visible: boolean;
  busy: boolean;
  onConfirm(): void;
  onCancel(): void;
}) {
  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      onRequestClose={() => {
        if (!busy) onCancel();
      }}
    >
      <SafeAreaView
        style={{
          flex: 1,
          backgroundColor: '#15253BBB',
          justifyContent: 'center',
          padding: 24,
        }}
      >
        <View accessibilityViewIsModal style={styles.card}>
          <Text accessibilityRole="header" style={styles.title}>
            {title}
          </Text>
          <Text style={styles.text}>{body}</Text>
          <Button title={busy ? copy.saving : copy.confirm} onPress={onConfirm} disabled={busy} />
          <Button title={copy.cancel} onPress={onCancel} disabled={busy} secondary />
        </View>
      </SafeAreaView>
    </Modal>
  );
}
