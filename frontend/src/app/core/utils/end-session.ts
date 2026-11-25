import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';

const SESSION_ENDED_MESSAGE = $localize`:@@error.sessionEnded:Your session has expired. Please sign in again.`;

/**
 * The server has said this tab's session is over - a refused request (errorInterceptor), or a
 * refused live connection (ReservationHubService), e.g. after a sign-out from another device
 * (ADR-0061): signs the user out and sends them to login. Safe to call for each of several
 * requests that fail together: the toast is de-duplicated by NotificationService, and only the
 * first call navigates.
 */
export function endSession(
  authService: AuthService,
  notifications: NotificationService,
  router: Router,
): void {
  authService.logout();
  notifications.info(SESSION_ENDED_MESSAGE);

  if (isOnOrGoingToLogin(router)) {
    return;
  }
  void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
}

function isOnOrGoingToLogin(router: Router): boolean {
  const navigation = router.currentNavigation();
  const target = navigation
    ? router.serializeUrl(navigation.finalUrl ?? navigation.extractedUrl)
    : router.url;
  return target === '/login' || target.startsWith('/login?');
}
