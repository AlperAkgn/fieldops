import { Component, type ReactNode } from 'react';
export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? (
      <main className="boot">
        <div className="empty" role="alert">
          <h1>Something went wrong</h1>
          <p>Reload the workspace to recover. Your saved work is kept on the server.</p>
          <button className="btn primary" onClick={() => window.location.reload()}>
            Reload workspace
          </button>
        </div>
      </main>
    ) : (
      this.props.children
    );
  }
}
