
import { Component, type ErrorInfo, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/index.css';

type RuntimeErrorBoundaryState = {
  hasError: boolean;
  message: string;
};

function postSmartHomeDebugLog(message: string, detail?: string) {
  try {
    window.chrome?.webview?.postMessage({
      type: 'smart-home.debugLog',
      payload: {
        scope: 'app-runtime',
        at: new Date().toISOString(),
        message: detail ? `${message} :: ${detail}` : message,
      },
    });
  } catch {
  }
}

class RuntimeErrorBoundary extends Component<{ children: ReactNode }, RuntimeErrorBoundaryState> {
  public constructor(props: { children: ReactNode }) {
    super(props);
    this.state = {
      hasError: false,
      message: '',
    };
  }

  public static getDerivedStateFromError(error: unknown): RuntimeErrorBoundaryState {
    return {
      hasError: true,
      message: error instanceof Error ? error.message : 'Unknown Smart Home UI error.',
    };
  }

  public componentDidCatch(error: unknown, info: ErrorInfo) {
    const message = error instanceof Error ? error.message : 'Unknown Smart Home UI error.';
    const detail = error instanceof Error
      ? `${error.stack ?? ''}\n${info.componentStack}`.trim()
      : info.componentStack;

    postSmartHomeDebugLog(message, detail);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div
          style={{
            minHeight: '100vh',
            background: 'linear-gradient(180deg, #050A12 0%, #09111A 100%)',
            color: '#D8F9FF',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '32px',
            fontFamily: 'Segoe UI, sans-serif',
          }}
        >
          <div
            style={{
              width: 'min(720px, 100%)',
              borderRadius: '28px',
              background: 'rgba(5, 10, 18, 0.92)',
              border: '1px solid rgba(255, 120, 120, 0.28)',
              boxShadow: '0 24px 60px rgba(0, 0, 0, 0.35)',
              padding: '28px',
            }}
          >
            <p style={{ margin: 0, fontSize: '11px', letterSpacing: '0.24em', textTransform: 'uppercase', color: '#FF9D9D' }}>
              Smart Home UI Error
            </p>
            <h1 style={{ margin: '12px 0 0', fontSize: '28px', fontWeight: 600, color: '#F5FBFF' }}>
              Atlas hit a Smart Home render failure.
            </h1>
            <p style={{ margin: '14px 0 0', fontSize: '14px', lineHeight: 1.7, color: 'rgba(216, 249, 255, 0.74)' }}>
              The Smart Home page failed during startup. Atlas logged the runtime error to the Smart Home debug channel instead of leaving this area blank.
            </p>
            <div
              style={{
                marginTop: '18px',
                borderRadius: '18px',
                background: 'rgba(255, 255, 255, 0.04)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
                padding: '14px 16px',
                fontSize: '13px',
                color: '#FFD7D7',
                wordBreak: 'break-word',
              }}
            >
              {this.state.message}
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

function RuntimeFailureCard({ message }: { message: string }) {
  return (
    <div
      style={{
        minHeight: '100vh',
        background: 'linear-gradient(180deg, #050A12 0%, #09111A 100%)',
        color: '#D8F9FF',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '32px',
        fontFamily: 'Segoe UI, sans-serif',
      }}
    >
      <div
        style={{
          width: 'min(720px, 100%)',
          borderRadius: '28px',
          background: 'rgba(5, 10, 18, 0.92)',
          border: '1px solid rgba(255, 120, 120, 0.28)',
          boxShadow: '0 24px 60px rgba(0, 0, 0, 0.35)',
          padding: '28px',
        }}
      >
        <p style={{ margin: 0, fontSize: '11px', letterSpacing: '0.24em', textTransform: 'uppercase', color: '#FF9D9D' }}>
          Smart Home UI Error
        </p>
        <h1 style={{ margin: '12px 0 0', fontSize: '28px', fontWeight: 600, color: '#F5FBFF' }}>
          Atlas hit a Smart Home startup failure.
        </h1>
        <p style={{ margin: '14px 0 0', fontSize: '14px', lineHeight: 1.7, color: 'rgba(216, 249, 255, 0.74)' }}>
          The Smart Home page failed before React could finish booting. Atlas logged the failure instead of leaving this area blank.
        </p>
        <div
          style={{
            marginTop: '18px',
            borderRadius: '18px',
            background: 'rgba(255, 255, 255, 0.04)',
            border: '1px solid rgba(255, 255, 255, 0.08)',
            padding: '14px 16px',
            fontSize: '13px',
            color: '#FFD7D7',
            wordBreak: 'break-word',
          }}
        >
          {message}
        </div>
      </div>
    </div>
  );
}

window.addEventListener('error', (event) => {
  postSmartHomeDebugLog('window.error', event.error instanceof Error ? `${event.error.message}\n${event.error.stack ?? ''}` : String(event.message ?? 'Unknown window error'));
});

window.addEventListener('unhandledrejection', (event) => {
  const reason = event.reason instanceof Error
    ? `${event.reason.message}\n${event.reason.stack ?? ''}`
    : String(event.reason ?? 'Unknown promise rejection');
  postSmartHomeDebugLog('window.unhandledrejection', reason);
});

const rootElement = document.getElementById('root');

if (!rootElement) {
  throw new Error('Smart Home root element was not found.');
}

const root = createRoot(rootElement);

async function bootstrap() {
  try {
    const module = await import('./app/App.tsx');
    const App = module.default;

    root.render(
      <RuntimeErrorBoundary>
        <App />
      </RuntimeErrorBoundary>,
    );
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown Smart Home startup error.';
    const detail = error instanceof Error ? `${error.message}\n${error.stack ?? ''}` : String(error);
    postSmartHomeDebugLog('bootstrap.import-failed', detail);
    root.render(<RuntimeFailureCard message={message} />);
  }
}

void bootstrap();
  