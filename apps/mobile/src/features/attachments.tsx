import { useEffect, useRef, useState } from 'react';
import { Image, Text } from 'react-native';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import * as ImagePicker from 'expo-image-picker';
import * as DocumentPicker from 'expo-document-picker';
import { Directory, File, Paths } from 'expo-file-system';
import { isAvailableAsync, shareAsync } from 'expo-sharing';
import { sameOfflineScope, type MobileSession, type OfflineIdentity } from '../auth/session';
import { Button, Card, ErrorBox, styles } from '../components/ui';
import { useOffline } from '../offline/runtime';
import { useApi } from './queries';
import { message } from './orders';

type Attachment = { id: string; fileName: string; contentType: string; size: number };
type Selection = { uri: string; name: string; type: string; operation: string };
const extensions = { 'image/jpeg': 'jpg', 'image/png': 'png', 'application/pdf': 'pdf' } as const;
const allowed = Object.keys(extensions);
const maxBytes = 8 * 1024 * 1024;
let viewCache: Directory | undefined;

function attachmentType(type: string): type is keyof typeof extensions {
  return Object.hasOwn(extensions, type);
}

function newPreviewFile(type: keyof typeof extensions) {
  if (!viewCache) {
    const directory = new Directory(Paths.cache, 'fieldops-attachment-views');
    if (directory.exists) directory.delete();
    viewCache = directory;
  }
  return new File(viewCache, `${randomUUID()}.${extensions[type]}`);
}

function discardTemporary(uri: string) {
  if (!uri.startsWith(`${Paths.cache.uri.replace(/\/$/, '')}/`)) return;
  try {
    const file = new File(uri);
    if (file.exists) file.delete();
  } catch {
    /* App cache can already be evicted. */
  }
}

async function saveDownload(
  response: Response,
  file: File,
  attachment: Attachment,
  signal: AbortSignal,
) {
  const body = response.body;
  if (!body || response.headers.get('content-type') !== attachment.contentType) {
    void body?.cancel().catch(() => undefined);
    throw new Error('The downloaded file type did not match the attachment.');
  }
  const reader = body.getReader();
  let received = 0;
  let finished = false;
  try {
    if (signal.aborted) throw new Error('Download cancelled.');
    file.create({ intermediates: true });
    const handle = file.open();
    try {
      while (true) {
        const chunk = await reader.read();
        if (signal.aborted) throw new Error('Download cancelled.');
        if (chunk.done) break;
        received += chunk.value.byteLength;
        if (received > attachment.size)
          throw new Error('The download exceeded the attachment size.');
        await handle.writeBytes(chunk.value);
      }
      if (received !== attachment.size)
        throw new Error('The attachment download was incomplete. Try again.');
      finished = true;
    } finally {
      handle.close();
    }
  } finally {
    if (!finished) void reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}

function StoredAttachments({
  files,
  path,
  session,
  owner,
}: {
  files: Attachment[];
  path: string;
  session: MobileSession;
  owner: OfflineIdentity;
}) {
  const [preview, setPreview] = useState<(Attachment & { uri: string }) | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState('');
  const request = useRef<AbortController | null>(null);
  const previewFile = useRef<string | null>(null);
  const mounted = useRef(true);
  const active = () => mounted.current && sameOfflineScope(session.snapshot().access, owner);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      request.current?.abort();
      if (previewFile.current) discardTemporary(previewFile.current);
      previewFile.current = null;
    };
  }, []);

  function closePreview() {
    if (previewFile.current) discardTemporary(previewFile.current);
    previewFile.current = null;
    setPreview(null);
  }

  async function view(attachment: Attachment) {
    if (!active() || request.current) return;
    setError('');
    if (
      !attachmentType(attachment.contentType) ||
      !Number.isSafeInteger(attachment.size) ||
      attachment.size < 1 ||
      attachment.size > maxBytes
    ) {
      setError(
        'This file cannot be opened. Attachments must be JPEG, PNG or PDF and at most 8 MiB.',
      );
      return;
    }
    if (!session.transport.origin.startsWith('https:')) {
      setError('Use a secure connection to open attachments.');
      return;
    }
    const controller = new AbortController();
    request.current = controller;
    setDownloading(true);
    closePreview();
    let file: File | undefined;
    let retained = false;
    try {
      const destination = newPreviewFile(attachment.contentType);
      file = destination;
      await session.request(
        `${path}/${encodeURIComponent(attachment.id)}`,
        { signal: controller.signal, expectedScope: owner },
        (response) => saveDownload(response, destination, attachment, controller.signal),
      );
      if (active() && !controller.signal.aborted) {
        previewFile.current = destination.uri;
        setPreview({ ...attachment, uri: destination.uri });
        retained = true;
      }
    } catch (failure) {
      if (active() && !controller.signal.aborted) setError(message(failure));
    } finally {
      if (file && !retained) discardTemporary(file.uri);
      request.current = null;
      if (active()) setDownloading(false);
    }
  }

  async function openPdf(uri: string) {
    setError('');
    try {
      if (!(await isAvailableAsync())) throw new Error('No system file viewer is available.');
      if (active() && previewFile.current === uri)
        await shareAsync(uri, { mimeType: 'application/pdf', UTI: 'com.adobe.pdf' });
    } catch (failure) {
      if (active()) setError(message(failure));
    }
  }

  return (
    <>
      {files.map((file) => (
        <Card key={file.id}>
          <Text style={styles.text}>
            {file.fileName} · {Math.max(1, Math.round(file.size / 1024))} KB
          </Text>
          <Button
            title={`View ${file.fileName}`}
            secondary
            disabled={downloading}
            onPress={() => void view(file)}
          />
        </Card>
      ))}
      {downloading && (
        <Text accessibilityLiveRegion="polite" style={styles.text}>
          Downloading attachment…
        </Text>
      )}
      {error ? <ErrorBox text={error} /> : null}
      {preview && (
        <>
          {preview.contentType === 'application/pdf' ? (
            <Button title="Open PDF in another app" onPress={() => void openPdf(preview.uri)} />
          ) : (
            <Image
              source={{ uri: preview.uri }}
              accessibilityLabel={`Preview of ${preview.fileName}`}
              style={{ height: 220, width: '100%' }}
              resizeMode="contain"
            />
          )}
          <Button title="Close attachment" secondary onPress={closePreview} />
        </>
      )}
    </>
  );
}

