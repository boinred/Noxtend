import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'

test('document checks cover drafts, hook feedback, and the staged snapshot', () => {
  const root = mkdtempSync(join(tmpdir(), 'noxtend-doc-hooks-'))
  const script = new URL('./check-design-docs.mjs', import.meta.url).pathname
  const env = { ...process.env, GIT_CONFIG_GLOBAL: '/dev/null', GIT_CONFIG_NOSYSTEM: '1' }
  const localVars = spawnSync('git', ['rev-parse', '--local-env-vars'], { encoding: 'utf8' })
  assert.equal(localVars.status, 0)
  for (const name of localVars.stdout.trim().split('\n')) delete env[name]
  const git = (...args) => {
    const result = spawnSync('git', args, { cwd: root, env, encoding: 'utf8' })
    assert.equal(result.status, 0, result.stderr)
  }
  const run = (args = [], input) =>
    spawnSync(process.execPath, [script, ...args], {
      cwd: root,
      env,
      encoding: 'utf8',
      input: input && JSON.stringify(input),
    })
  const file = 'docs/superpowers/specs/example with spaces.md'
  const valid = '# 설계\n\n## 목적과 범위\n이미지 입력부터 시작한다.\n\n## 검증\nFake로 확인한다.\n'
  const write = (path, content) => {
    mkdirSync(dirname(join(root, path)), { recursive: true })
    writeFileSync(join(root, path), content)
  }
  try {
    git('init', '--quiet')
    git('config', 'user.name', 'Hook test')
    git('config', 'user.email', 'hook-test@example.invalid')
    git('config', 'commit.gpgsign', 'false')
    write('.githooks/check-design-docs.mjs', readFileSync(script, 'utf8'))
    write('baseline', '')
    git('add', 'baseline')
    git('commit', '--quiet', '-m', 'baseline')
    write(file, valid + '\n```text\nテスト\n```\n`テスト`\n')
    assert.equal(run().status, 0)
    assert.equal(run(['docs/superpowers/specs/missing.md']).status, 1)
    assert.equal(run(['baseline']).status, 1)
    for (const invalid of [
      valid + 'テスト\n',
      valid + 'TODO\n',
      valid.replace('## 검증', '## 설명'),
      valid + '\n```js\n',
      valid + '\n[근거](missing.md)\n',
    ]) {
      write(file, invalid)
      assert.equal(run().status, 1, invalid)
    }
    write(file, valid + '\n'.repeat(501))
    const long = run()
    assert.equal(long.status, 0)
    assert.match(long.stderr, /경고/)
    write(file, valid + 'テスト\n')
    const feedback = run(['--hook'], {
      hook_event_name: 'PostToolUse',
      tool_input: { file_path: join(root, file) },
    })
    assert.equal(feedback.status, 0)
    assert.equal(JSON.parse(feedback.stdout).decision, 'block')
    for (const config of ['../.codex/hooks.json', '../.claude/settings.json']) {
      const { hooks } = JSON.parse(readFileSync(new URL(config, import.meta.url), 'utf8'))
      for (const hook_event_name of ['PostToolUse', 'Stop']) {
        const command = hooks[hook_event_name][0].hooks[0].command
        const result = spawnSync('sh', ['-c', command], {
          cwd: root,
          env: { ...env, CLAUDE_PROJECT_DIR: root },
          encoding: 'utf8',
          input: JSON.stringify({ hook_event_name, tool_input: { file_path: join(root, file) } }),
        })
        assert.equal(result.status, 0, result.stderr)
        assert.equal(JSON.parse(result.stdout).decision, 'block')
      }
    }
    assert.equal(readFileSync(join(root, file), 'utf8'), valid + 'テスト\n')
    assert.equal(
      run(['--hook'], { hook_event_name: 'PostToolUse', tool_input: { command: 'git status' } })
        .stdout,
      '',
    )
    assert.equal(JSON.parse(run(['--hook'], { hook_event_name: 'Stop' }).stdout).decision, 'block')
    assert.equal(run(['--hook'], { hook_event_name: 'Stop', stop_hook_active: true }).stdout, '')
    git('add', file)
    write(file, valid)
    assert.equal(run(['--staged']).status, 1)
    git('add', file)
    write(file, valid + 'テスト\n')
    assert.equal(run(['--staged']).status, 0)
    write(file, valid + '\n[근거](../../../target.md)\n')
    write('target.md', '# 근거\n')
    git('add', file)
    assert.equal(run(['--staged']).status, 1)
    git('add', 'target.md')
    assert.equal(run(['--staged']).status, 0)
    write(
      'docs/superpowers/plans/example.md',
      '# 계획\n\n**Goal:** 이미지 입력\n**Spec:** [설계](../specs/example%20with%20spaces.md)\n\n## Global Constraints\n기존 URL 유지\n\n## Review Focus\nFake 검증\n\n## Task 1: 구현\n**Files:** Create `future.ts`\n- [ ] Fake 테스트\n',
    )
    write(file, valid)
    assert.equal(run().status, 0)
    write('docs/superpowers/plans/example.md', '# 계획\n')
    assert.equal(run().status, 1)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
