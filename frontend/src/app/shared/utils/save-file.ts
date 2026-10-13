/**
 * Hands a file the app fetched to the browser to save, as a download link would: the API's
 * files need the token, so they can't be a plain link.
 */
export function saveFile(file: Blob, name: string, document: Document): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  // The download has its own copy once it starts.
  setTimeout(() => URL.revokeObjectURL(url));
}
