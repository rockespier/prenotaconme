import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-dashboard',
  imports: [TranslocoPipe],
  template: `<h1>{{ 'nav.dashboard' | transloco }}</h1>`,
})
export class Dashboard {}
