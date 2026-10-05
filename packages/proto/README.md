# packages/proto — Protobuf 계약 (미작성)

Plan §2.2에 따라 **이 단계에서는 `.proto` 정의와 code generation 설정을 작성하지 않는다.**
전송 방식만 Protobuf + gRPC-Web으로 확정한 상태다.

## 정본 분할 (Design §4.4)

| 대상 | 정본 | 이유 |
|---|---|---|
| `GraphDocument` | **TypeScript** — `apps/frontend/src/domain/graph/types.ts` | 로컬 영속 대상. 전송 스키마와 수명주기가 다름. `schemaVersion`으로 독립 진화 |
| 실행 계약 (`SubmitRunRequest`, `RunEvent`) | **`.proto`** — 이 패키지 (backend 단계 작성) | 양 언어 stub 생성 대상. 계약 드리프트 방지 |

두 정본이 만나는 지점은 `apps/frontend/src/infra/execution/GrpcWebAdapter.ts`(후속) 한 곳이다.
도메인 코드는 생성된 stub 타입을 **절대 import하지 않는다** (Plan §3.2 백엔드 독립성 NFR).

## 작성 시 참고할 frontend 타입

- `src/domain/execution/port.ts` — `SubmitRunRequest`, `RunEvent`, `ExecutionPort`
- `src/domain/execution/types.ts` — `RunState`, `NodeRunState`, `RunLogEntry`, `AssetRef`
- `src/domain/graph/types.ts` — `GraphDocument` (proto 메시지로 변환할 스냅샷 형태)
