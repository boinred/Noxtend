import { readFileSync } from 'node:fs'

const reason =
  '일괄 stage와 commit -a는 다른 작업의 변경을 섞을 수 있어 경로를 지정해 stage한 뒤 commit해야 한다. 일괄 stage가 꼭 필요하면 사용자가 직접 실행한다'

// unquoted operator and whitespace tokenizer
function segments(command) {
  const result = []
  let tokens = []
  let token = ''
  let quote = ''
  let started = false
  const endToken = () => {
    if (started) tokens.push(token)
    token = ''
    started = false
  }
  const endSegment = () => {
    endToken()
    if (tokens.length) result.push(tokens)
    tokens = []
  }
  for (const char of command) {
    if (quote) {
      if (char === quote) quote = ''
      else token += char
    } else if (char === '"' || char === "'") {
      quote = char
      started = true
    } else if (/\s/.test(char) && char !== '\n') endToken()
    else if (char === '\n' || char === ';' || char === '|' || char === '&') endSegment()
    else {
      token += char
      started = true
    }
  }
  endSegment()
  return result
}

// short option bundle scan up to value options
const hasShort = (arg, letters, valueOptions = '') => {
  if (!/^-[^-]/.test(arg)) return false
  for (const char of arg.slice(1)) {
    if (letters.includes(char)) return true
    if (valueOptions.includes(char)) return false
  }
  return false
}

function bulk(tokens) {
  let i = tokens.findIndex((token) => !/^\w+=/.test(token))
  if (tokens[i] !== 'git') return false
  const valueGlobals = ['-C', '-c', '--git-dir', '--work-tree', '--namespace']
  for (i += 1; tokens[i]?.startsWith('-'); i += 1) if (valueGlobals.includes(tokens[i])) i += 1
  const sub = tokens[i]
  const args = tokens.slice(i + 1)
  if (sub === 'add' || sub === 'stage')
    return args.some((arg) => ['.', ':/', '--all', '--update'].includes(arg) || hasShort(arg, 'Au'))
  if (sub !== 'commit') return false
  const valueOptions = ['-m', '-F', '--message', '--file']
  for (let j = 0; j < args.length; j += 1) {
    if (valueOptions.includes(args[j])) j += 1
    else if (args[j] === '--all' || hasShort(args[j], 'a', 'mFCc')) return true
  }
  return false
}

try {
  const event = JSON.parse(readFileSync(0, 'utf8'))
  const command = event?.tool_input?.command
  if (event?.tool_name === 'Bash' && typeof command === 'string' && segments(command).some(bulk))
    console.log(
      JSON.stringify({
        hookSpecificOutput: {
          hookEventName: 'PreToolUse',
          permissionDecision: 'deny',
          permissionDecisionReason: reason,
        },
      }),
    )
} catch {
  // malformed input passes silently
}
