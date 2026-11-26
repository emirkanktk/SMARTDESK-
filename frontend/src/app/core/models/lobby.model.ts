import { DoorBlock, DoorBooking } from './door-screen.model';
import { ResourceStatus } from './resource.model';

// Shapes of the lobby screen's calls - see docs/API_CONTRACTS.md, Lobby screens (ADR-0064).

/** What a lobby screen shows, asked with its own token: no user, and no names. */
export interface Lobby {
  screenName: string;
  /** The floor it shows; null for the whole office. */
  location: string | null;
  /** The server's clock, so the screen's own can be off without showing wrong states. */
  now: string;
  /** The app's address, for the QR code that opens it on a phone. */
  appUrl: string;
  /** Everyone in today, across the office (ADR-0063). */
  peopleToday: number;
  rooms: LobbyRoom[];
  desks: LobbyDesks[];
}

/** A meeting room with the rest of today, as its door screen shows it. */
export interface LobbyRoom {
  id: string;
  name: string;
  location: string;
  capacity: number;
  status: ResourceStatus;
  bookings: DoorBooking[];
  blocks: DoorBlock[];
}

/** A floor's desks in use, and how many of them are free now. */
export interface LobbyDesks {
  location: string;
  free: number;
  total: number;
}

/** A lobby screen as an Admin sees it. */
export interface LobbyScreen {
  id: string;
  name: string;
  location: string | null;
  createdAt: string;
  lastSeenAt: string | null;
}

/** A screen just set up, with the link to open on it: shown this once. */
export interface NewLobbyScreen {
  screen: LobbyScreen;
  link: string;
}

/** The header a screen sends its token in, so it stays out of addresses and logs. */
export const LOBBY_TOKEN_HEADER = 'X-Lobby-Token';
