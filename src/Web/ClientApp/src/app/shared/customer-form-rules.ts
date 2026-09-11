import { Validators } from '@angular/forms';

export const customerIdValidators = [Validators.required, Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9-]+$/)];
export const displayNameValidators = [Validators.required, Validators.maxLength(200), Validators.pattern(/\S/)];
