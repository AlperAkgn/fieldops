import { afterEach, beforeEach, expect, it } from 'vitest';
import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Modal } from './ui';

const prototype = HTMLDialogElement.prototype;
const originalShow = Object.getOwnPropertyDescriptor(prototype, 'showModal');
const originalClose = Object.getOwnPropertyDescriptor(prototype, 'close');
beforeEach(() => {
  Object.defineProperty(prototype, 'showModal', {
    configurable: true,
    value() {
      this.setAttribute('open', '');
      this.querySelector('button')?.focus();
    },
  });
  Object.defineProperty(prototype, 'close', {
    configurable: true,
    value() {
      this.removeAttribute('open');
    },
  });
});
afterEach(() => {
  if (originalShow) Object.defineProperty(prototype, 'showModal', originalShow);
  else Reflect.deleteProperty(prototype, 'showModal');
  if (originalClose) Object.defineProperty(prototype, 'close', originalClose);
  else Reflect.deleteProperty(prototype, 'close');
});
function Harness({ remove }: { remove: boolean }) {
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [trigger, setTrigger] = useState(true);
  return (
    <main id="main" tabIndex={-1}>
      {trigger && (
        <button disabled={pending} onClick={() => setOpen(true)}>
          Edit
        </button>
      )}
      {open && (
        <Modal title="Edit record" onClose={() => setOpen(false)}>
          <button onClick={() => setPending(true)}>Begin saving</button>
          <button
            onClick={() => {
              if (remove) setTrigger(false);
              setOpen(false);
            }}
          >
            Close while saving
          </button>
        </Modal>
      )}
    </main>
  );
}
it('focuses the main landmark when a connected return trigger is still disabled', async () => {
  render(<Harness remove={false} />);
  await userEvent.click(screen.getByRole('button', { name: 'Edit' }));
  await userEvent.click(screen.getByRole('button', { name: 'Begin saving' }));
  expect((screen.getByRole('button', { name: 'Edit' }) as HTMLButtonElement).disabled).toBe(true);
  await userEvent.click(screen.getByRole('button', { name: 'Close while saving' }));
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('main'));
});
it('focuses the main landmark if the return trigger unmounts while closing', async () => {
  render(<Harness remove />);
  await userEvent.click(screen.getByRole('button', { name: 'Edit' }));
  await userEvent.click(screen.getByRole('button', { name: 'Begin saving' }));
  await userEvent.click(screen.getByRole('button', { name: 'Close while saving' }));
  expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('main'));
});
