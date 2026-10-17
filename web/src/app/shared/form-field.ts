import { Provider } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';

// docs/DESIGN.md: outline inputs everywhere. Provided by each page with inputs, not in
// app.config.ts: importing the token there pulls all of form-field and @angular/forms into
// the initial bundle.
export const OUTLINE_FORM_FIELDS: Provider = {
  provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
  useValue: { appearance: 'outline' },
};
