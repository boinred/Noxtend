/**
 * 3D 결과를 형식으로 골라 받는 메뉴.
 *
 * Design Ref: §4.2 · Plan D-04·D-05
 *
 * **줄 수가 형식 수를 따라가지 않는 것이 요점이다.** 전에는 GLB 와 FBX 가 각각 한 줄로
 * 서 있었고, 셋째 형식이 오면 세 줄이 됐다.
 *
 * **행과 뷰어 양쪽이 이것을 쓴다.** 같은 동작을 두 군데 둔 것이 아니라 두 개의 다른
 * 순간이다 — 행은 이미 아는 것을 빨리 집는 자리, 뷰어는 돌려 보고 정하는 자리다.
 */
import { meshDownloadUrl, meshFbxDownloadUrl } from '@/app/queries/media'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { meshDownloadOptions } from '@/domain/job/types'
import { backgroundStyles as styles } from './backgroundStyles'
import type { GeneratedMesh, MeshDownloadFormat } from '@/domain/job/types'

/**
 * 형식을 주소로 옮긴다.
 *
 * **도메인이 이것을 못 한다** — `domain` 은 어떤 것도 import 하지 않으므로 (§9.2)
 * 주소를 만들 수단이 없다. 무엇을 보여줄지는 도메인이 정하고, 어디서 받을지는 여기서 정한다.
 */
function hrefOf(mesh: GeneratedMesh, format: MeshDownloadFormat): string {
  return format === 'fbx' ? meshFbxDownloadUrl(mesh.id) : meshDownloadUrl(mesh.id)
}

export function MeshDownloadMenu({ mesh, partName }: { mesh: GeneratedMesh; partName: string }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger data-testid="mesh-download-menu" aria-label={`${partName} 3D 내려받기`}>
        내려받기
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start">
        {meshDownloadOptions(mesh).map((option) => (
          <DropdownMenuItem key={option.format} asChild>
            {/*
              `asChild` 로 앵커를 그대로 쓴다 — 새 탭·주소 복사 같은 브라우저 기본 동작이
              살아 있어야 한다. onClick 으로 내려받으면 그것들이 전부 사라진다
            */}
            <a
              className={styles.meshDownloadItem}
              href={hrefOf(mesh, option.format)}
              download
              data-testid={`mesh-download-${option.format}`}
            >
              {option.label}
            </a>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
