import { RESOURCE_PHOTO } from '../../core/models/resource.model';

const QUALITY = 0.85;

/** The size a width x height picture gets when its longest side may be at most `maxSide`. */
export function fitWithin(
  width: number,
  height: number,
  maxSide: number,
): { width: number; height: number } {
  const scale = Math.min(1, maxSide / Math.max(width, height));
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

/**
 * Gets a chosen picture ready to upload as a resource photo (ADR-0027): at most
 * `RESOURCE_PHOTO.maxSide` on its longest side, or `maxSide` (a floor's plan is kept larger), and
 * written again as WebP - JPEG where the browser can't write WebP. Writing it again also leaves
 * out what the camera put in the file, such as where the photo was taken. Null when the browser
 * can't read the file as a picture.
 */
export async function preparePhoto(
  file: Blob,
  maxSide: number = RESOURCE_PHOTO.maxSide,
): Promise<Blob | null> {
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    return null;
  }

  try {
    const size = fitWithin(bitmap.width, bitmap.height, maxSide);
    const canvas = document.createElement('canvas');
    canvas.width = size.width;
    canvas.height = size.height;
    const context = canvas.getContext('2d');
    if (!context) {
      return null;
    }

    context.drawImage(bitmap, 0, 0, size.width, size.height);
    const webp = await toBlob(canvas, 'image/webp');
    if (webp?.type === 'image/webp') {
      return webp;
    }

    // No WebP here (the browser wrote a PNG instead). JPEG has no transparency, so give the
    // picture a white background rather than a black one.
    context.globalCompositeOperation = 'destination-over';
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, size.width, size.height);
    return await toBlob(canvas, 'image/jpeg');
  } finally {
    bitmap.close();
  }
}

function toBlob(canvas: HTMLCanvasElement, type: string): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, QUALITY));
}
