// Backdrop handlers for modals: close only when the press started on the backdrop itself.
// A drag that starts inside the modal (e.g. selecting text) and is released outside does not close it.
// The flag lives on the backdrop element so a re-render between press and release doesn't lose it.
export function overlayCloseProps(onClose) {
  return {
    onMouseDown: (e) => { e.currentTarget.dataset.pressedOnBackdrop = String(e.target === e.currentTarget); },
    onClick: (e) => {
      const pressedOnBackdrop = e.currentTarget.dataset.pressedOnBackdrop === 'true';
      e.currentTarget.dataset.pressedOnBackdrop = 'false';
      if (pressedOnBackdrop && e.target === e.currentTarget) onClose();
    }
  };
}
