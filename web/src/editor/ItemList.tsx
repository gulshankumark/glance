import React from 'react';
import type { Item, SensorsResponse } from '../api/types';
import { describeItem } from './catalog';
import styles from './Editor.module.css';

export interface ItemListProps {
  items: Item[];
  catalog: SensorsResponse | null;
  selectedIds: readonly string[];
  onSelect: (ids: string[]) => void;
  /** Removes these items (the whole selection when the focused item is part of a multi-selection). */
  onRemove: (ids: string[]) => void;
}

/**
 * The layout's items, topmost last (as drawn): the keyboard way to reach them. Enter or Space selects an item on the
 * canvas (where arrows then nudge it), Delete removes it (or the whole selection it is part of). Shift/Ctrl/Cmd+click
 * or Shift/Ctrl/Cmd+Space adds it to or takes it out of the selection.
 */
export const ItemList: React.FC<ItemListProps> = ({ items, catalog, selectedIds, onSelect, onRemove }) => {
  const toggle = (id: string) =>
    onSelect(selectedIds.includes(id) ? selectedIds.filter((i) => i !== id) : [...selectedIds, id]);
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
              aria-pressed={selectedIds.includes(item.id)}
              onClick={(e) => (e.shiftKey || e.ctrlKey || e.metaKey ? toggle(item.id) : onSelect([item.id]))}
              onKeyDown={(e) => {
                if (e.key === 'Delete' || e.key === 'Backspace') {
                  e.preventDefault();
                  const inGroup = selectedIds.length > 1 && selectedIds.includes(item.id);
                  onRemove(inGroup ? [...selectedIds] : [item.id]);
                } else if (e.key === ' ' && (e.shiftKey || e.ctrlKey || e.metaKey)) {
                  e.preventDefault(); // no click on key up
                  toggle(item.id);
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
