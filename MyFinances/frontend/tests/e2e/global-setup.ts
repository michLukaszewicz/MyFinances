import { execFileSync } from 'node:child_process';
import { DB_CONNECTION, DB_PORT } from '../../playwright.config';

const CONTAINER = 'myfinances-e2e-db';

const run = (cmd: string, args: string[]) => execFileSync(cmd, args, { encoding: 'utf8', stdio: 'pipe' });

// Fresh Postgres + schema on every run, so the auth setup can register the test user from scratch.
export default async function globalSetup() {
  try {
    run('docker', ['rm', '-f', CONTAINER]);
  } catch {
    // no previous container
  }
  run('docker', [
    'run', '-d', '--rm', '--name', CONTAINER,
    '-e', 'POSTGRES_PASSWORD=e2e', '-e', 'POSTGRES_DB=myfinances_e2e',
    '-p', `${DB_PORT}:5432`, 'postgres:17-alpine',
  ]);

  // pg_isready answers true during the image's init phase too, so ask a real query over TCP.
  for (let attempt = 0; ; attempt++) {
    try {
      run('docker', ['exec', CONTAINER, 'psql', '-h', 'localhost', '-U', 'postgres', '-d', 'myfinances_e2e', '-c', 'select 1']);
      break;
    } catch (error) {
      if (attempt >= 60) throw new Error(`Postgres container did not become ready: ${error}`);
      await new Promise((r) => setTimeout(r, 1000));
    }
  }

  run('dotnet', ['ef', 'database', 'update', '--project', '../backend', '--no-build', '--connection', DB_CONNECTION]);

  return async () => {
    try {
      run('docker', ['rm', '-f', CONTAINER]);
    } catch {
      // already gone
    }
  };
}
