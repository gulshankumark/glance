import React from 'react';
import { Card } from '../ui/Card';

export const DevicesTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  return (
    <div>
      <Card title="Devices">
        <p style={{ color: 'var(--muted)' }}>Connected display devices will arrive here.</p>
      </Card>
    </div>
  );
};
