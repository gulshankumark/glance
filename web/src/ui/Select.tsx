import React from 'react';

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  options?: Array<{ value: string; label: string }>;
}

export const Select: React.FC<SelectProps> = ({ options, children, style, ...props }) => {
  return (
    <select
      style={{
        width: '100%',
        padding: '8px 12px',
        backgroundColor: 'var(--surface)',
        color: 'var(--text)',
        border: '1px solid var(--surface-border)',
        borderRadius: 'var(--radius)',
        ...style,
      }}
      {...props}
    >
      {options
        ? options.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))
        : children}
    </select>
  );
};
