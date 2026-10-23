import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';

function scanDir(dir: string, files: string[] = []): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      scanDir(fullPath, files);
    } else if (/\.(ts|tsx|js|jsx)$/.test(entry.name) && !entry.name.endsWith('security.test.ts')) {
      files.push(fullPath);
    }
  }
  return files;
}

describe('Security scan', () => {
  it('no dangerouslySetInnerHTML/innerHTML/eval/new Function in src', () => {
    const srcDir = path.resolve(process.cwd(), 'src');
    const files = scanDir(srcDir);
    expect(files.length).toBeGreaterThan(0);

    const forbiddenPatterns = [
      /\bdangerouslySetInnerHTML\b/,
      /\binnerHTML\b/,
      /\beval\s*\(/,
      /\bnew\s+Function\s*\(/,
    ];

    const violations: string[] = [];

    for (const file of files) {
      const content = fs.readFileSync(file, 'utf-8');
      for (const pattern of forbiddenPatterns) {
        if (pattern.test(content)) {
          violations.push(`${file} matches ${pattern.toString()}`);
        }
      }
    }

    expect(violations).toEqual([]);
  });
});
