#!/usr/bin/env node
// Stop hook: when this turn changed backend or frontend files, run the matching
// gates (dotnet test = build + tests, frontend typecheck). Exit 2 + stderr is the
// only combination Claude sees. One retry via stop_hook_active; the CI gate catches the rest.
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';

const env = { ...process.env, NO_COLOR: '1', FORCE_COLOR: '0', DOTNET_CLI_UI_LANGUAGE: 'en', VSLANG: '1033', DOTNET_NOLOGO: '1' };

let input = {};
try {
  const raw = await new Promise((resolve) => {
    let s = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (c) => (s += c));
    process.stdin.on('end', () => resolve(s));
    process.stdin.on('error', () => resolve(s));
  });
  input = JSON.parse(raw);
} catch {
  input = {};
}
if (input?.stop_hook_active === true || (input?.loop_count ?? 0) !== 0) process.exit(0);

const start = input?.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();
const run = (cmd, args, cwd, timeout) =>
  spawnSync(cmd, args, { cwd, env, encoding: 'utf8', shell: true, timeout, maxBuffer: 64 * 1024 * 1024 });

const top = run('git', ['rev-parse', '--show-toplevel'], start, 15000);
if (top.status !== 0) process.exit(0);
const root = top.stdout.trim();

const lines = (r) => (r.stdout || '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
const changed = [
  ...new Set([
    ...lines(run('git', ['diff', '--name-only', 'HEAD'], root, 15000)),
    ...lines(run('git', ['ls-files', '-o', '--exclude-standard'], root, 15000)),
  ]),
].filter((f) => existsSync(path.join(root, f)));
if (changed.length === 0) process.exit(0);

const junk = /(^|\/)(bin|obj|node_modules|build|StrykerOutput|\.react-router)\//;
const backend = changed.some((f) => f.startsWith('MyFinances/backend/') && !junk.test(f) && /\.(cs|csproj|slnx|json|config)$/.test(f));
const frontend = changed.some(
  (f) =>
    f.startsWith('MyFinances/frontend/') &&
    !junk.test(f) &&
    (/^MyFinances\/frontend\/app\/.*\.(ts|tsx|css)$/.test(f) || /^MyFinances\/frontend\/[^/]+\.(ts|json)$/.test(f)),
);
if (!backend && !frontend) process.exit(0);

const trim = (text) => {
  const l = text.split(/\r?\n/).filter((x) => x.trim() && !/ warning |Determining projects|up-to-date for restore/.test(x));
  return l.length > 70 ? [...l.slice(0, 55), `... (${l.length - 65} lines omitted) ...`, ...l.slice(-10)].join('\n') : l.join('\n');
};

let report = '';
if (backend) {
  const r = run('dotnet', ['test', '--nologo'], path.join(root, 'MyFinances/backend'), 100000);
  if (r.error || r.status !== 0) report += `\nBackend build/tests fail (dotnet test):\n${trim((r.stdout || '') + (r.stderr || '') + (r.error ? String(r.error) : ''))}\n`;
}
if (frontend) {
  const r = run('npm', ['run', 'typecheck', '--silent'], path.join(root, 'MyFinances/frontend'), 100000);
  if (r.error || r.status !== 0) report += `\nFrontend typecheck fails (npm run typecheck):\n${trim((r.stdout || '') + (r.stderr || '') + (r.error ? String(r.error) : ''))}\n`;
}

if (report) {
  process.stderr.write(`Fix these before you finish:${report}`);
  process.exit(2);
}
process.exit(0);
