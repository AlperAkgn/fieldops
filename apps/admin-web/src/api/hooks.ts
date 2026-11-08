import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../components/ui-state';
import { api } from './client';
export function useAction(onSuccess?: (result: unknown) => void) {
  const client = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: ({
      path,
      method = 'POST',
      body,
    }: {
      path: string;
      method?: string;
      body?: unknown;
    }) => api(path, { method, ...(body === undefined ? {} : { body }) }),
    onSuccess: async (result) => {
      await client.invalidateQueries();
      toast('Changes saved.');
      onSuccess?.(result);
    },
  });
}
export const dateTime = (value: string | null | undefined) =>
  value
    ? new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(
        new Date(value),
      )
    : 'Not scheduled';
export const localDateTime = (value: string | null | undefined) =>
  value
    ? new Date(new Date(value).getTime() - new Date(value).getTimezoneOffset() * 60000)
        .toISOString()
        .slice(0, 16)
    : '';
export const initialValues = (value: object) =>
  Object.fromEntries(
    Object.entries(value).map(([key, item]) => [
      key,
      item === null || item === undefined ? '' : String(item),
    ]),
  );
