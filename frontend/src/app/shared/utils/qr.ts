import qrcode from 'qrcode-generator';

/** A QR code as one SVG path over a `size` x `size` grid of modules, one unit each. */
export interface QrModules {
  size: number;
  path: string;
}

/**
 * The QR code for `text`, with error correction level M (a printed code still reads with some
 * of it scuffed or covered). The quiet zone around it is left to whoever draws it.
 */
export function qrModules(text: string): QrModules {
  const qr = qrcode(0, 'M');
  qr.addData(text);
  qr.make();

  const size = qr.getModuleCount();
  let path = '';
  for (let row = 0; row < size; row++) {
    for (let column = 0; column < size; column++) {
      if (qr.isDark(row, column)) {
        path += `M${column} ${row}h1v1h-1z`;
      }
    }
  }
  return { size, path };
}

/** What a resource's QR code is read out as, in place of the picture: "QR code for Board Room". */
export function qrCodeLabel(resourceName: string): string {
  return $localize`:@@qr.label:QR code for ${resourceName}:resource:`;
}
