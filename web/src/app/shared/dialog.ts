import { MatDialogConfig } from '@angular/material/dialog';

// docs/DESIGN.md "Dialogs": full screen on phones (the global .full-screen-dialog panel class),
// otherwise a fixed width that never runs off a small screen.
export function dialogSize(phone: boolean, width = '640px'): MatDialogConfig {
  return phone
    ? { width: '100vw', maxWidth: '100vw', height: '100dvh', panelClass: 'full-screen-dialog' }
    : { width, maxWidth: 'calc(100vw - 32px)' };
}
