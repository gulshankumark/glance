import React from 'react';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  title?: string;
}

export const Card: React.FC<CardProps> = ({ title, children, style, ...props }) => {
  return (
    <div
      style={{
        backgroundColor: 'var(--surface)',
        border: '1px solid var(--surface-border)',
        borderRadius: 'var(--radius)',
        padding: 'var(--space-4)',
        marginBottom: 'var(--space-4)',
        ...style,
      }}
      {...props}
    >
      {title && (
        <h2 style={{ marginBottom: 'var(--space-3)', fontSize: '1.25rem', fontWeight: 600 }}>
          {title}
        </h2>
      )}
      {children}
    </div>
  );
};
