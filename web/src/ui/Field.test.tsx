import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Field } from './Field';

describe('Field', () => {
  it('associates its label with the control', () => {
    render(
      <Field label="Name">
        <input />
      </Field>,
    );
    expect(screen.getByLabelText('Name').tagName).toBe('INPUT');
  });

  it('announces the error and links it to the control', () => {
    render(
      <Field label="Name" error="name is required">
        <input />
      </Field>,
    );
    const alert = screen.getByRole('alert');
    expect(alert.textContent).toBe('name is required');
    const input = screen.getByLabelText('Name');
    expect(input.getAttribute('aria-describedby')).toContain(alert.id);
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('links the hint while there is no error', () => {
    render(
      <Field label="Name" hint="shown on the screen">
        <input />
      </Field>,
    );
    const input = screen.getByLabelText('Name');
    const hintId = input.getAttribute('aria-describedby');
    expect(hintId).toBeTruthy();
    expect(document.getElementById(hintId!)?.textContent).toBe('shown on the screen');
  });
});
