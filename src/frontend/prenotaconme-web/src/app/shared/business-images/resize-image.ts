/** F5-08: sizes the booking page uses (logo in a round avatar, cover as a wide banner). */
export const IMAGE_SIZES = {
  logo: { width: 512, height: 512 },
  cover: { width: 1600, height: 600 },
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

  const webp = await toBlob(canvas, 'image/webp', 0.85);
  if (webp?.type === 'image/webp' && webp.size <= MAX_IMAGE_BYTES) return webp;

  const fallback = kind === 'logo' ? await toBlob(canvas, 'image/png') : null;
  if (fallback && fallback.size <= MAX_IMAGE_BYTES) return fallback;

  const jpeg = await toBlob(canvas, 'image/jpeg', 0.82);
  if (!jpeg) throw new Error('encode');
  return jpeg;
}
