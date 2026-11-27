/** F5-08: sizes the booking page uses (logo in a round avatar, cover as a wide banner). */
export const IMAGE_SIZES = {
  logo: { width: 512, height: 512 },
  cover: { width: 1600, height: 600 },
  /** F5-10: home-screen icon made from the logo (iOS size, opaque). */
  icon: { width: 180, height: 180 },
} as const;

export type BusinessImageKind = keyof typeof IMAGE_SIZES;

/** Same limit as the API (TenantImageRules.MaxBytes). */
export const MAX_IMAGE_BYTES = 1024 * 1024;

/** Centered crop that fills `target` (like CSS object-fit: cover). */
export function coverCrop(
  source: { width: number; height: number },
  target: { width: number; height: number },
): { sx: number; sy: number; sw: number; sh: number } {
  const scale = Math.max(target.width / source.width, target.height / source.height);
  const sw = target.width / scale;
  const sh = target.height / scale;
  return { sx: (source.width - sw) / 2, sy: (source.height - sh) / 2, sw, sh };
}

/**
 * F5-10: background for the home-screen icon of a logo with transparent parts. A round or shaped logo gets the most
 * common opaque colour near its edges (a dark round logo becomes a dark square, not a dark circle on white); a logo
 * with almost nothing opaque near the edges (just text or a symbol) gets white.
 */
export function iconBackground(rgba: Uint8ClampedArray, width: number, height: number): string {
  const band = Math.max(1, Math.round(Math.min(width, height) * 0.08));
  // Similar colours share a bucket (5 bits per channel); the result is the average of the biggest bucket.
  const buckets = new Map<number, { count: number; r: number; g: number; b: number }>();
  let opaque = 0;
  let total = 0;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (x >= band && x < width - band && y >= band && y < height - band) continue;
      total++;
      const i = (y * width + x) * 4;
      if (rgba[i + 3] < 250) continue;
      opaque++;
      const key = ((rgba[i] >> 3) << 10) | ((rgba[i + 1] >> 3) << 5) | (rgba[i + 2] >> 3);
      const bucket = buckets.get(key) ?? { count: 0, r: 0, g: 0, b: 0 };
      bucket.count++;
      bucket.r += rgba[i];
      bucket.g += rgba[i + 1];
      bucket.b += rgba[i + 2];
      buckets.set(key, bucket);
    }
  }
  if (opaque < total * 0.1) return '#ffffff';
  const top = [...buckets.values()].reduce((best, bucket) => (bucket.count > best.count ? bucket : best));
  const hex = (sum: number) => Math.round(sum / top.count).toString(16).padStart(2, '0');
  return `#${hex(top.r)}${hex(top.g)}${hex(top.b)}`;
}

function toBlob(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality));
}

/**
 * Crops and resizes in the browser, so the server only stores small files. WebP where the browser can encode it
 * (Safari cannot): otherwise PNG for the logo (keeps transparency) and JPEG for the cover.
 */
export async function resizeImage(file: Blob, kind: BusinessImageKind): Promise<Blob> {
  const target = IMAGE_SIZES[kind];
  const bitmap = await createImageBitmap(file);
  const canvas = document.createElement('canvas');
  canvas.width = target.width;
  canvas.height = target.height;
  const context = canvas.getContext('2d')!;
  context.imageSmoothingQuality = 'high';
  const { sx, sy, sw, sh } = coverCrop(bitmap, target);
  context.drawImage(bitmap, sx, sy, sw, sh, 0, 0, target.width, target.height);
  bitmap.close();

  if (kind === 'icon') {
    // iOS paints transparent pixels black: fill them, behind the logo, with the logo's own edge colour.
    const pixels = context.getImageData(0, 0, target.width, target.height);
    context.globalCompositeOperation = 'destination-over';
    context.fillStyle = iconBackground(pixels.data, target.width, target.height);
    context.fillRect(0, 0, target.width, target.height);
    const png = await toBlob(canvas, 'image/png');
    if (!png) throw new Error('encode');
    return png;
  }

  const webp = await toBlob(canvas, 'image/webp', 0.85);
  if (webp?.type === 'image/webp' && webp.size <= MAX_IMAGE_BYTES) return webp;

  const fallback = kind === 'logo' ? await toBlob(canvas, 'image/png') : null;
  if (fallback && fallback.size <= MAX_IMAGE_BYTES) return fallback;

  const jpeg = await toBlob(canvas, 'image/jpeg', 0.82);
  if (!jpeg) throw new Error('encode');
  return jpeg;
}
