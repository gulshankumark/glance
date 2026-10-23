import React, { useState } from 'react';
import { api, ApiError } from '../api/client';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { returnHashFrom } from './returnHash';

export const LoginPage: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      await api('POST', '/api/auth/login', { password }, { skipAuthRedirect: true });
      navigate(returnHashFrom(window.location.hash));
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.errors.join(', ') || 'Login failed');
      } else {
        setError('Login failed');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        maxWidth: '400px',
        margin: 'var(--space-6) auto',
        padding: '0 var(--space-4)',
      }}
    >
      <Card title="Login">
        <form onSubmit={handleSubmit}>
          <Field label="Password" error={error || undefined}>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              autoFocus
              placeholder="Enter service password"
              style={{
                width: '100%',
                padding: '8px 12px',
                borderRadius: 'var(--radius)',
                border: '1px solid var(--surface-border)',
                backgroundColor: 'var(--bg)',
                color: 'var(--text)',
              }}
            />
          </Field>
          <Button type="submit" disabled={loading} style={{ width: '100%', marginTop: 'var(--space-2)' }}>
            {loading ? 'Logging in…' : 'Log in'}
          </Button>
        </form>
      </Card>
    </div>
  );
};
