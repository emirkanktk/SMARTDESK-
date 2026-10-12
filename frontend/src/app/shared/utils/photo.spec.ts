import { fitWithin, preparePhoto } from './photo';

describe('fitWithin', () => {
  it('keeps a picture that already fits', () => {
    expect(fitWithin(1200, 900, 1600)).toEqual({ width: 1200, height: 900 });
    expect(fitWithin(1600, 1600, 1600)).toEqual({ width: 1600, height: 1600 });
  });

  it('shrinks the longest side to the limit and keeps the proportions', () => {
    expect(fitWithin(4000, 3000, 1600)).toEqual({ width: 1600, height: 1200 });
    expect(fitWithin(3024, 4032, 1600)).toEqual({ width: 1200, height: 1600 });
  });

  it('never goes below one pixel', () => {
    expect(fitWithin(20000, 2, 1600)).toEqual({ width: 1600, height: 1 });
  });
});

describe('preparePhoto', () => {
  // The test DOM can neither decode pictures nor draw them: both are stood in for. The canvas
  // "writes" its size, so the tests can see what was drawn.
  let browserWritesWebp: boolean;
  let decoded: { width: number; height: number; close: ReturnType<typeof vi.fn> };
  let background: string | null;

  beforeEach(() => {
    browserWritesWebp = true;
    background = null;
    decoded = { width: 4000, height: 3000, close: vi.fn() };
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(async () => decoded),
    );
    const context = {
      globalCompositeOperation: 'source-over',
      drawImage: vi.fn(),
      fillRect: vi.fn(function (this: { fillStyle: string; globalCompositeOperation: string }) {
        background = `${this.fillStyle} ${this.globalCompositeOperation}`;
      }),
      fillStyle: '',
    };
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      context as unknown as CanvasRenderingContext2D,
    );
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
      this: HTMLCanvasElement,
      callback: BlobCallback,
      type?: string,
    ) {
      const written = type === 'image/webp' && !browserWritesWebp ? 'image/png' : type;
      callback(new Blob([`${this.width}x${this.height}`], { type: written }));
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('keeps a picture larger when asked, as a floor plan is', async () => {
    const plan = await preparePhoto(new Blob(['png'], { type: 'image/png' }), 2400);

    expect(await plan!.text()).toBe('2400x1800');
  });

  it('writes a large photo again as WebP, at most 1600 pixels on its longest side', async () => {
    const photo = await preparePhoto(new Blob(['jpeg'], { type: 'image/jpeg' }));

    expect(photo!.type).toBe('image/webp');
    expect(await photo!.text()).toBe('1600x1200');
    expect(background).toBeNull();
    expect(decoded.close).toHaveBeenCalled();
  });

  it('writes a small picture again too, at its own size, leaving out what the camera added', async () => {
    decoded = { width: 800, height: 600, close: vi.fn() };

    const photo = await preparePhoto(new Blob(['jpeg'], { type: 'image/jpeg' }));

    expect(photo!.type).toBe('image/webp');
    expect(await photo!.text()).toBe('800x600');
  });

  it('writes JPEG on a white background where the browser has no WebP', async () => {
    browserWritesWebp = false;

    const photo = await preparePhoto(new Blob(['png'], { type: 'image/png' }));

    expect(photo!.type).toBe('image/jpeg');
    expect(background).toBe('#ffffff destination-over');
  });

  it("answers null for a file the browser can't read as a picture", async () => {
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(async () => {
        throw new DOMException('The source image could not be decoded.', 'InvalidStateError');
      }),
    );

    expect(await preparePhoto(new Blob(['not a picture'], { type: 'text/plain' }))).toBeNull();
  });
});
