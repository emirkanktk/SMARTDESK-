/**
 * Text as the search boxes compare it: lower case, without accents, and with Turkish's dotted
 * and dotless i as one letter. So "istanbul", "ISTANBUL" and "İSTANBUL" all find "İstanbul",
 * and "isik" finds "Işık", in every browser and language.
 *
 * Plain toLowerCase() turns "İ" into an "i" plus a combining dot, so "İlker" didn't contain
 * "ilk". toLocaleLowerCase() follows the browser's language, and in Turkish it turns the "I" of
 * "HDMI" into a dotless "ı".
 */
export function searchKey(text: string): string {
  return text.toLowerCase().normalize('NFD').replace(/\p{M}/gu, '').replace(/ı/g, 'i');
}
