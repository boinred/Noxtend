/**
 * Design Ref: §8.4 · §8.5 — L2/L3 의 API 대역.
 *
 * **설계는 "API (Fake LLM)" 를 적었지만 라우트 스텁으로 구현했다.** 이유가 둘이다.
 *
 * ① L2 #10(실패 주입)과 #20(API 없음)은 **살아 있는 서버 하나로 동시에 만들 수 없다.**
 *    Fake LLM 은 성공만 하고, 서버를 끄면 나머지 25건이 못 돈다.
 * ② §8.6 이 요구하는 "기존 테스트는 백엔드 없이 계속 돈다" 와 같은 제약이다 —
 *    E2E 가 k8s 클러스터를 요구하면 회귀 검증이 환경에 묶인다.
 *
 * 대신 계약은 백엔드와 같은 봉투(§4.0)와 같은 응답 모양(§4.2)을 쓴다.
 * 실제 서버와의 일치는 curl 관통과 L1-B 가 담당한다.
 */
import { expect, type Locator, type Page, type Route } from '@playwright/test'

export interface FakeApiOptions {
  /** 등록된 공급자. 빈 배열이면 스튜디오가 `/admin` 안내를 띄운다 (L2 #6) */
  providers?: FakeProvider[]
  /** 작업이 끝나는 상태. 실패 주입에 쓴다 (L2 #10) */
  outcome?: 'succeeded' | 'failed'
  /**
   * 접수 시점에 3D 를 고르는가 (사이클 #11).
   *
   * `false` 면 `models.mesh` 가 null 인 채로 끝난다 — 뒤늦게 붙이기의 대상이다.
   */
  meshAtIntake?: boolean
  /**
   * 3D 모델 목록 조회를 실패시킨다 (사이클 #11).
   *
   * 공급자는 등록돼 있는데 키가 죽었거나 공급자 쪽이 흔들리는 경우다. 그때 시작을
   * 막지 않으면 서버가 거절하는 요청을 사용자가 보내게 된다.
   */
  meshModelsError?: { status: number; code: string; message: string }
  /**
   * 3D 결과를 어떻게 낼지 (사이클 #10).
   *
   * 기본은 전부 성공이다 — 기존 시나리오가 "이미지가 다 되면 작업이 성공" 을 전제하므로
   * 3D 실패를 상시로 섞으면 그 전제가 깨진다. 상태별 렌더를 보는 테스트만 `mixed` 를 쓴다.
   */
  meshOutcome?: 'succeeded' | 'mixed' | 'none'
  /** Meshy 만 FBX 를 낸다 (사이클 #12). Tripo 결과에는 그 자리가 비어 있다. */
  meshProducesFbx?: boolean
  /** 공급자가 렌더 이미지를 늘 주지는 않는다 (사이클 #13 F-08). */
  meshHasPreview?: boolean
  /** 3D 공급자의 남은 크레딧. `null` 은 "못 읽었다" 다 (0 과 구분). */
  meshCreditBalance?: number | null
  /** 완료 전에 몇 번 running 으로 응답할지. 0이면 즉시 완료 */
  runningPolls?: number
  /** 파츠 분해 뒤 부분 이미지·3D 가 채워지는 진행 응답. */
  liveResult?: boolean
  /** 이미 존재하는 종료된 작업 — 홈 "최근 작업" 용 (L2 #19) */
  seedTerminalJobs?: number
  /**
   * 중간까지 진행된 활성 작업 하나를 심는다.
   *
   * `buildJob(id, 'running')` 은 세 공정을 전부 running 으로 만들어 진행률이 항상
   * 0% 다. 홈 spotlight 는 **"몇 번째 단계에서 몇 퍼센트인가"** 를 보여주는 것이
   * 목적이므로, 앞은 끝나고 가운데가 도는 실제 모양이 있어야 검증이 성립한다.
   */
  seedRunningJob?: boolean
  /**
   * 공급자가 지원하는 모델. 빈 배열이면 스튜디오가 실행을 막는다 —
   * 승인된 실패 경로(자유 입력 폴백 없음)의 화면 측 증거다.
   */
  models?: FakeModel[]
  /** 모델 목록 조회를 실패시킨다. 오류 코드로 안내 문구가 갈린다 */
  modelsError?: { status: number; code: string; message: string }
  /**
   * 장면 팔레트 교체 (사이클 #9).
   *
   * 흰 칩의 경계처럼 특정 색에서만 드러나는 것을 보려면 기본 fixture 로는 부족하다.
   */
  palette?: { name: string; hex: string | null }[]

  /** 미리 등록된 골든 샘플 (사이클 #5) */
  goldenSamples?: {
    id: string
    storedImageId: string
    name: string
    expectedNote: string
    createdAt: string
  }[]

  /**
   * 생성 공정의 결과 (사이클 #7).
   *
   * `'all'` 은 파츠 전부 성공, `'partial'` 은 마지막 하나만 실패, `'none'` 은 전부 실패다.
   * 부분 성공이 이 사이클의 핵심 상태라 기본값으로 둘 수 없다 — 시나리오가 명시한다.
   */
  generation?: 'none' | 'partial' | 'all'

  /** 이미지 생성 모델 목록. 빈 배열이면 스튜디오가 접수를 막는다 */
  imageModels?: FakeModel[]

  /**
   * 기준 물체 배치들의 크기 편차 (background-scale-calibration #18 D-05).
   *
   * 기본은 정상 범위다. 실측 유적 광장의 3.9배처럼 큰 값을 주면 화면이 경고를 켠다 —
   * 경고가 *켜지는* 쪽을 검증하려면 이 값을 넘겨야 한다.
   */
  anchorSpread?: number

  /**
   * 면을 덮는 파츠를 섞는다 (background-surface-parts #20).
   *
   * 표면은 축별 배율이 갈리고 요약이 그 수를 센다. 기본이 0 이면 표면 경로가 한 번도
   * 실행되지 않아, 축별 배율이 균일로 붕괴해도 화면 검증이 눈치채지 못한다.
   */
  surfaceParts?: boolean

  /**
   * 크기 기준 물체가 표면으로 표시된 상태 (#20 §6).
   *
   * 보정 계수를 못 믿는다는 뜻이라 화면이 경고를 켠다. 경고가 *켜지는* 쪽을
   * 검증하려면 이 값을 넘겨야 한다.
   */
  anchorIsSurface?: boolean

  /** 인스턴스 상한에 걸려 버려진 배치 수 (#20 FR-06) — 화면이 경고를 켠다. */
  droppedCount?: number

  /** 바닥으로 표시됐으나 GLB 는 서 있는 형태인 파츠 수 — 부차 신호 (#20 §4.1). */
  surfaceShapeMismatch?: number

  /**
   * 검수 게이트 시뮬레이션 (review-gate).
   *
   * 제품 기본값이 켬이라 접수 본문에는 늘 `requiresReview: true` 가 실리는데,
   * 게이트 이전에 쓰인 스펙들(시작 지점 120여 곳)은 분석 완료 화면을 바로 기대한다.
   * fake 는 이 옵션을 켠 스펙에서만 검수 대기로 보내고, 나머지는 곧장 완료시킨다 —
   * 게이트 동작 자체는 review-gate.spec 이 이 옵션으로 검증한다.
   */
  reviewGate?: boolean

  /** 골든 샘플의 실행 이력. 비교 화면이 읽는다 */
  goldenRuns?: {
    jobId: string
    status: string
    model: string | null
    promptVersions: Record<string, number>
    partCount: number
    verdict: { isPass: boolean; memo: string; at: string } | null
    createdAt: string
  }[]
}

/** 모델 필드가 없다 — 공급자는 접속 수단이고 모델은 공정이 갖는다 */
export interface FakeProvider {
  id: string
  displayName: string
  kind: string
  capabilities?: ('textAnalysis' | 'imageGeneration' | 'meshGeneration' | 'similarityEvaluation')[]
  apiKeyMasked: string
  isEnabled: boolean
}

export interface FakeModel {
  id: string
  displayName: string
}

export const DEFAULT_PROVIDER: FakeProvider = {
  id: 'provider-1',
  displayName: 'OpenAI 운영',
  kind: 'openai',
  capabilities: ['textAnalysis', 'imageGeneration', 'similarityEvaluation'],
  apiKeyMasked: '••••••••4f2c',
  isEnabled: true,
}

/** 서버가 이미 걸러 보낸 목록이라는 전제 — 여기 있는 것은 모두 추출 가능한 모델이다 */
export const DEFAULT_MODELS: FakeModel[] = [
  { id: 'claude-opus-5', displayName: 'Claude Opus 5' },
  { id: 'claude-sonnet-5', displayName: 'Claude Sonnet 5' },
]

/**
 * 이미지 생성 모델 — **텍스트 목록과 다른 엔드포인트**에서 온다 (사이클 #7 §4.2 #7).
 *
 * 한 목록에 섞으면 사용자가 텍스트 단계에 이미지 모델을 고를 수 있고, 그 오류는
 * 접수가 아니라 실행 시점에야 드러난다.
 */
export const DEFAULT_IMAGE_MODELS: FakeModel[] = [
  { id: 'gpt-image-2', displayName: 'GPT Image 2' },
  { id: 'gemini-3.1-flash-image', displayName: 'Gemini 3.1 Flash Image' },
]

/**
 * 장면 명세 — 사이클 #5 에서 자유 문장을 대체한다.
 *
 * `camera` · `light` · `scale` 이 조립을 좌우하는 셋이다. 실제 서버가 이 셋 없이는
 * 공정을 실패시키므로 Fake 도 반드시 채운다.
 */
const SCENE = {
  /**
   * 이름과 색이 나뉜 신규 계약 (사이클 #9).
   *
   * 마지막 칸은 색상어를 식별할 수 없는 **레거시 항목**이다. 구형 `string[]` 을 프론트에
   * 직접 주지는 않는다 — 그 해석 책임은 백엔드 저장 경계에 있고, 화면이 보는 것은 승격이
   * 끝난 뒤의 `hex: null` 하나뿐이다 (§8.4).
   */
  palette: [
    { name: '청회색 바다', hex: '#2E5C6E' },
    { name: '주황색 목재', hex: '#D97A3C' },
    { name: '옅은 안개', hex: '#F2E8DC' },
    { name: '젖은 포장 표면', hex: null },
  ],
  timeOfDay: '해질녘',
  mood: '고요하고 서늘함',
  renderingStyle: '수채 느낌의 반사실',
  materialFeel: '거친 목재와 젖은 돌',
  camera: { type: 'one-point', eyeLevel: '지면에서 1.6m', horizonY: 0.55 },
  light: { direction: '좌측 후방 15° 고도', temperature: '따뜻함', shadowHardness: '부드러움' },
  scale: { object: '부두 기둥', realWorldSize: '높이 3m', heightMeters: 3 },
}

/** 실행 순서. 화면이 "어느 단계에서 멈췄나" 를 보여주므로 셋 다 필요하다 */
const STAGES = ['analyze', 'extract', 'decompose'] as const
const VIEW_DIRECTIONS = ['front', 'right', 'back', 'left'] as const

// 프롬프트 관리의 이미지 생성 단계 포함
// main 의 재서술 단계(occludedby-recompute)도 실제 시드처럼 기본 슬롯을 갖는다
const PROMPT_STAGES = [...STAGES, 'rewriteDescriptions', 'generate'] as const

// 에셋 카테고리 — 격자 열과 폴백 계산에 쓴다 (prompt-category-axis)
const PROMPT_CATEGORIES = ['character', 'object', 'background'] as const

