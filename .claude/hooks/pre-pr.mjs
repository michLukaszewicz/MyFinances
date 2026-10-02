#!/usr/bin/env node
// PreToolUse hook (Bash): runs the CI gates locally before `gh pr create` goes through, because
// GitHub Actions is unavailable. Gates: backend build + tests, frontend typecheck, full Playwright
// E2E suite. Exit 2 + stderr blocks the command and shows Claude why. Mutation testing stays out
// (report-only in CI, too slow here).
import { spawnSync } from 'node:child_process';
import net from 'node:net';
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
// Only a command that actually starts with `gh pr create` (alone or after && || ;), not text that mentions it.
if (!/(^|&&|\|\||;)\s*gh\s+pr\s+create\b/.test(input?.tool_input?.command ?? '')) process.exit(0);

const start = input?.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();
const run = (cmd, args, cwd, timeout) =>
  spawnSync(cmd, args, { cwd, env, encoding: 'utf8', shell: true, timeout, maxBuffer: 64 * 1024 * 1024 });

const top = run('git', ['rev-parse', '--show-toplevel'], start, 15000);
if (top.status !== 0) process.exit(0);
const root = top.stdout.trim();

const trim = (text) => {
  const l = text.split(/\r?\n/).filter((x) => x.trim() && !/ warning |Determining projects|up-to-date for restore|^\[WebServer\]/.test(x));
  return l.length > 80 ? [...l.slice(0, 40), `... (${l.length - 70} lines omitted) ...`, ...l.slice(-30)].join('\n') : l.join('\n');
};
const out = (r) => (r.stdout || '') + (r.stderr || '') + (r.error ? String(r.error) : '');

const portInUse = (port) =>
  new Promise((resolve) => {
    const s = net.connect({ port, host: '127.0.0.1' });
    s.once('connect', () => (s.destroy(), resolve(true)));
    s.once('error', () => resolve(false));
  });

const failures = [];

const backend = run('dotnet', ['test', '--nologo'], path.join(root, 'MyFinances/backend'), 300000);
if (backend.error || backend.status !== 0) failures.push(`Backend build/tests fail (dotnet test):\n${trim(out(backend))}`);

const frontendDir = path.join(root, 'MyFinances/frontend');
const typecheck = run('npm', ['run', 'typecheck', '--silent'], frontendDir, 120000);
if (typecheck.error || typecheck.status !== 0) failures.push(`Frontend typecheck fails (npm run typecheck):\n${trim(out(typecheck))}`);

// E2E needs Docker (throwaway Postgres) and a free API port: a dev server already on 5007 would be silently reused.
const e2ePort = Number(process.env.E2E_PORT ?? 5007);
if (run('docker', ['info'], root, 30000).status !== 0) {
  failures.push('E2E suite not run: Docker is not running (global-setup starts a throwaway Postgres container). Start Docker Desktop.');
} else if (await portInUse(e2ePort)) {
  failures.push(`E2E suite not run: port ${e2ePort} is in use (a dev server would be silently reused). Stop it or set E2E_PORT.`);
} else {
  const e2e = run('npx', ['playwright', 'test', '--reporter=line'], frontendDir, 600000);
  if (e2e.error || e2e.status !== 0) failures.push(`Playwright E2E suite fails (npx playwright test):\n${trim(out(e2e))}`);
}

if (failures.length) {
  process.stderr.write(`PR not created: local CI gates failed. Fix these, then run gh pr create again.\n\n${failures.join('\n\n')}\n`);
  process.exit(2);
}
process.exit(0);
