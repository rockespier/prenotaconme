import { Clipboard } from '@angular/cdk/clipboard';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatStepperModule } from '@angular/material/stepper';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { SessionService } from '../../../core/session/session.service';
import { Notifier } from '../../../shared/api-errors';
import { BusinessImages } from '../../../shared/business-images/business-images';
import { PricePipe } from '../../../shared/price.pipe';
import { LocationDialog } from '../locations/location-dialog';
import { ServiceDialog, ServiceDialogData } from '../services/service-dialog';
import { StaffDialog, StaffDialogData } from '../staff/staff-dialog';
import { weeklyHours, workingHoursFromOpening } from './onboarding-hours';

type Location = Schemas['LocationDto'];
type Service = Schemas['ServiceDto'];
type Staff = Schemas['StaffDto'];

/**
 * F5-02: guided setup right after registering a business (and from the dashboard checklist). Each step uses the
 * same dialogs and endpoints as the backoffice pages, with shortcuts for the common case: the owner also works
 * there, and staff work during the opening hours.
 */
@Component({
  selector: 'app-onboarding-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatStepperModule, TranslocoPipe, BusinessImages, PricePipe],
  template: `
    <header class="page-header">
      <h1>{{ 'onboarding.title' | transloco }}</h1>
    </header>
    <p class="muted intro">{{ 'onboarding.intro' | transloco }}</p>

    @if (loaded()) {
      <mat-stepper orientation="vertical" [linear]="false" #stepper>
        <!-- 1. Location -->
        <mat-step [completed]="locationDone()">
          <ng-template matStepLabel>{{ 'onboarding.location.title' | transloco }}</ng-template>
          <p class="muted">{{ 'onboarding.location.hint' | transloco }}</p>
          @if (location(); as l) {
            <div class="card">
              <strong>{{ l.name }}</strong>
              @if (l.address || l.city) {
                <span>{{ l.address }}@if (l.address && l.city) {, }{{ l.city }}</span>
              } @else {
                <span class="warn"><mat-icon inline>info</mat-icon>{{ 'onboarding.location.noAddress' | transloco }}</span>
              }
              <span class="muted">{{ 'onboarding.location.days' | transloco: { count: openDays(l) } }}</span>
            </div>
            <div class="actions">
              <button mat-stroked-button (click)="editLocation(l)"><mat-icon>edit</mat-icon>{{ 'onboarding.location.edit' | transloco }}</button>
              <button mat-flat-button matStepperNext>{{ 'onboarding.next' | transloco }}</button>
            </div>
          }
        </mat-step>

        <!-- 2. Services -->
        <mat-step [completed]="activeServices().length > 0">
          <ng-template matStepLabel>{{ 'onboarding.services.title' | transloco }}</ng-template>
          <p class="muted">{{ 'onboarding.services.hint' | transloco }}</p>
          <ul class="list">
            @for (s of services(); track s.id) {
              <li [class.inactive]="!s.isActive">
                <span class="grow">
                  <strong>{{ s.name }}</strong>
                  <span class="muted">{{ s.durationMinutes }} min · {{ s.price | price }}</span>
                </span>
                <button mat-icon-button (click)="editService(s)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
              </li>
            }
          </ul>
          <div class="actions">
            <button mat-stroked-button (click)="editService(null)"><mat-icon>add</mat-icon>{{ 'onboarding.services.add' | transloco }}</button>
            <button mat-flat-button matStepperNext>{{ 'onboarding.next' | transloco }}</button>
          </div>
        </mat-step>

        <!-- 3. Staff -->
        <mat-step [completed]="activeStaff().length > 0">
          <ng-template matStepLabel>{{ 'onboarding.staff.title' | transloco }}</ng-template>
          <p class="muted">{{ 'onboarding.staff.hint' | transloco }}</p>
          <ul class="list">
            @for (s of staff(); track s.id) {
              <li [class.inactive]="!s.isActive">
                <span class="grow">
                  <strong>{{ s.displayName }}</strong>
                  <span class="muted">{{ s.email }}</span>
                </span>
                <button mat-icon-button (click)="editStaff(s)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
              </li>
            } @empty {
              <li class="muted">{{ 'onboarding.staff.empty' | transloco }}</li>
            }
          </ul>
          <div class="actions">
            @if (!meInStaff()) {
              <button mat-flat-button (click)="addMe()" [disabled]="busy()"><mat-icon>person_add</mat-icon>{{ 'onboarding.staff.addMe' | transloco }}</button>
            }
            <button mat-stroked-button (click)="editStaff(null)"><mat-icon>group_add</mat-icon>{{ 'onboarding.staff.add' | transloco }}</button>
            <button mat-button matStepperNext>{{ 'onboarding.next' | transloco }}</button>
          </div>
        </mat-step>

        <!-- 4. Working hours -->
        <mat-step [completed]="hoursDone()">
          <ng-template matStepLabel>{{ 'onboarding.hours.title' | transloco }}</ng-template>
          <p class="muted">{{ 'onboarding.hours.hint' | transloco }}</p>
          <ul class="list">
            @for (s of activeStaff(); track s.id) {
              <li>
                <span class="grow">
                  <strong>{{ s.displayName }}</strong>
                  @if (hours()[s.id]; as h) {
                    <span class="muted">{{ 'onboarding.hours.perWeek' | transloco: { count: h } }}</span>
                  } @else {
                    <span class="warn"><mat-icon inline>schedule</mat-icon>{{ 'onboarding.hours.none' | transloco }}</span>
                  }
                </span>
                <button mat-stroked-button (click)="useOpeningHours(s)" [disabled]="busy()">{{ 'onboarding.hours.useOpening' | transloco }}</button>
                <a mat-button [routerLink]="['/admin/staff', s.id]">{{ 'onboarding.hours.customize' | transloco }}</a>
              </li>
            } @empty {
              <li class="muted">{{ 'onboarding.hours.noStaff' | transloco }}</li>
            }
          </ul>
          <div class="actions">
            <button mat-flat-button matStepperNext>{{ 'onboarding.next' | transloco }}</button>
          </div>
        </mat-step>

        <!-- 5. Images (F5-08) -->
        <mat-step [optional]="true">
          <ng-template matStepLabel>{{ 'onboarding.images.title' | transloco }}</ng-template>
          <app-business-images />
          <div class="actions">
            <button mat-flat-button matStepperNext>{{ 'onboarding.next' | transloco }}</button>
          </div>
        </mat-step>

        <!-- 6. Public link -->
        <mat-step>
          <ng-template matStepLabel>{{ 'onboarding.link.title' | transloco }}</ng-template>
          @if (ready()) {
            <p>{{ 'onboarding.link.ready' | transloco }}</p>
          } @else {
            <p class="warn"><mat-icon inline>info</mat-icon>{{ 'onboarding.link.notReady' | transloco }}</p>
          }
          <code class="url">{{ publicUrl() }}</code>
          <div class="actions">
            <button mat-flat-button (click)="copy()"><mat-icon>content_copy</mat-icon>{{ 'dashboard.copy' | transloco }}</button>
            <a mat-stroked-button [href]="whatsAppUrl()" target="_blank" rel="noopener"><mat-icon>share</mat-icon>{{ 'onboarding.link.share' | transloco }}</a>
            <a mat-button [href]="publicUrl()" target="_blank" rel="noopener">{{ 'dashboard.open' | transloco }}</a>
          </div>
          <div class="actions finish">
            <a mat-flat-button routerLink="/admin">{{ 'onboarding.finish' | transloco }}</a>
          </div>
        </mat-step>
      </mat-stepper>
    }
  `,
  styles: `
    :host { display: block; max-width: 760px; }
    .intro { margin: calc(-1 * var(--space-3)) 0 var(--space-5); }
    .card {
      display: flex; flex-direction: column; gap: var(--space-1);
      padding: var(--space-3) var(--space-4); margin-bottom: var(--space-3);
      border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--radius-m);
    }
    .list { list-style: none; margin: 0 0 var(--space-3); padding: 0; border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--radius-m); }
    .list li { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; padding: var(--space-2) var(--space-3); }
    .list li + li { border-top: 1px solid var(--mat-sys-outline-variant); }
    .list li.inactive { opacity: 0.55; }
    .grow { flex: 1; min-width: 160px; display: flex; flex-direction: column; }
    .grow .muted, .card .muted { font: var(--mat-sys-body-small); }
    .warn { display: inline-flex; align-items: center; gap: var(--space-1); color: var(--mat-sys-error); font: var(--mat-sys-body-small); }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-3); }
    .finish { margin-top: var(--space-5); }
    .url { display: block; padding: var(--space-2) var(--space-3); border-radius: var(--radius-s); background: var(--mat-sys-surface-container); overflow-wrap: anywhere; }
  `,
})
export class OnboardingPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly clipboard = inject(Clipboard);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(SessionService);

  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly locations = signal<Location[]>([]);
  protected readonly services = signal<Service[]>([]);
  protected readonly staff = signal<Staff[]>([]);
  /** Weekly hours per staff id (0 = none). */
  protected readonly hours = signal<Record<string, number>>({});

  protected readonly location = computed(() => this.locations().find((l) => l.isActive) ?? this.locations()[0] ?? null);
  protected readonly activeServices = computed(() => this.services().filter((s) => s.isActive));
  protected readonly activeStaff = computed(() => this.staff().filter((s) => s.isActive));
  protected readonly locationDone = computed(() => (this.location()?.openingHours.length ?? 0) > 0);
  protected readonly hoursDone = computed(() => this.activeStaff().some((s) => (this.hours()[s.id] ?? 0) > 0));
  protected readonly ready = computed(
    () => this.locationDone() && this.activeServices().length > 0 && this.activeStaff().length > 0 && this.hoursDone(),
  );
  protected readonly meInStaff = computed(() => {
    const email = this.session.me()?.email?.toLowerCase();
    return !!email && this.staff().some((s) => s.email.toLowerCase() === email);
  });
  protected readonly publicUrl = computed(() => `${globalThis.location?.origin ?? ''}/${this.session.currentBusiness()?.slug ?? ''}`);
  protected readonly whatsAppUrl = computed(
    () => `https://wa.me/?text=${encodeURIComponent(this.transloco.translate('onboarding.link.message', { url: this.publicUrl() }))}`,
  );

  ngOnInit(): void {
    void this.load();
  }

  protected openDays(l: Location): number {
    return new Set(l.openingHours.map((h) => h.day)).size;
  }

  protected async editLocation(location: Location): Promise<void> {
    const saved = await firstValueFrom(this.dialog.open(LocationDialog, { data: location, width: '640px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  protected async editService(service: Service | null): Promise<void> {
    const data: ServiceDialogData = { service, locations: this.locations() };
    const saved = await firstValueFrom(this.dialog.open(ServiceDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  protected async editStaff(staff: Staff | null): Promise<void> {
    const data: StaffDialogData = { staff, locations: this.locations(), services: this.services() };
    const saved = await firstValueFrom(this.dialog.open(StaffDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  /** The owner usually cuts hair too: add them with every location and service (their e-mail links them at once). */
  protected async addMe(): Promise<void> {
    const me = this.session.me();
    if (!me?.email) return;
    this.busy.set(true);
    const { error } = await this.api.POST('/api/staff', {
      body: {
        displayName: me.displayName || me.email,
        email: me.email,
        isActive: true,
        locationIds: this.locations().filter((l) => l.isActive).map((l) => l.id),
        serviceIds: this.activeServices().map((s) => s.id),
      },
    });
    this.busy.set(false);
    if (error) this.notifier.error(error);
    await this.load();
  }

  /** Replaces the person's hours with the opening hours of their first location (whole hours). */
  protected async useOpeningHours(staff: Staff): Promise<void> {
    const location = this.locations().find((l) => staff.locationIds.includes(l.id) && l.isActive);
    if (!location) {
      this.notifier.error({ title: 'onboarding.noLocation' });
      return;
    }
    this.busy.set(true);
    const { error } = await this.api.PUT('/api/staff/{id}/working-hours', {
      params: { path: { id: staff.id } },
      body: workingHoursFromOpening(location.id, location.openingHours),
    });
    this.busy.set(false);
    if (error) this.notifier.error(error);
    else this.notifier.saved();
    await this.load();
  }

  protected copy(): void {
    this.clipboard.copy(this.publicUrl());
    this.snackBar.open(this.transloco.translate('dashboard.copied'), undefined, { duration: 2000 });
  }

  private async load(): Promise<void> {
    const [locations, services, staff] = await Promise.all([
      this.api.GET('/api/locations'),
      this.api.GET('/api/catalog/services'),
      this.api.GET('/api/staff'),
    ]);
    this.locations.set(locations.data ?? []);
    this.services.set(services.data ?? []);
    this.staff.set(staff.data ?? []);

    const active = (staff.data ?? []).filter((s) => s.isActive);
    const schedules = await Promise.all(active.map((s) => this.api.GET('/api/staff/{id}/schedule', { params: { path: { id: s.id } } })));
    this.hours.set(Object.fromEntries(active.map((s, i) => [s.id, weeklyHours(schedules[i].data?.workingHours ?? [])])));
    this.loaded.set(true);
  }
}
