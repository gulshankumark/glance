import React, { useEffect, useId, useRef } from 'react';

export interface DialogProps {
  open: boolean;
  title: string;
  children: React.ReactNode;
  /** Called for Escape and a click on the backdrop: the same as the dialog's Cancel. */
  onClose: () => void;
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Open dialogs, innermost last: only the innermost one handles Escape and keeps focus. */
const openDialogs: object[] = [];

const focusables = (root: HTMLElement) =>
  Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.getClientRects().length > 0);

/**
 * A modal dialog: focus moves inside (to an autoFocus control, else the first control), Tab and Shift+Tab stay
 * inside, Escape closes, and focus goes back to the control that had it before the dialog opened.
 */
export const Dialog: React.FC<DialogProps> = ({ open, title, children, onClose }) => {
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  // The opener is read while rendering: an autoFocus control inside takes focus before any effect runs.
  const openerRef = useRef<Element | null>(null);
  if (open && openerRef.current === null) openerRef.current = document.activeElement;
  // The control focused first (kept so a re-run effect, as in StrictMode, focuses the same one).
  const initialRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) return;
    const panel = panelRef.current!;
    const opener = openerRef.current;
    const token = {};
    openDialogs.push(token);
    const active = document.activeElement;
    if (initialRef.current?.isConnected) initialRef.current.focus();
    else if (active instanceof HTMLElement && panel.contains(active)) initialRef.current = active;
    else {
      initialRef.current = focusables(panel)[0] ?? panel;
      initialRef.current.focus();
    }

    const onKeyDown = (e: KeyboardEvent) => {
      if (openDialogs[openDialogs.length - 1] !== token) return;
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        onCloseRef.current();
      } else if (e.key === 'Tab') {
        const list = focusables(panel);
        const focused = document.activeElement;
        if (list.length === 0) {
          e.preventDefault();
          panel.focus();
        } else if (e.shiftKey && (focused === list[0] || !panel.contains(focused))) {
          e.preventDefault();
          list[list.length - 1].focus();
        } else if (!e.shiftKey && (focused === list[list.length - 1] || !panel.contains(focused))) {
          e.preventDefault();
          list[0].focus();
        }
      }
    };
    document.addEventListener('keydown', onKeyDown, true);

    return () => {
      document.removeEventListener('keydown', onKeyDown, true);
      openDialogs.splice(openDialogs.indexOf(token), 1);
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus();
    };
  }, [open]);

  if (!open) {
    openerRef.current = null;
    initialRef.current = null;
    return null;
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
        padding: 'var(--space-4)',
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={panelRef}
        tabIndex={-1}
        style={{
          backgroundColor: 'var(--surface)',
          border: '1px solid var(--surface-border)',
          borderRadius: 'var(--radius)',
          maxWidth: '480px',
          width: '100%',
          maxHeight: '100%',
          overflowY: 'auto',
          padding: 'var(--space-5)',
          boxShadow: '0 10px 25px rgba(0, 0, 0, 0.5)',
        }}
      >
        <h2 id={titleId} style={{ marginBottom: 'var(--space-4)', fontSize: '1.25rem' }}>{title}</h2>
        {children}
      </div>
    </div>
  );
};
