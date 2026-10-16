import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Resource } from '../../../core/models/resource.model';
import { FakeFavorites, favoritesStub } from '../../../../testing/favorites-stub';
import { fakeResource } from '../../../../testing/resource-fixtures';
import { FavoriteToggle } from './favorite-toggle';

@Component({
  imports: [FavoriteToggle],
  template: `<div (click)="rowClicks = rowClicks + 1">
    <app-favorite-toggle [resource]="room()" />
  </div>`,
})
class Host {
  readonly room = signal<Resource>(fakeResource({ name: 'Board Room' }));
  rowClicks = 0;
}

describe('FavoriteToggle', () => {
  let favorites: FakeFavorites;
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    favorites = new FakeFavorites();
    TestBed.configureTestingModule({ providers: [favoritesStub(favorites)] });
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
  });

  const star = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;

  it("is a toggle named after the resource, pressed while it's a favourite", async () => {
    expect(favorites.load).toHaveBeenCalled();
    expect(star().getAttribute('aria-label')).toBe('Board Room in your favourites');
    expect(star().getAttribute('aria-pressed')).toBe('false');
    expect(star().title).toBe('Add it to your favourites');

    favorites.list.set([fixture.componentInstance.room()]);
    await fixture.whenStable();

    expect(star().getAttribute('aria-pressed')).toBe('true');
    expect(star().classList).toContain('on');
    expect(star().title).toBe('Take it off your favourites');
  });

  it('stars the resource, and only that, in a row that opens something itself', async () => {
    star().click();
    await fixture.whenStable();

    expect(favorites.toggle).toHaveBeenCalledWith(fixture.componentInstance.room());
    expect(fixture.componentInstance.rowClicks).toBe(0);
  });
});
