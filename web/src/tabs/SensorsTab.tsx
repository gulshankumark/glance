import React, { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { SensorsResponse } from '../api/types';
import { Card } from '../ui/Card';

export const SensorsTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  const [data, setData] = useState<SensorsResponse | null>(null);

  useEffect(() => {
    api<SensorsResponse>('GET', '/api/sensors')
      .then(setData)
      .catch(() => {
        // Handled by api client if 401
      });
  }, []);

  return (
    <div>
      <Card title="Sensors">
        <p style={{ color: 'var(--muted)' }}>
          {data ? `${data.sensors.length} sensors loaded` : 'Sensors list will arrive here.'}
        </p>
      </Card>
    </div>
  );
};
