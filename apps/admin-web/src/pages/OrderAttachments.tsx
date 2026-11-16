import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, binaryAuthorization, sessionStore } from '../api/client';
import { useSession } from '../api/session';
import { ErrorBox, Panel } from '../components/ui';

type Attachment = { id: string; fileName: string; contentType: string; size: number };

export function OrderAttachments({ orderId, execute }: { orderId: string; execute: boolean }) {
  const session = useSession();
  const scope = session?.sessionId;
  const client = useQueryClient();
  const key = [
    session?.tenantId,
    session?.userId,
    session?.role,
    scope,
    'work-orders',
    orderId,
    'attachments',
  ];
  const path = `/work-orders/${encodeURIComponent(orderId)}/attachments`;
  const files = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api<Attachment[]>(path, { signal }),
  });
  const [selected, setSelected] = useState<{ file: File; key: string } | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [preview, setPreview] = useState<{ url: string; name: string } | null>(null);
  const request = useRef<XMLHttpRequest | null>(null);
  const download = useRef<AbortController | null>(null);
  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
      download.current?.abort();
    },
    [],
  );
  useEffect(
    () => () => {
      if (preview) URL.revokeObjectURL(preview.url);
    },
    [preview],
  );

  async function upload() {
    if (!selected || !scope || request.current) return;
    const xhr = new XMLHttpRequest();
    request.current = xhr;
    setError(null);
    setProgress(0);
    try {
      const { file, key: operation } = selected;
      if (
        file.size > 8 * 1024 * 1024 ||
        !['image/png', 'image/jpeg', 'application/pdf'].includes(file.type)
      )
        throw new Error('Choose a JPEG, PNG or PDF file up to 8 MiB.');
      const { origin, token } = await binaryAuthorization(scope);
      if (request.current !== xhr || sessionStore.getSnapshot()?.sessionId !== scope) return;
      await new Promise<void>((resolve, reject) => {
        xhr.open('POST', `${origin}/api/v1${path}`);
        xhr.timeout = 30_000;
        xhr.setRequestHeader('Authorization', `Bearer ${token}`);
        xhr.setRequestHeader('Content-Type', file.type);
        xhr.setRequestHeader('X-File-Name', encodeURIComponent(file.name));
        xhr.setRequestHeader('Idempotency-Key', operation);
        xhr.upload.onprogress = (event) => {
          if (event.lengthComputable) setProgress(Math.round((event.loaded * 100) / event.total));
        };
        xhr.onload = () => {
          if (xhr.status === 201) resolve();
          else {
            const fallback = `Upload failed (${xhr.status}). Retry when the service is available.`;
            try {
              const problem = JSON.parse(xhr.responseText);
              reject(new Error(problem.detail || problem.title || fallback));
            } catch {
              reject(new Error(fallback));
            }
          }
        };
        xhr.onabort = () => reject(new DOMException('Upload cancelled.', 'AbortError'));
        xhr.onerror = xhr.ontimeout = () =>
          reject(new Error('Upload interrupted. Retry the selected file.'));
        xhr.send(file);
      });
      if (sessionStore.getSnapshot()?.sessionId !== scope) return;
      setSelected(null);
      await client.invalidateQueries({ queryKey: key });
    } catch (failure) {
      if (failure instanceof Error && failure.name !== 'AbortError') setError(failure);
    } finally {
      request.current = null;
      setProgress(null);
    }
  }

  async function open(file: Attachment, show: boolean) {
    if (!scope) return;
    download.current?.abort();
    const controller = new AbortController();
    download.current = controller;
    setError(null);
    try {
      const { origin, token } = await binaryAuthorization(scope);
      const response = await fetch(`${origin}/api/v1${path}/${encodeURIComponent(file.id)}`, {
        headers: { Authorization: `Bearer ${token}` },
        cache: 'no-store',
        signal: AbortSignal.any([controller.signal, AbortSignal.timeout(20_000)]),
      });
      if (!response.ok) throw new Error('This attachment could not be downloaded.');
      const blob = await response.blob();
      if (controller.signal.aborted || sessionStore.getSnapshot()?.sessionId !== scope) return;
      const url = URL.createObjectURL(blob);
      if (show) setPreview({ url, name: file.fileName });
      else {
        const link = document.createElement('a');
        link.href = url;
        link.download = file.fileName;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      }
    } catch (failure) {
      if (!controller.signal.aborted && failure instanceof Error) setError(failure);
    }
  }

  return (
    <Panel title="Attachments">
      {files.isPending && <p role="status">Loading attachments…</p>}
      {files.error && <ErrorBox error={files.error} retry={() => void files.refetch()} />}
      {error && <ErrorBox error={error} />}
      {files.data?.length === 0 && <p>No attachments.</p>}
      {files.data?.map((file) => (
        <div className="person-row" key={file.id}>
          <strong>{file.fileName}</strong>
          <button className="btn secondary" onClick={() => void open(file, false)}>
            Download
          </button>
          {file.contentType.startsWith('image/') && (
            <button className="btn secondary" onClick={() => void open(file, true)}>
              Preview
            </button>
          )}
        </div>
      ))}
      {preview && (
        <div>
          <img src={preview.url} alt={preview.name} />
          <button className="btn secondary" onClick={() => setPreview(null)}>
            Close preview
          </button>
        </div>
      )}
      {execute && (
        <div>
          <label>
            Choose attachment{' '}
            <input
              type="file"
              accept="image/jpeg,image/png,application/pdf"
              disabled={progress !== null}
              onChange={(event) => {
                const file = event.currentTarget.files?.[0];
                if (file) {
                  setSelected({ file, key: crypto.randomUUID() });
                  setError(null);
                }
                event.currentTarget.value = '';
              }}
            />
          </label>
          {selected && <p>{selected.file.name}</p>}
          <button
            className="btn secondary"
            disabled={!selected || progress !== null}
            onClick={() => void upload()}
          >
            {error && selected ? 'Retry upload' : 'Upload attachment'}
          </button>
          {progress !== null && (
            <progress value={progress} max={100} aria-label="Upload progress" />
          )}
        </div>
      )}
    </Panel>
  );
}
