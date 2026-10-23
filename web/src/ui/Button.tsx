import React from 'react';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger';
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { children, variant = 'primary', style, disabled, ...props },
  ref,
) {
  const baseStyle: React.CSSProperties = {
    padding: '8px 16px',
    borderRadius: 'var(--radius)',
    border: '1px solid transparent',
    fontWeight: 500,
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.6 : 1,
    transition: 'background-color 0.15s, border-color 0.15s',
    ...style,
  };

  let variantStyle: React.CSSProperties = {
    backgroundColor: 'var(--accent)',
    color: 'var(--accent-text)',
  };

  if (variant === 'secondary') {
    variantStyle = {
      backgroundColor: 'var(--surface)',
      color: 'var(--text)',
      borderColor: 'var(--surface-border)',
    };
  } else if (variant === 'danger') {
    variantStyle = {
      backgroundColor: 'var(--danger)',
      color: '#ffffff',
    };
  }

  return (
    <button ref={ref} style={{ ...baseStyle, ...variantStyle }} disabled={disabled} {...props}>
      {children}
    </button>
  );
});
