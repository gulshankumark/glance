import React from 'react';
import { Card } from '../ui/Card';

export const LayoutsTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  return (
    <div>
      <Card title="Layouts">
        <p style={{ color: 'var(--muted)' }}>Layout canvas and editor will arrive here.</p>
      </Card>
    </div>
  );
};
