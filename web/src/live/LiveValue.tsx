import React from 'react';
import { useLiveStale } from './LiveProvider';

/** A value that comes from the live event stream: greyed out and aria-busy while it may be stale. */
export const LiveValue: React.FC<{ children: React.ReactNode; className?: string }> = ({ children, className }) => {
  const stale = useLiveStale();
  return (
    <span
      className={className}
      aria-busy={stale ? 'true' : undefined}
      style={stale ? { opacity: 0.45, transition: 'opacity 0.2s' } : { transition: 'opacity 0.2s' }}
    >
      {children}
    </span>
  );
};
