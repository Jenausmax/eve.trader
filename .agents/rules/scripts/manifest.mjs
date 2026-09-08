#!/usr/bin/env node
// Generates / verifies MANIFEST.json — sha256 of every tracked rule file.
//
//   node scripts/manifest.mjs           # verify, exit 1 on drift
//   node scripts/manifest.mjs --write   # regenerate
//
// Consumers use the same hashes to detect a shared rule edited in place
// instead of upstream (see README.md).

import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

const root = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const manifestPath = join(root, 'MANIFEST.json');
const SKIP = new Set(['.git', 'node_modules', 'scripts']);
const SKIP_FILES = new Set(['MANIFEST.json']);

// Файлы с префиксом local- принадлежат ПОТРЕБИТЕЛЮ, а не набору: они лежат
// в том же каталоге, потому что агент читает правила из одного места, но в
// манифест не входят. Иначе у каждого потребителя манифест «дрейфует» просто
// оттого, что у него есть свои правила, и проверка перестаёт что-либо значить.
const isLocal = name => name.startsWith('local-');

function walk(dir, acc = []) {
  for (const entry of readdirSync(dir)) {
    if (SKIP.has(entry) || isLocal(entry)) continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, acc);
    else if (!SKIP_FILES.has(entry)) acc.push(full);
  }
  return acc;
}

const files = walk(root)
  .map((f) => relative(root, f).split(sep).join('/'))
  .sort();

const hashes = Object.fromEntries(
  files.map((rel) => [
    rel,
    createHash('sha256').update(readFileSync(join(root, rel))).digest('hex'),
  ]),
);

const manifest = {
  schemaVersion: 1,
  algorithm: 'sha256',
  generated: new Date().toISOString().slice(0, 10),
  fileCount: files.length,
  files: hashes,
};

if (process.argv.includes('--write')) {
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`MANIFEST.json written — ${files.length} files`);
  process.exit(0);
}

const current = JSON.parse(readFileSync(manifestPath, 'utf8'));
const drift = files
  .filter((rel) => current.files[rel] !== hashes[rel])
  .concat(Object.keys(current.files).filter((rel) => !(rel in hashes)));

if (drift.length === 0) {
  console.log(`MANIFEST.json is current — ${files.length} files`);
  process.exit(0);
}

console.error('MANIFEST.json drift:');
for (const rel of drift) console.error(`  ${rel}`);
console.error('run: node scripts/manifest.mjs --write');
process.exit(1);
