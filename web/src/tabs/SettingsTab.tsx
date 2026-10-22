import React from 'react';
import { Card } from '../ui/Card';

export const SettingsTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  return (
    <div>
      <Card title="Settings">
        <p style={{ color: 'var(--muted)' }}>Service configuration and settings will arrive here.</p>
      </Card>
    </div>
  );
};
