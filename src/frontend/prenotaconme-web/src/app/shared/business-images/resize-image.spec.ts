import { coverCrop, iconBackground } from './resize-image';

describe('coverCrop', () => {
  it('crops the sides of a wide photo for the square logo', () => {
    expect(coverCrop({ width: 2000, height: 1000 }, { width: 512, height: 512 })).toEqual({ sx: 500, sy: 0, sw: 1000, sh: 1000 });
  });

  it('crops top and bottom of a tall photo for the wide cover', () => {
    expect(coverCrop({ width: 1600, height: 1200 }, { width: 1600, height: 600 })).toEqual({ sx: 0, sy: 300, sw: 1600, sh: 600 });
  });
});

describe('iconBackground', () => {
  /** A size×size image where `paint(x, y)` returns the RGBA of each pixel. */
  function image(size: number, paint: (x: number, y: number) => [number, number, number, number]): Uint8ClampedArray {
    const data = new Uint8ClampedArray(size * size * 4);
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) data.set(paint(x, y), (y * size + x) * 4);
    return data;
  }

  it('fills the corners of a dark round logo with its own colour', () => {
    // Dark disc with a thin gold ring inside the edge: the ring must not tint the background.
    const round = image(100, (x, y) => {
      const d = Math.hypot(x - 49.5, y - 49.5);
      if (d > 50) return [0, 0, 0, 0];
      return d > 44 && d < 46 ? [184, 134, 59, 255] : [31, 31, 31, 255];
    });

    expect(iconBackground(round, 100, 100)).toBe('#1f1f1f');
  });

  it('uses white behind a logo that is just a symbol in the middle', () => {
    const symbol = image(100, (x, y) => (x > 30 && x < 70 && y > 30 && y < 70 ? [200, 0, 0, 255] : [0, 0, 0, 0]));

    expect(iconBackground(symbol, 100, 100)).toBe('#ffffff');
  });
});
