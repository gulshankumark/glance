import React from 'react';

export interface DialogProps {
  open: boolean;
  title: string;
  children: React.ReactNode;
  onClose: () => void;
}

export const Dialog: React.FC<DialogProps> = ({ open, title, children, onClose }) => {
  if (!open) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
        padding: 'var(--space-4)',
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--surface)',
          border: '1px solid var(--surface-border)',
          borderRadius: 'var(--radius)',
          maxWidth: '480px',
          width: '100%',
          padding: 'var(--space-5)',
          boxShadow: '0 10px 25px rgba(0, 0, 0, 0.5)',
        }}
      >
        <h2 style={{ marginBottom: 'var(--space-4)', fontSize: '1.25rem' }}>{title}</h2>
        {children}
      </div>
    </div>
  );
};
