import React, { useEffect, useState } from 'react';
import { dismissNotice, getNotices, type Notice } from '../api/client';
import { Banner } from './Banner';
import { Button } from './Button';

/** One dismissible banner per active service notice. Text is rendered as text, never HTML. */
export const NoticeBanner: React.FC = () => {
  const [notices, setNotices] = useState<Notice[]>([]);

  useEffect(() => {
    let cancelled = false;
    getNotices()
      .then((list) => {
        if (!cancelled) setNotices(list);
      })
      .catch(() => {
        // Notices are advisory; a failed fetch must not disturb the page.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const dismiss = (id: string) => {
    setNotices((list) => list.filter((n) => n.id !== id));
    dismissNotice(id).catch(() => {});
  };

  if (notices.length === 0) return null;

  return (
    <div
      style={{
        maxWidth: '1200px',
        margin: 'var(--space-3) auto 0',
        padding: '0 var(--space-4)',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-2)',
      }}
    >
      {notices.map((n) => (
        <Banner key={n.id} variant={n.severity === 'error' ? 'danger' : 'warning'} data-testid={`notice-${n.id}`}>
          <span style={{ flex: 1 }}>{n.text}</span>
          <Button type="button" variant="secondary" onClick={() => dismiss(n.id)}>
            Dismiss
          </Button>
        </Banner>
      ))}
    </div>
  );
};
