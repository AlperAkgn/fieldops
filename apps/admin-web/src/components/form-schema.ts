import { z } from 'zod';
export interface Field {
  name: string;
  label: string;
  type?:
    'text' | 'email' | 'password' | 'textarea' | 'select' | 'date' | 'datetime-local' | 'number';
  required?: boolean;
  max?: number;
  options?: readonly string[] | { value: string; label: string }[];
  lookup?: string;
  dependsOn?: string;
  param?: string;
  disabled?: boolean;
  help?: string;
}
export const passwordRule = z
  .string()
  .min(12, 'Use at least 12 characters.')
  .regex(/[a-z]/, 'Include a lowercase letter.')
  .regex(/[A-Z]/, 'Include an uppercase letter.')
  .regex(/[0-9]/, 'Include a number.')
  .regex(/[^a-zA-Z0-9]/, 'Include a symbol.');
export function formSchema(fields: Field[]) {
  const shape: Record<string, z.ZodString> = {};
  fields.forEach((field) => {
    let schema = z
      .string()
      .trim()
      .max(field.max ?? (field.type === 'textarea' ? 4000 : 200));
    if (field.required) schema = schema.min(1, `${field.label} is required.`);
    if (field.type === 'email')
      schema = schema.refine(
        (value) => !value || z.email().safeParse(value).success,
        'Enter a valid email address.',
      );
    if (field.name === 'password' || field.name === 'newPassword') schema = passwordRule;
    if (field.type === 'number')
      schema = schema.refine(
        (value) => !value || Number.isFinite(Number(value)),
        'Enter a valid number.',
      );
    shape[field.name] = schema;
  });
  return z.object(shape).superRefine((value, ctx) => {
    if (value.plannedStartAt && value.plannedEndAt && value.plannedEndAt <= value.plannedStartAt)
      ctx.addIssue({ code: 'custom', path: ['plannedEndAt'], message: 'End must be after start.' });
    if (!!value.latitude !== !!value.longitude)
      ctx.addIssue({
        code: 'custom',
        path: [value.latitude ? 'longitude' : 'latitude'],
        message: 'Provide both coordinates.',
      });
    if (
      value.installationDate &&
      value.warrantyExpirationDate &&
      value.warrantyExpirationDate < value.installationDate
    )
      ctx.addIssue({
        code: 'custom',
        path: ['warrantyExpirationDate'],
        message: 'Warranty must end after installation.',
      });
    for (const [name, minimum, maximum] of [
      ['latitude', -90, 90],
      ['longitude', -180, 180],
      ['position', 0, 999],
    ] as const) {
      const input = value[name];
      if (
        input &&
        (Number(input) < minimum ||
          Number(input) > maximum ||
          (name === 'position' && !Number.isInteger(Number(input))))
      )
        ctx.addIssue({
          code: 'custom',
          path: [name],
          message: `Enter ${name === 'position' ? 'an integer' : 'a number'} between ${minimum} and ${maximum}.`,
        });
    }
  });
}
