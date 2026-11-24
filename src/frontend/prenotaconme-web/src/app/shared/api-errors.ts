import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';

interface ProblemLike {
  title?: string;
  errors?: Record<string, string[]>;
}

/** First error code of an API problem response: validation errors first, then a coded title (e.g. "email.taken"). */
export function firstErrorCode(error: unknown): string {
  const problem = (error ?? {}) as ProblemLike;
  const fromErrors = Object.values(problem.errors ?? {}).flat()[0];
  if (fromErrors) return fromErrors;
  if (problem.title && /^[a-zA-Z]+(\.[a-zA-Z]+)+$/.test(problem.title)) return problem.title;
  return 'generic';
}

/** Shows translated success/error messages for backoffice actions. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  saved(): void {
    this.snackBar.open(this.transloco.translate('common.saved'), undefined, { duration: 2500 });
  }

  error(error: unknown): void {
    const key = `errors.${firstErrorCode(error)}`;
    const text = this.transloco.translate(key);
    // Fall back to the generic message when the code has no translation yet.
    this.snackBar.open(text === key ? this.transloco.translate('errors.generic') : text, 'OK', { duration: 5000 });
  }
}
