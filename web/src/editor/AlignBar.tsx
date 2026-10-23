import React, { useRef, useState } from 'react';
import { Button } from '../ui/Button';
import type { AlignMode } from './align';
import styles from './Editor.module.css';

export interface AlignBarProps {
  /** Number of selected items; distributing needs at least three. */
  count: number;
  onAlign: (mode: AlignMode) => void;
  onDistribute: (axis: 'x' | 'y') => void;
}

/**
 * Lines up or evenly spaces the selected items by what is drawn of them. One Tab stop (roving tabindex): the last
 * focused button, or the first enabled one; ArrowLeft/ArrowRight move (wrapping), Home/End jump, disabled buttons
 * are skipped.
 */
export const AlignBar: React.FC<AlignBarProps> = ({ count, onAlign, onDistribute }) => {
  const actions: ReadonlyArray<{ name: string; disabled: boolean; run: () => void }> = [
    { name: 'Align left', disabled: false, run: () => onAlign('left') },
    { name: 'Align centre', disabled: false, run: () => onAlign('centre') },
    { name: 'Align right', disabled: false, run: () => onAlign('right') },
    { name: 'Align top', disabled: false, run: () => onAlign('top') },
    { name: 'Align middle', disabled: false, run: () => onAlign('middle') },
    { name: 'Align bottom', disabled: false, run: () => onAlign('bottom') },
    { name: 'Distribute horizontally', disabled: count < 3, run: () => onDistribute('x') },
    { name: 'Distribute vertically', disabled: count < 3, run: () => onDistribute('y') },
  ];
  const enabled = actions.flatMap((a, i) => (a.disabled ? [] : [i]));
  const [last, setLast] = useState(0);
  const current = enabled.includes(last) ? last : enabled[0];
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  const onKeyDown = (e: React.KeyboardEvent) => {
    const at = enabled.indexOf(current);
    const n = enabled.length;
    const to: Record<string, number> = { ArrowRight: (at + 1) % n, ArrowLeft: (at - 1 + n) % n, Home: 0, End: n - 1 };
    if (!(e.key in to)) return;
    e.preventDefault();
    const i = enabled[to[e.key]];
    setLast(i);
    refs.current[i]?.focus();
  };

  return (
    <div role="toolbar" aria-label="Align" className={styles.alignBar} onKeyDown={onKeyDown}>
      {actions.map((a, i) => (
        <Button
          key={a.name}
          ref={(el) => { refs.current[i] = el; }}
          type="button"
          variant="secondary"
          disabled={a.disabled}
          tabIndex={i === current ? 0 : -1}
          onFocus={() => setLast(i)}
          onClick={a.run}
        >
          {a.name}
        </Button>
      ))}
    </div>
  );
};