/** 1x1 투명 PNG. 화면이 `<img>` 를 실제로 그리는지만 보면 되므로 내용은 최소다. */
const PNG_PIXEL = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64',
)

/**
 * 공정별 호출 내역 (사이클 #7 FR-18).
 *
 * **넷이 서로 다른 모양이어야 한다**: 토큰 호출 · 장당 호출 · 단가 미등록 · 사용량 없음.
 * 화면이 이 넷을 뭉개지 않는지가 FR-20 과 C-6·C-7 의 검증 대상이다.
 *
 * 앞서 셋이던 시절, "단가 미등록" 자리에 선 것은 **사용량이 없는 실패 호출**이었다 —
 * 단가표를 보기도 전에 `null` 이 되는 모양이라 미등록 경로를 한 번도 밟지 않았다.
 */
const JOB_CALLS = [
  {
    id: 'call-1',
    taskId: 'task-job-1-analyze',
    kind: 'analyze',
    promptVersionId: 'prompt-seed-analyze',
    model: 'claude-opus-5',
    requestPayload: '[system]\n장면을 분석하라',
    responsePayload: '{"palette":[]}',
    inputTokens: 1240,
    outputTokens: 380,
    latencyMs: 4200,
    succeeded: true,
    failureReason: null,
    at: '2026-07-28T12:00:12Z',
    estimatedCostUsd: 0.011,
    outputImages: null,
  },
  {
    // 이미지 호출 — 토큰 칸이 비고 장 수가 들어간다 (D-16 · C-6)
    id: 'call-2',
    taskId: 'task-job-1-generate-0',
    kind: 'generate',
    promptVersionId: 'prompt-seed-generate',
    model: 'gpt-image-1',
    requestPayload: '[prompt]\n단독 파츠를 그려라',
    responsePayload: 'image/png · 20480바이트',
    inputTokens: null,
    outputTokens: null,
    latencyMs: 38100,
    succeeded: true,
    failureReason: null,
    at: '2026-07-28T12:00:58Z',
    estimatedCostUsd: 0.04,
    outputImages: 1,
  },
  {
    // 단가 미등록 — 쓴 것은 있는데 단가 행이 없다. 화면이 이것을 `$0` 으로 뭉개면
    // 합계가 실제보다 낮게 읽힌다 (FR-20). 사용량이 **있어야** 이 경우가 된다
    id: 'call-3',
    taskId: 'task-job-1-generate-2',
    kind: 'generate',
    promptVersionId: 'prompt-seed-generate',
    model: 'unlisted-image-model',
    requestPayload: '[prompt]\n단독 파츠를 그려라',
    responsePayload: 'image/png · 20480바이트',
    inputTokens: 2075,
    outputTokens: 1377,
    latencyMs: 15400,
    succeeded: true,
    failureReason: null,
    at: '2026-07-28T12:01:04Z',
    estimatedCostUsd: null,
    outputImages: 1,
  },
  {
    // 사용량 없음 — 실패해서 쓴 것이 없다. 단가를 등록해도 값이 생기지 않으므로
    // 미등록과 같은 낱말로 적으면 운영자가 고칠 수 없는 것을 고치러 간다
    id: 'call-4',
    taskId: 'task-job-1-generate-3',
    kind: 'generate',
    promptVersionId: 'prompt-seed-generate',
    model: 'gpt-image-1',
    requestPayload: '[prompt]\n단독 파츠를 그려라',
    responsePayload: null,
    inputTokens: null,
    outputTokens: null,
    latencyMs: 12000,
    succeeded: false,
    failureReason: 'GENERATION_EMPTY_RESPONSE',
    at: '2026-07-28T12:01:10Z',
    estimatedCostUsd: null,
    outputImages: null,
  },
]
const PART_NAMES = ['등대', '목조 부두', '어선', '가로등']

/**
 * 3D 공급자 (사이클 #10).
 *
 * 텍스트·이미지와 따로 둔다 — capability 가 다르므로 화면이 세 목록을 각자 거른다.
 */
export const MESH_PROVIDER: FakeProvider = {
  id: 'provider-mesh',
  displayName: 'Tripo 운영',
  kind: 'tripo',
  capabilities: ['meshGeneration'],
  apiKeyMasked: '••••••••9a1b',
  isEnabled: true,
}

export const MESH_MODELS = [{ id: 'P1-20260311', displayName: 'Tripo P1 (Low Poly)' }]

/**
 * 두 번째 3D 공급자 (사이클 #12).
 *
 * **Tripo 와 다른 것은 FBX 뿐이다** — 화면에서 보이는 차이는 그것 하나이므로,
 * 가짜에서도 그 차이만 지키면 "FBX 는 있을 때만" 규칙을 그대로 검증할 수 있다.
 */
export const MESHY_PROVIDER: FakeProvider = {
  id: 'provider-meshy',
  displayName: 'Meshy 운영',
  kind: 'meshy',
  capabilities: ['meshGeneration'],
  apiKeyMasked: '••••••••7c3d',
  isEnabled: true,
}

export const MESHY_MODELS = [{ id: 'meshy-7', displayName: 'Meshy 7 (Multi-Image)' }]

/**
 * 삼각형 하나짜리 glTF 2.0 이진 컨테이너 (사이클 #13).
 *
 * **형식을 흉내내는 것이 아니라 실제로 열리는 바이트다.** 가짜 GLB 를 내주지 않으면
 * 뷰어가 `loadfailure` 로 떨어져 실패 상태만 검사하게 되고, "실제로 불러오는가" 를
 * 볼 수 없다.
 *
 * **장면과 mesh 가 둘 다 있어야 한다.** `asset` 만 넣으면 "Model does not have a
 * scene" 으로, 빈 장면만 넣으면 `loadfailure` 로 거절당한다 — 둘 다 실측으로 확인했다.
 */
function minimalGlb(): Buffer {
  // 삼각형 세 꼭짓점 × float 셋
  const positions = new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0])
  const bin = Buffer.from(positions.buffer)

  const json = Buffer.from(
    JSON.stringify({
      asset: { version: '2.0' },
      scene: 0,
      scenes: [{ nodes: [0] }],
      nodes: [{ mesh: 0 }],
      meshes: [{ primitives: [{ attributes: { POSITION: 0 } }] }],
      buffers: [{ byteLength: bin.length }],
      bufferViews: [{ buffer: 0, byteOffset: 0, byteLength: bin.length, target: 34962 }],
      accessors: [
        {
          bufferView: 0,
          componentType: 5126,
          count: 3,
          type: 'VEC3',
          min: [0, 0, 0],
          max: [1, 1, 0],
        },
      ],
    }),
    'utf8',
  )

  // 청크는 4바이트 경계에 맞아야 한다 — JSON 은 공백, BIN 은 0 으로 채운다
  const jsonChunk = Buffer.concat([json, Buffer.alloc((4 - (json.length % 4)) % 4, 0x20)])
  const binChunk = Buffer.concat([bin, Buffer.alloc((4 - (bin.length % 4)) % 4, 0)])

  const total = 12 + 8 + jsonChunk.length + 8 + binChunk.length
  const glb = Buffer.alloc(total)

  glb.write('glTF', 0, 'ascii')
  glb.writeUInt32LE(2, 4)
  glb.writeUInt32LE(total, 8)

  glb.writeUInt32LE(jsonChunk.length, 12)
  glb.writeUInt32LE(0x4e4f534a, 16) // 'JSON'
  jsonChunk.copy(glb, 20)

  const binHeader = 20 + jsonChunk.length
  glb.writeUInt32LE(binChunk.length, binHeader)
  glb.writeUInt32LE(0x004e4942, binHeader + 4) // 'BIN\0'
  binChunk.copy(glb, binHeader + 8)

  return glb
}

function ok(route: Route, data: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ data, error: null }),
  })
}

function fail(route: Route, status: number, code: string, message: string) {
  return route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify({ data: null, error: { code, message, fields: null } }),
  })
}

