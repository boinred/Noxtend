/**
 * Design Ref: §5.1 — 홈. 작업 현황의 자리 (FR-06).
 *
 * 직전 사이클에서 홈은 "무엇을 만들 것인가" 를 고르는 화면이었다.
 * 사이드바가 그 역할을 가져갔으므로 (Plan §1.2) 홈은 다른 것을 맡는다:
 * 지금 무슨 일이 돌고 있고, 무엇을 했는지.
 *
 * background-studio 사이클에서 **두 섹션이 실제 목록을 받는다** (FR-10).
 * 빈 상태 경로는 그대로 남는데, 이것이 §8.6 의 회귀 방어 장치다 —
 * `useJobList` 가 API 부재를 빈 배열로 흡수하므로 백엔드 없이도 홈이 깨지지 않는다.
 *
 * 자체 헤더가 없다 — `AppLayout` 의 공통 헤더가 대신한다 (§4.3 #6).
 *
 * 폭은 `PageContainer` 의 `list`(780) 다 (§5.0). 사이드바 사이클에서 자체
 * `max-width: 760px` 를 갖고 있었는데, 규약이 생긴 뒤에도 홈만 이관되지 않았다.
 *
 * 이관할 때 처음엔 `narrow`(960)로 옮겼다 — §5.0 표가 "결과 · 목록" 을 한 칸에
 * 묶었기 때문이다. 실측하니 행이 960px 를 차지하는데 내용은 200px 였다.
 * 960 의 근거는 산문 읽기 폭이고, 여기 콘텐츠는 짧은 행이라 그 근거가 적용되지 않는다.
 */
import { Link } from 'react-router-dom'
import { Icon } from '@/features/shell/Icon'
import { PageContainer } from '@/features/screens/PageContainer'
import { WorkStatusSection } from '@/features/screens/home/WorkStatusSection'
import { ActiveJobSpotlight } from '@/features/screens/home/ActiveJobSpotlight'
import { useDeleteJob, useJobList } from '@/app/queries/useJobList'
import { ROUTES } from '@/routes/paths'
import { prefetchBackgroundStudio } from '@/routes/prefetch'
import { homeScreenStyles as styles } from './homeStyles'

export function HomeScreen() {
  const active = useJobList('active')
  const terminal = useJobList('terminal')
  const deleteMutation = useDeleteJob()

  // 완료 작업 삭제 처리. **문구가 실제와 같아야 한다** — 목록에서만 치우는 것이
  // 아니라 생성 이미지·3D·원본과 비용 기록까지 사라지고, 되돌릴 수 없다
  function handleDeleteJob(jobId: string) {
    const warning =
      '이 작업을 완전히 삭제할까요?\n\n' +
      '생성된 이미지와 3D, 원본 이미지, 비용 기록까지 함께 지워집니다.\n' +
      '되돌릴 수 없고, 사용량의 지난 총액도 그만큼 줄어듭니다.'

    if (window.confirm(warning)) {
      deleteMutation.mutate(jobId)
    }
  }

  return (
    <PageContainer
      width="max"
      title="오늘의 제작 현황"
      subtitle="새 작업을 시작하거나 진행 중인 공정을 이어서 확인하세요."
      testId="home-screen"
    >
      <div className={styles.sections}>
        <div
          className="grid grid-cols-2 gap-4 max-[720px]:grid-cols-1"
          data-testid="home-launch-cards"
        >
          <Link
            to={ROUTES.background}
            className={styles.launch}
            data-testid="home-cta-studio"
            onMouseEnter={prefetchBackgroundStudio}
            onFocus={prefetchBackgroundStudio}
          >
            <span className={styles.launchIcon}>
              <Icon name="image" size={22} />
            </span>
            <span className={styles.launchBody}>
              <span className={styles.launchEyebrow}>새 작업</span>
              <span className={styles.launchTitle}>배경 이미지 분석 시작</span>
              <span className={styles.launchCopy}>
                이미지를 올리면 장면 구조와 제작 가능한 파츠를 단계별로 분석합니다.
              </span>
            </span>
            <span className={styles.launchAction}>
              스튜디오 열기
              <Icon name="arrow-right" size={16} />
            </span>
          </Link>

          <Link
            to={ROUTES.character}
            className={styles.launch}
            data-testid="home-cta-character-studio"
          >
            <span className={styles.launchIcon}>
              <Icon name="user" size={22} />
            </span>
            <span className={styles.launchBody}>
              <span className={styles.launchEyebrow}>새 작업</span>
              <span className={styles.launchTitle}>캐릭터 튜닝·파츠 추출 시작</span>
              <span className={styles.launchCopy}>
                캐릭터 이미지를 올리면 성별과 파츠 힌트를 반영해 4방향 파츠를 추출합니다.
              </span>
            </span>
            <span className={styles.launchAction}>
              스튜디오 열기
              <Icon name="arrow-right" size={16} />
            </span>
          </Link>
        </div>

        <WorkStatusSection
          testId="section-running"
          title="실행 중"
          icon="play"
          featured={active.jobs[0] ? <ActiveJobSpotlight summary={active.jobs[0]} /> : undefined}
          jobs={active.jobs.slice(1)}
          total={active.total}
          emptyTitle="진행 중인 작업이 없습니다"
          emptyBody="스튜디오에서 이미지를 분석하면 진행 상황이 여기에 표시됩니다."
          action={
            <Link
              to={ROUTES.background}
              className={styles.cta}
              onMouseEnter={prefetchBackgroundStudio}
              onFocus={prefetchBackgroundStudio}
            >
              <Icon name="globe" size={14} />
              배경 스튜디오로 가기
            </Link>
          }
        />

        <WorkStatusSection
          testId="section-recent"
          title="최근 작업"
          icon="clock"
          jobs={terminal.jobs}
          total={terminal.total}
          onDelete={handleDeleteJob}
          emptyTitle="아직 완료된 작업이 없습니다"
          emptyBody={
            <>
              분석이 끝나면 최근 순으로 이 자리에 쌓입니다.
              <br />
              항목을 누르면 결과를 다시 열 수 있습니다.
            </>
          }
        />
      </div>
    </PageContainer>
  )
}
