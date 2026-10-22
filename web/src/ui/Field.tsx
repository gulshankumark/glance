import React, { useId } from 'react';

export interface FieldProps {
  label: string;
  error?: string;
  /** A single form control; it receives the id the label points at and aria-describedby for the hint or error. */
  children: React.ReactElement;
  hint?: string;
}

export const Field: React.FC<FieldProps> = ({ label, error, children, hint }) => {
  const baseId = useId();
  const controlId = (children.props as { id?: string }).id ?? `${baseId}-control`;
  const hintId = `${baseId}-hint`;
  const errorId = `${baseId}-error`;
  const describedBy = error ? errorId : hint ? hintId : undefined;

  const control = React.cloneElement(children as React.ReactElement<Record<string, unknown>>, {
    id: controlId,
    'aria-describedby': describedBy,
    'aria-invalid': error ? true : undefined,
  });

  return (
    <div style={{ marginBottom: 'var(--space-3)' }}>
      <label
        htmlFor={controlId}
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
      {control}
      {hint && !error && (
        <p id={hintId} style={{ marginTop: 'var(--space-1)', fontSize: '0.8rem', color: 'var(--muted)' }}>
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} role="alert" style={{ marginTop: 'var(--space-1)', fontSize: '0.8rem', color: 'var(--danger)' }}>
          {error}
        </p>
      )}
    </div>
  );
};
