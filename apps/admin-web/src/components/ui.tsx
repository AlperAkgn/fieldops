import {
  useEffect,
  useLayoutEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
  type RefObject,
} from 'react';
import { AlertCircle, ArrowLeft, ArrowRight, Check, Inbox, X } from 'lucide-react';
import { ToastContext } from './ui-state';
export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState('');
  useEffect(() => {
    if (message) {
      const timer = setTimeout(() => setMessage(''), 5000);
      return () => clearTimeout(timer);
    }
  }, [message]);
  return (
    <ToastContext.Provider value={setMessage}>
      {children}
      <div className="toast" role="status">
        {message && (
          <>
            <Check size={18} />
            {message}
            <button aria-label="Dismiss notification" onClick={() => setMessage('')}>
              <X size={16} />
            </button>
          </>
        )}
      </div>
    </ToastContext.Provider>
  );
}
export function Skeleton() {
  return (
    <div className="skeletons" aria-label="Loading" role="status">
      {[1, 2, 3].map((i) => (
        <div className="skeleton" key={i} />
      ))}
    </div>
  );
}
export function Empty({
  title = 'Nothing here yet',
  text = 'Create your first record to get started.',
  children,
}: {
  title?: string;
  text?: string;
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <Inbox size={30} />
      <h3>{title}</h3>
      <p>{text}</p>
      {children}
    </div>
  );
}
export function ErrorBox({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <div role="alert" className="error-box">
      <AlertCircle size={20} />
      <div>
        <strong>We couldn’t complete that request</strong>
        <p>{error instanceof Error ? error.message : 'Check your connection and try again.'}</p>
        {retry && (
          <button className="btn secondary" onClick={retry}>
            Try again
          </button>
        )}
      </div>
    </div>
  );
}
export function Badge({ children }: { children: ReactNode }) {
  return (
    <span className={`badge badge-${String(children).toLowerCase()}`}>
      {String(children).replace(/([a-z])([A-Z])/g, '$1 $2')}
    </span>
  );
}
export function PageHeader({
  eyebrow,
  title,
  description,
  children,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  children?: ReactNode;
}) {
  return (
    <header className="page-header">
      <div>
        {eyebrow && <div className="eyebrow">{eyebrow}</div>}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      <div className="header-actions">{children}</div>
    </header>
  );
}
export function Panel({
  title,
  children,
  action,
}: {
  title?: string | undefined;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <section className="panel">
      {title && (
        <div className="panel-heading">
          <h2>{title}</h2>
          {action}
        </div>
      )}
      {children}
    </section>
  );
}
export function Pagination({
  page,
  total,
  size = 20,
  onChange,
}: {
  page: number;
  total: number;
  size?: number;
  onChange: (page: number) => void;
}) {
  return (
    <nav aria-label="Pagination" className="pagination">
      <span>
        {total === 0
          ? '0 records'
          : `${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total}`}
      </span>
      <div>
        <button
          aria-label="Previous page"
          className="btn secondary icon"
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
        >
          <ArrowLeft size={16} />
        </button>
        <span>Page {page}</span>
        <button
          aria-label="Next page"
          className="btn secondary icon"
          disabled={page * size >= total}
          onClick={() => onChange(page + 1)}
        >
          <ArrowRight size={16} />
        </button>
      </div>
    </nav>
  );
}
export function Modal({
  title,
  children,
  onClose,
  returnFocusRef,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLElement | null>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useLayoutEffect(() => {
    const element = dialog.current;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const previousFocus =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    element?.showModal();
    return () => {
      element?.close();
      document.body.style.overflow = previousOverflow;
      if (returnFocusRef) returnFocusRef.current = previousFocus;
      if (previousFocus?.isConnected && previousFocus !== document.body) previousFocus.focus();
      // A connected trigger may still be disabled or inert during a mutation.
      if (
        !previousFocus ||
        previousFocus === document.body ||
        document.activeElement !== previousFocus
      )
        document.getElementById('main')?.focus();
    };
  }, [returnFocusRef]);
  return (
    <dialog
      ref={dialog}
      aria-labelledby={titleId}
      onKeyDown={(event) => {
        if (event.key !== 'Tab') return;
        const nodes = Array.from(
          event.currentTarget.querySelectorAll<HTMLElement>(
            'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
          ),
        ).filter((node) => node.getClientRects().length > 0);
        const first = nodes[0];
        const last = nodes.at(-1);
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first?.focus();
        }
      }}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        const bounds = event.currentTarget.getBoundingClientRect();
        if (
          event.target === event.currentTarget &&
          (event.clientX < bounds.left ||
            event.clientX > bounds.right ||
            event.clientY < bounds.top ||
            event.clientY > bounds.bottom)
        )
          onClose();
      }}
    >
      <div className="modal-heading">
        <h2 id={titleId}>{title}</h2>
        <button
          type="button"
          className="btn secondary icon"
          aria-label="Close dialog"
          onClick={onClose}
        >
          <X size={18} />
        </button>
      </div>
      {children}
    </dialog>
  );
}
export function OffsetPagination({
  page,
  shown,
  hasNext,
  onChange,
}: {
  page: number;
  shown: number;
  hasNext: boolean;
  onChange: (page: number) => void;
}) {
  return (
    <nav className="pagination" aria-label="Pagination">
      <span>
        Page {page} · {shown} records shown
      </span>
      <div>
        <button
          className="btn secondary icon"
          aria-label="Previous page"
          disabled={page <= 1}
          onClick={() => onChange(Math.max(1, page - 1))}
        >
          <ArrowLeft size={16} />
        </button>
        <button
          className="btn secondary icon"
          aria-label="Next page"
          disabled={!hasNext}
          onClick={() => onChange(page + 1)}
        >
          <ArrowRight size={16} />
        </button>
      </div>
    </nav>
  );
}
export function Confirm({
  title,
  text,
  pending,
  onConfirm,
  onClose,
  error,
}: {
  title: string;
  text: string;
  pending: boolean;
  error?: unknown;
  onConfirm: () => void;
  onClose: () => void;
}) {
  return (
    <Modal title={title} onClose={onClose}>
      <p>{text}</p>
      {!!error && <ErrorBox error={error} />}
      <div className="form-actions">
        <button className="btn secondary" onClick={onClose}>
          Keep record
        </button>
        <button className="btn danger" disabled={pending} onClick={onConfirm}>
          {pending ? 'Saving…' : 'Confirm'}
        </button>
      </div>
    </Modal>
  );
}
export function DataTable({ headings, children }: { headings: string[]; children: ReactNode }) {
  return (
    <div className="table-scroll">
      <table>
        <thead>
          <tr>
            {headings.map((heading) => (
              <th key={heading} scope="col">
                {heading}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}
