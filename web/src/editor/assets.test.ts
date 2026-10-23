import { describe, expect, it } from 'vitest';
import { ApiError } from '../api/client';
import { assetLabel, importErrorText, kindFromFileName } from './assets';

describe('assets', () => {
  it('takes the asset kind from the file extension', () => {
    expect(kindFromFileName('photo.PNG')).toBe('image');
    expect(kindFromFileName('a.jpg')).toBe('image');
    expect(kindFromFileName('a.b.jpeg')).toBe('image');
    expect(kindFromFileName('loop.gif')).toBe('gif');
    expect(kindFromFileName('theme.zt')).toBe('zt');
    expect(kindFromFileName('notes.txt')).toBeNull();
    expect(kindFromFileName('zt')).toBeNull();
  });

  it('explains a failed import', () => {
    expect(importErrorText(new ApiError(413, ['file is too large']))).toBe('File is too large (max 64 MB).');
    expect(importErrorText(new ApiError(400, ['invalid media']))).toBe('invalid media');
    expect(importErrorText(new TypeError('x'))).toBe('The file could not be uploaded.');
    expect(importErrorText(new DOMException('x', 'TimeoutError'))).toBe('The upload timed out.');
    expect(importErrorText(new DOMException('x', 'AbortError'))).toBe('The upload was cancelled.');
  });

  it('labels assets by kind, number and size', () => {
    expect(assetLabel({ id: 'q', kind: 'image', width: 3, height: 2, frameCount: 1 }, 2)).toBe('Image 2, 3×2');
    expect(assetLabel({ id: 'q', kind: 'zt', width: 3, height: 2, frameCount: 9 }, 1)).toBe('Animation 1, 3×2, 9 frames');
  });
});
