/**
 * Design Ref: design-system §6 Module 4 — Background Studio Tailwind 스타일 계약.
 * 입력·진행·결과의 상태별 계산값과 반응형 규칙을 한곳에 유지한다.
 */

// 스튜디오 입력과 모드 선택
export const backgroundStyles = {
  // ─── 3D 조립 (scene-assembly §5) ───
  viewTabs: 'mb-4 flex gap-1 border-b border-border',
  viewTab:
    '-mb-px cursor-pointer border-b-2 border-transparent px-4 py-2 text-[0.84rem] font-semibold text-[var(--text-dim)] transition-colors hover:text-muted-foreground aria-selected:border-primary aria-selected:text-primary disabled:cursor-not-allowed disabled:opacity-45',
  // 3D 는 최종 산출물이다 — 남은 화면 높이를 전부 쓴다 (FR-05).
  // **높이는 래퍼가 정한다.** R3F Canvas 는 인라인 height:100% 를 박으므로 Canvas 쪽
  // className 높이는 늘 무시된다 (실측) — 부모가 크기를 정하는 것이 R3F 의 계약이다.
  // 240px = 헤더·제목·탭·여백. 상한을 두면 넓은 화면에서 레터박스가 된다
  sceneWrap:
    'relative h-[max(calc(100dvh-240px),480px)] w-full overflow-hidden rounded-[14px] border border-border bg-popover',
  sceneControls: 'absolute right-3.5 top-3.5 flex gap-2',
  sceneToggle:
    'inline-flex cursor-pointer items-center gap-1.5 rounded-lg border border-border bg-black/45 px-3 py-[7px] text-xs font-semibold text-muted-foreground backdrop-blur transition-colors hover:text-foreground aria-pressed:border-[var(--active-border)] aria-pressed:bg-[var(--active-bg)] aria-pressed:text-primary',
  sceneMeta:
    'pointer-events-none absolute bottom-3.5 left-3.5 flex flex-col items-start gap-1.5 text-xs',
  sceneMetaLine:
    'rounded-lg border border-border bg-black/45 px-2.5 py-1.5 font-medium text-muted-foreground backdrop-blur',
  sceneMetaWarn:
    'rounded-lg border border-amber-400/25 bg-amber-400/10 px-2.5 py-1.5 font-medium text-amber-300 backdrop-blur',
  // 호버는 색으로 예고만, 클릭이 확정이다 — 즉발 흐림은 깜빡임으로 읽힌다 (실측 피드백).
  // 지목되지 않은 이름표는 함께 물러난다 — 오브젝트만 흐려지면 이름표가 유령 위에 떠 있다
  assemblyLabel:
    'cursor-pointer whitespace-nowrap rounded-md border border-border bg-black/65 px-2 py-0.5 text-[11px] font-semibold text-foreground backdrop-blur transition-[opacity,color,border-color,background-color] duration-300 hover:border-[var(--active-border)] hover:text-primary data-[focused=true]:border-[var(--active-border)] data-[focused=true]:bg-[var(--active-bg)] data-[focused=true]:text-primary data-[dimmed=true]:opacity-15',
  sceneEmpty:
    'flex min-h-[240px] flex-col items-center justify-center gap-2 rounded-[14px] border border-dashed border-border bg-popover text-[0.85rem] text-muted-foreground',

  studioInput: 'w-full',
  studioResult: 'w-full',
  tabs: 'mb-5 inline-flex gap-1 rounded-[10px] border border-border bg-card p-1',
  tab: "cursor-pointer rounded-[7px] border-0 bg-transparent px-4 py-[7px] text-sm font-semibold text-muted-foreground transition-[background,color] duration-[var(--dur-hover)] ease-[var(--ease-out)] aria-[selected='true']:bg-[var(--sunken-bg)] aria-[selected='true']:text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1",

  // 이미지 입력과 거절 상태
  dropzone:
    "flex min-h-[200px] w-full cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-[1.5px] border-dashed border-border bg-card p-8 text-[0.9375rem] text-muted-foreground transition-[border-color,background] duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[dragging='true']:border-primary data-[dragging='true']:bg-[color-mix(in_srgb,var(--primary)_8%,var(--card))] focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2",
  dropzoneHint: 'text-[0.8125rem] text-muted-foreground',
  preview: 'max-h-[280px] max-w-full rounded-lg object-contain',
  fileName: 'text-[0.8125rem] text-foreground',
  rejection: 'mt-2 text-[0.8125rem] text-destructive',

  // 공급자와 모델 설정
  settingsRow:
    'mt-5 grid grid-cols-2 items-start gap-5 [&>*]:m-0 [&>:only-child]:col-span-full max-[720px]:grid-cols-1 max-[720px]:gap-3.5',
  field: 'mt-5 flex flex-col gap-1.5',
  label: 'text-[0.8125rem] font-semibold text-muted-foreground',
  textarea:
    'min-h-[120px] resize-y rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-sm text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1',
  // review-gate — 검수 화면의 파츠 추가 폼
  input:
    'rounded-lg border border-border bg-[var(--sunken-bg)] px-3 py-[9px] text-sm text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1',
  actions: 'mt-5 flex justify-end gap-2',
  notice: 'mb-5 rounded-[10px] border border-border bg-card p-4 text-sm text-muted-foreground',
  noticeLink: 'font-semibold text-primary',
  // 처음 쓰는 사람에게 "여기서 뭘 해야 하는지"를 알리는 안내 — 카드와 같은 회색이면
  // 배경에 묻혀 눈에 안 띈다는 지적으로 노란 계열로 분리했다
  onboardingNotice:
    'mb-5 rounded-[10px] border border-amber-500/40 bg-amber-500/10 p-4 text-sm text-foreground dark:border-amber-400/40 dark:bg-amber-400/10',

  // 작업이 무엇으로 도는가 — 진행·결과 공통 머리줄
  modelSummary: 'mb-4 flex flex-wrap items-center gap-2',
  modelBadge:
    'inline-flex items-center gap-2 rounded-full border border-border bg-card px-3 py-1 text-xs',
  modelBadgeLabel: 'font-semibold text-muted-foreground',
  modelBadgeProvider: 'text-muted-foreground',
  // 모델 id 는 코드값이므로 고정폭으로 — 비교할 때 눈이 자리를 잡는다
  modelBadgeModel: 'font-mono text-foreground',

  // 실행 진행 카드
  progress:
    'flex items-center gap-5 rounded-xl border border-border bg-card p-6 max-[720px]:items-start max-[720px]:gap-3.5 max-[720px]:p-4',
  progressThumb:
    'size-[120px] shrink-0 rounded-lg bg-[var(--sunken-bg)] object-contain max-[720px]:size-[76px]',
  progressBody: 'min-w-0 flex-1',
  progressTitle: 'mb-1.5 text-base font-semibold text-foreground',
  progressHint: 'text-[0.8125rem] text-muted-foreground',
  progressActions: 'mt-4',

  // 진행 중 결과 상단 — 부분 산출물과 유료 작업 중단 동작의 고정 자리
  liveActionBar:
    'mb-5 flex min-h-14 items-center justify-between gap-4 rounded-xl border border-primary/35 bg-[color-mix(in_srgb,var(--primary)_7%,var(--card))] px-4 py-3 max-[560px]:items-start',
  liveActivity: 'flex min-w-0 items-center gap-2 text-sm font-semibold text-foreground',
  livePulse:
    'size-2.5 shrink-0 rounded-full bg-primary shadow-[0_0_0_4px_color-mix(in_srgb,var(--primary)_16%,transparent)] animate-pulse motion-reduce:animate-none',

  // 장면 명세
  scene: 'rounded-xl border border-border bg-card px-[18px] py-4',
  sceneHeader: 'mb-3 flex items-baseline gap-2.5',
  sceneLabel: 'text-[0.8125rem] font-semibold text-muted-foreground',
  sceneSummary: 'text-[0.9375rem] font-semibold text-foreground',
  palette: 'mb-3.5 flex flex-wrap gap-x-3 gap-y-1.5',
  swatch: 'inline-flex items-center gap-1.5 text-xs text-muted-foreground',
  // 색을 아는 칩은 인라인 backgroundColor 가 채운다. 밝은 색도 경계가 보이도록 border 유지
  swatchChip: 'size-3.5 rounded border border-border',
  // 색상 미확정 — `transparent` 로 두면 지금 고치는 결함과 똑같이 빈 칩으로 읽힌다 (D-11)
  swatchChipUnresolved: 'size-3.5 rounded border border-dashed border-border bg-[var(--sunken-bg)]',
  sceneGrid: 'grid grid-cols-[repeat(auto-fit,minmax(260px,1fr))] gap-x-5 gap-y-2.5',
  sceneField: 'min-w-0',
  sceneFieldLabel: 'mb-0.5 text-xs text-muted-foreground',
  sceneFieldValue: 'text-[0.8125rem] leading-normal text-foreground',

  // 원본 이미지 좌표 오버레이
  overlay: 'mt-5',
  overlayFrame: 'relative overflow-hidden rounded-xl border border-border bg-[var(--sunken-bg)]',
  overlayImage: 'block w-full',
  // 평소엔 없다 — 지목했을 때만 뜬다 (사이클 #9 §8.2)
  box: "pointer-events-none absolute box-border rounded-[3px] border-2 opacity-0 transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[shown='true']:opacity-100",
  // 상자를 끄는 손잡이 — 화면 픽셀 고정이라 상자가 작아져도 잡을 수 있다
  handle:
    'absolute z-[2] size-[10px] -translate-x-1/2 -translate-y-1/2 rounded-[2px] border border-white bg-[var(--fg)] shadow-[0_1px_3px_rgba(0,0,0,0.45)]',
  // 상자 안쪽을 잡아 통째로 옮긴다
  handleBody: 'absolute z-[1] cursor-move',
  // 앵커 점 — 흰 링이 없으면 밝은 배경에서 사라진다
  anchor:
    "absolute z-[1] size-[9px] -translate-x-1/2 -translate-y-1/2 rounded-full shadow-[0_0_0_2px_rgba(255,255,255,0.9),0_1px_3px_rgba(0,0,0,0.4)] transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[dimmed='true']:opacity-30",
  // 겹침을 피해 밀린 칩을 앵커로 되묶는다
  tether:
    "absolute z-[1] w-[1.5px] -translate-x-1/2 rounded-full opacity-70 transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)] data-[dimmed='true']:opacity-[0.16]",
  chipGroup: 'absolute z-[2] mt-1.5 flex items-center gap-[5px]',
  // 사진 위에 뜨므로 그림자로 경계를 만든다 — 테두리로 그으면 파츠 색과 싸운다
  chip: "cursor-pointer whitespace-nowrap rounded-[10px] px-[9px] py-1.5 text-[0.71875rem] font-bold leading-none text-white shadow-[0_1px_2px_rgba(0,0,0,0.28),0_4px_10px_rgba(0,0,0,0.22)] transition-[transform,box-shadow,opacity] duration-[var(--dur-hover)] ease-[var(--ease-out)] active:scale-[0.97] data-[dimmed='true']:opacity-[0.34] data-[stuck='true']:outline data-[stuck='true']:outline-2 data-[stuck='true']:outline-offset-1 data-[stuck='true']:outline-white hover:shadow-[0_2px_5px_rgba(0,0,0,0.32),0_10px_24px_rgba(0,0,0,0.3)]",
  // 파츠 색과 구분돼야 다른 동작임이 읽힌다
  chipJump:
    'cursor-pointer whitespace-nowrap rounded-[10px] bg-black/70 px-2 py-1.5 text-[0.6875rem] font-bold leading-none text-white shadow-[0_1px_2px_rgba(0,0,0,0.28),0_4px_10px_rgba(0,0,0,0.22)] transition-transform duration-[var(--dur-hover)] ease-[var(--ease-out)] active:scale-[0.97]',
  horizon:
    'absolute inset-x-0 border-t-2 border-dashed border-white/85 [&_span]:absolute [&_span]:-top-[18px] [&_span]:right-1 [&_span]:text-[0.6875rem] [&_span]:font-bold [&_span]:text-white [&_span]:[text-shadow:0_0_4px_#000]',
  overlayEmpty: 'mt-2.5 text-[0.8125rem] text-muted-foreground',

  // 파츠 명세 카드
  partsSection: 'mt-7',
  partsTitle: 'mb-3 text-sm font-semibold text-muted-foreground',
  parts: 'flex list-none flex-col gap-2.5 p-0',
  part: 'rounded-[10px] border border-border bg-card px-3.5 py-3 text-foreground',
  partHead: 'flex flex-wrap items-baseline gap-2',
  partName: 'text-[0.9375rem] font-semibold',
  partCategory:
    'rounded-full border border-border px-[7px] py-0.5 text-[0.6875rem] text-muted-foreground',
  partDepth:
    'rounded-full border border-border px-[7px] py-0.5 text-[0.6875rem] text-muted-foreground',
  partDescription: 'mt-2 text-[0.8125rem] leading-[1.6] text-muted-foreground',
  partOccluded: 'mt-1.5 text-xs text-muted-foreground',

  // 파츠 제작 파이프라인 리스트 — 파츠·4방향 이미지·3D 에셋의 한 행 연결
  tally: 'mb-3 flex flex-wrap items-baseline gap-2 text-sm text-muted-foreground',
  // 부분 성공 배지는 성공과 색이 달라야 한다 (원칙 ③) — 뭉개면 사용자가 N장을 받았다고 믿는다
  statusBadge:
    "rounded-full border px-2.5 py-0.5 text-xs font-semibold data-[status='succeeded']:border-primary data-[status='succeeded']:text-primary data-[status='partiallySucceeded']:border-[var(--warning,#b8860b)] data-[status='partiallySucceeded']:text-[var(--warning,#b8860b)] data-[status='failed']:border-destructive data-[status='failed']:text-destructive data-[status='canceled']:border-border data-[status='canceled']:text-muted-foreground",
  partPipelineHeader:
    'grid grid-cols-[minmax(180px,0.9fr)_minmax(440px,2fr)_minmax(180px,0.8fr)] gap-5 rounded-t-xl border border-border bg-[var(--sunken-bg)] px-5 py-2.5 text-[0.6875rem] font-semibold uppercase tracking-[0.08em] text-muted-foreground max-[900px]:hidden',
  partPipelineList:
    'list-none divide-y divide-border overflow-hidden rounded-b-xl border-x border-b border-border bg-card p-0 max-[900px]:rounded-xl max-[900px]:border',
  // 개체 수 — 이름 옆에 붙어 "몇 개짜리 파츠인가" 를 목록에서 바로 읽게 한다
  partCountBadge:
    'ml-1.5 shrink-0 rounded bg-[var(--sunken-bg)] px-1.5 py-px text-[0.6875rem] font-semibold tabular-nums text-muted-foreground',
  // 배치 목록 — 한 줄에 하나. 스무 개까지 오므로 스크롤을 붙인다
  placementList: 'mt-1 flex max-h-44 flex-col gap-0.5 overflow-y-auto',
  placementRow:
    'flex items-baseline gap-2 font-mono text-[0.75rem] tabular-nums text-muted-foreground',
  placementIndex:
    'inline-flex min-w-4 justify-end text-[0.6875rem] font-semibold text-foreground/45',
  partSpecificationCount:
    'ml-1.5 rounded bg-[var(--sunken-bg)] px-1.5 py-px text-[0.6875rem] font-semibold tabular-nums text-muted-foreground',
  partRow:
    'grid grid-cols-[minmax(180px,0.9fr)_minmax(440px,2fr)_minmax(120px,0.5fr)] items-center gap-5 px-5 py-4 max-[900px]:grid-cols-1 max-[900px]:gap-4 scroll-mt-6 data-[landed=true]:animate-[land_1400ms_var(--ease-out)]',
  partRowInfo:
    'flex min-w-0 cursor-pointer flex-col items-start gap-1.5 rounded-lg border-0 bg-transparent p-0 text-left text-foreground outline-none transition-[color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] hover:text-primary active:scale-[var(--press-scale)] focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-2 motion-reduce:transition-none motion-reduce:active:scale-100',
  partRowInfoHeader: 'flex w-full min-w-0 items-center justify-between gap-2',
  partSpecToggle: 'shrink-0 text-[0.6875rem] font-semibold text-muted-foreground',
  partRowDescription: 'line-clamp-2 text-xs leading-relaxed text-muted-foreground',
  // **3D 칸이 0.5fr 인 것과 짝이다** — 이 칸이 2fr 에 넷이므로 한 칸이 0.5fr 이고,
  // 그래서 3D 타일이 방향 이미지 한 칸과 같은 크기로 선다
  // 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 체크박스 — 4방향 이미지 위, 항상 펼쳐짐
  partMeshAxis:
    'mb-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[0.6875rem] text-muted-foreground',
  partMeshAxisLabel: 'flex items-center gap-1 whitespace-nowrap',
  partMeshAxisError: 'w-full text-[0.6875rem] font-medium text-destructive',
  partViews: 'grid grid-cols-4 gap-2 max-[560px]:grid-cols-2',
  partView: 'relative overflow-hidden rounded-md bg-[var(--sunken-bg)]',
  partViewLabel:
    'pointer-events-none absolute left-1.5 top-1.5 z-10 rounded bg-black/70 px-1.5 py-0.5 text-[0.625rem] font-semibold text-white',
  partCardImageButton:
    'group relative block w-full cursor-zoom-in overflow-hidden rounded-md outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-inset',
  partCardImage: 'aspect-square w-full rounded-md object-contain bg-[var(--sunken-bg)]',
  partCardImageHint:
    'pointer-events-none absolute bottom-1.5 right-1.5 rounded-full bg-black/65 px-1.5 py-0.5 text-[0.625rem] font-semibold text-white opacity-0 transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)] group-hover:opacity-100 group-focus-visible:opacity-100 motion-reduce:transition-none',
  partImageDialog:
    'm-auto w-[min(960px,calc(100vw-2rem))] max-h-[calc(100dvh-2rem)] overflow-visible rounded-xl border border-border bg-card p-0 text-foreground shadow-2xl [&::backdrop]:bg-black/80',
  partImageDialogBody: 'flex max-h-[calc(100dvh-2rem)] flex-col overflow-hidden rounded-xl',
  partImageDialogHeader:
    'flex min-h-12 items-center justify-between gap-3 border-b border-border px-3 py-2',
  partImageDialogHeading: 'flex min-w-0 items-baseline gap-2',
  partImagePreview: 'h-[min(66dvh,640px)] min-h-0 w-full bg-[#f2f2f2] object-contain',
  partImageDialogTitle: 'truncate text-sm font-semibold text-foreground',
  partCarouselPosition: 'shrink-0 text-xs text-muted-foreground',
  partCarouselStage:
    'grid min-h-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 bg-black px-2 py-2',
  partCarouselArrow: 'z-10 bg-card/90 shadow-lg backdrop-blur-sm',
  partCarouselThumbs: 'grid grid-cols-4 gap-2 border-t border-border p-3',
  partCarouselThumb:
    'grid min-w-0 grid-cols-[40px_minmax(0,1fr)] items-center gap-2 rounded-lg border border-border p-1.5 text-left text-xs font-semibold text-muted-foreground outline-none transition-[border-color,color,transform] duration-[var(--dur-release)] ease-[var(--ease-out)] hover:text-foreground active:scale-[var(--press-scale)] aria-[current=true]:border-primary aria-[current=true]:text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1 motion-reduce:transition-none motion-reduce:active:scale-100 max-[560px]:grid-cols-1 max-[560px]:text-center',
  partCarouselThumbImage:
    'aspect-square size-10 rounded bg-[#f2f2f2] object-contain max-[560px]:mx-auto',
  partCarouselThumbEmpty:
    'flex aspect-square size-10 items-center justify-center rounded bg-[var(--sunken-bg)] text-destructive max-[560px]:mx-auto',
  partCarouselFailed:
    'flex h-[min(66dvh,640px)] min-h-0 w-full flex-col items-center justify-center gap-2 bg-[#f2f2f2] p-6 text-center text-xs text-destructive',
  partCarouselFailedMark:
    'flex size-9 items-center justify-center rounded-full border border-destructive text-lg font-bold',
  partCarouselFailedTitle: 'text-sm font-semibold',
  partCarouselPending:
    'flex h-[min(66dvh,640px)] min-h-0 w-full items-center justify-center bg-[#f2f2f2] text-sm text-muted-foreground',
  partCardPlaceholder:
    'flex aspect-square w-full flex-col items-center justify-center gap-2.5 overflow-hidden rounded-md border border-border/70 bg-[linear-gradient(145deg,var(--sunken-bg),color-mix(in_srgb,var(--primary)_5%,var(--card)))] p-2.5 text-center data-[activity=running]:border-primary/30 data-[activity=queued]:border-border',
  partCardFailed:
    'flex aspect-square w-full flex-col items-center justify-center gap-1.5 rounded-md border border-destructive bg-[color-mix(in_srgb,var(--destructive)_8%,transparent)] p-2 text-center text-xs text-destructive',
  partCardName: 'truncate text-[0.8125rem] font-semibold text-foreground',
  partCardMeta: 'truncate text-xs text-muted-foreground',
  part3dState: 'flex min-w-0 flex-col items-start gap-2',
  meshStatusCard:
    'flex aspect-square w-[calc(100%-6px)] min-w-0 flex-col items-center justify-center gap-2.5 overflow-hidden rounded-md border border-border/70 bg-[linear-gradient(145deg,var(--sunken-bg),color-mix(in_srgb,var(--primary)_5%,var(--card)))] p-2.5 text-center data-[activity=running]:border-primary/30 data-[activity=queued]:border-border data-[activity=failed]:border-destructive/45 data-[activity=failed]:bg-[color-mix(in_srgb,var(--destructive)_7%,var(--card))]',
  statusActivity:
    "relative grid size-8 shrink-0 place-items-center rounded-full border border-primary/25 bg-primary/10 text-xs font-bold text-primary after:absolute after:-top-0.5 after:left-1/2 after:size-2 after:-translate-x-1/2 after:rounded-full after:bg-primary after:content-[''] data-[activity=running]:animate-[spin_720ms_linear_infinite] data-[activity=queued]:animate-pulse data-[activity=idle]:border-border data-[activity=idle]:bg-card data-[activity=idle]:text-muted-foreground data-[activity=idle]:after:hidden data-[activity=failed]:border-destructive/40 data-[activity=failed]:bg-destructive/10 data-[activity=failed]:text-destructive data-[activity=failed]:after:hidden motion-reduce:animate-none",
  statusCopy: 'flex min-w-0 flex-col items-center gap-0.5',
  statusTitle: 'max-w-full text-xs font-semibold leading-tight text-foreground',
  statusDetail: 'line-clamp-2 text-[0.6875rem] leading-tight text-muted-foreground',

  // 3D 진행률 — 막대가 뒤로 가면 사용자는 실패로 읽으므로 값은 앞으로만 간다 (서버가 보장)
  meshProgressTrack: 'h-1.5 w-full overflow-hidden rounded-full bg-muted',
  meshProgressFill:
    "relative h-full overflow-hidden rounded-full bg-primary transition-[width] duration-[var(--dur-progress)] ease-[var(--ease-out)] after:absolute after:inset-0 after:content-[''] after:bg-[linear-gradient(90deg,transparent,color-mix(in_srgb,white_35%,transparent),transparent)] after:animate-[stripe-drift_720ms_linear_infinite] motion-reduce:transition-none motion-reduce:after:animate-none",

  // 3D 뒤늦게 붙이기 (사이클 #11). 결과와 "다시 분석" 사이에 선다
  meshBackfill: 'flex flex-col gap-2 rounded-lg border border-border bg-card/40 p-4',
  meshBackfillNote: 'text-xs text-muted-foreground',

  // **`w-full` 이 필요하다** — 부모 `part3dState` 가 `items-start` 라 자식이 내용 폭으로
  // 줄어들고, 그러면 타일의 `w-full` 이 줄어든 폭을 기준으로 풀린다 (실측: 83px)
  meshResult: 'flex w-full min-w-0 flex-col items-stretch gap-2',

  // 3D 타일 (사이클 #13). **방향 이미지 한 칸과 같은 규격이다** — 가장 비싼 산출물이
  // 가장 작게 보이던 64px 고정을 없앴다
  // **`-6px` 는 임의의 값이 아니다.** 3D 칸은 0.5fr 이고 이미지 칸은 2fr 에 넷이라 열 폭은
  // 같지만, 이미지 쪽은 내부 간격 셋(`gap-2` = 8px)을 나눠 진다 — 한 칸이 `0.5fr - 6px` 다.
  // 그만큼 빼야 두 타일이 같은 크기로 선다 (3 × 8px ÷ 4)
  meshTileButton:
    'group relative block w-[calc(100%-6px)] cursor-pointer overflow-hidden rounded-md outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-inset',
  meshTileImage: 'aspect-square w-full rounded-md object-contain bg-[var(--sunken-bg)]',
  meshTilePlaceholder:
    'flex aspect-square w-full items-center justify-center rounded-md bg-[var(--sunken-bg)] text-xs font-semibold text-muted-foreground',
  meshTileHint:
    'pointer-events-none absolute bottom-1.5 right-1.5 rounded-full bg-black/65 px-1.5 py-0.5 text-[0.625rem] font-semibold text-white opacity-0 transition-opacity duration-[var(--dur-hover)] ease-[var(--ease-out)] group-hover:opacity-100 group-focus-visible:opacity-100 motion-reduce:transition-none',
  meshDownloadItem: 'w-full no-underline',

  // 3D 뷰어 — 이미지 캐러셀과 같은 높이 규약을 쓴다
  meshViewerDialog:
    'm-auto w-[min(960px,calc(100vw-2rem))] max-h-[calc(100dvh-2rem)] overflow-visible rounded-xl border border-border bg-card p-0 text-foreground shadow-2xl [&::backdrop]:bg-black/80',
  meshViewerBody: 'flex max-h-[calc(100dvh-2rem)] flex-col overflow-hidden rounded-xl',
  meshViewerHeader:
    'flex min-h-12 items-center justify-between gap-3 border-b border-border px-3 py-2',
  meshViewerHeading: 'flex min-w-0 items-baseline gap-2',
  meshViewerActions: 'flex shrink-0 items-center gap-2',
  meshViewerStage: 'relative h-[min(66dvh,640px)] min-h-0 w-full bg-[#f2f2f2]',
  meshViewerModel: 'size-full',
  meshViewerLoading:
    'pointer-events-none absolute inset-0 flex items-center justify-center text-sm text-muted-foreground',
  meshViewerFallback:
    'flex h-[min(66dvh,640px)] min-h-0 w-full flex-col items-center justify-center gap-2 bg-[#f2f2f2] p-6 text-center text-sm text-muted-foreground',
  meshViewerNotice: 'border-t border-border px-3 py-2 text-xs text-muted-foreground',
  partSpecificationDialog:
    'm-auto w-[min(720px,calc(100vw-2rem))] max-h-[calc(100dvh-2rem)] overflow-visible rounded-xl border border-border bg-card p-0 text-foreground shadow-2xl [&::backdrop]:bg-black/80',
  partSpecificationDialogBody: 'overflow-hidden rounded-xl',
  partSpecificationDialogHeader:
    'flex min-h-12 items-center justify-between gap-3 border-b border-border px-4 py-3',
  partSpecificationDialogHeading: 'flex min-w-0 items-baseline gap-2',
  partSpecification:
    'grid grid-cols-[repeat(4,minmax(0,1fr))] gap-x-5 gap-y-5 p-5 max-[620px]:grid-cols-2 max-[460px]:grid-cols-1',
  partSpecificationField: 'min-w-0',
  partSpecificationWide: 'col-span-2 min-w-0 max-[460px]:col-span-1',
  partSpecificationLabel: 'mb-1 text-[0.6875rem] font-semibold text-muted-foreground',
  partSpecificationValue: 'm-0 text-xs leading-relaxed text-foreground',

  // 사용량 — 스튜디오는 접힌 채, 관리자는 전부 편다 (D-14 · FR-19)
  usage:
    'mt-6 border-t border-border [&_summary]:w-fit [&_summary]:cursor-pointer [&_summary]:pt-3 [&_summary]:text-xs [&_summary]:font-semibold [&_summary]:text-muted-foreground [&[open]_summary]:text-foreground',
  usageTable: 'mt-3 w-full border-collapse text-xs',
  usageHeadCell: 'border-b border-border pb-1.5 text-left font-semibold text-muted-foreground',
  usageCell: 'border-b border-border py-1.5 text-foreground',
  usageNumeric: 'border-b border-border py-1.5 text-right tabular-nums text-foreground',
  usageUnpriced: 'border-b border-border py-1.5 text-right text-muted-foreground',

  // 실패 상태
  failure:
    'rounded-xl border border-destructive bg-[color-mix(in_srgb,var(--destructive)_8%,transparent)] p-5',
  failureTitle: 'mb-1.5 text-base font-semibold text-foreground',
  failureReason: 'mb-4 text-sm text-muted-foreground',

  // 공정 진행 막대와 요약
  stageProgress: 'w-full',
  progressBar: 'relative my-3.5 mb-1.5 h-1.5 overflow-hidden rounded-full bg-[var(--sunken-bg)]',
  progressBuffer:
    "absolute left-0 top-0 h-full overflow-hidden rounded-full bg-[color-mix(in_srgb,var(--primary)_22%,transparent)] transition-[width] duration-[var(--dur-progress)] ease-[var(--ease-out)] after:absolute after:inset-0 after:w-[calc(100%+20px)] after:content-[''] after:bg-[repeating-linear-gradient(45deg,color-mix(in_srgb,var(--primary)_34%,transparent)_0_7.07px,transparent_7.07px_14.14px)] after:animate-[stripe-drift_1s_linear_infinite] motion-reduce:transition-none motion-reduce:after:animate-none",
  progressBufferIdle: 'after:animate-none',
  progressValue:
    'absolute left-0 top-0 h-full rounded-full bg-primary transition-[width] duration-[var(--dur-progress)] ease-[var(--ease-out)] motion-reduce:transition-none',
  progressSummary:
    'grid grid-cols-[auto_minmax(0,1fr)_auto] items-baseline gap-2 text-xs text-muted-foreground max-[720px]:grid-cols-[auto_minmax(0,1fr)]',
  progressPosition: 'font-mono font-semibold text-foreground',
  progressCurrent: 'min-w-0 truncate',
  progressPercent: 'tabular-nums max-[720px]:col-span-full',

  // 공정 상세 목록
  stageDetails:
    'mt-3 border-t border-border [&_summary]:w-fit [&_summary]:cursor-pointer [&_summary]:pt-2.5 [&_summary]:text-xs [&_summary]:font-semibold [&_summary]:text-muted-foreground [&[open]_summary]:text-foreground [&_.stage-list]:max-h-80 [&_.stage-list]:overflow-y-auto [&_.stage-list]:pr-2',
  stages: 'stage-list mt-3 flex list-none flex-col gap-1.5 p-0',
  stage:
    'group/stage grid grid-cols-[16px_minmax(72px,auto)_1fr] items-baseline gap-2 text-[0.8125rem] text-muted-foreground',
  stageMark:
    'text-center group-data-[status=failed]/stage:text-destructive group-data-[status=running]/stage:font-semibold group-data-[status=running]/stage:text-foreground group-data-[status=succeeded]/stage:text-primary',
  stageName:
    'group-data-[status=running]/stage:font-semibold group-data-[status=running]/stage:text-foreground',
  stageNote: 'min-w-0 break-words group-data-[status=failed]/stage:text-destructive',

  // ─── 유사도 inspector (background-similarity-tuning §14) ───
  // 장면 아래 전체 폭 — 3D 가 화면 높이를 다 쓰므로(§FR-05) 옆에 세우면 장면이 좁아진다
  // (사용자 결정, 설계 §14.1 의 우측 360px 을 대체)
  sceneRow: 'flex flex-col gap-4',
  sceneMain: 'min-w-0 w-full',
  // 최초 진입 180~220ms ease-out, opacity + 4px translate 만 (§14.3)
  similarityPanel:
    'w-full rounded-[14px] border border-border bg-popover p-4 motion-safe:animate-[similarity-enter_200ms_var(--ease-out)] motion-reduce:animate-none',
  similarityTitle: 'mb-3 text-sm font-bold text-foreground',
  similarityHint: 'mt-2 text-xs text-muted-foreground',
  similarityError: 'mt-2 text-xs text-destructive',
  similarityBlocking: 'flex list-disc flex-col gap-1 pl-4 text-xs text-muted-foreground',
  similarityForm: 'flex flex-col gap-2.5',
  // 전체 폭에서는 선택 세 칸이 한 줄에 선다 — 세로로 쌓으면 아래 결과가 멀어진다
  similarityFormRow: 'grid gap-2.5 sm:grid-cols-3',
  similarityField: 'flex flex-col gap-1 text-xs font-semibold text-muted-foreground',
  similaritySelect:
    'rounded-lg border border-border bg-background px-2.5 py-1.5 text-[0.8125rem] font-normal text-foreground',
  similarityEstimate: 'text-xs font-semibold text-foreground',
  similarityConfirm: 'flex items-center gap-2 text-xs text-muted-foreground',
  similarityRun: 'flex flex-col gap-3',
  similarityStatus: 'text-xs font-semibold text-muted-foreground',
  // 점수는 count-up 하지 않는다 — 도착 즉시 그대로 (§14.3)
  similarityOverall: 'text-3xl font-bold tabular-nums text-foreground',
  similarityOverallUnit: 'ml-1 text-sm font-normal text-muted-foreground',
  similarityDims: 'flex list-none flex-col gap-1.5 p-0',
  similarityDim: 'grid grid-cols-[52px_1fr_32px] items-center gap-2 text-xs',
  similarityDimLabel: 'text-muted-foreground',
  similarityDimBar: 'h-1.5 overflow-hidden rounded-full bg-[var(--sunken-bg)]',
  similarityDimFill: 'block h-full rounded-full bg-primary',
  similarityDimScore: 'text-right font-mono tabular-nums text-foreground',
  similarityAdjustments: 'flex flex-col gap-2 border-t border-border pt-3',
  similarityAdjustment:
    'flex cursor-pointer items-start gap-2 rounded-lg border border-border p-2.5 text-xs',
  similarityAdjustmentBody: 'flex min-w-0 flex-col gap-0.5',
  similarityAdjustmentSummary: 'font-semibold text-foreground',
  similarityAdjustmentReason: 'text-muted-foreground',
  similarityNotes: 'rounded-lg bg-[var(--sunken-bg)] p-2.5 text-xs',
  similarityNotesTitle: 'mb-1 font-semibold text-foreground',
  similarityNote: 'text-muted-foreground',
  similarityActions: 'flex flex-wrap gap-2',
  // 비교 슬라이더 (§14.1) — clip-path 로 렌더를 가른다. spring·관성 없음 (§14.3)
  similarityCompare: 'flex flex-col gap-1.5',
  // 스테이지 비율은 인라인 style(캡처 프레임 = 원본 비율)이 정한다 — 여기는 폭·상한만.
  // 세로형 원본이 화면을 넘지 않게 높이 상한을 두고, 가운데 정렬한다
  similarityCompareStage:
    'relative mx-auto aspect-square max-h-[70vh] w-full max-w-full overflow-hidden rounded-lg border border-border bg-[var(--sunken-bg)]',
  similarityCompareImage: 'absolute inset-0 h-full w-full object-contain',
  similarityCompareBar:
    'pointer-events-none absolute inset-y-0 w-px -translate-x-1/2 bg-primary/70',
  similarityCompareRange: 'w-full accent-[var(--primary)]',
  similarityHistory:
    'mt-3 border-t border-border pt-2.5 text-xs [&_summary]:cursor-pointer [&_summary]:font-semibold [&_summary]:text-muted-foreground [&[open]_summary]:text-foreground',
  similarityHistoryList: 'mt-2 flex list-none flex-col gap-1.5 p-0',
  similarityHistoryItem: 'flex items-center justify-between gap-2',
  similarityHistoryLabel: 'text-muted-foreground data-[state=active]:text-foreground',
  similarityHistoryRestore:
    'cursor-pointer rounded-md border border-border px-2 py-0.5 text-xs font-semibold text-muted-foreground hover:text-foreground',
} as const
