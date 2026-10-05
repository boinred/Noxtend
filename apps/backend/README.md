# apps/backend — Noxtend API (.NET 10)

배경 스튜디오의 실행 백본. **작업(`Job`) → 공정(`Task`) → 파츠(`AssetPart`)** 로
모델링한 다단계 파이프라인이다. 이번 사이클의 단계는 추출 하나뿐이지만, 분해·생성·조립이
붙을 때 **행이 늘 뿐 구조는 그대로**다.

정본 설계: [`docs/archive/2026-07/background-studio/background-studio.design.md`](../../docs/archive/2026-07/background-studio/background-studio.design.md)

## 계층

Design §9 — Option B (Clean Architecture). 경계는 관습이 아니라 **프로젝트 참조로 강제**한다.

```
Api ──▶ Application ──▶ Domain ◀── Infrastructure
 └────────────────────────────────────▶ (조립만)
```

| 프로젝트 | 책임 | 참조 |
|---|---|---|
| `Noxtend.Domain` | 엔티티 · 상태 전이 · Port 7종 | **없음** (`Logging.Abstractions` 만) |
| `Noxtend.Application` | 유스케이스 · `JobOrchestrator` · 스위퍼 | `Domain` |
| `Noxtend.Infrastructure` | EF · Blob · Redis · LLM · 암복호화 | `Domain`, `Application` |
| `Noxtend.Api` | 컨트롤러 · DI 조립 · 워커 호스팅 | `Application`, `Infrastructure` |

`Domain.csproj` 에 EF Core·Redis·HttpClient 를 추가하려는 시도 자체가 설계 위반 신호다.
`System.Net.Http` 는 implicit using 에서 명시적으로 빼두었다 — 그래야 가드가 실제로 작동한다.

## 현재 상태

| 모듈 | 범위 | 상태 |
|---|---|:-:|
| `module-1` | 솔루션·k8s 4워크로드·`/health` 관통 | ✅ |
| `module-2` | 엔티티·Port 7종·Fake·오케스트레이터·스위퍼·L1-B | ✅ |
| `module-3` | EF Core·마이그레이션·Blob 어댑터·업로드 엔드포인트 | ⏳ |
| `module-4` | Redis Streams·`TaskWorker`·`TaskSweeper`·공급자 어댑터·CRUD | ⏳ |

**구현된 엔드포인트**: `GET /health` · `GET /health/live` 뿐이다.
나머지 11개(§4.1)는 module-3·4 에서 붙는다.

## 개발

```bash
dotnet build Noxtend.slnx
dotnet test  Noxtend.slnx        # 67건. 인프라를 요구하지 않는다
```

> **테스트가 DB·Redis·Blob·k8s·실제 LLM 을 하나도 요구하지 않는 것이 Option B 를 고른
> 이유다** (Design §2.0). 가장 위험한 로직 — 리스·협조적 취소·오케스트레이션·스위핑 —
> 이 인프라가 뜨기도 전에 검증된다.

로컬 전체 기동은 `deploy/local-up.sh` 한 명령이다 —
빌드·이미지 반입·매니페스트 적용·`/health` 확인까지 한다.
절차의 내막은 [`deploy/k8s/README.md`](../../deploy/k8s/README.md) 에 있다.

## 이 사이클의 규약

Design §10.4 —

- **엔티티를 직렬화하지 않는다.** 컨트롤러는 응답 DTO 를 반환한다
- **응답 봉투 고정** — `{ data, error }`
- **마이그레이션은 SqlServer 한 벌**
- **단계 추가는 `TaskKind` + 워커 + 스트림 세 가지로 끝나야 한다** — 그 외 파일이 바뀌면 설계가 샌 것이다

## ⚠️ 배포 차단 조건

**인증이 없다** (Design §7 · Plan R-2). 공급자 API 키를 DB 에 저장하므로,
**인증 구현 전까지 로컬·사내망 밖 배포를 금지한다.** 키 격리(§2.2)는 구조로 되어 있지만
인증 부재가 그것을 무력화할 수 있다.
