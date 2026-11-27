import { coverCrop } from './resize-image';

describe('coverCrop', () => {
  it('crops the sides of a wide photo for the square logo', () => {
    expect(coverCrop({ width: 2000, height: 1000 }, { width: 512, height: 512 })).toEqual({ sx: 500, sy: 0, sw: 1000, sh: 1000 });
  });

  it('crops top and bottom of a tall photo for the wide cover', () => {
    expect(coverCrop({ width: 1600, height: 1200 }, { width: 1600, height: 600 })).toEqual({ sx: 0, sy: 300, sw: 1600, sh: 600 });
  });
});
