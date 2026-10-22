// SmartDesk's service worker: Angular's own (ADR-0054), which keeps the app for use offline and
// shows the notifications (ADR-0056), and beside it the number on the installed app's icon
// (ADR-0058). Every notification says how many of its owner's messages were unread when it was
// sent, and the icon - on a phone's home screen, a computer's taskbar - shows that number, with the
// app closed too. While the app is open, AppBadgeService keeps it up to date instead.
importScripts('./ngsw-worker.js');

self.addEventListener('push', (event) => {
  const unread = unreadIn(event);
  const navigator = self.navigator;
  if (
    unread === null ||
    typeof navigator.setAppBadge !== 'function' ||
    typeof navigator.clearAppBadge !== 'function'
  ) {
    return;
  }

  // A browser that won't put a number on this app's icon - not installed, say - says so, and
  // nothing else changes: the notification shows all the same.
  const shown = unread > 0 ? navigator.setAppBadge(unread) : navigator.clearAppBadge();
  event.waitUntil(shown.catch(() => undefined));
});

// The unread count a notification brings, or null for one that brings none.
function unreadIn(event) {
  try {
    const badge = event.data ? event.data.json().badge : undefined;
    return Number.isInteger(badge) && badge >= 0 ? badge : null;
  } catch {
    return null;
  }
}
