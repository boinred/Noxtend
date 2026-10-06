import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'

const directories = ['docs/superpowers/specs/', 'docs/superpowers/plans/']
const scoped = (file) => directories.some((dir) => file.startsWith(dir)) && file.endsWith('.md')
const args = process.argv.slice(2)
const hook = args.includes('--hook')
let event

try {
  if (hook) {
    event = JSON.parse(readFileSync(0, 'utf8'))
    if (event.hook_event_name === 'Stop' && event.stop_hook_active) process.exit(0)
    if (
      event.hook_event_name === 'PostToolUse' &&
      !JSON.stringify(event.tool_input ?? {}).includes('docs/superpowers/')
    )
      process.exit(0)
  }
  const root = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim()
  const git = (...params) => execFileSync('git', params, { cwd: root, encoding: 'utf8' })
  const paths = (output) => output.split('\0').filter(Boolean)
  const staged = args.includes('--staged')
  const index = staged ? new Set(paths(git('ls-files', '-z'))) : null
  const requested = args.filter((arg) => !arg.startsWith('--'))
  let files
  if (staged) files = paths(git('diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'))
  else if (hook)
    files = paths(
      git('diff', '--name-only', '--diff-filter=ACMR', '-z', 'HEAD', '--', ...directories),
    ).concat(paths(git('ls-files', '--others', '--exclude-standard', '-z', '--', ...directories)))
  else
    files = requested.length
      ? requested.map((file) => relative(root, resolve(file)))
      : paths(
          git('ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', ...directories),
        )
  if (requested.length && files.some((file) => !scoped(file) || !existsSync(resolve(root, file))))
    throw new Error('검사 대상 설계·계획 문서 경로 확인 필요')

  const errors = []
  const warnings = []
  for (const file of new Set(files.filter(scoped))) {
    if (!staged && !existsSync(resolve(root, file))) continue
    const text = staged ? git('show', `:${file}`) : readFileSync(resolve(root, file), 'utf8')
    const fail = (message) => errors.push(`${file}: ${message}`)
    let fence
    const prose = []
    for (const [i, line] of text.split('\n').entries()) {
      const marker = line.match(/^\s{0,3}(`{3,}|~{3,})(.*)$/)
      if (!fence && marker) fence = marker[1]
      else if (
        fence &&
        marker &&
        marker[1][0] === fence[0] &&
        marker[1].length >= fence.length &&
        !marker[2].trim()
      )
        fence = undefined
      else if (!fence) {
        const plain = line.replace(/(`+).*?\1/g, '').replace(/\]\([^)]*\)/g, ']')
        if (/[\u3041-\u3096\u30a1-\u30fa]/u.test(plain))
          fail(`${i + 1}행: 한국어 설명에 일본어 가나 혼입`)
        if (/\b(TODO|TBD|FIXME)\b/.test(plain)) fail(`${i + 1}행: 미완성 placeholder`)
        prose.push(line)
      }
    }
    if (fence) fail('닫히지 않은 코드 블록')
    const body = prose.join('\n')
    if (!/^#\s+\S/m.test(body)) fail('문서 제목 누락')
    // ponytail: inline Markdown links only; Markdown parser for reference-style links
    for (const match of body.matchAll(/\[[^\]\n]*\]\(([^)\s]+)\)/g)) {
      const url = match[1].replace(/^<|>$/g, '')
      if (/^(?:[a-z][a-z\d+.-]*:|\/\/|#)/i.test(url)) continue
      const target = relative(
        root,
        resolve(root, dirname(file), decodeURIComponent(url.split('#')[0])),
      )
      const found = staged
        ? index.has(target) || [...index].some((path) => path.startsWith(`${target}/`))
        : existsSync(resolve(root, target))
      if (!found) fail(`참조 파일 없음: ${url}`)
    }
    if (file.startsWith(directories[0])) {
      for (const [pattern, name] of [
        [/^#{2,3} .*?(목적|Goal|Purpose)/im, '목적'],
        [/^#{2,3} .*?(범위|Scope)/im, '범위'],
        [/^#{2,3} .*?(검증|테스트|Validation|Verification|Test)/im, '검증'],
      ]) {
        if (!pattern.test(body)) fail(`${name} 항목 누락`)
      }
    } else {
      for (const [pattern, name] of [
        [/\*\*(Goal|목표):?\*\*/i, '목표'],
        [/\*\*(Spec|설계):?\*\*.*\[[^\]]+\]\([^)]+\)/i, '설계 링크'],
        [/^## (Global Constraints|제약)/im, '제약'],
        [/^## (Review Focus|검증|리뷰)/im, '검증·리뷰'],
      ]) {
        if (!pattern.test(body)) fail(`${name} 항목 누락`)
      }
      const tasks = body.split(/^## (?:Task|작업) \d+.*$/m).slice(1)
      if (!tasks.length) fail('구현 작업 누락')
      for (const [i, task] of tasks.entries()) {
        if (!/\*\*(Files|파일):?\*\*/i.test(task) || !/^- \[[ x]\]/m.test(task))
          fail(`작업 ${i + 1}: 파일·체크리스트 누락`)
      }
    }
    if (text.split('\n').length > 500)
      warnings.push(`${file}: 500행 초과 — 승인 설계와 중복·과도한 상세화 검토`)
  }
  if (hook) {
    if (errors.length) console.log(JSON.stringify({ decision: 'block', reason: errors.join('\n') }))
    else if (warnings.length)
      console.log(JSON.stringify({ systemMessage: `문서 검사 경고\n${warnings.join('\n')}` }))
  } else {
    if (warnings.length) console.error(`문서 검사 경고\n${warnings.join('\n')}`)
    if (errors.length) console.error(`문서 검사 실패\n${errors.join('\n')}`)
    else if (files.some(scoped)) console.log('설계·계획 문서 검사 통과')
    process.exitCode = errors.length ? 1 : 0
  }
} catch (error) {
  const reason = `문서 검사 실행 실패: ${error.message}`
  if (hook) console.log(JSON.stringify({ decision: 'block', reason }))
  else {
    console.error(reason)
    process.exitCode = 1
  }
}
