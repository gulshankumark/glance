import React from 'react';

export interface FieldProps {
  label: string;
  error?: string;
  children: React.ReactNode;
  hint?: string;
}

export const Field: React.FC<FieldProps> = ({ label, error, children, hint }) => {
  return (
    <div style={{ marginBottom: 'var(--space-3)' }}>
      <label
        style={{
          display: 'block',
          marginBottom: 'var(--space-1)',
          fontSize: '0.875rem',
          fontWeight: 500,
          color: 'var(--text)',
        }}
      >
        {label}
      </label>
      {children}
      {hint && !error && (
        <p style={{ marginTop: 'var(--space-1)', fontSize: '0.8rem', color: 'var(--muted)' }}>
          {hint}
        </p>
      )}
      {error && (
        <p style={{ marginTop: 'var(--space-1)', fontSize: '0.8rem', color: 'var(--danger)' }}>
          {error}
        </p>
      )}
    </div>
  );
};
