import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { fakeObjectUrls } from '../../../../testing/object-urls';
import { ResourcePhoto } from './resource-photo';

describe('ResourcePhoto', () => {
  let fixture: ComponentFixture<ResourcePhoto>;
  let http: HttpTestingController;
  let urls: ReturnType<typeof fakeObjectUrls>;

  beforeEach(() => {
    urls = fakeObjectUrls();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ResourcePhoto);
  });

  afterEach(() => {
    http.verify();
    urls.restore();
  });

  async function show(inputs: {
    resourceId?: string | null;
    typeName: string;
    photoUpdatedAt?: string | null;
    alt?: string;
  }): Promise<void> {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
  }

  const image = () => (fixture.nativeElement as HTMLElement).querySelector('img');

  it("shows the type's picture for a resource without a photo, fetching nothing", async () => {
    await show({ resourceId: 'res-1', typeName: 'Desk', photoUpdatedAt: null });

    expect(image()!.getAttribute('src')).toBe('images/resource-types/desk.webp');
    expect(image()!.getAttribute('alt')).toBe('');
    http.expectNone(() => true);
  });

  it("shows the type's picture for a resource that isn't saved yet", async () => {
    await show({ resourceId: null, typeName: 'Camera', photoUpdatedAt: '2026-10-05T08:00:00Z' });

    expect(image()!.getAttribute('src')).toBe('images/resource-types/camera.webp');
    http.expectNone(() => true);
  });

  it('shows nothing while the photo loads, then the photo', async () => {
    await show({ resourceId: 'res-1', typeName: 'Desk', photoUpdatedAt: 'v1', alt: 'Desk 12' });
    expect(image()).toBeNull();

    http.expectOne((r) => r.url.endsWith('/resources/res-1/photo')).flush(new Blob(['png']));
    await fixture.whenStable();

    expect(image()!.getAttribute('src')).toBe('blob:test-1');
    expect(image()!.getAttribute('alt')).toBe('Desk 12');
  });

  it("falls back to the type's picture when the photo can't be loaded", async () => {
    await show({ resourceId: 'res-1', typeName: 'Projector', photoUpdatedAt: 'v1' });

    http
      .expectOne((r) => r.url.endsWith('/resources/res-1/photo'))
      .flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(image()!.getAttribute('src')).toBe('images/resource-types/projector.webp');
  });

  it('follows a new photo, and a photo taken down', async () => {
    await show({ resourceId: 'res-1', typeName: 'Desk', photoUpdatedAt: 'v1' });
    http.expectOne((r) => r.params.get('v') === 'v1').flush(new Blob(['old']));
    await fixture.whenStable();

    await show({ typeName: 'Desk', photoUpdatedAt: 'v2' });
    expect(image()).toBeNull();
    http.expectOne((r) => r.params.get('v') === 'v2').flush(new Blob(['new']));
    await fixture.whenStable();
    expect(image()!.getAttribute('src')).toBe('blob:test-2');

    await show({ typeName: 'Desk', photoUpdatedAt: null });
    expect(image()!.getAttribute('src')).toBe('images/resource-types/desk.webp');
  });
});
