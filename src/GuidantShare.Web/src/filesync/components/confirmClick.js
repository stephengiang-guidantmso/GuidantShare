import { useState, useRef, useEffect } from 'react';

// Two-click confirmation without a browser popup: the first click "arms" the button (show a
// "Click again to confirm" label while armed), a second click within a few seconds runs the action.
export function useConfirmClick(timeoutMs = 4000) {
  const [armed, setArmed] = useState(false);
  const timer = useRef(null);

  useEffect(() => () => clearTimeout(timer.current), []);

  const click = (action) => {
    clearTimeout(timer.current);
    if (armed) {
      setArmed(false);
      action();
      return;
    }
    setArmed(true);
    timer.current = setTimeout(() => setArmed(false), timeoutMs);
  };

  return [armed, click];
}
