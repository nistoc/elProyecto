import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
} from 'react';
import {
  SIDEBAR_DEFAULT_WIDTH,
  SIDEBAR_KEY_STEP,
  SIDEBAR_MIN_WIDTH,
  SIDEBAR_WIDTH_KEY,
  clampSidebarWidth,
  parseStoredSidebarWidth,
  sidebarMaxWidth,
} from '../utils/sidebarWidth';

function viewportWidth(): number {
  return typeof window === 'undefined' ? 1920 : window.innerWidth;
}

function readStoredWidth(): number {
  let stored: number | null = null;
  try {
    stored = parseStoredSidebarWidth(localStorage.getItem(SIDEBAR_WIDTH_KEY));
  } catch {
    // A browser that refuses storage: the list keeps the usual width.
  }
  return clampSidebarWidth(stored ?? SIDEBAR_DEFAULT_WIDTH, viewportWidth());
}

export interface SidebarWidthState {
  width: number;
  minWidth: number;
  maxWidth: number;
  /** The edge is being dragged right now. */
  dragging: boolean;
  onPointerDown: (e: ReactPointerEvent<HTMLElement>) => void;
  onKeyDown: (e: ReactKeyboardEvent<HTMLElement>) => void;
  /** Back to the usual width. */
  reset: () => void;
}

/**
 * Width of the jobs list: dragged by its edge with the pointer, moved with the arrow keys, kept in the
 * browser between visits and pulled back inside the window when the window gets narrower.
 */
export function useSidebarWidth(): SidebarWidthState {
  const [width, setWidth] = useState(readStoredWidth);
  const [dragging, setDragging] = useState(false);
  const [maxWidth, setMaxWidth] = useState(() =>
    sidebarMaxWidth(viewportWidth())
  );
  const drag = useRef<{ startX: number; startWidth: number } | null>(null);

  useEffect(() => {
    const onResize = () => {
      setMaxWidth(sidebarMaxWidth(viewportWidth()));
      setWidth((w) => clampSidebarWidth(w, viewportWidth()));
    };
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  // Written when the drag ends, not on every pixel of it.
  useEffect(() => {
    if (dragging) return;
    try {
      localStorage.setItem(SIDEBAR_WIDTH_KEY, String(width));
    } catch {
      // Not kept: the next visit starts from the usual width.
    }
  }, [width, dragging]);

  const onPointerDown = useCallback(
    (e: ReactPointerEvent<HTMLElement>) => {
      if (e.button !== 0) return;
      e.preventDefault();
      const el = e.currentTarget;
      drag.current = { startX: e.clientX, startWidth: width };
      setDragging(true);
      try {
        el.setPointerCapture(e.pointerId);
      } catch {
        // No capture (an old browser): the drag still follows the pointer over the edge.
      }
      const move = (ev: PointerEvent) => {
        const d = drag.current;
        if (!d) return;
        setWidth(
          clampSidebarWidth(
            d.startWidth + ev.clientX - d.startX,
            viewportWidth()
          )
        );
      };
      const end = () => {
        drag.current = null;
        setDragging(false);
        el.removeEventListener('pointermove', move);
        el.removeEventListener('pointerup', end);
        el.removeEventListener('pointercancel', end);
        el.removeEventListener('lostpointercapture', end);
      };
      el.addEventListener('pointermove', move);
      el.addEventListener('pointerup', end);
      el.addEventListener('pointercancel', end);
      el.addEventListener('lostpointercapture', end);
    },
    [width]
  );

  const onKeyDown = useCallback((e: ReactKeyboardEvent<HTMLElement>) => {
    const step = (e.shiftKey ? 4 : 1) * SIDEBAR_KEY_STEP;
    let next: ((w: number) => number) | null = null;
    if (e.key === 'ArrowLeft') next = (w) => w - step;
    else if (e.key === 'ArrowRight') next = (w) => w + step;
    else if (e.key === 'Home') next = () => SIDEBAR_MIN_WIDTH;
    else if (e.key === 'End') next = () => sidebarMaxWidth(viewportWidth());
    if (!next) return;
    e.preventDefault();
    const change = next;
    setWidth((w) => clampSidebarWidth(change(w), viewportWidth()));
  }, []);

  const reset = useCallback(
    () => setWidth(clampSidebarWidth(SIDEBAR_DEFAULT_WIDTH, viewportWidth())),
    []
  );

  return {
    width,
    minWidth: SIDEBAR_MIN_WIDTH,
    maxWidth,
    dragging,
    onPointerDown,
    onKeyDown,
    reset,
  };
}
