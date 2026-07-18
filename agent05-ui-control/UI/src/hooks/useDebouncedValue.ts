import { useEffect, useRef, useState } from 'react';
import { debounce } from '../utils/debounce';

/**
 * Trailing-edge debounced mirror of `value`: rapid changes collapse into at most one
 * update per `delayMs`. The first value passes through immediately so the initial load
 * is not delayed; job switches also stay instant because consumers key on jobId directly.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  const firstRef = useRef(true);
  const setterRef = useRef(debounce((v: T) => setDebounced(v), delayMs));

  useEffect(() => {
    const setter = setterRef.current;
    if (firstRef.current) {
      firstRef.current = false;
      return;
    }
    setter(value);
    return () => setter.cancel();
  }, [value]);

  return debounced;
}
