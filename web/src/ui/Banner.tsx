import React from 'react';

export interface BannerProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: 'warning' | 'danger' | 'info';
}

export const Banner: React.FC<BannerProps> = ({
  children,
  variant = 'warning',
  style,
  ...props
}) => {
  let bg = 'var(--warning-bg)';
  let color = 'var(--warning)';

  if (variant === 'danger') {
    bg = 'var(--danger-bg)';
    color = 'var(--danger)';
  } else if (variant === 'info') {
    bg = 'var(--surface)';
    color = 'var(--text)';
  }

  return (
    <div
      role="status"
      style={{
        backgroundColor: bg,
        color: color,
        border: '1px solid currentColor',
        borderRadius: 'var(--radius)',
        padding: 'var(--space-2) var(--space-4)',
        fontSize: '0.9rem',
        fontWeight: 500,
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--space-2)',
        ...style,
      }}
      {...props}
    >
      {children}
    </div>
  );
};
