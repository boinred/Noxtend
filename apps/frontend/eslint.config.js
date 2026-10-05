import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import reactHooks from 'eslint-plugin-react-hooks'
import eslintConfigPrettier from 'eslint-config-prettier/flat'

/**
 * Design Ref: §9.3 — 계층 간 import 규칙을 린트로 강제한다.
 * 이 규칙이 Option C의 "경계 3개" 를 실제로 지켜주는 장치다.
 *
 * 주의: `no-restricted-imports` 는 뒤에 오는 설정 블록이 앞 블록을 통째로 덮어쓴다.
 *       따라서 파일 그룹마다 필요한 패턴을 한 블록에 모두 모아 선언한다.
 */

const DENY_OUTER_FROM_DOMAIN = {
  group: ['@/features/*', '@/infra/*', '@/app/*'],
  message: 'domain/ 은 바깥 계층을 import 할 수 없습니다 (Design §9.3).',
}

const DENY_LIBS_FROM_DOMAIN = {
  group: ['react', 'react-dom', 'react/*', 'zustand', '@tanstack/*'],
  message: 'domain/ 은 순수 TypeScript 여야 합니다 (Design §1.2, §9.3). 외부 라이브러리 의존 금지.',
}

/** app-shell Design §9.2 — 라우팅은 Presentation 계층에만 존재한다 */
const DENY_ROUTER = {
  group: ['react-router', 'react-router-dom'],
  message: 'react-router-dom 은 app/ 과 features/ 안에서만 import 할 수 있습니다 (Design §9.2).',
}

const DENY_OUTER_FROM_INFRA = {
  group: ['@/features/*', '@/app/*'],
  message: 'infra/ 는 domain/ 만 import 합니다 (Design §9.3).',
}

/**
 * sidebar-layout Design §9.2 — 신설된 라우팅 계층 규칙.
 *
 * `routes/` 는 경로 정의와 화면 매핑만 한다. 상태·어댑터를 알면
 * "경로를 바꾸려다 저장 로직을 건드리는" 변경이 생긴다.
 */
const DENY_INNER_FROM_ROUTES = {
  group: ['@/infra/*'],
  message: 'routes/ 는 경로와 화면만 안다. infra/ import 금지 (Design §9.2).',
}

/**
 * `routes/index` 는 화면을 import 하므로, 화면이 다시 이를 import 하면 순환이다.
 * 경로가 필요하면 아무것도 import 하지 않는 `routes/paths` 를 쓴다.
 *
 * group 이 아니라 paths(정확한 이름 일치)를 쓴다. group 은 gitignore 문법이라
 * `@/routes` 하나로 디렉터리 전체가 막히고, 제외된 디렉터리의 자식은
 * `!` 로도 되살릴 수 없다 (gitignore 규칙). 여기서 막을 것은 두 개뿐이다.
 */
const ROUTE_TREE_MESSAGE =
  '화면은 라우트 트리를 import 할 수 없습니다 (순환). 경로는 @/routes/paths, 항목은 @/routes/navItems 를 쓰세요 (Design §9.2).'

const DENY_ROUTE_TREE_PATHS = [
  { name: '@/routes', message: ROUTE_TREE_MESSAGE },
  { name: '@/routes/index', message: ROUTE_TREE_MESSAGE },
]

const DENY_ROUTES_FROM_INNER = {
  group: ['@/routes', '@/routes/*'],
  message: 'domain/·infra/ 는 라우팅 계층을 알지 않습니다 (Design §9.2).',
}

const DENY_PROJECT_FROM_LIB = {
  group: ['@/*'],
  message: 'lib/ 은 프로젝트 계층을 참조하지 않는 순수 유틸리티입니다 (design-system Design §9).',
}

const DENY_NON_LIB_FROM_UI = {
  regex: '^@/(?!lib(?:/|$))',
  message:
    'components/ui/ 는 lib/ 외 프로젝트 계층을 참조할 수 없습니다 (design-system Design §9).',
}

const restrict = (...patterns) => ({
  'no-restricted-imports': ['error', { patterns }],
})

/** 정확한 모듈 이름 금지가 필요할 때 (group 의 디렉터리 의미론을 피한다) */
const restrictWithPaths = (paths, ...patterns) => ({
  'no-restricted-imports': ['error', { paths, patterns }],
})

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'playwright-report', 'test-results'] },

  js.configs.recommended,
  ...tseslint.configs.recommended,

  {
    files: ['src/**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      '@typescript-eslint/consistent-type-imports': [
        'error',
        { prefer: 'type-imports', fixStyle: 'separate-type-imports' },
      ],
    },
  },

  // Domain — 외부 라이브러리 전부 금지 + 바깥 계층 금지
  {
    files: ['src/domain/**/*.ts'],
    rules: restrict(
      DENY_LIBS_FROM_DOMAIN,
      DENY_ROUTER,
      DENY_ROUTES_FROM_INNER,
      DENY_OUTER_FROM_DOMAIN,
    ),
  },

  // Infra — routing and outer presentation dependencies are forbidden
  {
    files: ['src/infra/**/*.ts'],
    rules: restrict(DENY_ROUTER, DENY_ROUTES_FROM_INNER, DENY_OUTER_FROM_INFRA),
  },

  // Routing — 경로와 화면만 안다
  {
    files: ['src/routes/**/*.{ts,tsx}'],
    rules: restrict(DENY_INNER_FROM_ROUTES),
  },

  // Features — route tree reverse references are forbidden
  {
    files: ['src/features/**/*.{ts,tsx}'],
    rules: restrictWithPaths(DENY_ROUTE_TREE_PATHS),
  },

  // Design-system primitives — project layer back-references are forbidden
  {
    files: ['src/lib/**/*.{ts,tsx}'],
    rules: restrict(DENY_PROJECT_FROM_LIB),
  },
  {
    files: ['src/components/ui/**/*.{ts,tsx}'],
    rules: restrict(DENY_NON_LIB_FROM_UI),
  },

  // 테스트는 계층 규칙 대상이 아니다
  {
    files: ['src/**/*.test.ts', 'tests/**/*.ts'],
    rules: { 'no-restricted-imports': 'off' },
  },

  // 빌드 스크립트는 Node 환경에서 실행된다
  {
    files: ['scripts/**/*.mjs'],
    languageOptions: {
      globals: { Buffer: 'readonly', console: 'readonly', process: 'readonly' },
    },
  },

  // Formatter compatibility
  eslintConfigPrettier,
)
