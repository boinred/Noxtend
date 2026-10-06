import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { delimiter, dirname, join } from 'node:path'
import test from 'node:test'

test('frontend lint gates Git commits without blocking backend-only changes', () => {
  const root = mkdtempSync(join(tmpdir(), 'noxtend-hooks-'))
  const log = join(root, 'lint.log')
  const env = { ...process.env }
  const localVars = spawnSync('git', ['rev-parse', '--local-env-vars'], { encoding: 'utf8' })
  assert.equal(localVars.status, 0)
  for (const name of localVars.stdout.trim().split('\n')) delete env[name]
  Object.assign(env, {
    GIT_CONFIG_GLOBAL: '/dev/null',
    GIT_CONFIG_NOSYSTEM: '1',
    PATH: `${join(root, 'bin')}${delimiter}${env.PATH}`,
    NOXTEND_HOOK_LOG: log,
    NOXTEND_LINT_EXIT: '23',
  })

  const git = (...args) => spawnSync('git', args, { cwd: root, env, encoding: 'utf8' })
  const ok = (...args) => {
    const result = git(...args)
    assert.equal(result.status, 0, result.stderr)
    return result.stdout.trim()
  }
  const write = (file, content, mode = 0o644) => {
    const path = join(root, file)
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, content, { mode })
  }

  try {
    ok('init', '--quiet', '--initial-branch=main')
    ok('config', 'user.name', 'Hook test')
    ok('config', 'user.email', 'hook-test@example.invalid')
    ok('config', 'commit.gpgsign', 'false')
    write('apps/backend/server.cs', 'baseline')
    ok('add', 'apps/backend/server.cs')
    ok('commit', '--quiet', '-m', 'baseline')
    write('.githooks/pre-commit', readFileSync(new URL('./pre-commit', import.meta.url)), 0o755)
    write(
      'bin/pnpm',
      '#!/bin/sh\nprintf \'%s\\n\' "$*" >> "$NOXTEND_HOOK_LOG"\nexit "$NOXTEND_LINT_EXIT"\n',
      0o755,
    )
    ok('config', 'core.hooksPath', '.githooks')

    ok('hook', 'run', 'pre-commit')
    write('apps/backend/server.cs', 'backend change')
    ok('add', 'apps/backend/server.cs')
    ok('commit', '--quiet', '-m', 'backend')
    assert.equal(existsSync(log), false)

    for (const path of ['apps/frontend/src/file with spaces.ts', 'pnpm-lock.yaml']) {
      write(path, 'staged change')
      ok('add', path)
      const head = ok('rev-parse', 'HEAD')
      assert.notEqual(git('commit', '--quiet', '-m', 'lint failure').status, 0)
      assert.equal(ok('rev-parse', 'HEAD'), head)
      assert.equal(readFileSync(join(root, path), 'utf8'), 'staged change')
      assert.equal(ok('diff', '--cached', '--name-only'), path)
      assert.equal(readFileSync(log, 'utf8'), '--filter @nextend/frontend lint\n')
      env.NOXTEND_LINT_EXIT = '0'
      ok('commit', '--quiet', '-m', 'lint success')
      rmSync(log)
      env.NOXTEND_LINT_EXIT = '23'
    }

    ok('rm', 'apps/frontend/src/file with spaces.ts')
    assert.notEqual(git('commit', '--quiet', '-m', 'frontend deletion').status, 0)
    assert.equal(readFileSync(log, 'utf8'), '--filter @nextend/frontend lint\n')
    rmSync(log)
    write('invalid-index', 'invalid Git index')
    env.GIT_INDEX_FILE = join(root, 'invalid-index')
    assert.notEqual(git('hook', 'run', 'pre-commit').status, 0)
    assert.equal(existsSync(log), false)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
