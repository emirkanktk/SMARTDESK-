import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ResourceBlockService } from './resource-block.service';

describe('ResourceBlockService', () => {
  let service: ResourceBlockService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/resource-blocks`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ResourceBlockService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the blocks overlapping a range', () => {
    service.getBlocks({ from: '2026-10-05T00:00:00Z', to: '2026-11-02T00:00:00Z' }).subscribe();

    const req = http.expectOne((r) => r.url === url);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-10-05T00:00:00Z');
    expect(req.request.params.get('to')).toBe('2026-11-02T00:00:00Z');
    expect(req.request.params.has('resourceId')).toBe(false);
    req.flush([]);
  });

  it('adds the resource filter only when one is chosen', () => {
    service
      .getBlocks({ from: '2026-10-05T00:00:00Z', to: '2026-11-02T00:00:00Z', resourceId: 'res-1' })
      .subscribe();

    const req = http.expectOne((r) => r.url === url);
    expect(req.request.params.get('resourceId')).toBe('res-1');
    req.flush([]);
  });

  it('creates a block', () => {
    const body = {
      resourceId: 'res-1',
      startDateTime: '2026-10-05T10:00:00.000Z',
      endDateTime: '2026-10-05T14:00:00.000Z',
      reason: 'Projector replacement',
    };
    service.create(body).subscribe();

    const req = http.expectOne(url);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ id: 'blk-1', ...body });
  });

  it('deletes a block by id', () => {
    service.delete('blk/1').subscribe();

    const req = http.expectOne(`${url}/blk%2F1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
