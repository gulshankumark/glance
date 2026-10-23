import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoginPage } from './LoginPage';

describe('LoginPage', () => {
  it('gives the password input an accessible name', () => {
    render(<LoginPage navigate={() => undefined} />);
    const input = screen.getByLabelText('Password');
    expect(input.getAttribute('type')).toBe('password');
  });
});
