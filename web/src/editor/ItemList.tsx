import React from 'react';
import type { Item, SensorsResponse } from '../api/types';
import { describeItem } from './catalog';
import styles from './Editor.module.css';

export interface ItemListProps {
  items: Item[];
  catalog: SensorsResponse | null;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onRemove: (id: string) => void;
}

/**
 * The layout's items, topmost last (as drawn): the keyboard way to reach them. Enter or Space selects an item on the
 * canvas (where arrows then nudge it), Delete removes it.
 */
export const ItemList: React.FC<ItemListProps> = ({ items, catalog, selectedId, onSelect, onRemove }) => {
  if (items.length === 0) return <p className={styles.muted}>No items yet — add one above or drag a sensor.</p>;
  return (
    <ul aria-label="Items" className={styles.leaves}>
      {items.map((item) => {
        const name = describeItem(item, catalog);
        return (
          <li key={item.id}>
            <button
              type="button"
              className={styles.leaf}
              aria-label={name}
              aria-pressed={item.id === selectedId}
              onClick={() => onSelect(item.id)}
              onKeyDown={(e) => {
                if (e.key === 'Delete' || e.key === 'Backspace') {
                  e.preventDefault();
                  onRemove(item.id);
                }
              }}
            >
              <span className={styles.leafName}>{name}</span>
            </button>
          </li>
        );
      })}
    </ul>
  );
};
