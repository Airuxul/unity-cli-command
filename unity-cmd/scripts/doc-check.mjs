import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(__dirname, '..');
const connectorRoot = path.join(root, '..', 'unity-connector');

function readVersion(pkgPath) {
  const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
  return pkg.version;
}

function readDocVersion(docPath) {
  const text = fs.readFileSync(docPath, 'utf8');
  const match = text.match(/^Version:\s*(\S+)/m);
  return match?.[1] ?? null;
}

const cmdVer = readVersion(path.join(root, 'package.json'));
const conVer = readVersion(path.join(connectorRoot, 'package.json'));

const docs = [
  [path.join(root, 'docs', 'IMPLEMENTATION.md'), cmdVer],
  [path.join(connectorRoot, 'docs', 'IMPLEMENTATION.md'), conVer],
];

let ok = true;
for (const [docPath, expected] of docs) {
  if (!fs.existsSync(docPath)) {
    console.error(`Missing ${docPath}`);
    ok = false;
    continue;
  }
  const docVer = readDocVersion(docPath);
  if (docVer !== expected) {
    console.error(`${docPath}: Version ${docVer} !== package.json ${expected}`);
    ok = false;
  }
}

if (!ok) process.exit(1);
console.log('doc:check OK');
