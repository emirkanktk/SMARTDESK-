import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { Roles } from '../constants/roles';
import { AccountService } from './account.service';

describe('AccountService', () => {
  const api = environment.apiUrl;
  let service: AccountService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists accounts with only the filters that are set, the search trimmed', () => {
    service.getAccounts({ status: 'PendingApproval', role: null, search: '  kaya ' }).subscribe();

    const req = http.expectOne((r) => r.url === `${api}/users`);
    expect(req.request.params.get('status')).toBe('PendingApproval');
    expect(req.request.params.has('role')).toBe(false);
    expect(req.request.params.get('search')).toBe('kaya');
    req.flush([]);
  });

  it('sends each admin action to its endpoint', () => {
    service.approve('a-1').subscribe();
    service.approve('a-1', 'm-1').subscribe();
    service.reject('a-1').subscribe();
    service.changeRole('a-1', Roles.Manager).subscribe();
    service.assignManager('a-1', null).subscribe();
    service.setEnabled('a-1', false).subscribe();
    service.setEnabled('a-1', true).subscribe();
    service.setTemporaryPassword('a-1', 'Temp-Pass-2026').subscribe();

    const expected: [string, string, unknown][] = [
      ['POST', `${api}/users/a-1/approve`, null],
      ['POST', `${api}/users/a-1/approve`, { managerId: 'm-1' }],
      ['POST', `${api}/users/a-1/reject`, null],
      ['PATCH', `${api}/users/a-1/role`, { role: 'Manager' }],
      ['PATCH', `${api}/users/a-1/manager`, { managerId: null }],
      ['POST', `${api}/users/a-1/disable`, null],
      ['POST', `${api}/users/a-1/enable`, null],
      ['POST', `${api}/users/a-1/password`, { password: 'Temp-Pass-2026' }],
    ];
    const requests = http.match(() => true);
    expect(requests.map((r) => [r.request.method, r.request.url, r.request.body])).toEqual(
      expected,
    );
    requests.forEach((r) => r.flush(null));
  });

  it('creates an account with its temporary password', () => {
    const body = {
      firstName: 'Elif',
      lastName: 'Şahin',
      email: 'elif@company.example',
      role: Roles.Employee,
      password: 'Temp-Pass-2026',
      managerId: 'm-1',
    };

    service.createAccount(body).subscribe();

    const req = http.expectOne({ method: 'POST', url: `${api}/users` });
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('sends each team action to the team endpoints', () => {
    const member = {
      firstName: 'Elif',
      lastName: 'Şahin',
      email: 'elif@company.example',
      password: 'Temp-Pass-2026',
    };
    service.getTeam().subscribe();
    service.addTeamMember(member).subscribe();
    service.removeTeamMember('m-2').subscribe();
    service.setTeamMemberEnabled('m-2', false).subscribe();
    service.setTeamMemberEnabled('m-2', true).subscribe();
    service.setTeamMemberTemporaryPassword('m-2', 'Temp-Pass-2026').subscribe();
    service.approveRequest('r-1').subscribe();
    service.rejectRequest('r-1').subscribe();

    http.expectOne({ method: 'GET', url: `${api}/team` }).flush([]);
    expect(http.expectOne({ method: 'POST', url: `${api}/team/members` }).request.body).toEqual(
      member,
    );
    http.expectOne({ method: 'DELETE', url: `${api}/team/members/m-2` }).flush(null);
    http.expectOne({ method: 'POST', url: `${api}/team/members/m-2/disable` }).flush({});
    http.expectOne({ method: 'POST', url: `${api}/team/members/m-2/enable` }).flush({});
    expect(
      http.expectOne({ method: 'POST', url: `${api}/team/members/m-2/password` }).request.body,
    ).toEqual({ password: 'Temp-Pass-2026' });
    http.expectOne({ method: 'POST', url: `${api}/team/requests/r-1/approve` }).flush({});
    http.expectOne({ method: 'POST', url: `${api}/team/requests/r-1/reject` }).flush(null);
  });
});
