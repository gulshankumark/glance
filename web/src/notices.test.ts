import { describe, expect, it } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';

interface LockEntry {
  version: string;
  license?: string;
  dev?: boolean;
}

const root = process.cwd();
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf-8')) as {
  dependencies: Record<string, string>;
  devDependencies: Record<string, string>;
};
const lock = JSON.parse(fs.readFileSync(path.join(root, 'package-lock.json'), 'utf-8')) as {
  packages: Record<string, LockEntry>;
};
const notices = fs.readFileSync(path.join(root, '..', 'THIRD-PARTY-NOTICES.md'), 'utf-8');
const webSection = notices.slice(notices.indexOf('## Web UI npm packages'));

const entry = (name: string) => lock.packages[`node_modules/${name}`];
const row = (name: string) => webSection.split('\n').find((l) => l.startsWith(`| ${name} `)) ?? '';

describe('THIRD-PARTY-NOTICES.md web section', () => {
  it('lists every direct dependency with its locked version and licence', () => {
    for (const name of [...Object.keys(pkg.dependencies), ...Object.keys(pkg.devDependencies)]) {
      const { version, license } = entry(name);
      expect(row(name), name).toContain(`| ${name} ${version} |`);
      expect(row(name), name).toContain(`| ${license} |`);
    }
  });

  it('lists every runtime (non-dev) package with its locked version and licence', () => {
    const runtime = Object.entries(lock.packages).filter(([key, e]) => key !== '' && !e.dev);
    expect(runtime.length).toBeGreaterThan(0);
    for (const [key, { version, license }] of runtime) {
      const name = key.slice(key.lastIndexOf('node_modules/') + 'node_modules/'.length);
      expect(row(name), name).toContain(`| ${name} ${version} |`);
      expect(row(name), name).toContain(`| ${license} |`);
    }
  });

  it('names every licence found in the dev-only tooling', () => {
    const devLicences = new Set(
      Object.entries(lock.packages)
        .filter(([key, e]) => key !== '' && e.dev)
        .map(([, e]) => e.license ?? 'UNKNOWN'),
    );
    const start = webSection.indexOf('Dev-only tooling');
    // The summary paragraph (it may wrap over several lines), up to the next blank line or the end.
    const summary = start === -1 ? '' : webSection.slice(start).split(/\r?\n\r?\n/)[0];
    for (const licence of devLicences) expect(summary, licence).toContain(licence);
  });

  it('pins @types/node to a supported Node.js major (>= 20)', () => {
    expect(pkg.devDependencies['@types/node']).toMatch(/^\^(20|22)\./);
  });
});
