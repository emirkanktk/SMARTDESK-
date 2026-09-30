/**
 * Stands in for URL.createObjectURL / revokeObjectURL, which the test DOM lacks: created URLs
 * are numbered ("blob:test-1", ...), and revoked ones are recorded. Call `restore` afterwards.
 */
export function fakeObjectUrls(): { revoked: string[]; restore: () => void } {
  const create = URL.createObjectURL;
  const revoke = URL.revokeObjectURL;
  const revoked: string[] = [];
  let created = 0;
  URL.createObjectURL = () => `blob:test-${++created}`;
  URL.revokeObjectURL = (url: string) => void revoked.push(url);
  return {
    revoked,
    restore: () => {
      URL.createObjectURL = create;
      URL.revokeObjectURL = revoke;
    },
  };
}
