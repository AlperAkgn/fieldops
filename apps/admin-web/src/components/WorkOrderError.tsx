import { ApiError } from '../api/client';
import { ErrorBox } from './ui';
export function WorkOrderError({ error, reload }: { error: unknown; reload: () => void }) {
  if (!error) return null;
  return (
    <div>
      <ErrorBox error={error} />
      {error instanceof ApiError && error.status === 409 && error.code.includes('version') && (
        <button type="button" className="btn secondary" onClick={reload}>
          Reload current work order
        </button>
      )}
    </div>
  );
}
