import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule, TranslocoPipe],
  template: `
    <h1>{{ 'notFound.title' | transloco }}</h1>
    <a mat-flat-button routerLink="/">{{ 'notFound.back' | transloco }}</a>
  `,
})
export class NotFound {}
