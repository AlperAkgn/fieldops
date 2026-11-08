import type { Field } from '../components/form-schema';
export type CatalogKind = 'customers' | 'service-locations' | 'assets' | 'asset-categories';
export const catalogNames: Record<
  CatalogKind,
  { title: string; singular: string; description: string }
> = {
  customers: {
    title: 'Customers',
    singular: 'customer',
    description: 'The people and businesses you keep moving.',
  },
  'service-locations': {
    title: 'Service locations',
    singular: 'service location',
    description: 'Every site, address and service contact in one place.',
  },
  assets: {
    title: 'Assets',
    singular: 'asset',
    description: 'Know your equipment. Keep every service connected.',
  },
  'asset-categories': {
    title: 'Asset categories',
    singular: 'category',
    description: 'A consistent equipment taxonomy for your organization.',
  },
};
export function catalogFields(kind: CatalogKind, editing = false): Field[] {
  const name: Field = {
    name: 'name',
    label: 'Name',
    required: true,
    max: kind === 'asset-categories' ? 100 : 160,
  };
  const notes: Field = { name: 'notes', label: 'Notes', type: 'textarea' };
  const customer: Field = {
    name: 'customerId',
    label: 'Customer',
    lookup: 'customers',
    required: true,
    disabled: editing,
  };
  const location: Field = {
    name: 'serviceLocationId',
    label: 'Service location',
    lookup: 'service-locations',
    required: true,
    dependsOn: 'customerId',
    param: 'customerId',
    disabled: editing,
  };
  if (kind === 'customers')
    return [
      name,
      {
        name: 'customerType',
        label: 'Customer type',
        type: 'select',
        options: ['Business', 'Individual'],
        required: true,
      },
      { name: 'email', label: 'Email', type: 'email', max: 254 },
      { name: 'phone', label: 'Phone', max: 50 },
      notes,
    ];
  if (kind === 'service-locations')
    return [
      customer,
      name,
      { name: 'addressLine', label: 'Address', required: true, max: 250 },
      { name: 'city', label: 'City', required: true, max: 120 },
      { name: 'country', label: 'Country', required: true, max: 100 },
      { name: 'stateOrProvince', label: 'State / province', max: 120 },
      { name: 'postalCode', label: 'Postal code', max: 25 },
      { name: 'contactName', label: 'Contact name', max: 120 },
      { name: 'contactPhone', label: 'Contact phone', max: 50 },
      { name: 'latitude', label: 'Latitude', type: 'number' },
      { name: 'longitude', label: 'Longitude', type: 'number' },
    ];
  if (kind === 'asset-categories')
    return [name, { name: 'description', label: 'Description', type: 'textarea', max: 1000 }];
  return [
    customer,
    location,
    { name: 'assetCode', label: 'Asset code', required: true, max: 64 },
    name,
    {
      name: 'status',
      label: 'Status',
      type: 'select',
      options: ['Active', 'UnderMaintenance', 'OutOfService', 'Retired'],
      required: true,
    },
    { name: 'categoryId', label: 'Category', lookup: 'asset-categories' },
    { name: 'brand', label: 'Brand', max: 100 },
    { name: 'model', label: 'Model', max: 100 },
    { name: 'serialNumber', label: 'Serial number', max: 120 },
    { name: 'installationDate', label: 'Installation date', type: 'date' },
    { name: 'warrantyExpirationDate', label: 'Warranty expiration', type: 'date' },
    notes,
  ];
}
export function catalogBody(values: Record<string, string>, kind: CatalogKind, version?: number) {
  const result: Record<string, unknown> = {};
  catalogFields(kind).forEach((field) => {
    if (
      version &&
      (field.name === 'customerId' || field.name === 'serviceLocationId') &&
      kind !== 'customers'
    )
      return;
    const value = values[field.name];
    result[field.name] = field.type === 'number' ? (value ? Number(value) : null) : value || null;
  });
  if (version) result.expectedVersion = version;
  return result;
}
