import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { Layers3, ShieldCheck, ArrowUpRight, CheckCircle2 } from 'lucide-react';
import { login, publicPost, register } from '../api/client';
import { canManage, useSession } from '../api/session';
import { Form } from '../components/Form';
import type { Field } from '../components/form-schema';
const configs: Record<string, { title: string; text: string; submit: string; fields: Field[] }> = {
  login: {
    title: 'Welcome back.',
    text: 'Sign in to keep your field operations moving.',
    submit: 'Sign in to workspace',
    fields: [
      { name: 'email', label: 'Email address', type: 'email', required: true, max: 254 },
      { name: 'password', label: 'Password', type: 'password', required: true },
    ],
  },
  register: {
    title: 'Make room for better work.',
    text: 'Create your organization. We’ll send a secure activation code to your email.',
    submit: 'Create workspace',
    fields: [
      { name: 'displayName', label: 'Your name', required: true, max: 100 },
      { name: 'organizationName', label: 'Organization name', required: true, max: 160 },
      { name: 'email', label: 'Work email', type: 'email', required: true, max: 254 },
      {
        name: 'timeZoneId',
        label: 'Organization time zone',
        required: true,
        help: 'IANA name, for example Europe/Istanbul or UTC.',
      },
    ],
  },
  activate: {
    title: 'Activate your account.',
    text: 'Use the user ID and token in your activation email, then choose a password.',
    submit: 'Activate account',
    fields: [
      { name: 'userId', label: 'User ID', required: true },
      { name: 'token', label: 'Activation token', type: 'textarea', required: true, max: 4000 },
      {
        name: 'password',
        label: 'New password',
        type: 'password',
        required: true,
        help: '12+ characters, uppercase, lowercase, number and symbol.',
      },
    ],
  },
  'forgot-password': {
    title: 'Let’s get you back in.',
    text: 'If your account is eligible, you’ll receive password reset instructions.',
    submit: 'Send reset instructions',
    fields: [{ name: 'email', label: 'Email address', type: 'email', required: true, max: 254 }],
  },
  'reset-password': {
    title: 'A fresh start.',
    text: 'Enter the details from your password reset email.',
    submit: 'Reset password',
    fields: [
      { name: 'userId', label: 'User ID', required: true },
      { name: 'token', label: 'Reset token', type: 'textarea', required: true, max: 4000 },
      {
        name: 'newPassword',
        label: 'New password',
        type: 'password',
        required: true,
        help: '12+ characters, uppercase, lowercase, number and symbol.',
      },
    ],
  },
};
export function AuthPage({ mode }: { mode: string }) {
  const session = useSession();
  const client = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const [activation] = useState(() => ({
    userId: params.get('userId') || '',
    token: params.get('token') || '',
  }));
  useEffect(() => {
    if (params.has('token') || params.has('userId')) setParams({}, { replace: true });
  }, [params, setParams]);
  const [error, setError] = useState<unknown>();
  const [done, setDone] = useState(false);
  const config = configs[mode]!;
  if (session && mode === 'login')
    return <Navigate to={canManage(session.role) ? '/overview' : '/work-orders'} replace />;
  async function submit(values: Record<string, string>) {
    setError(undefined);
    try {
      if (mode === 'login') {
        const signedIn = await login(values);
        client.clear();
        const from = (location.state as { from?: string } | null)?.from;
        navigate(
          from?.startsWith('/') && !from.startsWith('//') && from !== '/' && from !== '/login'
            ? from
            : canManage(signedIn.role)
              ? '/overview'
              : '/work-orders',
        );
      } else if (mode === 'register') {
        await register(values);
        setDone(true);
      } else {
        await publicPost(mode === 'activate' ? 'confirm-email' : mode, values);
        setDone(true);
      }
    } catch (failure) {
      setError(failure);
    }
  }
  return (
    <div className="auth-layout">
      <aside className="auth-story">
        <Link className="brand" to="/login">
          <span className="brand-mark">
            <Layers3 size={23} />
          </span>
          fieldops.
        </Link>
        <div className="auth-story-copy">
          <div className="eyebrow">CONNECTED FIELD OPERATIONS</div>
          <h1>
            Great service.
            <br />
            Everywhere
            <br />
            <span>work happens.</span>
          </h1>
          <p>One workspace for your people, your equipment, and every job in between.</p>
          <div className="auth-illustration">
            <div className="orbit one" />
            <div className="orbit two" />
            <div className="orbit three" />
            <span className="illustration-center">
              <Layers3 size={42} />
            </span>
            <span className="orbit-dot dot-one" />
            <span className="orbit-dot dot-two" />
            <div className="illustration-tag">
              <CheckCircle2 size={17} />
              Operations, connected.
            </div>
          </div>
        </div>
        <div className="auth-security">
          <ShieldCheck size={18} />
          Secure by design. Built around your team.
        </div>
      </aside>
      <main className="auth-main">
        <div className="auth-top">
          {mode === 'login' ? (
            <>
              New to FieldOps?{' '}
              <Link to="/register">
                Create a workspace <ArrowUpRight size={14} />
              </Link>
            </>
          ) : (
            <Link to="/login">Back to sign in</Link>
          )}
        </div>
        <div className="auth-card" key={mode}>
          <div className="eyebrow">YOUR WORK, IN SYNC</div>
          <h2>{config.title}</h2>
          <p>{config.text}</p>
          {done ? (
            <div className="success-box" role="status">
              <CheckCircle2 />
              <h3>{mode === 'register' ? 'Check your inbox' : 'Request completed'}</h3>
              <p>
                {mode === 'register'
                  ? 'Use the activation details in your email to finish setting up your account.'
                  : 'Follow the email instructions or return to sign in.'}
              </p>
              <Link className="btn primary" to={mode === 'register' ? '/activate' : '/login'}>
                {mode === 'register' ? 'Activate account' : 'Sign in'}
              </Link>
            </div>
          ) : (
            <Form
              fields={config.fields}
              initial={{
                timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
                userId: activation.userId,
                token: activation.token,
              }}
              onSubmit={submit}
              submitLabel={config.submit}
              error={error}
            />
          )}
          {mode === 'login' && (
            <div className="auth-links">
              <Link to="/forgot-password">Forgot password?</Link>
              <Link to="/activate">Activate account</Link>
            </div>
          )}
          <small className="auth-footnote">
            Protected sessions · Secure cookies · Organization isolation
          </small>
        </div>
      </main>
    </div>
  );
}
