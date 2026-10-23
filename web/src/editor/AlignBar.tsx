import React from 'react';
import { Button } from '../ui/Button';
import type { AlignMode } from './align';
import styles from './Editor.module.css';

export interface AlignBarProps {
  /** Number of selected items; distributing needs at least three. */
  count: number;
  onAlign: (mode: AlignMode) => void;
  onDistribute: (axis: 'x' | 'y') => void;
}

const ALIGN: ReadonlyArray<[AlignMode, string]> = [
  ['left', 'Align left'],
  ['centre', 'Align centre'],
  ['right', 'Align right'],
  ['top', 'Align top'],
  ['middle', 'Align middle'],
  ['bottom', 'Align bottom'],
];

/** Lines up or evenly spaces the selected items by what is drawn of them. */
export const AlignBar: React.FC<AlignBarProps> = ({ count, onAlign, onDistribute }) => (
  <div role="toolbar" aria-label="Align" className={styles.alignBar}>
    {ALIGN.map(([mode, name]) => (
      <Button key={mode} type="button" variant="secondary" onClick={() => onAlign(mode)}>
        {name}
      </Button>
    ))}
    <Button type="button" variant="secondary" disabled={count < 3} onClick={() => onDistribute('x')}>
      Distribute horizontally
    </Button>
    <Button type="button" variant="secondary" disabled={count < 3} onClick={() => onDistribute('y')}>
      Distribute vertically
    </Button>
  </div>
);
