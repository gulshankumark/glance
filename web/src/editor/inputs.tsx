import React, { useEffect, useRef, useState } from 'react';
import { isColor, parseNumber, rgbOf, withRgb } from './fields';
import type { Field } from './schema';
import styles from './Editor.module.css';

export type Aria = Pick<React.InputHTMLAttributes<HTMLInputElement>, 'id' | 'aria-describedby' | 'aria-invalid' | 'aria-label'>;

const numberText = (v: number | null | undefined) => (v === null || v === undefined ? '' : String(v));

/** A number input that commits on Enter or blur, clamped to the field's bounds; bad input reverts. */
export const NumberInput: React.FC<Aria & { field: Field; value: number | null | undefined; onCommit: (v: number | null) => void }> = ({
  field, value, onCommit, ...aria
}) => {
  const [text, setText] = useState(numberText(value));
  const focused = useRef(false);
  useEffect(() => {
    if (!focused.current) setText(numberText(value));
  }, [value]);
  const commit = () => {
    const v = parseNumber(field, text);
    if (v === undefined) {
      setText(numberText(value));
      return;
    }
    setText(numberText(v));
    if (v !== value) onCommit(v);
  };
  return (
    <input
      {...aria}
      type="number"
      className={styles.input}
      min={field.min}
      max={field.max}
      step={field.step}
      placeholder={field.min === undefined ? 'auto' : undefined}
      value={text}
      onFocus={() => (focused.current = true)}
      onBlur={() => {
        focused.current = false;
        commit();
      }}
      onKeyDown={(e) => e.key === 'Enter' && commit()}
      onChange={(e) => setText(e.target.value)}
    />
  );
};

/** A colour as text (#RRGGBB or #AARRGGBB, committed on Enter or blur) plus a picker for the RGB part. */
export const ColorInput: React.FC<Aria & { value: string; pickerLabel: string; onCommit: (v: string) => void }> = ({
  value, pickerLabel, onCommit, ...aria
}) => {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);
  const commit = () => {
    const v = text.trim().toUpperCase();
    if (isColor(v) && v !== value) onCommit(v);
    else setText(value);
  };
  return (
    <span className={styles.colorRow}>
      <input
        type="color"
        aria-label={pickerLabel}
        className={styles.swatch}
        value={rgbOf(value).toLowerCase()}
        onChange={(e) => onCommit(withRgb(value, e.target.value.toUpperCase()))}
      />
      <input
        {...aria}
        className={styles.input}
        value={text}
        spellCheck={false}
        onBlur={commit}
        onKeyDown={(e) => e.key === 'Enter' && commit()}
        onChange={(e) => setText(e.target.value)}
      />
    </span>
  );
};
