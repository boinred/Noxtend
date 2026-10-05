/**
 * Bundle budget guard — runs after `vite build`.
 *
 * Plan NFR (sidebar-layout) put a number on the initial JS payload for the first time.
 * Two cycles before that, the same size was recorded but never judged, because
 * no threshold existed. A number that nothing enforces drifts back into a note.
 *
 * "Initial JS" is the entry chunk plus every statically imported chunk — exactly the
 * set Vite emits as <script type="module"> + <link rel="modulepreload"> in index.html.
 * Async chunks (dynamic import) are excluded: that is the point of splitting them.
 */
import { gzipSync } from 'node:zlib'
import { readFileSync, readdirSync } from 'node:fs'
import { resolve, dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const DIST = join(ROOT, 'dist')

/** Tailwind/shadcn foundation baseline with 15% headroom, gzip bytes. */
const BASELINE_GZIP = 109_123
const BUDGET_GZIP = Math.ceil(BASELINE_GZIP * 1.15)

function fail(message) {
  console.error(`\n✘ bundle check: ${message}\n`)
  process.exit(1)
}

function gzipBytes(path) {
  return gzipSync(readFileSync(path)).length
}

function kb(bytes) {
  return `${(bytes / 1024).toFixed(2)} kB`
}

// Vite lists the entry and every statically imported chunk in index.html.
function initialScripts(html) {
  const found = new Set()
  const patterns = [
    /<script[^>]+type="module"[^>]+src="([^"]+\.js)"/g,
    /<link[^>]+rel="modulepreload"[^>]+href="([^"]+\.js)"/g,
  ]
  for (const pattern of patterns) {
    for (const match of html.matchAll(pattern)) found.add(match[1].replace(/^\//, ''))
  }
  return [...found]
}

const html = readFileSync(join(DIST, 'index.html'), 'utf8')
const initial = initialScripts(html)
if (initial.length === 0) fail('no entry script found in dist/index.html')

const allJs = readdirSync(join(DIST, 'assets'))
  .filter((name) => name.endsWith('.js'))
  .map((name) => join('assets', name))
const asyncJs = allJs.filter((path) => !initial.includes(path))

let initialTotal = 0
console.log('\nInitial JS (entry + static imports)')
for (const path of initial) {
  const size = gzipBytes(join(DIST, path))
  initialTotal += size
  console.log(`  ${path.padEnd(44)} gzip ${kb(size).padStart(10)}`)
}
console.log(`  ${'—'.repeat(44)}      ${kb(initialTotal).padStart(10)}`)

if (asyncJs.length > 0) {
  console.log('\nAsync chunks (loaded on demand)')
  for (const path of asyncJs) {
    console.log(`  ${path.padEnd(44)} gzip ${kb(gzipBytes(join(DIST, path))).padStart(10)}`)
  }
}

if (initialTotal > BUDGET_GZIP) {
  fail(`initial JS ${kb(initialTotal)} exceeds budget ${kb(BUDGET_GZIP)}`)
}

const headroom = BUDGET_GZIP - initialTotal
console.log(
  `\n✓ initial JS ${kb(initialTotal)} / budget ${kb(BUDGET_GZIP)} (headroom ${kb(headroom)})\n`,
)
