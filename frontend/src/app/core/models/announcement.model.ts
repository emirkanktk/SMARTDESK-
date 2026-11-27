// Shapes of the announcements' calls - see docs/API_CONTRACTS.md, Announcements (ADR-0065).

/** How an announcement looks: news, good news, or a heads-up. */
export type AnnouncementTone = 'Info' | 'Good' | 'Warning';

export const ANNOUNCEMENT_TONES: readonly AnnouncementTone[] = ['Info', 'Good', 'Warning'];

/** As the API takes them (AnnouncementRules). */
export const ANNOUNCEMENT_TITLE_MAX = 100;
export const ANNOUNCEMENT_BODY_MAX = 1000;
/** The longest an announcement shows, from when it's posted. */
export const ANNOUNCEMENT_MAX_DAYS = 60;

/** An announcement, shown on everyone's dashboard from when it's posted until it ends. */
export interface Announcement {
  id: string;
  title: string;
  /** Its text, which may have several lines; none for a title alone. */
  body: string | null;
  tone: AnnouncementTone;
  createdAt: string;
  endsAt: string;
  authorId: string | null;
  /** Null when the author's account is gone. */
  authorName: string | null;
}

/** What an Admin posts. */
export interface NewAnnouncement {
  title: string;
  body: string | null;
  tone: AnnouncementTone;
  endsAt: string;
  /** Also to the phones with notifications on (ADR-0056), other than the author's. */
  notify: boolean;
}
