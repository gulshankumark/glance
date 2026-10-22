import React from 'react';
import { Card } from '../ui/Card';

export const DisplayTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  return (
    <div>
      <Card title="Display">
        <p style={{ color: 'var(--muted)' }}>Display controls and live preview will arrive here.</p>
      </Card>
    </div>
  );
};
