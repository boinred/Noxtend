import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import test from 'node:test'

const script = new URL('./guard-git-stage.mjs', import.meta.url).pathname
const run = (input) =>
  spawnSync(process.execPath, [script], {
    encoding: 'utf8',
    input: typeof input === 'string' ? input : JSON.stringify(input),
  })
const bash = (command) =>
  run({
    hook_event_name: 'PreToolUse',
    tool_name: 'Bash',
    tool_input: { command },
  })

const blocked = [
  'git add -A',
  'git add --all',
  'git add .',
  'git add -u',
  'git add :/',
  'git stage .',
  'git commit -a -m x',
  'git commit -am "x"',
  'cd apps && git add .',
  'git -C /repo add -A',
  'git status; git add -A',
]
const allowed = [
  'git add AGENTS.md docs/a.md',
  'git add .agents/skills/x.md',
  'git add ./src/a.ts',
  'git commit -m "git add -A later"',
  'git commit --amend --no-edit',
  'echo "git add -A"',
]

test('bulk staging commands are denied with a path hint', () => {
  for (const command of blocked) {
    const result = bash(command)
    assert.equal(result.status, 0, command)
    const { hookSpecificOutput } = JSON.parse(result.stdout)
    assert.equal(hookSpecificOutput.hookEventName, 'PreToolUse', command)
    assert.equal(hookSpecificOutput.permissionDecision, 'deny', command)
    assert.match(hookSpecificOutput.permissionDecisionReason, /경로를 지정해 stage/, command)
  }
})

test('path-scoped staging and unrelated input pass silently', () => {
  for (const command of allowed) {
    const result = bash(command)
    assert.equal(result.status, 0, command)
    assert.equal(result.stdout, '', command)
  }
  for (const input of [
    { tool_name: 'Edit', tool_input: { command: 'git add -A' } },
    '',
    '{not json',
  ]) {
    const result = run(input)
    assert.equal(result.status, 0)
    assert.equal(result.stdout, '')
  }
})
