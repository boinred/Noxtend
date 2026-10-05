/**
 * Design Ref: §5.1, §5.5 — 캐릭터·오브젝트·배경 공용 예고 화면 (FR-03).
 *
 * '준비 중' 은 고장이 아니라 예고임이 드러나야 한다 (§1.2).
 * 그래서 지금 할 수 있는 일(배경 스튜디오)로 가는 길을 함께 준다.
 *
 * sidebar-layout §4.3 #6 — 자체 헤더가 없어졌다.
 * 홈 복귀 링크는 사이드바가, 테마 토글은 공통 헤더가 대신한다.
 */
import { Link } from 'react-router-dom'
import { Icon } from '@/features/shell/Icon'
import { categoryMeta } from '@/features/screens/categoryLabels'
import { ROUTES } from '@/routes/paths'
import type { AssetCategory } from '@/domain/job/types'
import { comingSoonStyles as styles } from './comingSoonStyles'

export function ComingSoonScreen({ category }: { category: AssetCategory }) {
  const meta = categoryMeta(category)

  return (
    <div className={styles.screen} data-testid="coming-soon-screen" data-category={category}>
      <div className={styles.body}>
        <span className={styles.icon}>
          <Icon name={meta.icon} size={26} />
        </span>
        <h1 className={styles.title} data-testid="coming-soon-title">
          {meta.label}
        </h1>
        <p className={styles.message}>
          아직 준비 중인 기능입니다.
          <br />
          지금은 배경 스튜디오에서 이미지 분석 작업을 시작할 수 있습니다.
        </p>
        <Link to={ROUTES.background} className={styles.cta} data-testid="coming-soon-cta">
          <Icon name="globe" size={14} />
          배경 스튜디오 열기
        </Link>
      </div>
    </div>
  )
}
