import { TestBed } from '@angular/core/testing';
import { TenantSelection } from './tenant-selection';

describe('TenantSelection', () => {
  afterEach(() => localStorage.clear());

  it('persists the selected business', () => {
    TestBed.inject(TenantSelection).select('barberia-mario');

    expect(localStorage.getItem('tenant')).toBe('barberia-mario');
  });

  it('restores the selection on startup', () => {
    localStorage.setItem('tenant', 'salon-x');
    TestBed.resetTestingModule();

    expect(TestBed.inject(TenantSelection).slug()).toBe('salon-x');
  });

  it('clears the selection', () => {
    const selection = TestBed.inject(TenantSelection);
    selection.select('a-b-c');
    selection.select(null);

    expect(selection.slug()).toBeNull();
    expect(localStorage.getItem('tenant')).toBeNull();
  });
});
