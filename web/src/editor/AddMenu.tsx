import React, { useEffect, useId, useRef, useState } from 'react';
import type { Asset, Item, ItemType, Layout, SensorsResponse } from '../api/types';
import { Button } from '../ui/Button';
import { IMAGE_KINDS } from './assets';
import { ITEM_LABELS, sensorName } from './catalog';
import { centered, defaultItem } from './model';
import { AssetPicker, SensorPicker } from './pickers';
import styles from './Editor.module.css';

const GAUGES: ReadonlyArray<{ type: ItemType; label: string }> = (
  ['bar', 'segmentedBar', 'arcGauge', 'dial', 'historyGraph', 'sparkline'] as const
).map((type) => ({ type, label: ITEM_LABELS[type] }));

export interface AddMenuProps {
  layout: Pick<Layout, 'width' | 'height'>;
  catalog: SensorsResponse | null;
  assets: Asset[] | null;
  /** Adds the (fully bound) item to the layout and selects it. */
  onInsert: (item: Item) => void;
  /** Rendered inside the image picker, under the thumbnails (the import button). */
  imageImport?: React.ReactNode;
}

/** An image item at the asset's own size, shrunk to fit the layout. */
function imageItem(asset: Asset, layout: Pick<Layout, 'width' | 'height'>): Item {
  const scale = Math.min(1, layout.width / asset.width, layout.height / asset.height);
  const size = (v: number) => Math.max(1, Math.round(v * scale));
  return defaultItem('image', 0, 0, { assetId: asset.id, width: size(asset.width), height: size(asset.height) });
}

/**
 * "+ Text", "+ Clock", "+ Sensor", "+ Gauge ▸ …" and "+ Image". New items go in the middle of the layout. Items that
 * show a sensor or an image are never added unbound: a sensor or image picker comes first.
 */
export const AddMenu: React.FC<AddMenuProps> = ({ layout, catalog, assets, onInsert, imageImport }) => {
  const [gaugesOpen, setGaugesOpen] = useState(false);
  const [picking, setPicking] = useState<{ type: ItemType; label: string } | null>(null);

  const add = (item: Item) => onInsert(centered(item, layout));

  const menuId = useId();
  const anchorRef = useRef<HTMLSpanElement>(null);
  const menuRef = useRef<HTMLSpanElement>(null);
  const gaugeButtonRef = useRef<HTMLButtonElement>(null);
  const menuItems = () => Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);

  const closeGauges = () => {
    setGaugesOpen(false);
    gaugeButtonRef.current?.focus();
  };

  // An opened menu takes focus on its first item; a press outside closes it.
  useEffect(() => {
    if (!gaugesOpen) return;
    menuItems()[0]?.focus();
    const onPointerDown = (e: PointerEvent) => {
      if (!anchorRef.current?.contains(e.target as Node)) setGaugesOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [gaugesOpen]);

  const onMenuKeyDown = (e: React.KeyboardEvent) => {
    const items = menuItems();
    const at = items.indexOf(document.activeElement as HTMLElement);
    const go = (i: number) => items[(i + items.length) % items.length]?.focus();
    if (e.key === 'ArrowDown') go(at + 1);
    else if (e.key === 'ArrowUp') go(at - 1);
    else if (e.key === 'Home') go(0);
    else if (e.key === 'End') go(items.length - 1);
    else if (e.key === 'Escape') closeGauges();
    else if (e.key === 'Tab') {
      setGaugesOpen(false);
      return;
    } else return;
    e.preventDefault();
    e.stopPropagation();
  };

  return (
    <div className={styles.addMenu} data-testid="add-menu">
      <Button type="button" variant="secondary" onClick={() => add(defaultItem('text', 0, 0))}>
        + Text
      </Button>
      <Button type="button" variant="secondary" onClick={() => add(defaultItem('clock', 0, 0))}>
        + Clock
      </Button>
      <Button type="button" variant="secondary" onClick={() => setPicking({ type: 'sensor', label: 'sensor' })}>
        + Sensor
      </Button>
      <span ref={anchorRef} className={styles.menuAnchor}>
        <Button
          ref={gaugeButtonRef}
          type="button"
          variant="secondary"
          aria-haspopup="menu"
          aria-expanded={gaugesOpen}
          aria-controls={gaugesOpen ? menuId : undefined}
          onClick={() => setGaugesOpen((o) => !o)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
              e.preventDefault();
              setGaugesOpen(true);
            }
          }}
        >
          + Gauge ▸
        </Button>
        {gaugesOpen && (
          <span ref={menuRef} id={menuId} role="menu" aria-label="Gauge type" className={styles.menu} onKeyDown={onMenuKeyDown}>
            {GAUGES.map((g) => (
              <button
                key={g.type}
                type="button"
                role="menuitem"
                tabIndex={-1}
                className={styles.menuItem}
                onClick={() => {
                  // Focus goes back to the button first, so the picker returns it there when it closes.
                  closeGauges();
                  setPicking({ type: g.type, label: g.label.toLowerCase() });
                }}
              >
                {g.label}
              </button>
            ))}
          </span>
        )}
      </span>
      <Button type="button" variant="secondary" onClick={() => setPicking({ type: 'image', label: 'image' })}>
        + Image
      </Button>

      {picking && picking.type === 'image' && (
        <AssetPicker
          title="Choose an image"
          assets={assets}
          kinds={IMAGE_KINDS}
          onClose={() => setPicking(null)}
          onPick={(asset) => {
            setPicking(null);
            add(imageItem(asset, layout));
          }}
        >
          {imageImport}
        </AssetPicker>
      )}
      {picking && picking.type !== 'image' && (
        <SensorPicker
          title={`Sensor for the new ${picking.label}`}
          catalog={catalog}
          onClose={() => setPicking(null)}
          onPick={(ref) => {
            const type = picking.type;
            setPicking(null);
            const extra = type === 'sensor' ? { sensor: ref, label: sensorName(ref, catalog) } : { sensor: ref };
            add(defaultItem(type, 0, 0, extra));
          }}
        />
      )}
    </div>
  );
};