/** 봉투와 응답 모양을 백엔드와 맞춘 인메모리 API. */
export async function installFakeApi(page: Page, options: FakeApiOptions = {}) {
  const {
    providers: initialProviders = [DEFAULT_PROVIDER],
    meshOutcome = 'succeeded',
    meshAtIntake = true,
    meshProducesFbx = false,
    meshHasPreview = true,
    meshCreditBalance = 2_400,
    meshModelsError,
    outcome = 'succeeded',
    runningPolls = 0,
    liveResult = false,
    seedTerminalJobs = 0,
    seedRunningJob = false,
    models = DEFAULT_MODELS,
    modelsError,
    generation = 'all',
    imageModels = DEFAULT_IMAGE_MODELS,
    palette,
    reviewGate = false,
    anchorSpread = 1.2,
    surfaceParts = false,
    anchorIsSurface = false,
    droppedCount = 0,
    surfaceShapeMismatch = 0,
  } = options

  const providers = [...initialProviders]

  /** 단계별 허용 변수 — 서버의 `PromptTemplate.AllowedVariables` 와 같아야 한다 */
  const ALLOWED_VARIABLES: Record<string, string[]> = {
    analyze: [],
    // gender·partHints 는 캐릭터 고유 변수다 (character-studio §D-03). 종류는 추출, 개수는 분해로 흐른다
    extract: ['scene', 'gender', 'partHints'],
    decompose: ['scene', 'parts', 'gender', 'partHints'],
    // 생성은 파츠 하나를 그린다 — 목록이 아니라 그 파츠의 값들이다 (사이클 #7 §3.5).
    // gender 는 베이스바디에 필수라 넣고, partHints 는 제외한다 (§D-03)
    rewriteDescriptions: ['scene', 'targets'],
    generate: ['scene', 'partName', 'partDescription', 'partCategory', 'viewDirection', 'gender'],
  }

  const prompts = PROMPT_STAGES.map((kind) => ({
    id: `prompt-seed-${kind}`,
    kind,
    // 시드는 기본 슬롯(카테고리 없음) — 전용은 화면에서 만든다
    category: null as string | null,
    version: 1,
    system: kind === 'analyze' ? '장면을 분석하라' : `프롬프트 {{scene}}`,
    user: 'user',
    jsonSchema: '{}',
    note: '초기 버전',
    isActive: true,
    allowedVariables: ALLOWED_VARIABLES[kind]!,
    createdAt: new Date(Date.UTC(2026, 6, 31)).toISOString(),
  })) as Record<string, unknown>[] as {
    id: string
    kind: string
    category: string | null
    version: number
    system: string
    user: string
    jsonSchema: string
    note: string | null
    isActive: boolean
    allowedVariables: string[]
    createdAt: string
  }[]

  // 유사도 평가 슬롯 — 실제 시드처럼 Background 전용 활성이다 (background-similarity-tuning §15.1)
  prompts.push({
    id: 'prompt-seed-similarity',
    kind: 'similarityEvaluate',
    category: 'background',
    version: 1,
    system: '원본과 렌더를 비교하라',
    user: '',
    jsonSchema: '{}',
    note: '초기 버전',
    isActive: true,
    allowedVariables: [],
    createdAt: new Date(Date.UTC(2026, 8, 1)).toISOString(),
  })

  const golden: {
    id: string
    storedImageId: string
    name: string
    expectedNote: string
    createdAt: string
  }[] = [...(options.goldenSamples ?? [])]

  /** 비교 화면이 읽는 실행 이력 */
  const goldenRunList: {
    jobId: string
    status: string
    model: string | null
    promptVersions: Record<string, number>
    partCount: number
    verdict: { isPass: boolean; memo: string; at: string } | null
    createdAt: string
  }[] = [...(options.goldenRuns ?? [])]
  /**
   * 단가 — 모델당 시행일별로 한 행.
   *
   * 인상 행을 미리 심어 둔다. 화면이 "적용 중" 과 "예정" 을 구분하는지가
   * 이 표의 핵심이라, 한 행짜리 표로는 그것을 검증할 수 없다.
   */
  const modelPrices: {
    id: string
    model: string
    inputPerMillion: number
    outputPerMillion: number
    longContextFrom: number | null
    longInputPerMillion: number | null
    longOutputPerMillion: number | null
    effectiveFrom: string
    note: string
    perImage: number | null
  }[] = [
    {
      id: 'price-luna-1',
      model: 'gpt-5.6-luna',
      inputPerMillion: 0.2,
      outputPerMillion: 1.2,
      longContextFrom: 272000,
      longInputPerMillion: 0.4,
      longOutputPerMillion: 1.8,
      effectiveFrom: '2026-01-01T00:00:00Z',
      note: 'OpenAI 공식 단가',
      perImage: null,
    },
    {
      id: 'price-luna-2',
      model: 'gpt-5.6-luna',
      inputPerMillion: 0.25,
      outputPerMillion: 1.5,
      longContextFrom: null,
      longInputPerMillion: null,
      longOutputPerMillion: null,
      effectiveFrom: '2099-01-01T00:00:00Z',
      note: '인상 예정',
      perImage: null,
    },
    {
      id: 'price-opus-1',
      model: 'claude-opus-5',
      inputPerMillion: 5,
      outputPerMillion: 25,
      longContextFrom: null,
      longInputPerMillion: null,
      longOutputPerMillion: null,
      effectiveFrom: '2026-01-01T00:00:00Z',
      note: 'Anthropic 공식 단가',
      perImage: null,
    },
    {
      // 장당 과금 행 (사이클 #7 D-8) — 토큰 칸이 0 이고 계산에 쓰이지 않는다
      id: 'price-image-1',
      model: 'gpt-image-1',
      inputPerMillion: 0,
      outputPerMillion: 0,
      longContextFrom: null,
      longInputPerMillion: null,
      longOutputPerMillion: null,
      effectiveFrom: '2026-01-01T00:00:00Z',
      note: 'OpenAI 이미지 단가',
      perImage: 0.04,
    },
    // 3D 공급자가 있는 시나리오만 해당 단가를 심는다. 기본 관리자 단가표 픽스처는 유지한다.
    ...(providers.some((provider) => provider.kind === 'tripo')
      ? [
          {
            id: 'price-tripo-1',
            model: 'P1-20260311',
            inputPerMillion: 0,
            outputPerMillion: 0,
            longContextFrom: null,
            longInputPerMillion: null,
            longOutputPerMillion: null,
            effectiveFrom: '2026-01-01T00:00:00Z',
            note: 'Tripo 30크레딧 × $0.01',
            perImage: 0.3,
          },
        ]
      : []),
    ...(providers.some((provider) => provider.kind === 'meshy')
      ? [
          {
            id: 'price-meshy-1',
            model: 'meshy-7',
            inputPerMillion: 0,
            outputPerMillion: 0,
            longContextFrom: null,
            longInputPerMillion: null,
            longOutputPerMillion: null,
            effectiveFrom: '2026-01-01T00:00:00Z',
            note: 'Meshy 30크레딧 × $0.0133',
            perImage: 0.4,
          },
        ]
      : []),
  ]

  /**
   * 접수 때 고른 공급자·모델.
   *
   * 서버가 이것을 기억하지 않으면 재시도 프리필을 E2E 로 볼 수 없다 — 화면이 이어받는
   * 값의 출처가 응답이기 때문이다.
   */
  type JobModels = {
    text: { providerConfigId: string; model: string } | null
    image: { providerConfigId: string; model: string } | null
    mesh: { providerConfigId: string; model: string } | null
  }

  const DEFAULT_JOB_MODELS: JobModels = {
    text: { providerConfigId: DEFAULT_PROVIDER.id, model: models[0]?.id ?? 'claude-opus-5' },
    image: {
      providerConfigId: DEFAULT_PROVIDER.id,
      model: imageModels[0]?.id ?? 'gpt-image-2',
    },
    mesh: meshAtIntake ? { providerConfigId: MESH_PROVIDER.id, model: MESH_MODELS[0]!.id } : null,
  }

  const jobs = new Map<string, Record<string, unknown>>()
  const pollCounts = new Map<string, number>()
  // 유사도 run — job 당 하나 (background-similarity-tuning §5.1). 폴링 한 번 뒤 결과가 나온다
  const similarityRuns = new Map<string, Record<string, unknown>>()
  const adoptedLayouts = new Map<string, string>()
  // revision 이력 — 복원 화면의 대역. job 마다 r1(자동 배치)로 시작한다
  const sceneRevisions = new Map<string, Record<string, unknown>[]>()
  const revisionsFor = (jobId: string) => {
    if (!sceneRevisions.has(jobId)) {
      sceneRevisions.set(jobId, [
        {
          id: 'b1000000-0000-4000-8000-000000000001',
          revision: 1,
          state: 'active',
          origin: 'composed',
          composedAt: new Date(Date.UTC(2026, 7, 17)).toISOString(),
        },
      ])
    }
    return sceneRevisions.get(jobId)!
  }
  let sequence = 0

  for (let index = 0; index < seedTerminalJobs; index += 1) {
    const id = `seed-job-${index}`
    jobs.set(id, buildJob(id, 'succeeded'))
  }

  if (seedRunningJob) {
    // 장면 완료 · 추출 진행 중 · 분해 대기 → current=2/3, 확정 완료율 33%
    const job = buildJob('seed-running', 'running')
    const tasks = job.tasks as Record<string, unknown>[]
    tasks[0]!.status = 'succeeded'
    tasks[0]!.completedAt = new Date(Date.UTC(2026, 6, 28, 12, 0, 12)).toISOString()
    tasks[1]!.status = 'running'
    tasks[2]!.status = 'pending'
    tasks[2]!.startedAt = null
    jobs.set('seed-running', job)
  }

  /** 이 방향 이미지가 생성됐는가 — 부분 성공은 마지막 파츠의 좌측 한 장만 실패한다. */
  function generatedFor(partOrdinal: number, viewDirection: string): boolean {
    if (generation === 'none') return false
    if (generation === 'partial') {
      return partOrdinal < PART_NAMES.length - 1 || viewDirection !== 'left'
    }

    return true
  }

  /** 파츠마다 방향별 공정 네 개. 마지막 좌측 한 장 실패가 부분 성공을 재현한다. */
  function generationTasks(id: string) {
    return PART_NAMES.flatMap((_, partOrdinal) =>
      VIEW_DIRECTIONS.map((viewDirection, viewOrdinal) => {
        const succeeded = generatedFor(partOrdinal, viewDirection)

        return {
          id: `task-${id}-generate-${partOrdinal}-${viewDirection}`,
          kind: 'generate',
          ordinal: STAGES.length + partOrdinal * VIEW_DIRECTIONS.length + viewOrdinal,
          status: succeeded ? 'succeeded' : 'failed',
          failureReason: succeeded ? null : 'GENERATION_EMPTY_RESPONSE',
          attemptCount: succeeded ? 1 : 3,
          startedAt: new Date(Date.UTC(2026, 6, 28, 12, 0, 20)).toISOString(),
          completedAt: new Date(Date.UTC(2026, 6, 28, 12, 0, 58)).toISOString(),
          partId: `part-${id}-${partOrdinal}`,
          viewDirection,
          progress: null as number | null,
        }
      }),
    )
  }

  /**
   * 파츠마다 3D 공정 하나 (사이클 #10).
   *
   * **상태를 갈라 둔다** — 화면의 갈래가 일곱이라 하나만 두면 나머지를 못 본다.
   * 첫 파츠는 완료, 둘째는 진행, 셋째는 실패, 나머지는 대기다.
   */
  function reconstructTasks(id: string) {
    return PART_NAMES.map((_, partOrdinal) => {
      const status = meshStatusFor(partOrdinal)

      return {
        id: `task-${id}-reconstruct-${partOrdinal}`,
        kind: 'reconstruct',
        ordinal: STAGES.length + PART_NAMES.length * VIEW_DIRECTIONS.length + partOrdinal,
        status,
        failureReason: status === 'failed' ? 'MESH_TASK_FAILED' : null,
        attemptCount: 1,
        startedAt: new Date(Date.UTC(2026, 6, 28, 12, 2, 0)).toISOString(),
        completedAt:
          status === 'succeeded' || status === 'failed'
            ? new Date(Date.UTC(2026, 6, 28, 12, 4, 30)).toISOString()
            : null,
        partId: `part-${id}-${partOrdinal}`,
        // 3D 는 파츠 하나를 통째로 만든다 — 방향이 없다
        viewDirection: null,
        progress: status === 'running' ? 64 : null,
      }
    })
  }

  /**
   * 파츠별 3D 상태.
   *
   * `mixed` 는 화면의 갈래를 한 번에 보기 위한 것이다 — 완료·진행·실패·대기가 한 줄씩
   * 나온다. 기본은 전부 성공이라 기존 시나리오의 전제를 건드리지 않는다.
   */
  function meshStatusFor(partOrdinal: number) {
    if (meshOutcome === 'none') {
      return 'pending' // 완성 GLB 0 — "3D 배경" 탭이 잠겨야 한다 (FR-04)
    }

    if (meshOutcome === 'succeeded') {
      return 'succeeded'
    }

    const states = ['succeeded', 'running', 'failed', 'pending'] as const
    return states[Math.min(partOrdinal, states.length - 1)]!
  }

  /**
   * 팬아웃 결과가 작업 상태에 반영된 값.
   *
   * 성공한 파츠가 하나라도 있고 실패가 섞이면 **부분 성공**이다 — 이 구분이
   * 사이클 #7 의 핵심이라 Fake 도 뭉개지 않는다.
   */
  function terminalStatus(status: string): string {
    if (status !== 'succeeded') return status
    if (generation === 'all') return 'succeeded'

    return generation === 'partial' ? 'partiallySucceeded' : 'failed'
  }

  function buildJob(
    id: string,
    status: string,
    selected: JobModels = DEFAULT_JOB_MODELS,
    category = 'background',
    gender: string | null = null,
    partHints: { type: string; count: number; variant?: string }[] = [],
  ) {
    const done = status === 'succeeded'

    return {
      id,
      category,
      status: done ? terminalStatus(status) : status,
      sourceImageId: `upload-${id}`,
      scene: done ? { ...SCENE, palette: palette ?? SCENE.palette } : null,
      models: selected,
      // 캐릭터 "다시 시도" 이어받기의 입력(character-mesh-ui §FR-08) — 배경·오브젝트는 null/빈 배열
      gender,
      partHints,
      // 공정이 셋이다. 실패는 첫 공정에서 나고 뒤 둘은 대기로 남는다 —
      // 실제 오케스트레이터가 앞이 실패하면 뒤를 적재하지 않는다
      tasks: [
        ...STAGES.map((kind, ordinal) => ({
          id: `task-${id}-${kind}`,
          kind,
          ordinal,
          status: status === 'failed' && ordinal > 0 ? 'pending' : status,
          failureReason: status === 'failed' && ordinal === 0 ? 'PROVIDER_CALL_FAILED' : null,
          // 진행 표시가 읽는다. 실패한 단계는 한도까지 재시도됐다고 본다
          attemptCount: status === 'failed' && ordinal === 0 ? 3 : 1,
          startedAt:
            status === 'pending' ? null : new Date(Date.UTC(2026, 6, 28, 12, 0, 0)).toISOString(),
          completedAt:
            done || status === 'failed'
              ? new Date(Date.UTC(2026, 6, 28, 12, 0, 12)).toISOString()
              : null,
          partId: null as string | null,
          viewDirection: null,
          progress: null as number | null,
        })),
        // 팬아웃 (사이클 #7) — 분해가 끝나야 생기므로 done 일 때만 존재한다.
        // Ordinal 이 3 부터라 사용자가 본 파츠 순서와 진행 표시가 일치한다 (C-2)
        ...(done ? generationTasks(id) : []),

        // 팬인 (사이클 #10) — 파츠마다 하나씩. 상태를 갈라 두어 화면의 갈래를 다 볼 수 있게 한다
        ...(done ? reconstructTasks(id) : []),
      ],
      parts: done
        ? PART_NAMES.map((name, ordinal) => {
            // API가 방향별 최신 이미지 네 개와 정면 호환 필드를 함께 제공
            const generatedImages = VIEW_DIRECTIONS.filter((viewDirection) =>
              generatedFor(ordinal, viewDirection),
            ).map((viewDirection) => ({
              id: `image-${id}-${ordinal}-${viewDirection}`,
              viewDirection,
            }))

            return {
              id: `part-${id}-${ordinal}`,
              name,
              ordinal,
              description: `${name} — 낡은 표면, 해수에 색이 바램`,
              category: '구조물',
              // 첫 파츠만 배치 셋 — "전부 켜짐" 을 검증하려면 여럿인 것이 있어야 한다
              placements:
                ordinal === 0
                  ? [
                      { x: 0.05, y: 0.1, w: 0.12, h: 0.2 },
                      { x: 0.3, y: 0.12, w: 0.1, h: 0.18 },
                      { x: 0.55, y: 0.11, w: 0.11, h: 0.19 },
                    ]
                  : [{ x: 0.05, y: 0.1 + ordinal * 0.05, w: 0.3, h: 0.2 }],
              depthOrder: ordinal + 1,
              occludedBy: [],
              generatedImageId:
                generatedImages.find((image) => image.viewDirection === 'front')?.id ?? null,
              generatedImages,
              // 공정이 성공한 파츠에만 결과가 붙는다 — 화면의 성공 판정 기준이 결과다
              generatedMesh:
                meshStatusFor(ordinal) === 'succeeded'
                  ? {
                      id: `mesh-${id}-${ordinal}`,
                      hasPreview: meshHasPreview,
                      hasFbx: meshProducesFbx,
                      sizeBytes: 4_821_900,
                      fbxSizeBytes: meshProducesFbx ? 5_310_000 : null,
                      creditsConsumed: 50,
                      createdAt: new Date(Date.UTC(2026, 6, 28, 12, 5, 0)).toISOString(),
                    }
                  : null,
            }
          })
        : [],
      failureReason: status === 'failed' ? 'PROVIDER_CALL_FAILED' : null,
      createdAt: new Date(Date.UTC(2026, 6, 28, 12, index())).toISOString(),
      completedAt: status === 'pending' || status === 'running' ? null : new Date().toISOString(),
    }

    function index() {
      return 0
    }
  }

  /**
   * 분해 완료 뒤의 부분 결과 응답.
   *
   * 방향 공정 전체는 계획됐지만 앞 두 방향만 도착한 상태를 고정하고, 폴링 단계에 따라
   * 3D 결과만 하나 더 채운다. 같은 파츠 자리가 교체되는 화면을 시간 대기 없이 검증한다.
   */
  function buildLiveJob(id: string, selected: JobModels, category: string, phase: number) {
    const live = buildJob(id, 'succeeded', selected, category)
    const tasks = live.tasks as Record<string, unknown>[]
    const parts = live.parts as Record<string, unknown>[]
    const readyMeshes = phase > 0 ? 2 : 1

    live.status = 'running'
    live.completedAt = null

    // 선행 분석·추출·분해 완료와 방향 이미지 절반 도착
    for (const task of tasks) {
      if (STAGES.includes(task.kind as (typeof STAGES)[number])) {
        task.status = 'succeeded'
        continue
      }

      if (task.kind === 'generate') {
        // 앞 두 파츠는 4방향 완료, 뒤 둘은 생성 중·대기열 — 총 8/16 부분 결과
        const ready = task.partId === `part-${id}-0` || task.partId === `part-${id}-1`
        const drawing = task.partId === `part-${id}-2` && task.viewDirection === 'front'
        task.status = ready ? 'succeeded' : drawing ? 'running' : 'pending'
        task.failureReason = null
        task.completedAt = ready ? task.completedAt : null
      }
    }

    // 3D 한 칸 완성·다음 칸 진행·나머지 대기
    for (const [ordinal, part] of parts.entries()) {
      const reconstruct = tasks.find(
        (task) => task.kind === 'reconstruct' && task.partId === part.id,
      )
      if (reconstruct === undefined) continue

      reconstruct.status =
        ordinal < readyMeshes ? 'succeeded' : ordinal === readyMeshes ? 'running' : 'pending'
      reconstruct.progress = ordinal === readyMeshes ? 42 : null
      reconstruct.completedAt = ordinal < readyMeshes ? reconstruct.completedAt : null

      const generatedImages = ordinal < 2 ? (part.generatedImages as Record<string, unknown>[]) : []
      part.generatedImages = generatedImages
      part.generatedImageId =
        generatedImages.find((image) => image.viewDirection === 'front')?.id ?? null
      if (ordinal >= readyMeshes) part.generatedMesh = null
    }

    return live
  }

  /**
   * review-gate — 분해 완료 상태에서 팬아웃 공정·생성 결과만 걷어낸다.
   *
   * `buildJob('succeeded', ...)` 이 이미 만든 파츠 좌표·서술을 그대로 재사용한다 —
   * 검수 화면이 그릴 좌표가 필요하지 실제 생성 결과는 아직 없어야 하기 때문이다.
   */
  function toPendingReview(job: Record<string, unknown>): Record<string, unknown> {
    const tasks = (job.tasks as Record<string, unknown>[]).filter((task) =>
      STAGES.includes(task.kind as (typeof STAGES)[number]),
    )
    const parts = (job.parts as Record<string, unknown>[]).map((part) => ({
      ...part,
      generatedImageId: null,
      generatedImages: [],
      generatedMesh: null,
      descriptionSource: 'model',
    }))

    // review-gate-staged — 상자 단계에서 시작. 서버 응답에 없는 fake 내부 필드
    return {
      ...job,
      status: 'pendingReview',
      tasks,
      parts,
      completedAt: null,
      reviewPhase: 'boxes',
    }
  }

  /** 겹친 넓이 ÷ 더 작은 쪽 넓이 — 백엔드 `Bounds.OverlapCoefficient`와 같은 규칙(결정로그 T-03). */
  function overlapCoefficient(
    a: { x: number; y: number; w: number; h: number },
    b: { x: number; y: number; w: number; h: number },
  ): number {
    const iw = Math.max(0, Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x))
    const ih = Math.max(0, Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y))
    const intersection = iw * ih
    if (intersection === 0) return 0

    return intersection / Math.min(a.w * a.h, b.w * b.h)
  }

  const OVERLAP_THRESHOLD = 0.15

  /** 검수 화면이 그릴 파츠 한 줄 — `id` 접두사로 "직접 추가" 파츠를 가른다. */
  function reviewPartFrom(part: Record<string, unknown>) {
    return {
      id: part.id,
      partRef: `P${String((part.ordinal as number) + 1).padStart(2, '0')}`,
      name: part.name,
      category: part.category,
      description: part.description,
      placements: part.placements,
      source: String(part.id).startsWith('manual-part-') ? 'manual' : 'detected',
      occludedBy: part.occludedBy ?? [],
      occludes: [],
      descriptionSource: part.descriptionSource ?? 'model',
    }
  }

  /** 검수 상태 전체 — GET review 와 편집 응답 공통. */
  function reviewStateFrom(job: Record<string, unknown>) {
    return {
      status: job.status,
      parts: (job.parts as Record<string, unknown>[]).map(reviewPartFrom),
      descriptionsStale: [],
      // 서버는 상태가 아니라 RequiresReview·ReviewPhase 로만 계산한다(독립 리뷰 #6) — 재작성이
      // 도는 동안(status running)에도 reviewPhase 는 이미 'descriptions' 로 앞서 바뀌어 있다
      reviewPhase: job.reviewPhase && job.reviewPhase !== 'approved' ? job.reviewPhase : null,
      palette: (job.scene as { palette?: unknown[] } | null)?.palette ?? [],
    }
  }

  /** 검수 대기 + 단계 검사. 통과하면 null, 아니면 실패 응답. */
  function reviewPhaseGuard(
    route: Route,
    job: Record<string, unknown>,
    phase: 'boxes' | 'descriptions',
  ) {
    if (job.status !== 'pendingReview') {
      return fail(route, 409, 'REVIEW_NOT_PENDING', '검수 대기 상태가 아닙니다')
    }
    if ((job.reviewPhase ?? 'boxes') !== phase) {
      return fail(route, 409, 'REVIEW_PHASE_MISMATCH', '검수 단계가 맞지 않습니다')
    }
    return null
  }

  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const method = request.method()

    // 종류별 사용 용도 — 관리자 폼이 등록 전에 읽는다 (서버가 유일한 출처)
    if (path === '/api/providers/capabilities' && method === 'GET') {
      return ok(route, [
        {
          kind: 'openai',
          capabilities: ['textAnalysis', 'imageGeneration', 'similarityEvaluation'],
        },
        { kind: 'anthropic', capabilities: ['textAnalysis', 'similarityEvaluation'] },
        { kind: 'google', capabilities: ['imageGeneration'] },
        { kind: 'tripo', capabilities: ['meshGeneration'] },
        { kind: 'meshy', capabilities: ['meshGeneration'] },
      ])
    }

    // ─── 유사도 (background-similarity-tuning §10) ───

    const similarityStatus = /^\/api\/jobs\/([^/]+)\/similarity$/.exec(path)
    if (similarityStatus && method === 'GET') {
      const job = jobs.get(similarityStatus[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      const parts = (job.parts as Record<string, unknown>[]) ?? []
      const reasons: string[] = []
      if (job.status !== 'succeeded') reasons.push('제작이 완료된 작업만 비교할 수 있습니다')
      if (parts.some((part) => part.generatedMesh === null))
        reasons.push('3D 가 없는 파츠가 있습니다')

      const run = similarityRuns.get(similarityStatus[1]!) ?? null
      // 첫 폴링에서 평가 중 → 다음 폴링부터 결과 — 실제 워커의 비동기를 흉내낸다
      if (run && run.status === 'evaluating') {
        const polls = ((run.polls as number) ?? 0) + 1
        run.polls = polls
        if (polls >= 2) {
          const evaluations = run.evaluations as Record<string, unknown>[]
          const pending = evaluations.find((e) => e.status === 'pending')!
          pending.status = 'succeeded'

          if (pending.kind === 'baseline') {
            run.status = 'readyForAdjustment'
          } else {
            // 후보 채택 — 활성 revision 이 후보로 교대되고 run 은 종료 (§9.2)
            adoptedLayouts.set(similarityStatus[1]!, pending.layoutId as string)
            run.status = 'completed'
            const list = revisionsFor(similarityStatus[1]!)
            for (const row of list) if (row.state === 'active') row.state = 'superseded'
            list.unshift({
              id: pending.layoutId,
              revision: list.length + 1,
              state: 'active',
              origin: 'similarityAdjustment',
              composedAt: new Date(Date.UTC(2026, 8, 1)).toISOString(),
            })
          }
        }
      }

      return ok(route, {
        eligible: reasons.length === 0,
        blockingReasons: reasons,
        activeLayoutId:
          adoptedLayouts.get(similarityStatus[1]!) ?? 'b1000000-0000-4000-8000-000000000001',
        openRun:
          run && ['completed', 'failed', 'canceled'].includes(run.status as string) ? null : run,
      })
    }

    const similarityEstimate = /^\/api\/jobs\/([^/]+)\/similarity\/estimate/.exec(path)
    if (similarityEstimate && method === 'GET') {
      const url = new URL(route.request().url())
      const maxIterations = Number(url.searchParams.get('maxIterations') ?? '1')
      const maximumCalls = 1 + Math.min(Math.max(maxIterations, 1), 3)
      return ok(route, {
        maximumCalls,
        inputTokensPerCall: 6000,
        outputTokensPerCall: 2500,
        estimatedMaximumCostUsd: 0.11 * maximumCalls,
        priceKnown: true,
      })
    }

    const similarityStart = /^\/api\/jobs\/([^/]+)\/similarity-runs$/.exec(path)
    if (similarityStart && method === 'POST') {
      const jobId = similarityStart[1]!
      if (!route.request().headers()['idempotency-key']) {
        return fail(route, 409, 'SIMILARITY_CONFLICT', 'Idempotency-Key 헤더가 필요합니다')
      }

      const existing = similarityRuns.get(jobId)
      if (existing && !['completed', 'failed', 'canceled'].includes(existing.status as string)) {
        return ok(route, existing, 202)
      }

      const run = {
        id: `similarity-run-${jobId}`,
        jobId,
        model: 'claude-opus-5',
        maxIterations: 1,
        currentIteration: 0,
        maxCalls: 2,
        status: 'evaluating',
        failureCode: null,
        createdAt: new Date(Date.UTC(2026, 8, 1, 0, 0, 0)).toISOString(),
        completedAt: null,
        polls: 0,
        evaluations: [
          {
            id: `similarity-eval-${jobId}-1`,
            layoutId: 'b1000000-0000-4000-8000-000000000001',
            sequence: 1,
            kind: 'baseline',
            status: 'pending',
            attemptCount: 1,
            score: {
              overall: 68,
              dimensions: [
                {
                  kind: 'composition',
                  score: 72,
                  evidence: '주요 구조물 배치가 유사',
                  recommendation: '근경 좌측 이동',
                },
                {
                  kind: 'camera',
                  score: 68,
                  evidence: '시점 높이 유사',
                  recommendation: 'pitch 소폭 하향',
                },
                {
                  kind: 'scale',
                  score: 65,
                  evidence: '근경이 과대',
                  recommendation: '장면 배율 축소',
                },
                { kind: 'shape', score: 70, evidence: '실루엣 일치', recommendation: '유지' },
                {
                  kind: 'material',
                  score: 60,
                  evidence: '색감 차이',
                  recommendation: '재생성 검토',
                },
                {
                  kind: 'lighting',
                  score: 66,
                  evidence: '광원 방향 유사',
                  recommendation: '고도 하향',
                },
              ],
            },
            adjustments: [
              {
                id: 'adj-1',
                command: { type: 'scaleScene', factor: 0.9 },
                confidence: 0.85,
                reason: '근경 과대',
              },
              {
                id: 'adj-2',
                command: {
                  type: 'adjustLight',
                  azimuthDeltaDegrees: 5,
                  elevationDeltaDegrees: -5,
                  intensityFactor: 1.0,
                },
                confidence: 0.6,
                reason: '명암 완화',
              },
            ],
            regenerationNotes: ['좌측 구조물 텍스처 톤이 원본보다 차갑다'],
          },
        ],
      }
      similarityRuns.set(jobId, run)
      return ok(route, run, 202)
    }

    const similarityCandidate = /^\/api\/jobs\/([^/]+)\/similarity-runs\/([^/]+)\/candidates$/.exec(
      path,
    )
    if (similarityCandidate && method === 'POST') {
      const jobId = similarityCandidate[1]!
      const run = similarityRuns.get(jobId)
      if (!run || run.status !== 'readyForAdjustment') {
        return fail(route, 409, 'SIMILARITY_CONFLICT', '보정을 선택할 수 있는 상태가 아닙니다')
      }

      run.status = 'awaitingRender'
      run.currentIteration = 1
      const candidateLayoutId = 'b1000000-0000-4000-8000-00000000c001'
      const evaluation = {
        id: `similarity-eval-${jobId}-2`,
        layoutId: candidateLayoutId,
        sequence: 2,
        kind: 'candidate',
        status: 'awaitingRender',
        attemptCount: 0,
        score: {
          overall: 74,
          dimensions: [
            { kind: 'composition', score: 78, evidence: '근경 정리', recommendation: '유지' },
            { kind: 'camera', score: 72, evidence: '시점 개선', recommendation: '유지' },
            { kind: 'scale', score: 73, evidence: '배율 개선', recommendation: '유지' },
            { kind: 'shape', score: 74, evidence: '실루엣 일치', recommendation: '유지' },
            { kind: 'material', score: 66, evidence: '색감 유사', recommendation: '유지' },
            { kind: 'lighting', score: 72, evidence: '명암 개선', recommendation: '유지' },
          ],
        },
        adjustments: [],
        regenerationNotes: [],
      }
      ;(run.evaluations as unknown[]).push(evaluation)

      return ok(
        route,
        {
          run,
          candidateLayout: {
            id: candidateLayoutId,
            revision: 2,
            instances: [],
            camera: {
              position: { x: 0, y: 2.2, z: 9 },
              target: { x: 0, y: 1.29, z: -4 },
              fieldOfViewDegrees: 50,
            },
            light: {
              azimuthDegrees: 320,
              elevationDegrees: 35,
              keyIntensity: 1.3,
              keyColor: '#ffd9a8',
              ambientIntensity: 0.8,
              ambientColor: '#fff3e0',
            },
          },
          evaluation,
        },
        201,
      )
    }

    const similarityRender =
      /^\/api\/jobs\/([^/]+)\/similarity-runs\/([^/]+)\/evaluations\/([^/]+)\/render$/.exec(path)
    if (similarityRender && method === 'PUT') {
      const run = similarityRuns.get(similarityRender[1]!)
      if (!run) return fail(route, 404, 'SIMILARITY_RUN_NOT_FOUND', '이 작업의 실행이 아닙니다')

      const evaluation = (run.evaluations as Record<string, unknown>[]).find(
        (e) => e.id === similarityRender[3],
      )
      if (!evaluation)
        return fail(route, 404, 'SIMILARITY_RUN_NOT_FOUND', '이 실행의 평가가 아닙니다')

      evaluation.status = 'pending'
      run.status = 'evaluating'
      run.polls = 0
      return ok(route, evaluation, 202)
    }

    const similarityLifecycle =
      /^\/api\/jobs\/([^/]+)\/similarity-runs\/([^/]+)\/(retry|cancel|complete)$/.exec(path)
    if (similarityLifecycle && method === 'POST') {
      const run = similarityRuns.get(similarityLifecycle[1]!)
      if (!run) return fail(route, 404, 'SIMILARITY_RUN_NOT_FOUND', '이 작업의 실행이 아닙니다')

      const action = similarityLifecycle[3]!
      if (action === 'retry') {
        if (run.status !== 'failed') {
          return fail(route, 409, 'SIMILARITY_CONFLICT', '실패한 실행만 재시도할 수 있습니다')
        }
        run.status = 'evaluating'
        run.polls = 0
        return ok(route, run, 202)
      }
      if (action === 'cancel') {
        run.status = 'canceled'
        return route.fulfill({ status: 204, body: '' })
      }
      if (!['readyForAdjustment', 'awaitingRender'].includes(run.status as string)) {
        return fail(route, 409, 'SIMILARITY_CONFLICT', '종료할 수 있는 상태가 아닙니다')
      }
      run.status = 'completed'
      return route.fulfill({ status: 204, body: '' })
    }

    const sceneRevisionList = /^\/api\/jobs\/([^/]+)\/scene-layout\/revisions$/.exec(path)
    if (sceneRevisionList && method === 'GET') {
      return ok(route, revisionsFor(sceneRevisionList[1]!))
    }

    const sceneRestore = /^\/api\/jobs\/([^/]+)\/scene-layout\/revisions\/([^/]+)\/restore$/.exec(
      path,
    )
    if (sceneRestore && method === 'POST') {
      const list = revisionsFor(sceneRestore[1]!)
      const source = list.find((row) => row.id === sceneRestore[2])
      if (!source) return fail(route, 409, 'SCENE_REVISION_STALE', '이 작업의 revision 이 아닙니다')

      for (const row of list) if (row.state === 'active') row.state = 'superseded'
      const restored = {
        id: `restored-${list.length + 1}`,
        revision: list.length + 1,
        state: 'active',
        origin: 'restore',
        composedAt: new Date(Date.UTC(2026, 8, 2)).toISOString(),
      }
      list.unshift(restored)
      adoptedLayouts.set(sceneRestore[1]!, restored.id)
      return ok(route, restored, 201)
    }

    const similarityRunDetail = /^\/api\/jobs\/([^/]+)\/similarity-runs\/([^/]+)$/.exec(path)
    if (similarityRunDetail && method === 'GET') {
      const run = similarityRuns.get(similarityRunDetail[1]!)
      if (!run) return fail(route, 404, 'SIMILARITY_RUN_NOT_FOUND', '이 작업의 실행이 아닙니다')
      return ok(route, run)
    }

    // 조립 명세 (scene-assembly §4) — 서버 유도의 대역. GLB 있는 파츠의 배치마다 인스턴스 하나
    const layoutMatch = /^\/api\/jobs\/([^/]+)\/scene-layout$/.exec(path)
    if (layoutMatch && method === 'GET') {
      const job = jobs.get(layoutMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      const parts = (job.parts as Record<string, unknown>[]) ?? []
      const instances = parts.flatMap((part) => {
        const mesh = part.generatedMesh as { id: string } | null
        if (!mesh) return []
        const placements = part.placements as { x: number; y: number; w: number; h: number }[]
        // 표면으로 표시된 파츠는 눕는다 — 폭·깊이가 넓고 두께가 얇다 (#20 §4.3)
        const laid = surfaceParts && part.id === parts[0]!.id

        return placements.map((box, ordinal) => ({
          partId: part.id,
          partName: part.name,
          meshId: mesh.id,
          ordinal,
          position: { x: (box.x - 0.5) * 10, y: 0, z: -(1 - (box.y + box.h)) * 12 },
          rotationY: 0,
          scale: box.h * 3,
          // 낱개 물건은 세 축이 같다 (#20 §4.2) — 표면 파츠만 갈린다
          scaleVector: laid
            ? { x: box.w * 12, y: box.h * 0.3, z: box.h * 9 }
            : { x: box.h * 3, y: box.h * 3, z: box.h * 3 },
        }))
      })

      // 표면 배치 수 — 첫 파츠만 표면으로 표시한다
      const surfaceCount = surfaceParts
        ? ((parts[0]?.placements as unknown[] | undefined)?.length ?? 0)
        : 0
      const missingPartNames = parts
        .filter((part) => part.generatedMesh === null)
        .map((part) => part.name)

      return ok(route, {
        id: 'b1000000-0000-4000-8000-000000000001',
        revision: 1,
        sourceMeshSignature: 'f'.repeat(64),
        instances,
        camera: { eyeLevel: 'medium', horizonY: 0.42 },
        light: { direction: 'upper-left', temperature: 'warm' },
        // 서버 SceneStaging 이 낼 값과 같은 규칙 — medium 2.2m, horizonY 0.42 → pitch 4°
        numericCamera: {
          position: { x: 0, y: 2.2, z: 9 },
          target: { x: 0, y: 1.29, z: -4 },
          fieldOfViewDegrees: 50,
        },
        numericLight: {
          azimuthDegrees: 315,
          elevationDegrees: 40,
          keyIntensity: 1.3,
          keyColor: '#ffd9a8',
          ambientIntensity: 0.8,
          ambientColor: '#fff3e0',
        },
        missingPartNames,
        composedAt: new Date(Date.UTC(2026, 7, 17, 0, 0, 0)).toISOString(),
        // 유도 판단 (#18) — 고도 미상 배치가 있고 기준 편차는 정상 범위다
        composition: {
          groundedCount: 4,
          elevatedCount: 1,
          scaleDepthForElevated: 0.48,
          anchorSpread,
          surfaceCount,
          anchorIsSurface,
          droppedCount,
          surfaceShapeMismatch,
        },
      })
    }

    // 3D 산출물 — 뷰어가 실제로 파싱할 수 있어야 한다 (사이클 #13).
    // Content-Disposition 은 실서버 계약이다 — 없으면 다운로드 파일명 검증이 거짓말이 된다
    if (path.startsWith('/api/generated-meshes/') && method === 'GET') {
      if (path.endsWith('/preview')) {
        return route.fulfill({ status: 404, body: '' })
      }

      const meshId = path.split('/')[3]!
      if (path.endsWith('/fbx')) {
        return route.fulfill({
          status: 200,
          // headers 를 주면 contentType 옵션이 무시된다 — 둘 다 headers 에 싣는다
          headers: {
            'Content-Type': 'application/octet-stream',
            'Content-Disposition': `attachment; filename=${meshId}.fbx`,
          },
          body: Buffer.from('Kaydara FBX Binary  \0'),
        })
      }

      return route.fulfill({
        status: 200,
        headers: {
          'Content-Type': 'model/gltf-binary',
          'Content-Disposition': `attachment; filename=${meshId}.glb`,
        },
        body: minimalGlb(),
      })
    }

    if (path === '/api/providers' && method === 'GET') {
      return ok(route, providers)
    }

    if (path === '/api/providers' && method === 'POST') {
      const body = request.postDataJSON() as Record<string, string>
      const created: FakeProvider = {
        id: `provider-${(sequence += 1)}`,
        displayName: body.displayName!,
        kind: body.kind!,
        capabilities:
          body.kind === 'anthropic'
            ? ['textAnalysis']
            : body.kind === 'google'
              ? ['imageGeneration']
              : ['textAnalysis', 'imageGeneration'],
        // 서버와 같은 규칙 — 끝 4자리만 남는다 (§4.2 #9)
        apiKeyMasked: `••••••••${(body.apiKey ?? '').slice(-4)}`,
        isEnabled: true,
      }
      providers.push(created)
      return ok(route, created, 201)
    }

    const providerMatch =
      /^\/api\/providers\/([^/]+)(\/test|\/models|\/image-models|\/mesh-models)?$/.exec(path)
    if (providerMatch) {
      const id = providerMatch[1]!
      const index = providers.findIndex((provider) => provider.id === id)

      // 연결 확인은 모델 목록 조회다 — 같은 실패 조건을 공유한다
      if (providerMatch[2] === '/test') {
        const capabilities = providers[index]?.capabilities ?? []
        return modelsError
          ? fail(route, modelsError.status, modelsError.code, modelsError.message)
          : ok(route, {
              ok: true,
              latencyMs: 812,
              textModelCount: capabilities.includes('textAnalysis') ? models.length : null,
              imageModelCount: capabilities.includes('imageGeneration') ? imageModels.length : null,
              meshModelCount: capabilities.includes('meshGeneration') ? MESH_MODELS.length : null,
              // 3D 공급자만 값을 갖는다. null 은 "못 읽었다" 이지 0 이 아니다
              meshCreditBalance: capabilities.includes('meshGeneration') ? meshCreditBalance : null,
            })
      }

      // 3D 모델 — 이미지와 또 다른 경로다 (사이클 #10 §10.1)
      if (providerMatch[2] === '/mesh-models') {
        if (meshModelsError) {
          return fail(route, meshModelsError.status, meshModelsError.code, meshModelsError.message)
        }

        const meshProvider = providers[index]

        if (!meshProvider?.capabilities?.includes('meshGeneration')) {
          return ok(route, [])
        }

        // 공급자마다 모델 이름이 다르다 — 하나로 뭉개면 화면이 잘못된 모델을 보낸다
        return ok(route, meshProvider.kind === 'meshy' ? MESHY_MODELS : MESH_MODELS)
      }

      // 이미지 생성 모델 — 텍스트와 다른 경로다 (사이클 #7 §4.2 #7)
      if (providerMatch[2] === '/image-models') {
        // 빈 목록이 오류가 아니다 — 이미지 생성이 아예 없는 공급자가 있다
        return ok(route, imageModels)
      }

      if (providerMatch[2] === '/models') {
        if (modelsError) {
          return fail(route, modelsError.status, modelsError.code, modelsError.message)
        }

        // 서버는 빈 목록을 성공으로 내려보내지 않는다 (PROVIDER_NO_VISION_MODELS)
        return models.length === 0
          ? fail(
              route,
              400,
              'PROVIDER_NO_VISION_MODELS',
              '이 공급자에 이미지를 읽을 수 있는 모델이 없습니다',
            )
          : ok(route, models)
      }

      if (method === 'PUT') {
        const body = request.postDataJSON() as Record<string, string | undefined>
        const existing = providers[index]!
        providers[index] = {
          ...existing,
          displayName: body.displayName ?? existing.displayName,
          // 키를 보내지 않으면 마스킹이 그대로다 — 이것이 §4.2 #11 의 화면 측 증거다
          apiKeyMasked: body.apiKey ? `••••••••${body.apiKey.slice(-4)}` : existing.apiKeyMasked,
        }
        return ok(route, providers[index])
      }

      if (method === 'DELETE') {
        providers.splice(index, 1)
        return route.fulfill({ status: 204, body: '' })
      }
    }

    // ─── 튜닝 (사이클 #5) ───
    //
    // Check 단계 G-4: 이 엔드포인트들이 없어 관리자 신규 3화면이 E2E 에서
    // 열리지도 않았다. 사이클 가치의 절반이 회귀 방어 없이 있었다.

    // (단계 × 카테고리) 유효 활성 격자 — 서버와 같은 폴백 규칙(전용→기본)을 태운다 (§12.1)
    if (path === '/api/prompts/grid' && method === 'GET') {
      const activeExact = (kind: string, category: string | null) =>
        prompts.find((p) => p.kind === kind && p.category === category && p.isActive)

      const rows = [...PROMPT_STAGES, 'similarityEvaluate'].map((kind) => ({
        kind,
        cells: [null, ...PROMPT_CATEGORIES].map((category) => {
          const dedicated = activeExact(kind, category)
          if (dedicated) {
            return {
              category,
              status: 'dedicated',
              version: dedicated.version,
              versionId: dedicated.id,
            }
          }
          // 기본 열은 폴백이 없다 — 전용이 없으면 실행 불가
          const fallback = category === null ? undefined : activeExact(kind, null)
          return fallback
            ? { category, status: 'fallback', version: fallback.version, versionId: fallback.id }
            : { category, status: 'unavailable', version: null, versionId: null }
        }),
      }))

      return ok(route, { rows })
    }

    if (path === '/api/prompts' && method === 'GET') {
      return ok(
        route,
        prompts.filter((p) => p.isActive),
      )
    }

    const promptVersions = /^\/api\/prompts\/(\w+)\/versions$/.exec(path)
    if (promptVersions) {
      const kind = promptVersions[1]!

      if (method === 'GET') {
        // 카테고리 정확 일치 — 생략 시 기본(null) 슬롯. 캐시가 섞이지 않게 이력을 슬롯별로 가른다
        const category = new URL(request.url()).searchParams.get('category')
        return ok(
          route,
          prompts
            .filter((p) => p.kind === kind && p.category === (category ?? null))
            .sort((a, b) => b.version - a.version),
        )
      }

      const body = request.postDataJSON() as Record<string, string>
      const category = body.category ?? null

      // 서버와 같은 규칙 — 허용 목록에 없는 변수는 거부한다 (§2.3-5)
      const allowed = ALLOWED_VARIABLES[kind] ?? []
      const used = [...(body.system ?? '').matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]!)
      const unknown = used.find((v) => !allowed.includes(v))

      if (unknown) {
        return fail(
          route,
          400,
          'PROMPT_UNKNOWN_VARIABLE',
          `알 수 없는 변수 {{${unknown}}} — 쓸 수 있는 변수: ${allowed.map((v) => `{{${v}}}`).join(', ')}`,
        )
      }

      const created = {
        id: `prompt-${(sequence += 1)}`,
        kind,
        category,
        // 채번은 (단계, 카테고리) 스코프 — 카테고리마다 1부터 (§4.3)
        version:
          Math.max(
            0,
            ...prompts
              .filter((p) => p.kind === kind && p.category === category)
              .map((p) => p.version),
          ) + 1,
        system: body.system!,
        user: body.user!,
        jsonSchema: body.jsonSchema!,
        note: body.note ?? null,
        // 저장이 활성화가 아니다 (FR-09)
        isActive: false,
        allowedVariables: allowed,
        createdAt: new Date().toISOString(),
      }
      prompts.push(created)
      return ok(route, created, 201)
    }

    const activate = /^\/api\/prompts\/versions\/([^/]+)\/activate$/.exec(path)
    if (activate) {
      const target = prompts.find((p) => p.id === activate[1])!
      // 같은 (단계, 카테고리) 의 이전 활성만 내린다 — 카테고리 간 격리 (§8-8/8b)
      prompts
        .filter((p) => p.kind === target.kind && p.category === target.category)
        .forEach((p) => (p.isActive = false))
      target.isActive = true
      return ok(route, target)
    }

    if (path === '/api/prices' && method === 'GET') {
      return ok(route, modelPrices)
    }

    if (path === '/api/prices' && method === 'POST') {
      const body = request.postDataJSON() as (typeof modelPrices)[number]

      // 서버의 유니크 인덱스와 같은 규칙 — 둘이면 어느 쪽이 이길지 알 수 없다
      if (
        modelPrices.some((p) => p.model === body.model && p.effectiveFrom === body.effectiveFrom)
      ) {
        return fail(route, 400, 'PRICE_DUPLICATE', '같은 시행일의 단가가 이미 있습니다')
      }

      const created = { ...body, id: `price-${(sequence += 1)}` }
      modelPrices.push(created)
      return ok(route, created, 201)
    }

    const priceById = /^\/api\/prices\/([^/]+)$/.exec(path)

    if (priceById && method === 'PUT') {
      const index = modelPrices.findIndex((p) => p.id === priceById[1])
      const body = request.postDataJSON() as (typeof modelPrices)[number]

      // 모델명은 행의 정체성이라 바뀌지 않는다
      modelPrices[index] = { ...body, id: modelPrices[index]!.id, model: modelPrices[index]!.model }
      return ok(route, modelPrices[index])
    }

    if (priceById && method === 'DELETE') {
      modelPrices.splice(
        modelPrices.findIndex((p) => p.id === priceById[1]),
        1,
      )
      return route.fulfill({ status: 204, body: '' })
    }

    if (path === '/api/golden' && method === 'GET') {
      return ok(route, golden)
    }

    if (path === '/api/golden' && method === 'POST') {
      const body = request.postDataJSON() as Record<string, string>
      const created = {
        id: `golden-${(sequence += 1)}`,
        storedImageId: body.storedImageId!,
        name: body.name!,
        expectedNote: body.expectedNote!,
        createdAt: new Date().toISOString(),
      }
      golden.push(created)
      return ok(route, created, 201)
    }

    const goldenRuns = /^\/api\/golden\/([^/]+)\/runs$/.exec(path)
    if (goldenRuns) {
      return ok(route, goldenRunList)
    }

    const goldenItem = /^\/api\/golden\/([^/]+)$/.exec(path)
    if (goldenItem && method === 'DELETE') {
      const index = golden.findIndex((g) => g.id === goldenItem[1])
      if (index >= 0) golden.splice(index, 1)
      return route.fulfill({ status: 204, body: '' })
    }

    const verdict = /^\/api\/jobs\/([^/]+)\/verdict$/.exec(path)
    if (verdict && method === 'POST') {
      const body = request.postDataJSON() as { isPass: boolean; memo: string }
      const recorded = { ...body, at: new Date().toISOString() }
      const run = goldenRunList.find((r) => r.jobId === verdict[1])
      if (run) run.verdict = recorded
      return ok(route, recorded)
    }

    // 실패한 파츠 하나를 다시 돌린다 (사이클 #7 §4.2 #3 · FR-08)
    const retry = /^\/api\/jobs\/([^/]+)\/tasks\/([^/]+)\/retry$/.exec(path)
    if (retry && method === 'POST') {
      const job = jobs.get(retry[1]!)
      if (!job) {
        return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      }

      const tasks = job.tasks as Record<string, unknown>[]
      const target = tasks.find((t) => t.id === retry[2])

      // 실패한 생성 공정만 가능하다 — 앞 세 단계를 다시 돌리면 뒤가 무의미해진다
      if (!target || target.kind !== 'generate' || target.status !== 'failed') {
        return fail(
          route,
          400,
          'TASK_NOT_RETRYABLE',
          '실패한 파츠 생성 공정만 다시 돌릴 수 있습니다',
        )
      }

      target.status = 'succeeded'
      target.failureReason = null
      target.attemptCount = 1

      // 실패한 방향 이미지가 붙고, 남은 실패가 없으면 작업이 성공으로 올라간다
      const parts = job.parts as Record<string, unknown>[]
      const part = parts.find((p) => p.id === target.partId)
      if (part) {
        const viewDirection = target.viewDirection as string
        const generatedImages = part.generatedImages as Record<string, unknown>[]
        generatedImages.push({ id: `image-${retry[1]}-retried-${viewDirection}`, viewDirection })
        if (viewDirection === 'front') {
          part.generatedImageId = `image-${retry[1]}-retried-${viewDirection}`
        }
      }

      const stillFailing = tasks.some((t) => t.status === 'failed')
      job.status = stillFailing ? 'partiallySucceeded' : 'succeeded'

      return ok(route, { id: retry[1], status: job.status }, 202)
    }

    // 생성 이미지 바이트 — 봉투를 쓰지 않는다 (§4.2 #4)
    if (/^\/api\/generated-images\/[^/]+$/.test(path)) {
      return route.fulfill({ status: 200, contentType: 'image/png', body: PNG_PIXEL })
    }

    if (/^\/api\/jobs\/[^/]+\/calls$/.test(path)) {
      return ok(route, JOB_CALLS)
    }

    if (path === '/api/calls/stats') {
      return ok(route, { count: 12, approximateBytes: 48_000 })
    }

    if (path === '/api/uploads' && method === 'POST') {
      return ok(
        route,
        {
          id: `upload-${(sequence += 1)}`,
          originalName: 'harbor.png',
          contentType: 'image/png',
          sizeBytes: 2048,
        },
        201,
      )
    }

    if (path === '/api/jobs' && method === 'POST') {
      const id = `job-${(sequence += 1)}`
      // 접수 본문의 선택을 그대로 기억한다 — 재시도가 이어받는 값의 출처다
      const body = (route.request().postDataJSON() ?? {}) as Record<string, string> & {
        gender?: string
        partHints?: { type: string; count: number; variant?: string }[]
        requiresReview?: boolean
      }
      const selected: JobModels = {
        text:
          body.providerConfigId && body.model
            ? { providerConfigId: body.providerConfigId, model: body.model }
            : null,
        // `meshAtIntake: false` 는 3D 가 생기기 전에 만들어진 작업을 흉내낸다 —
        // 화면이 무엇을 보냈든 3D 없이 끝난다 (사이클 #11 의 대상)
        mesh:
          meshAtIntake && body.meshProviderConfigId && body.meshModel
            ? { providerConfigId: body.meshProviderConfigId, model: body.meshModel }
            : null,
        image:
          body.imageProviderConfigId && body.imageModel
            ? { providerConfigId: body.imageProviderConfigId, model: body.imageModel }
            : null,
      }

      // 접수 본문의 카테고리를 그대로 기억한다 — 홈 링크가 카테고리별 주소를 고르므로
      // (독립 리뷰 #2) fake 가 background 로 뭉개면 그 계약을 E2E 가 검증할 수 없다
      const category = body.category ?? 'background'
      // 병합: live-result 의 단계식 작업 + main 의 성별·파츠 힌트 전달을 모두 지킨다
      const initialJob =
        reviewGate && body.requiresReview
          ? // review-gate — 분해는 끝났지만 팬아웃은 승인 전까지 보류된다(§목표).
            // `reviewGate` 옵션을 켠 스펙만 이 경로를 탄다 — FakeApiOptions.reviewGate 참고
            toPendingReview(
              buildJob(
                id,
                'succeeded',
                selected,
                category,
                body.gender ?? null,
                body.partHints ?? [],
              ),
            )
          : liveResult && runningPolls > 0
            ? buildLiveJob(id, selected, category, 0)
            : buildJob(
                id,
                runningPolls > 0 ? 'running' : outcome,
                selected,
                category,
                body.gender ?? null,
                body.partHints ?? [],
              )
      jobs.set(id, initialJob)
      pollCounts.set(id, 0)
      return ok(route, { id, status: 'pending' }, 202)
    }

    if (path === '/api/jobs' && method === 'GET') {
      const filter = url.searchParams.get('status') ?? 'active'
      const terminal = ['succeeded', 'failed', 'canceled']

      const matching = [...jobs.values()].filter((job) => {
        const isTerminal = terminal.includes(job.status as string)
        return filter === 'terminal' ? isTerminal : !isTerminal
      })

      // 서버처럼 상한을 건다 — 걸지 않으면 "전체가 더 많은" 경우를 재현할 수 없다
      const limit = Number(url.searchParams.get('limit') ?? 10)
      const items = matching.slice(0, limit).map((job) => ({
        id: job.id,
        category: job.category,
        status: job.status,
        sourceImageId: job.sourceImageId,
        partCount: (job.parts as unknown[]).length,
        createdAt: job.createdAt,
      }))

      return ok(route, { items, total: matching.length })
    }

    const cancelMatch = /^\/api\/jobs\/([^/]+)\/cancel$/.exec(path)
    if (cancelMatch) {
      const job = jobs.get(cancelMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      jobs.set(cancelMatch[1]!, { ...job, status: 'canceled' })
      return ok(route, { id: cancelMatch[1], status: 'canceled' })
    }

    // 끝난 작업에 3D 를 뒤늦게 붙인다 (사이클 #11)
    const meshMatch = /^\/api\/jobs\/([^/]+)\/mesh$/.exec(path)
    if (meshMatch) {
      const id = meshMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      const models = job.models as JobModels

      if (models.mesh !== null) {
        return fail(
          route,
          409,
          'JOB_MESH_NOT_APPLICABLE',
          '이 작업에는 3D 를 붙일 수 없습니다. 목록을 새로 불러오세요',
        )
      }

      const body = (route.request().postDataJSON() ?? {}) as Record<string, string>

      // 3D 선택이 붙고 작업이 다시 열린다. 이미지 공정은 그대로다
      jobs.set(id, {
        ...job,
        status: 'running',
        models: {
          ...models,
          mesh: { providerConfigId: body.meshProviderConfigId!, model: body.meshModel! },
        },
      })

      return ok(route, { id, status: 'running' }, 202)
    }

    // 파츠별 "3D 전송 뷰 자유 선택 + 대칭" (spec 20260917) — 파츠마다 한 번씩 불린다
    const replanMeshMatch = /^\/api\/jobs\/([^/]+)\/replan-mesh$/.exec(path)
    if (replanMeshMatch && method === 'POST') {
      const id = replanMeshMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      jobs.set(id, { ...job, status: 'running' })
      return ok(route, { id, status: 'running' }, 202)
    }

    // review-gate — 검수 게이트 (docs/specs/2026-08-28-review-gate.md §입력→출력)
    const reviewMatch = /^\/api\/jobs\/([^/]+)\/review$/.exec(path)
    if (reviewMatch && method === 'GET') {
      const job = jobs.get(reviewMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      return ok(route, reviewStateFrom(job))
    }

    // 겹침 조회 (occludedby-recompute §입력→출력 1) — 순수 조회라 job 을 바꾸지 않는다
    const overlapsMatch = /^\/api\/jobs\/([^/]+)\/review\/parts\/overlaps$/.exec(path)
    if (overlapsMatch && method === 'POST') {
      const job = jobs.get(overlapsMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      if (job.status !== 'pendingReview') {
        return fail(route, 409, 'REVIEW_NOT_PENDING', '검수 대기 상태가 아닙니다')
      }

      const { bounds } = (route.request().postDataJSON() ?? {}) as {
        bounds: { x: number; y: number; w: number; h: number }
      }

      const overlapping = (job.parts as Record<string, unknown>[])
        .filter((part) =>
          (part.placements as { x: number; y: number; w: number; h: number }[]).some(
            (box) => overlapCoefficient(bounds, box) >= OVERLAP_THRESHOLD,
          ),
        )
        .map((part) => part.name as string)

      return ok(route, { overlapping })
    }

    const addPartMatch = /^\/api\/jobs\/([^/]+)\/review\/parts$/.exec(path)
    if (addPartMatch && method === 'POST') {
      const id = addPartMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const addGuard = reviewPhaseGuard(route, job, 'boxes')
      if (addGuard) return addGuard

      const body = (route.request().postDataJSON() ?? {}) as {
        name: string
        category: string
        bounds: { x: number; y: number; w: number; h: number }
        description?: string
        occludes?: string[]
      }

      const parts = job.parts as Record<string, unknown>[]

      if (parts.some((part) => part.name === body.name)) {
        return fail(route, 409, 'PART_NAME_DUPLICATE', '이미 있는 파츠 이름입니다')
      }

      const overlapping = parts
        .filter((part) =>
          (part.placements as { x: number; y: number; w: number; h: number }[]).some(
            (box) => overlapCoefficient(body.bounds, box) >= OVERLAP_THRESHOLD,
          ),
        )
        .map((part) => part.name as string)

      // 생략(undefined)이면 기본 추정 — 겹치는 것 전부. 빈 배열은 "아무것도 안 가림" 이다
      const occludes = body.occludes ?? overlapping

      const unrelated = occludes.find((partName) => !overlapping.includes(partName))
      if (unrelated !== undefined) {
        return fail(route, 400, 'PART_NOT_OVERLAPPING', `'${unrelated}' 과 겹치지 않습니다`)
      }

      // 가려지게 된 파츠의 occludedBy 를 갱신한다 — 서술 재작성은 승인 시점의 일이다
      for (const part of parts) {
        if (occludes.includes(part.name as string)) {
          part.occludedBy = [...(part.occludedBy as string[]), body.name]
        }
      }

      const newPart = {
        id: `manual-part-${id}-${(sequence += 1)}`,
        name: body.name,
        ordinal: parts.length,
        description: body.description ?? `${body.category} 카테고리의 '${body.name}' 파츠`,
        category: body.category,
        placements: [body.bounds],
        depthOrder: parts.length + 1,
        occludedBy: [],
        generatedImageId: null,
        generatedImages: [],
        generatedMesh: null,
        // 사람이 서술을 쓰면 human, 비우면 상자 확정 때 재작성으로 채워짐
        descriptionSource: body.description ? 'human' : 'model',
      }
      parts.push(newPart)

      return ok(route, reviewPartFrom(newPart), 201)
    }

    const removePartMatch = /^\/api\/jobs\/([^/]+)\/review\/parts\/([^/]+)$/.exec(path)
    if (removePartMatch && method === 'DELETE') {
      const job = jobs.get(removePartMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      if (job.status !== 'pendingReview') {
        return fail(route, 409, 'REVIEW_NOT_PENDING', '검수 대기 상태가 아닙니다')
      }

      const parts = job.parts as Record<string, unknown>[]
      const index = parts.findIndex((part) => part.id === removePartMatch[2])
      if (index === -1) return fail(route, 404, 'PART_NOT_FOUND', '파츠를 찾을 수 없습니다')

      parts.splice(index, 1)
      return route.fulfill({ status: 204, body: '' })
    }

    // review-gate-staged 사이클 1 — 상자 확정. 재작성 공정은 흉내내지 않고 서술 단계로 바로 넘어간다.
    // 서술 없이 직접 추가한 파츠는 재작성이 채운 것으로 표시
    const approveMatch = /^\/api\/jobs\/([^/]+)\/review\/approve$/.exec(path)
    if (approveMatch && method === 'POST') {
      const id = approveMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const approveGuard = reviewPhaseGuard(route, job, 'boxes')
      if (approveGuard) return approveGuard

      for (const part of job.parts as Record<string, unknown>[]) {
        if (String(part.id).startsWith('manual-part-') && part.descriptionSource === 'model') {
          part.descriptionSource = 'rewritten'
        }
      }
      jobs.set(id, { ...job, reviewPhase: 'descriptions' })
      return ok(route, { id, status: 'pendingReview' }, 202)
    }

    const returnMatch = /^\/api\/jobs\/([^/]+)\/review\/return-to-boxes$/.exec(path)
    if (returnMatch && method === 'POST') {
      const id = returnMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const returnGuard = reviewPhaseGuard(route, job, 'descriptions')
      if (returnGuard) return returnGuard

      const next = { ...job, reviewPhase: 'boxes' }
      jobs.set(id, next)
      return ok(route, reviewStateFrom(next))
    }

    const descriptionMatch = /^\/api\/jobs\/([^/]+)\/review\/parts\/([^/]+)\/description$/.exec(
      path,
    )
    if (descriptionMatch && method === 'PUT') {
      const job = jobs.get(descriptionMatch[1]!)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const descriptionGuard = reviewPhaseGuard(route, job, 'descriptions')
      if (descriptionGuard) return descriptionGuard

      const { description } = (route.request().postDataJSON() ?? {}) as { description?: string }
      if (!description?.trim())
        return fail(route, 400, 'PART_DESCRIPTION_EMPTY', '서술이 비었습니다')
      const target = (job.parts as Record<string, unknown>[]).find(
        (part) => part.id === descriptionMatch[2],
      )
      if (!target) return fail(route, 404, 'PART_NOT_FOUND', '파츠를 찾을 수 없습니다')

      target.description = description.trim()
      target.descriptionSource = 'human'
      return ok(route, reviewStateFrom(job))
    }

    const paletteMatch = /^\/api\/jobs\/([^/]+)\/review\/palette$/.exec(path)
    if (paletteMatch && method === 'PUT') {
      const id = paletteMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const paletteGuard = reviewPhaseGuard(route, job, 'descriptions')
      if (paletteGuard) return paletteGuard

      const { palette } = (route.request().postDataJSON() ?? {}) as {
        palette: { name: string; hex: string | null }[]
      }
      const valid =
        palette.length >= 3 &&
        palette.length <= 8 &&
        palette.every((entry) => entry.name.trim() && /^#[0-9A-F]{6}$/.test(entry.hex ?? ''))
      if (!valid)
        return fail(
          route,
          400,
          'PALETTE_INVALID',
          '팔레트는 3~8칸, 이름과 대문자 #RRGGBB 가 필요합니다',
        )

      const next = { ...job, scene: { ...(job.scene as object), palette } }
      jobs.set(id, next)
      return ok(route, reviewStateFrom(next))
    }

    // 서술 확정 — 보류됐던 팬아웃 재개 (사이클 1 이전의 전체 승인 동작)
    const confirmMatch = /^\/api\/jobs\/([^/]+)\/review\/confirm-descriptions$/.exec(path)
    if (confirmMatch && method === 'POST') {
      const id = confirmMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')
      const confirmGuard = reviewPhaseGuard(route, job, 'descriptions')
      if (confirmGuard) return confirmGuard

      // 승인 — 보류됐던 팬아웃을 재개한다. Fake 는 실시간 생성을 흉내내지 않으므로
      // 다음 폴링에서 기존 running→완료 전이 로직(§ 위 buildJob outcome)을 그대로 탄다.
      // 승인 뒤 완료로 넘어갈 때 부재/추가한 파츠 편집은 반영되지 않는다 — 이 부분은
      // 검수 화면 자체의 동작(추가·삭제·겹침 거부·승인 트리거)만 검증하면 충분하다.
      jobs.set(id, { ...job, status: 'running', reviewPhase: 'approved' })
      pollCounts.set(id, 0)
      return ok(route, { id, status: 'running' }, 202)
    }

    const jobMatch = /^\/api\/jobs\/([^/]+)$/.exec(path)
    if (jobMatch) {
      const id = jobMatch[1]!
      const job = jobs.get(id)
      if (!job) return fail(route, 404, 'JOB_NOT_FOUND', '작업을 찾을 수 없습니다')

      if (method === 'DELETE') {
        // **서버와 같은 규칙이어야 한다.** 상태를 안 보고 지우면, 진행 중 작업에
        // 삭제 버튼이 노출되는 회귀를 E2E 가 통과시킨다
        const terminal: readonly string[] = [
          'succeeded',
          'partiallySucceeded',
          'failed',
          'canceled',
        ]

        if (!terminal.includes(String(job.status))) {
          return fail(
            route,
            409,
            'JOB_ACTIVE_CANNOT_DELETE',
            '진행 중인 작업은 삭제할 수 없습니다. 먼저 취소해 주세요',
          )
        }

        jobs.delete(id)
        // 서버는 204 로 답한다 — 본문이 없다
        return route.fulfill({ status: 204, body: '' })
      }

      // 진행 → 완료 전이를 폴링 횟수로 재현한다. 실제 워커의 지연을 대신한다
      const polls = (pollCounts.get(id) ?? 0) + 1
      pollCounts.set(id, polls)

      if (job.status === 'running' && polls > runningPolls) {
        // 전이 때 접수의 카테고리·모델 선택을 이어간다 — running 으로 심을 때와 같은 값.
        // 카테고리까지 보존해야 홈 링크가 카테고리별 주소를 고른다(character 독립 리뷰 #2)
        const done = buildJob(id, outcome, job.models as JobModels, job.category as string)
        jobs.set(id, done)
        return ok(route, done)
      }

      if (liveResult && job.status === 'running') {
        // 첫 상세 응답은 3D 1개, 다음 폴링부터 2개인 점진 결과
        const live = buildLiveJob(
          id,
          job.models as JobModels,
          job.category as string,
          Math.max(0, polls - 1),
        )
        jobs.set(id, live)
        return ok(route, live)
      }

      return ok(route, job)
    }

    return fail(route, 404, 'NOT_FOUND', path)
  })

  // 이미지 라우트를 **나중에** 등록한다. Playwright 는 마지막에 등록한 핸들러를 먼저
  // 보므로, 위의 포괄 핸들러보다 앞서야 이 경로가 404 로 떨어지지 않는다.
  // 실제 바이트를 돌려주는 이유는 <img> 가 깨지면 Playwright 가 요소를 hidden 으로
  // 보고, 결과 화면 검증이 "안 보인다" 로 실패하기 때문이다
  await page.route('**/api/uploads/*/content', (route) =>
    route.fulfill({ status: 200, contentType: 'image/png', body: PNG_1X1 }),
  )
}

/** 유효한 1×1 PNG. 렌더되는 바이트여야 한다 — 임의 버퍼는 그려지지 않는다. */
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
)

/** §6 — API 자체가 없는 상황. 화면이 깨지지 않아야 한다 (L2 #20). */
export async function installUnreachableApi(page: Page) {
  await page.route('**/api/**', (route) => route.abort('connectionrefused'))
}

/**
 * 파일 선택. 실제 드래그 앤 드롭 대신 input 에 직접 심는다.
 *
 * 기본값은 **렌더되는 PNG** 다. 임의 바이트를 넣으면 미리보기 `<img>` 가 그려지지
 * 않고, Playwright 가 그것을 hidden 으로 보아 "미리보기가 뜬다" 검증이 실패한다.
 * 거부를 검증할 때만 크기를 지정하며, 그때는 유효성이 무의미하다.
 */
export async function selectImage(
  page: Page,
  { name = 'harbor.png', mimeType = 'image/png', size }: SelectImageOptions = {},
) {
  await page.getByTestId('image-input').setInputFiles({
    name,
    mimeType,
    buffer: size === undefined ? PNG_1X1 : Buffer.alloc(size, 1),
  })
}

interface SelectImageOptions {
  name?: string
  mimeType?: string
  /** 지정하면 그 크기의 더미 버퍼를 쓴다 — 크기 초과 거부 검증용 */
  size?: number
}

/**
 * Select 조작 — 네이티브 `<select>` 와 달리 **열기 전에는 목록이 DOM 에 없다.**
 *
 * 그래서 `selectOption()` 도, 닫힌 채로 옵션 텍스트를 읽는 `toContainText()` 도 쓸 수 없다.
 * 여는 것과 고르는 것을 나눠 둔 이유는 "무엇이 보이는가" 와 "골랐을 때 무엇이 남는가" 가
 * 서로 다른 단언이기 때문이다.
 */
export function openOptions(page: Page, testId: string): Promise<Locator> {
  return page
    .getByTestId(testId)
    .click()
    .then(() => page.getByRole('listbox').getByRole('option'))
}

/** 옵션을 사람이 읽는 이름으로 고르고, 목록이 닫힐 때까지 기다린다 */
export async function chooseOption(page: Page, testId: string, optionLabel: string) {
  await page.getByTestId(testId).click()
  await page.getByRole('option', { name: optionLabel, exact: true }).click()
  // 닫힘을 기다리지 않으면 다음 조작이 팝오버에 가로막힌다
  await expect(page.getByRole('listbox')).toHaveCount(0)
}
