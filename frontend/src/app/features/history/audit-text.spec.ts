import { AuditEntry } from '../../core/models/audit.model';
import {
  AuditContext,
  actionLabel,
  actorLabel,
  changeLines,
  entityLabel,
  subject,
} from './audit-text';

// Local wall-clock times as the UTC ISO strings the API keeps, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

const context: AuditContext = {
  people: { 'u-ayse': 'Ayşe Yılmaz', 'u-selin': 'Selin Kaya', 'u-mehmet': 'Mehmet Demir' },
  resources: { 'res-board': 'Board Room', 'res-desk': 'Desk 3-01' },
  types: { 'type-room': 'Meeting Room' },
  locale: 'en-US',
};

function entry(overrides: Partial<AuditEntry>): AuditEntry {
  return {
    id: 1,
    at: utc('2026-10-19T10:00'),
    actor: 'User',
    userId: 'u-ayse',
    entityType: 'Reservation',
    entityId: 'rsv-1',
    action: 'Created',
    resourceId: 'res-board',
    label: `${utc('2026-10-19T11:00')} ${utc('2026-10-19T12:00')}`,
    changes: [],
    ...overrides,
  };
}

describe('audit text', () => {
  it('names the kind, what was done, and who did it', () => {
    expect(entityLabel('Reservation')).toBe('Booking');
    expect(entityLabel('UserRole')).toBe('Role');
    expect(entityLabel('Unknown')).toBe('Unknown');
    expect(actionLabel('Deleted')).toBe('Removed');
    expect(actorLabel(entry({}), context.people)).toBe('Ayşe Yılmaz');
    expect(actorLabel(entry({ userId: 'u-gone' }), context.people)).toBe('Someone');
    expect(actorLabel(entry({ actor: 'Anonymous', userId: null }), context.people)).toBe(
      'Someone without an account',
    );
    expect(actorLabel(entry({ actor: 'System', userId: null }), context.people)).toBe(
      'SmartDesk, by itself',
    );
  });

  it("says which row it was: a booking's resource and times, a role and whose", () => {
    expect(subject(entry({}), context)).toBe('Board Room · Mon 19 Oct 2026, 11:00-12:00');
    expect(subject(entry({ entityType: 'Resource', label: 'Old name' }), context)).toBe(
      'Board Room',
    );
    expect(subject(entry({ entityType: 'IssueReport', label: 'AirConditioning' }), context)).toBe(
      'Board Room · Air conditioning',
    );
    expect(
      subject(
        entry({
          entityType: 'UserRole',
          resourceId: null,
          label: 'Manager',
          changes: [{ property: 'UserId', old: null, new: 'u-mehmet' }],
        }),
        context,
      ),
    ).toBe('Manager · Mehmet Demir');
    expect(
      subject(entry({ entityType: 'OfficeHours', resourceId: null, label: null }), context),
    ).toBe('');
  });

  it("names a passkey and whose it is, and doesn't repeat the person below (ADR-0057)", () => {
    const removed = entry({
      entityType: 'Passkey',
      entityId: 'AQID',
      action: 'Deleted',
      resourceId: null,
      label: 'iPhone',
      changes: [{ property: 'UserId', old: 'u-ayse', new: null }],
    });

    expect(entityLabel('Passkey')).toBe('Passkey');
    expect(subject(removed, context)).toBe('iPhone · Ayşe Yılmaz');
    expect(changeLines(removed, context)).toEqual([]);
  });

  it('says what changed in words, before and after', () => {
    const lines = changeLines(
      entry({
        action: 'Updated',
        changes: [
          { property: 'Status', old: 'Confirmed', new: 'Cancelled' },
          { property: 'StartDateTime', old: utc('2026-10-19T11:00'), new: utc('2026-10-19T13:30') },
          { property: 'CheckedInByUserId', old: null, new: 'u-selin' },
          { property: 'CheckInRequired', old: 'false', new: 'true' },
          { property: 'ResourceId', old: 'res-desk', new: 'res-board' },
          { property: 'SomethingNew', old: 'a', new: 'b' },
        ],
      }),
      context,
    );

    expect(lines).toEqual([
      { label: 'Status', text: 'Confirmed → Cancelled' },
      {
        label: 'Start',
        text: 'Mon 19 Oct 2026, 11:00 → Mon 19 Oct 2026, 13:30',
      },
      { label: 'Checked in by', text: 'Selin Kaya' },
      { label: 'Needs a check-in', text: 'No → Yes' },
      { label: 'Resource', text: 'Desk 3-01 → Board Room' },
      { label: 'SomethingNew', text: 'a → b' },
    ]);
  });

  it('says only that a secret was set, replaced, set up or turned off', () => {
    const resource = (changes: AuditEntry['changes']) =>
      changeLines(entry({ entityType: 'Resource', action: 'Updated', changes }), context);

    expect(resource([{ property: 'CheckInCode', old: '***', new: '***' }])).toEqual([
      { label: 'QR code', text: 'replaced' },
    ]);
    expect(resource([{ property: 'DoorScreenTokenHash', old: null, new: '***' }])).toEqual([
      { label: 'Door screen', text: 'set up' },
    ]);
    expect(resource([{ property: 'DoorScreenTokenHash', old: '***', new: null }])).toEqual([
      { label: 'Door screen', text: 'turned off' },
    ]);
    expect(
      changeLines(
        entry({
          entityType: 'User',
          action: 'Created',
          changes: [{ property: 'PasswordHash', old: null, new: '***' }],
        }),
        context,
      ),
    ).toEqual([{ label: 'Password', text: 'set' }]);
  });

  it('leaves out what the subject already says, and what nobody needs to read', () => {
    const created = changeLines(
      entry({
        changes: [
          { property: 'ResourceId', old: null, new: 'res-board' },
          { property: 'Note', old: null, new: 'Sprint planning' },
        ],
      }),
      context,
    );
    const photo = changeLines(
      entry({
        entityType: 'ResourcePhoto',
        action: 'Updated',
        changes: [
          { property: 'UpdatedAt', old: utc('2026-10-18T10:00'), new: utc('2026-10-19T10:00') },
        ],
      }),
      context,
    );

    expect(created).toEqual([{ label: 'Note', text: 'Sprint planning' }]);
    expect(photo).toEqual([]);
  });
});
