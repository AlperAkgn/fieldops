import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Form } from './Form';
import { formSchema, passwordRule } from './form-schema';
import { catalogBody, catalogFields } from '../pages/catalog-fields';

describe('operational form validation', () => {
  it('rejects an empty name and invalid email before calling the API', async () => {
    const submit = vi.fn().mockResolvedValue(undefined);
    render(
      <Form
        fields={[
          { name: 'name', label: 'Name', required: true },
          { name: 'email', label: 'Email', type: 'email' },
        ]}
        onSubmit={submit}
      />,
    );
    await userEvent.type(screen.getByLabelText('Email'), 'invalid');
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Name is required.')).toBeTruthy();
    expect(screen.getByText('Enter a valid email address.')).toBeTruthy();
    expect(submit).not.toHaveBeenCalled();
  });
  it('submits trimmed operational fields and exposes server errors', async () => {
    const submit = vi.fn().mockResolvedValue(undefined);
    render(
      <Form
        fields={[{ name: 'name', label: 'Name', required: true }]}
        onSubmit={submit}
        error={new Error('The record changed. Reload it.')}
      />,
    );
    await userEvent.type(screen.getByLabelText(/Name/), '  Acme  ');
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(submit).toHaveBeenCalledWith({ name: 'Acme' }));
    expect(screen.getByRole('alert').textContent).toContain('Reload it');
  });
  it('rejects backwards schedules, mismatched coordinates and invalid warranty dates', () => {
    expect(
      formSchema([
        { name: 'plannedStartAt', label: 'Start' },
        { name: 'plannedEndAt', label: 'End' },
      ]).safeParse({ plannedStartAt: '2027-01-01T11:00', plannedEndAt: '2027-01-01T10:00' })
        .success,
    ).toBe(false);
    expect(
      formSchema(catalogFields('service-locations')).safeParse({
        customerId: 'x',
        name: 'Site',
        addressLine: 'Road',
        city: 'City',
        country: 'TR',
        latitude: '42',
        longitude: '',
        stateOrProvince: '',
        postalCode: '',
        contactName: '',
        contactPhone: '',
      }).success,
    ).toBe(false);
    expect(
      formSchema([
        { name: 'installationDate', label: 'Installed' },
        { name: 'warrantyExpirationDate', label: 'Warranty' },
      ]).safeParse({ installationDate: '2027-01-02', warrantyExpirationDate: '2027-01-01' })
        .success,
    ).toBe(false);
  });
  it('enforces password policy without modifying password characters', () => {
    expect(passwordRule.safeParse('short').success).toBe(false);
    expect(passwordRule.safeParse('OnlyLettersPassword').success).toBe(false);
    expect(passwordRule.parse(' Strong-Password-42! ')).toBe(' Strong-Password-42! ');
  });
  it('preserves nullable patches and immutable location ownership', () => {
    expect(
      catalogBody(
        { name: 'Site', customerId: 'untrusted-change', latitude: '', longitude: '' },
        'service-locations',
        3,
      ),
    ).toMatchObject({ name: 'Site', latitude: null, longitude: null, expectedVersion: 3 });
    expect(catalogBody({}, 'service-locations', 3)).not.toHaveProperty('customerId');
  });
});
