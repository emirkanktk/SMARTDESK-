import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Role } from '../constants/roles';
import {
  AccountQuery,
  NewAccountRequest,
  NewTeamMemberRequest,
  UserAccount,
} from '../models/account.model';

/**
 * Account management (ADR-0020): every account for Admins (`/api/users`), and a Manager's own
 * team (`/api/team`). The API enforces who may do what; these only call it.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly usersUrl = `${environment.apiUrl}/users`;
  private readonly teamUrl = `${environment.apiUrl}/team`;

  // --- Admin: every account ---

  getAccounts(query: AccountQuery = {}): Observable<UserAccount[]> {
    let params = new HttpParams();
    if (query.status) params = params.set('status', query.status);
    if (query.role) params = params.set('role', query.role);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    return this.http.get<UserAccount[]>(this.usersUrl, { params });
  }

  createAccount(request: NewAccountRequest): Observable<UserAccount> {
    return this.http.post<UserAccount>(this.usersUrl, request);
  }

  /** `managerId` puts an Employee into that team on the way. */
  approve(id: string, managerId: string | null = null): Observable<UserAccount> {
    return this.http.post<UserAccount>(
      `${this.usersUrl}/${id}/approve`,
      managerId ? { managerId } : null,
    );
  }

  reject(id: string): Observable<void> {
    return this.http.post<void>(`${this.usersUrl}/${id}/reject`, null);
  }

  changeRole(id: string, role: Role): Observable<UserAccount> {
    return this.http.patch<UserAccount>(`${this.usersUrl}/${id}/role`, { role });
  }

  assignManager(id: string, managerId: string | null): Observable<UserAccount> {
    return this.http.patch<UserAccount>(`${this.usersUrl}/${id}/manager`, { managerId });
  }

  setEnabled(id: string, enabled: boolean): Observable<UserAccount> {
    return this.http.post<UserAccount>(
      `${this.usersUrl}/${id}/${enabled ? 'enable' : 'disable'}`,
      null,
    );
  }

  /** A temporary password the owner must replace at their next sign-in; their sessions end. */
  setTemporaryPassword(id: string, password: string): Observable<UserAccount> {
    return this.http.post<UserAccount>(`${this.usersUrl}/${id}/password`, { password });
  }

  // --- Manager: their own team ---

  getTeam(): Observable<UserAccount[]> {
    return this.http.get<UserAccount[]>(this.teamUrl);
  }

  addTeamMember(request: NewTeamMemberRequest): Observable<UserAccount> {
    return this.http.post<UserAccount>(`${this.teamUrl}/members`, request);
  }

  removeTeamMember(id: string): Observable<void> {
    return this.http.delete<void>(`${this.teamUrl}/members/${id}`);
  }

  setTeamMemberEnabled(id: string, enabled: boolean): Observable<UserAccount> {
    return this.http.post<UserAccount>(
      `${this.teamUrl}/members/${id}/${enabled ? 'enable' : 'disable'}`,
      null,
    );
  }

  setTeamMemberTemporaryPassword(id: string, password: string): Observable<UserAccount> {
    return this.http.post<UserAccount>(`${this.teamUrl}/members/${id}/password`, { password });
  }

  /** A request to join the caller's team, from someone who signed up with their email. */
  approveRequest(id: string): Observable<UserAccount> {
    return this.http.post<UserAccount>(`${this.teamUrl}/requests/${id}/approve`, null);
  }

  rejectRequest(id: string): Observable<void> {
    return this.http.post<void>(`${this.teamUrl}/requests/${id}/reject`, null);
  }
}
