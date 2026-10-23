import React, { useEffect, useRef, useState } from 'react';
import { navBlocked } from './lib/navGuard';
import { LiveProvider, useLiveStatus } from './live/LiveProvider';
import { TemperatureUnitProvider } from './live/TemperatureUnit';
import { Banner } from './ui/Banner';
import { DisplayTab } from './tabs/DisplayTab';
import { LayoutsTab } from './tabs/LayoutsTab';
import { SensorsTab } from './tabs/SensorsTab';
import { DevicesTab } from './tabs/DevicesTab';
import { SettingsTab } from './tabs/SettingsTab';
import { LoginPage } from './login/LoginPage';
import './ui/tokens.css';

export function useHashRoute(): [string, (hash: string) => void] {
  const [route, setRoute] = useState(() => window.location.hash || '#display');
  const routeRef = useRef(route);
  routeRef.current = route;

  useEffect(() => {
    const onHashChange = () => {
      const next = window.location.hash || '#display';
      if (next !== routeRef.current && navBlocked(next)) {
        // Held back (unsaved work asks first): show the previous address again without a new history entry.
        window.history.replaceState(null, '', routeRef.current);
        return;
      }
      setRoute(next);
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const navigate = (hash: string) => {
    const formatted = hash.startsWith('#') ? hash : `#${hash}`;
    window.location.hash = formatted;
  };

  return [route, navigate];
}

const navLinks = [
  { hash: '#display', label: 'Display' },
  { hash: '#layouts', label: 'Layouts' },
  { hash: '#sensors', label: 'Sensors' },
  { hash: '#devices', label: 'Devices' },
  { hash: '#settings', label: 'Settings' },
];

interface ShellProps {
  baseRoute: string;
  navigate: (hash: string) => void;
}

const Shell: React.FC<ShellProps> = ({ baseRoute, navigate }) => {
  const liveStatus = useLiveStatus();

  let currentTab: React.ReactNode;
  if (baseRoute.startsWith('#layouts')) {
    currentTab = <LayoutsTab navigate={navigate} route={baseRoute} />;
  } else if (baseRoute === '#sensors') {
    currentTab = <SensorsTab navigate={navigate} />;
  } else if (baseRoute === '#devices') {
    currentTab = <DevicesTab navigate={navigate} />;
  } else if (baseRoute === '#settings') {
    currentTab = <SettingsTab navigate={navigate} />;
  } else {
    currentTab = <DisplayTab navigate={navigate} />;
  }

  const navRef = useRef<HTMLElement>(null);
  useEffect(() => {
    navRef.current?.querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [baseRoute]);

  const isCurrentTab = (hash: string) => {
    if (hash === '#display' && (baseRoute === '#display' || baseRoute === '')) return true;
    if (hash === '#layouts' && baseRoute.startsWith('#layouts')) return true;
    return baseRoute === hash;
  };

  return (
    <div style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column' }}>
      <header
        style={{
          borderBottom: '1px solid var(--surface-border)',
          backgroundColor: 'var(--surface)',
          padding: 'var(--space-3) var(--space-4)',
        }}
      >
        <div
          style={{
            maxWidth: '1200px',
            margin: '0 auto',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--space-4)',
            flexWrap: 'wrap',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
            <span style={{ fontWeight: 700, fontSize: '1.2rem', letterSpacing: '-0.5px' }}>
              Glance
            </span>
          </div>

          <nav ref={navRef} style={{ display: 'flex', gap: 'var(--space-1)', maxWidth: '100%', overflowX: 'auto' }}>
            {navLinks.map((link) => {
              const active = isCurrentTab(link.hash);
              return (
                <a
                  key={link.hash}
                  href={link.hash}
                  aria-current={active ? 'page' : undefined}
                  style={{
                    padding: 'var(--space-2) var(--space-3)',
                    borderRadius: 'var(--radius)',
                    color: active ? 'var(--accent)' : 'var(--muted)',
                    backgroundColor: active ? 'rgba(46, 134, 222, 0.1)' : 'transparent',
                    fontWeight: active ? 600 : 500,
                    textDecoration: 'none',
                  }}
                >
                  {link.label}
                </a>
              );
            })}
          </nav>
        </div>
      </header>

      {liveStatus !== 'open' && (
        <div style={{ maxWidth: '1200px', margin: 'var(--space-3) auto 0', padding: '0 var(--space-4)', width: '100%' }}>
          <Banner data-testid="reconnecting-banner" variant="warning">
            {liveStatus === 'connecting' ? 'Connecting…' : 'Reconnecting…'}
          </Banner>
        </div>
      )}

      <main
        style={{
          flex: 1,
          maxWidth: '1200px',
          width: '100%',
          margin: '0 auto',
          padding: 'var(--space-5) var(--space-4)',
        }}
      >
        {currentTab}
      </main>
    </div>
  );
};

export const App: React.FC = () => {
  const [route, navigate] = useHashRoute();
  const [baseRoute] = route.split('?');

  // The login page needs no live connection: the event stream would only fail until the user has logged in.
  if (baseRoute === '#login') {
    return <LoginPage navigate={navigate} />;
  }

  return (
    <LiveProvider>
      <TemperatureUnitProvider>
        <Shell baseRoute={baseRoute} navigate={navigate} />
      </TemperatureUnitProvider>
    </LiveProvider>
  );
};

export default App;
