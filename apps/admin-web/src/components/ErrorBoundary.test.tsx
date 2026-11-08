import { expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ErrorBoundary } from './ErrorBoundary';
it('shows a recoverable fallback without exposing private exception details', () => {
  const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  function Broken(): never {
    throw new Error('private-payload-do-not-display');
  }
  try {
    render(
      <ErrorBoundary>
        <Broken />
      </ErrorBoundary>,
    );
    expect(screen.getByRole('alert').textContent).toContain('Something went wrong');
    expect(screen.queryByText('private-payload-do-not-display')).toBeNull();
    expect(screen.getByRole('button', { name: 'Reload workspace' })).toBeTruthy();
  } finally {
    log.mockRestore();
  }
});
