import { environment } from '../../../environments/environment';

/** True for requests to our own API (and nothing else, e.g. not `/apifoo` or another host). */
export function isApiUrl(url: string): boolean {
  return url === environment.apiUrl || url.startsWith(`${environment.apiUrl}/`);
}
