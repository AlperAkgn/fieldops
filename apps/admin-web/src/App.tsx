import { Activity, ArrowUpRight, Check, Database, Layers3, Radio, RefreshCw } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { fetchHealth, getApiOrigin } from './api/health';
import type { HealthEndpoint } from './api/health';
import { messages as t } from './i18n/en';

const apiBaseUrl = (() => {
  try { return getApiOrigin(import.meta.env.VITE_API_BASE_URL || 'https://localhost:5443'); }
  catch { return ''; }
})();

function ServiceCard({ endpoint, title, description }: { endpoint: HealthEndpoint; title: string; description: string }) {
  const query = useQuery({
    queryKey: ['health', apiBaseUrl, endpoint],
    queryFn: ({ signal }) => fetchHealth(apiBaseUrl, endpoint, signal),
  });
  const status = query.isPending ? t.checking : query.isError ? t.unavailable : query.data.status;
  const tone = query.isPending ? 'pending' : query.isError || query.data.status === 'Unhealthy' ? 'error'
    : query.data.status === 'Degraded' ? 'warning' : 'success';

  return (
    <article className="service-card">
      <div className="service-card-top">
        <div className="service-icon">{endpoint === 'live' ? <Radio size={22} /> : <Database size={22} />}</div>
        <span className={`status status-${tone}`} role="status"><span />{status}</span>
      </div>
      <h3>{title}</h3>
      <p>{description}</p>
      <div className="endpoint">GET /health/{endpoint}</div>
      {query.isError && <p className="error-message" role="alert">{apiBaseUrl ? t.errorHelp : t.configurationError}</p>}
    </article>
  );
}

export default function App() {
  const live = useQuery({ queryKey: ['health', apiBaseUrl, 'live'], queryFn: ({ signal }) => fetchHealth(apiBaseUrl, 'live', signal) });
  const ready = useQuery({ queryKey: ['health', apiBaseUrl, 'ready'], queryFn: ({ signal }) => fetchHealth(apiBaseUrl, 'ready', signal) });
  const checking = live.isFetching || ready.isFetching;

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="/" aria-label="FieldOps home"><span className="brand-mark"><Layers3 size={23} /></span><span>{t.productName}</span></a>
        <div className="sidebar-label">{t.productCategory}</div>
        <div className="nav-item"><Activity size={18} />{t.foundation}<span>00</span></div>
        <div className="sidebar-bottom"><span className="small-dot" />{t.workspace}</div>
      </aside>
      <div className="main-shell">
        <header className="topbar"><span>{t.platform} / <strong>{t.foundation}</strong></span><span className="environment-badge">{t.environment}</span></header>
        <main>
          <section className="intro">
            <div className="eyebrow"><span>{t.phase}</span>{t.phaseName}</div>
            <h1>{t.heading}</h1>
            <p>{t.description}</p>
            <div className="foundation-tag"><Check size={14} />{t.foundationTag}</div>
          </section>
          <section className="connectivity" aria-labelledby="service-title">
            <div className="section-heading"><div><h2 id="service-title">{t.serviceHeading}</h2><p>{t.serviceDescription}</p></div>
              <button className="refresh-button" disabled={checking} onClick={() => { void live.refetch(); void ready.refetch(); }}><RefreshCw size={15} className={checking ? 'spinning' : ''} />{checking ? t.refreshing : t.refresh}</button>
            </div>
            <div className="service-grid"><ServiceCard endpoint="live" title={t.liveTitle} description={t.liveDescription} /><ServiceCard endpoint="ready" title={t.readyTitle} description={t.readyDescription} /></div>
            <div className="api-address"><span>{t.endpointLabel}</span><code>{apiBaseUrl || t.invalidConfiguration}</code></div>
          </section>
          <section className="next-phase"><div className="next-icon"><ArrowUpRight size={22} /></div><div><div className="eyebrow">{t.nextLabel}</div><h2>{t.nextTitle}</h2><p>{t.nextDescription}</p></div><span className="planned">{t.planned}</span></section>
          <p className="scope-note">{t.scopeNote}</p>
        </main>
        <footer><span>{t.productName}</span>{t.footer}</footer>
      </div>
    </div>
  );
}