export function MobileAttachments({ id }: { id: string }) {
  const { session, state, scope } = useApi();
  const { offline } = useOffline();
  const client = useQueryClient();
  const key = [...scope, 'attachments', id];
  const path = `/work-orders/${encodeURIComponent(id)}/attachments`;
  const files = useQuery({
    queryKey: key,
    enabled: !offline && state.status === 'authenticated',
    queryFn: ({ signal }) => session.request<Attachment[]>(path, { signal }),
  });
  const [selected, setSelected] = useState<Selection | null>(null);
  const [error, setError] = useState('');
  const [progress, setProgress] = useState<number | null>(null);
  const request = useRef<AbortController | null>(null);
  const mounted = useRef(true);
  useEffect(
    () => () => {
      if (selected) discardTemporary(selected.uri);
    },
    [selected],
  );
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      request.current?.abort();
    };
  }, []);

  async function pick(source: 'camera' | 'gallery' | 'document') {
    setError('');
    let file: { uri: string; name: string; type: string | null | undefined } | undefined;
    try {
      if (source === 'document') {
        const result = await DocumentPicker.getDocumentAsync({
          type: allowed,
          copyToCacheDirectory: true,
        });
        const asset = result.assets?.[0];
        if (asset) file = { uri: asset.uri, name: asset.name, type: asset.mimeType };
      } else {
        if (source === 'camera' && !(await ImagePicker.requestCameraPermissionsAsync()).granted)
          throw new Error(
            'Camera permission is required to take a photo. You can choose an existing file instead.',
          );
        const open =
          source === 'camera' ? ImagePicker.launchCameraAsync : ImagePicker.launchImageLibraryAsync;
        const asset = (await open({ mediaTypes: ['images'], quality: 0.85, base64: false }))
          .assets?.[0];
        if (asset)
          file = {
            uri: asset.uri,
            name: asset.fileName ?? (asset.mimeType === 'image/png' ? 'photo.png' : 'photo.jpg'),
            type: asset.mimeType,
          };
      }
      if (!file) return;
      if (!mounted.current || session.snapshot().access?.sessionId !== state.access?.sessionId) {
        discardTemporary(file.uri);
        return;
      }
      if (!file.type || !allowed.includes(file.type))
        throw new Error('Choose a JPEG, PNG or PDF file.');
      const size = new File(file.uri).size;
      if (!size || size > maxBytes) throw new Error('Choose a file between 1 byte and 8 MiB.');
      setSelected({ ...file, type: file.type, operation: randomUUID() });
    } catch (failure) {
      if (file) discardTemporary(file.uri);
      if (mounted.current) setError(message(failure));
    }
  }

  async function upload() {
    const access = state.access;
    if (
      !selected ||
      offline ||
      !access ||
      request.current ||
      !session.transport.origin.startsWith('https:')
    )
      return;
    const controller = new AbortController();
    request.current = controller;
    setError('');
    setProgress(0);
    try {
      if (Date.parse(access.expiresAt) <= Date.now() + 30_000) await session.refresh();
      const current = session.snapshot().access;
      if (
        controller.signal.aborted ||
        current?.sessionId !== access.sessionId ||
        current.role !== 'Technician'
      )
        return;
      const result = await new File(selected.uri).upload(
        `${session.transport.origin}/api/v1${path}`,
        {
          headers: {
            Authorization: `Bearer ${current.accessToken}`,
            'Content-Type': selected.type,
            'X-File-Name': encodeURIComponent(selected.name),
            'Idempotency-Key': selected.operation,
          },
          mimeType: selected.type,
          sessionType: 'foreground',
          signal: controller.signal,
          onProgress: ({ bytesSent, totalBytes }) => {
            if (totalBytes > 0 && !controller.signal.aborted)
              setProgress(Math.round((bytesSent * 100) / totalBytes));
          },
        },
      );
      if (result.status !== 201) {
        if (result.status === 401 && session.snapshot().access?.sessionId === access.sessionId) {
          await session.request('/auth/me').catch(() => undefined);
        }
        let reason = `Upload failed (${result.status}). Retry the selected file.`;
        try {
          const problem = JSON.parse(result.body) as { detail?: string; title?: string };
          reason = problem.detail ?? problem.title ?? reason;
        } catch {
          /* Non-JSON response. */
        }
        throw new Error(reason);
      }
      if (!mounted.current || session.snapshot().access?.sessionId !== access.sessionId) return;
      setSelected(null);
      await client.invalidateQueries({ queryKey: key });
    } catch (failure) {
      if (!controller.signal.aborted) setError(message(failure));
    } finally {
      request.current = null;
      if (mounted.current) setProgress(null);
    }
  }

  return (
    <Card>
      <Text accessibilityRole="header" style={styles.label}>
        Attachments
      </Text>
      {offline ? (
        <Text style={styles.text}>Connect to upload or view attachments.</Text>
      ) : (
        <>
          {files.error && (
            <ErrorBox text={message(files.error)} retry={() => void files.refetch()} />
          )}
          {error && <ErrorBox text={error} />}
          {files.data?.length === 0 && <Text style={styles.text}>No attachments.</Text>}
          {!!files.data?.length && state.access && (
            <StoredAttachments
              key={JSON.stringify(scope)}
              files={files.data}
              path={path}
              session={session}
              owner={state.access}
            />
          )}
          <Button
            title="Take photo"
            secondary
            disabled={progress !== null}
            onPress={() => void pick('camera')}
          />
          <Button
            title="Choose photo"
            secondary
            disabled={progress !== null}
            onPress={() => void pick('gallery')}
          />
          <Button
            title="Choose document"
            secondary
            disabled={progress !== null}
            onPress={() => void pick('document')}
          />
          {selected && <Text style={styles.text}>{selected.name}</Text>}
          {selected?.type.startsWith('image/') && (
            <Image
              source={{ uri: selected.uri }}
              accessibilityLabel={`Preview of ${selected.name}`}
              style={{ height: 160, width: '100%' }}
              resizeMode="contain"
            />
          )}
          {selected && (
            <Button
              title={error ? 'Retry upload' : 'Upload attachment'}
              disabled={progress !== null}
              onPress={() => void upload()}
            />
          )}
          {progress !== null && (
            <Text accessibilityLiveRegion="polite" style={styles.text}>
              Upload {progress}%
            </Text>
          )}
        </>
      )}
    </Card>
  );
}
