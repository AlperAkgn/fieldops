import { useId, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery } from '@tanstack/react-query';
import { api, queryString } from '../api/client';
import type { Page } from '../api/contracts';
import { useSession } from '../api/session';
import { formSchema, type Field } from './form-schema';
import { ErrorBox, Pagination } from './ui';

function Lookup({
  field,
  value,
  parent,
  onChange,
  id,
}: {
  field: Field;
  value: string;
  parent: string;
  onChange: (value: string) => void;
  id: string;
}) {
  const session = useSession();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = queryString({
    page,
    pageSize: 10,
    search,
    ...(field.param ? { [field.param]: parent } : {}),
  });
  const result = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      field.lookup,
      query,
    ],
    queryFn: ({ signal }) =>
      api<Page<{ id: string; name: string; assetCode?: string }>>(`/${field.lookup}?${query}`, {
        signal,
      }),
    enabled: !field.dependsOn || !!parent,
  });
  return (
    <div className="lookup">
      <input
        aria-label={`Search ${field.label}`}
        placeholder={`Search ${field.label.toLowerCase()}…`}
        value={search}
        onChange={(event) => {
          setSearch(event.target.value);
          setPage(1);
        }}
        disabled={field.disabled}
      />
      <select
        id={id}
        aria-required={field.required}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={field.disabled || (!!field.dependsOn && !parent)}
      >
        <option value="">Select {field.label.toLowerCase()}</option>
        {value && !result.data?.items.some((item) => item.id === value) && (
          <option value={value}>Selected record · {value.slice(0, 8)}</option>
        )}
        {result.data?.items.map((item) => (
          <option key={item.id} value={item.id}>
            {item.assetCode ? `${item.assetCode} · ` : ''}
            {item.name}
          </option>
        ))}
      </select>
      {result.isPending && (!field.dependsOn || parent) && <small>Loading options…</small>}
      {result.isError && (
        <ErrorBox
          error={result.error}
          retry={() => {
            void result.refetch();
          }}
        />
      )}
      {(result.data?.totalCount ?? 0) > 10 && (
        <Pagination page={page} size={10} total={result.data?.totalCount ?? 0} onChange={setPage} />
      )}
    </div>
  );
}
export function Form({
  fields,
  initial = {},
  onSubmit,
  submitLabel = 'Save changes',
  pending = false,
  error,
  children,
}: {
  fields: Field[];
  initial?: Record<string, string>;
  onSubmit: (values: Record<string, string>) => Promise<unknown>;
  submitLabel?: string;
  pending?: boolean;
  error?: unknown;
  children?: React.ReactNode;
}) {
  const id = useId();
  const defaults = Object.fromEntries(
    fields.map((field) => [field.name, initial[field.name] ?? '']),
  );
  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<Record<string, string>>({
    defaultValues: defaults,
    resolver: zodResolver(formSchema(fields)),
  });
  const values = useWatch({ control });
  return (
    <form
      onSubmit={(event) => {
        void handleSubmit(async (data) => {
          await onSubmit(data).catch(() => undefined);
        })(event);
      }}
      noValidate
      className="form-grid"
    >
      {fields.map((field) => {
        const fieldId = `${id}-${field.name}`;
        const shared = {
          id: fieldId,
          disabled: field.disabled || pending || isSubmitting,
          'aria-invalid': !!errors[field.name],
          'aria-describedby': `${fieldId}-help`,
        };
        return (
          <div
            key={field.name}
            className={`field ${field.type === 'textarea' || field.lookup ? 'full' : ''}`}
          >
            <label htmlFor={fieldId}>
              {field.label}
              {field.required && <span aria-hidden="true"> *</span>}
            </label>
            {field.lookup ? (
              <Lookup
                field={field}
                id={fieldId}
                value={values[field.name] ?? ''}
                parent={values[field.dependsOn ?? ''] ?? ''}
                onChange={(value) => {
                  setValue(field.name, value, { shouldValidate: true });
                  fields
                    .filter((child) => child.dependsOn === field.name)
                    .forEach((child) => setValue(child.name, ''));
                }}
              />
            ) : field.type === 'textarea' ? (
              <textarea {...shared} {...register(field.name)} rows={3} />
            ) : field.type === 'select' ? (
              <select {...shared} {...register(field.name)}>
                <option value="">Select…</option>
                {field.options?.map((option) => (
                  <option
                    key={typeof option === 'string' ? option : option.value}
                    value={typeof option === 'string' ? option : option.value}
                  >
                    {typeof option === 'string'
                      ? option.replace(/([a-z])([A-Z])/g, '$1 $2')
                      : option.label}
                  </option>
                ))}
              </select>
            ) : (
              <input
                {...shared}
                type={field.type || 'text'}
                autoComplete={
                  field.type === 'password'
                    ? 'new-password'
                    : field.name === 'email'
                      ? 'email'
                      : 'off'
                }
                {...register(field.name)}
              />
            )}
            <small id={`${fieldId}-help`} className={errors[field.name] ? 'field-error' : ''}>
              {errors[field.name]?.message || field.help}
            </small>
          </div>
        );
      })}
      {children}
      {!!error && (
        <div className="full">
          <ErrorBox error={error} />
        </div>
      )}
      <div className="form-actions full">
        <button type="submit" className="btn primary" disabled={pending || isSubmitting}>
          {pending || isSubmitting ? 'Saving…' : submitLabel}
        </button>
      </div>
    </form>
  );
}
