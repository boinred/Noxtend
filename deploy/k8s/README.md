# deploy/k8s — 로컬 기동 절차

Design §11.1 의 4 워크로드(`api` · `mssql` · `redis` · `azurite`)를 Docker Desktop
Kubernetes 에 올린다. **module-1 의 완료 조건은 `/health` 가 셋 모두 `ok` 를 내는 것**이다
(Design §4.2 #1 · §11.2 #3).

## 전제

- Docker Desktop + Kubernetes 활성화 (Design §12.2 확인 완료)
- `kubectl config current-context` → `docker-desktop`

## 0. 한 명령으로 (권장)

```bash
cp deploy/k8s/secrets.example.yaml deploy/k8s/secrets.yaml   # 최초 1회, 비밀번호 수정
deploy/local-up.sh
```

빌드 → 이미지 반입 → 매니페스트 적용 → 준비 대기 → `/health` 확인까지 한 번에 한다.
코드를 고친 뒤 다시 올릴 때도 같은 명령이며, 매니페스트만 바꿨다면 `--skip-build` 를 붙인다.
`-d`(또는 `--detach`)를 붙이면 빌드·배포·확인 후 포트 포워딩을 백그라운드에 남기고
터미널로 돌아온다. 출력된 로그 경로로 확인하고 `kill <PID>`로 종료한다.
`--forward`는 포트 포워딩(`localhost:18080`)을 터미널에서 `Ctrl+C`까지 유지한다.
API 파드가 재시작되면 두 방식 모두 포워딩이 끊긴다. 재배포 없이 다시 연결하려면
`kubectl -n noxtend port-forward svc/api 18080:8080`을 사용한다.
이미 포트 18080을 쓰는 포워딩이 있으면 해당 프로세스를 종료한 뒤 실행한다.
Frontend는 별도로 `pnpm dev`로 실행하고
`http://localhost:5173`에 접속한다. `18080`은 API 주소이며 `/`는 화면을 제공하지 않는다.

> **Plan §4.1 은 "매니페스트 적용 한 번으로 로컬 전체 기동" 을 완료 조건으로 잡았다.**
> `kubectl apply` 만으로는 그 조건이 성립하지 않는다 — 이미지 반입 단계가 빠지기 때문이다(§2).
> 스크립트가 그 간극을 메운다. 아래 절들은 스크립트가 무엇을 하는지, 그리고 수동으로
> 단계를 나눠 밟아야 할 때의 절차다.

---

## 1. 비밀 준비

실제 secret 은 커밋하지 않는다. 예시를 복사해 값만 바꾼다.

```bash
cp deploy/k8s/secrets.example.yaml deploy/k8s/secrets.yaml
# MSSQL_SA_PASSWORD 와 ConnectionStrings__Db 의 비밀번호를 같은 값으로 바꾼다
```

> SA 비밀번호는 SQL Server 복잡도 정책(대·소문자 + 숫자 + 기호, 8자 이상)을 만족해야
> 한다. 만족하지 않으면 컨테이너가 기동 직후 종료한다.

`deploy/k8s/secrets.yaml` 은 `.gitignore` 에 있다.

## 2. API 이미지 빌드 · 반입

레지스트리를 거치지 않는다. 매니페스트는 `imagePullPolicy: Never` 로 로컬 이미지를 집는다.

```bash
docker build -t noxtend-api:local apps/backend
docker save noxtend-api:local | docker exec -i desktop-control-plane ctr -n k8s.io images import -
```

> **두 번째 줄을 빠뜨리면 `ErrImageNeverPull` 이 난다.** 최근 Docker Desktop 의
> Kubernetes 는 kind 기반이라 노드가 별도 컨테이너(`desktop-control-plane`)이고
> **containerd 이미지 저장소가 `docker build` 의 것과 다르다.** `docker images` 에
> 보이는 것과 kubelet 이 보는 것이 같지 않다.
>
> 코드를 고칠 때마다 두 줄을 다시 돌리고 `kubectl -n noxtend rollout restart deploy/api`
> 로 파드를 교체한다.

## 3. 적용

```bash
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/secrets.yaml
kubectl apply -f deploy/k8s/mssql.yaml \
               -f deploy/k8s/redis.yaml \
               -f deploy/k8s/azurite.yaml \
               -f deploy/k8s/api.yaml
```

네임스페이스와 비밀이 먼저다. 나머지 넷은 순서가 없다 — API 는 의존이 준비될 때까지
`/health` 로 503 을 낼 뿐 기동에는 실패하지 않는다.

```bash
kubectl -n noxtend get pods -w
```

`mssql` 은 첫 기동에 시스템 데이터베이스를 만드느라 30초~2분이 걸린다. `readinessProbe`
의 `failureThreshold: 30` 이 그 시간을 감당한다.

## 4. 확인 — module-1 완료 조건

kind 기반 클러스터에서는 NodePort 가 호스트로 뚫리지 않는다. 포트 포워딩을 쓴다.

```bash
kubectl -n noxtend port-forward svc/api 18080:8080 &
curl -s http://localhost:18080/health | jq
```

```jsonc
{ "data": { "db": "ok", "redis": "ok", "blob": "ok" }, "error": null }
```

하나라도 끊겨 있으면 503 과 함께 어느 의존인지 나온다.

```jsonc
{ "data": null, "error": { "code": "DEPENDENCY_UNAVAILABLE", "message": "redis" } }
```

## 5. 정리

```bash
kubectl delete namespace noxtend
```

PVC 3종(`mssql-data` · `azurite-data` · `api-dataprotection`)이 함께 지워진다.
데이터를 남기려면 네임스페이스 대신 Deployment 만 지운다.

---

## 설계 메모

**프로브가 `/health` 가 아니라 `/health/live` 를 본다.** `/health` 는 의존 상태를
보고하며 Redis·DB 가 끊기면 503 을 낸다. 이것을 `readinessProbe` 에 걸면 파드가
Service 에서 빠져 **읽을 수 있는 503 이 connection refused 로 바뀐다.** 화면이
배너를 띄우려면 응답이 있어야 한다 (Design §6).

**Redis 에는 PVC 가 없다.** 스트림을 잃어도 작업을 잃지 않는다 — 정본은 DB 이고
스위퍼가 재적재한다 (Design §1.2 · §3.3). 영속을 붙이면 설계가 의존하지 않는
내구성 보장을 암시하게 된다.

**`db` 프로브는 `master` 를 본다.** 애플리케이션 데이터베이스는 마이그레이션이 만든다
(module-3). 그전까지 `Noxtend` 를 지정해 접속하면 "login failed" 가 나오는데, 서버는
멀쩡한데 자격 증명 문제처럼 읽힌다. `/health` 가 답하는 질문은 **인프라가 서로를 보는가**
이고 (Design §11.2 #3), 스키마 준비 여부는 주체가 다른 별개의 질문이다.

**Azurite 에 `--skipApiVersionCheck` 가 붙어 있다.** Azure SDK 가 에뮬레이터보다 새
서비스 버전을 협상하는데 Azurite 는 폴백 대신 400 을 낸다. 에뮬레이터만의 문제이므로
애플리케이션 코드에서 버전을 고정하지 않는다 — 실제 Azure 는 같은 요청을 받는다.

**Data Protection 키는 볼륨에 남는다.** 파드가 재시작할 때 링이 새로 만들어지면
저장된 공급자 키가 전부 복호화 불가가 된다 — 설정 실수가 데이터 유실로 보인다
(Design §7).
