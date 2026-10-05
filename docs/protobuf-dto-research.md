# C# DTO와 Protocol Buffers 선택 조사

> 조사 기준일: 2026-08-10
> 범위: C# DTO 정의·전송, Protocol Buffers, gRPC, JSON 및 MessagePack 대안
> 출처 원칙: Microsoft, Google Protocol Buffers, 각 프로젝트의 공식 문서·저장소·NuGet만 사용

## 결론

Protocol Buffers는 **C#에서 DTO를 만들기 위한 일반 도구**라기보다, 여러 프로세스·언어가 공유하는 **버전 가능한 전송 계약과 바이너리 형식**이 필요할 때 좋은 선택이다. `.proto`를 정본으로 삼아 C#과 TypeScript 타입을 함께 생성하거나 gRPC 스트리밍을 사용할 때 가치가 크다. 반대로 같은 ASP.NET Core 애플리케이션 안의 계층 간 DTO, 관리용 REST API, 사람이 읽고 디버깅할 JSON까지 모두 Protobuf 생성 타입으로 바꾸면 계약 도구가 도메인과 애플리케이션 계층에 침투하고 빌드·배포 복잡성만 늘기 쉽다.

Noxtend에는 다음 선택이 적합하다.

1. `SubmitRunRequest`, `RunEvent` 같은 **실행 경계만** `.proto` 정본과 공식 `Google.Protobuf` + `grpc-dotnet` 조합을 사용한다.
2. 생성 타입은 `sealed partial class` 기반의 mutable 전송 모델이므로 Domain/Application 모델로 사용하지 않고, `GrpcWebAdapter`와 API 경계에서 명시적으로 매핑한다.
3. 다만 현재 브라우저 실행 이벤트가 **서버→클라이언트 단방향**이라면, .NET 10의 `TypedResults.ServerSentEvents`를 사용하는 REST/OpenAPI + SSE가 gRPC-Web보다 단순할 가능성이 크다. 구현 전에 두 경로를 작은 spike로 비교한다.
4. 강한 다언어 IDL과 생성 계약이 우선이면 Protobuf를 유지한다. 기존 REST/polling 응답 DTO와 DB 저장 JSON은 어느 경우든 `System.Text.Json`을 유지한다.
5. 단지 “C# DTO 직렬화 속도”가 목적이라면 Protobuf 도입을 먼저 결정하지 않는다. 실제 payload, 압축, 지연, CPU, 할당량을 기준으로 `System.Text.Json` source generation과 함께 벤치마크한다.

이 권고는 이미 [`packages/proto/README.md`](../packages/proto/README.md)에 기록된 “실행 계약만 `.proto` 정본, `GraphDocument`와 도메인 코드는 생성 stub과 분리” 원칙과 일치한다.

## 2026년 기술 기준선

| 항목 | 2026-08-10 상태 | 판단 |
| --- | --- | --- |
| .NET | 프로젝트는 이미 `net10.0`. .NET 10 LTS는 2028-11-14까지 지원된다. .NET 8 LTS와 .NET 9 STS는 모두 2026-11-10 지원 종료다. | 신규 작업은 .NET 10 유지. 8/9를 새 기준으로 택할 이유가 작다. |
| Google Protobuf | C# 활성 지원선은 3.35.x이고, 안정 NuGet은 `Google.Protobuf` 3.35.1이다. 최신 `protoc`는 Editions 2024도 지원한다. | 공식 wire/runtime과 가장 직접적으로 정렬된 선택이다. |
| grpc-dotnet | `Grpc.AspNetCore` 2.83.0은 `net8.0`을 대상으로 하며 .NET 8/9/10과 호환되고, `Grpc.Tools`와 `Google.Protobuf`를 포함한다. | ASP.NET Core 서버의 기본 선택이다. |
| protobuf-net | 안정 NuGet은 3.2.56. C# 속성 기반 code-first와 `.proto` 기반 생성 양쪽을 제공한다. | C# 중심 POCO 경험은 좋지만 공식 Google 생성 타입과 별도 구현·운영 선택이다. |
| MessagePack-CSharp | 안정 NuGet은 3.1.8. .NET 8 최적화와 컴파일 타임 formatter source generator를 제공한다. | 폐쇄된 C#/Unity 고성능 경계에는 후보지만 언어 중립 RPC 계약의 기본값은 아니다. |

지원 기간은 [Microsoft 제품 수명 주기](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core), Protobuf 지원선은 [공식 Version Support](https://protobuf.dev/support/version-support/), 패키지 상태는 [Google.Protobuf](https://www.nuget.org/packages/google.protobuf), [Grpc.AspNetCore](https://www.nuget.org/packages/Grpc.AspNetCore), [protobuf-net](https://www.nuget.org/packages/protobuf-net), [MessagePack](https://www.nuget.org/packages/MessagePack)에서 확인했다.

## 공식 Google.Protobuf 생성 타입의 성격

`protoc`는 메시지마다 `IMessage<T>`를 구현하는 `sealed partial class`를 만든다. 일반적인 C# `record` DTO가 아니며, 생성된 parser·descriptor·clone/equality·직렬화 동작과 `Google.Protobuf` 컬렉션에 결합된다. `repeated`와 `map`은 각각 `RepeatedField<T>`, `MapField<TKey,TValue>`가 되고 둘 다 속성 자체는 읽기 전용 컬렉션이다. 자세한 형태는 [C# Generated Code Guide](https://protobuf.dev/reference/csharp/csharp-generated/)에 정의되어 있다.

이 특성은 계약 일관성과 언어 간 생성에는 유리하지만 다음 비용이 있다.

- 도메인 불변식이나 C# 관용적인 immutable record를 표현하는 타입이 아니다.
- `.proto` 변경 뒤 모든 소비자의 code generation과 패키지 버전을 관리해야 한다.
- 생성 타입을 API·도메인·영속 모델로 재사용하면 전송 스키마 변경이 내부 모델까지 전파된다.
- `partial`로 편의 기능을 붙일 수는 있지만 생성 코드 자체를 수정해서는 안 된다.

따라서 생성 메시지는 **transport DTO**로만 보고, 도메인 타입과 수명주기를 분리하는 편이 안전하다.

## nullable, optional, required의 차이

Protobuf는 C# nullable reference types와 동일한 모델이 아니다.

- presence가 없는 proto3 scalar는 미지정과 기본값(`0`, `false`, `""`)을 구분하지 못한다.
- proto3 scalar에 `optional`을 붙이면 C#에 `HasFoo`와 `ClearFoo()`가 생성되어 미지정과 명시적 기본값을 구분한다. Google은 proto3 기본 타입에 항상 `optional`을 붙이는 것을 권장한다. [Field Presence](https://protobuf.dev/programming-guides/field_presence/)
- 일반 `string`/`bytes` 필드에는 `null`을 넣을 수 없고 미지정 조회는 빈 값이다. message 필드는 `null`로 clear할 수 있다. wrapper value type은 `Nullable<T>`로 생성되지만, 새 계약에서는 wrapper보다 `optional`을 우선 검토한다. [C# Generated Code Guide](https://protobuf.dev/reference/csharp/csharp-generated/#fields)
- `repeated`와 `map`은 presence를 추적하지 않으므로 “미지정 컬렉션”과 “빈 컬렉션”을 기본적으로 구분하지 않는다.
- proto2 `required`는 진화에 불리해 권장되지 않는다. 필수 비즈니스 규칙은 수신 경계 validation으로 검증하는 것이 낫다. [Proto Best Practices](https://protobuf.dev/best-practices/dos-donts/)

`System.Text.Json`은 C# 모델을 그대로 사용할 수 있지만 JSON의 missing과 `null`도 별도 개념이다. .NET 9부터 `RespectNullableAnnotations`가 non-nullable 멤버의 명시적 `null`을 제한할 수 있으나, missing 필드를 필수로 만들지는 않는다. 필수 presence에는 C# `required`, `[JsonRequired]` 또는 contract 설정이 필요하고, 컬렉션 원소·generic·root type의 nullable 검증에는 제한이 있다. [System.Text.Json nullable annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations)

최신 Protobuf 문법은 Editions 2024까지 안정 지원되지만, C# nullable reference type 생성 지원은 2026-07 공식 공지에서 Edition 2026의 계획 항목이다. 아직 안정 기능으로 전제하지 않는다. 현재 계약은 호환성이 넓은 proto3 + 명시적 `optional`이 보수적이며, Editions를 택할 때는 모든 generator/plugin 버전을 고정해 검증한다. [Protobuf Editions](https://protobuf.dev/editions/overview/), [Edition 2026 계획](https://protobuf.dev/news/2026-07-13/)

.NET 10의 `JsonSerializerOptions.Strict`는 unmapped/duplicate property 거부, 대소문자 구분, nullable annotation과 required constructor parameter 검사를 묶은 최신 JSON 선택지다. 새 REST 계약의 엄격한 입력 검증에는 유용하지만 기존 client에 곧바로 켜면 호환성 변경이므로 endpoint별 도입이 필요하다. [.NET 10 libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries)

## 스키마 진화와 unknown fields

Protobuf binary의 핵심 장점은 field number 기반 진화다.

- 새 필드 추가는 binary wire-safe다.
- 기존 field number를 바꾸거나 재사용하면 안 된다.
- 삭제한 field number와 JSON/TextFormat 호환이 필요한 이름은 `reserved`로 남긴다.
- wire-safe 변경도 생성된 C# switch나 비즈니스 의미에는 breaking change가 될 수 있다.

구체적인 안전·비안전 변경 목록은 [Proto3 Updating a Message Type](https://protobuf.dev/programming-guides/proto3/#updating)과 [Proto Best Practices](https://protobuf.dev/best-practices/dos-donts/)를 기준으로 삼는다.

Binary parser는 모르는 필드를 unknown field로 보존하고 다시 binary로 직렬화할 때 포함한다. 다만 JSON으로 변환하거나 새 객체에 필드를 하나씩 복사하면 unknown fields가 사라진다. [Unknown Fields](https://protobuf.dev/programming-guides/proto3/#unknowns) 따라서 구버전 중계 서비스가 메시지를 그대로 round-trip해야 한다면 생성 메시지의 `MergeFrom`/`Clone` 계열을 사용해야 한다. Noxtend처럼 경계에서 도메인 모델로 매핑한 뒤 새 응답을 만드는 구조에서는 unknown-field 자동 보존을 기대해서는 안 된다.

ProtoJSON은 binary와 같은 진화 보장을 주지 않는다. 기본 parser는 unknown JSON 필드를 거부할 수 있고, field name·enum name 변경의 영향도 받으며, JSON round-trip은 binary unknown fields를 잃는다. [ProtoJSON Format](https://protobuf.dev/programming-guides/json/) 즉 “Protobuf를 쓴다”는 사실만으로 REST JSON 호환성이 자동 보장되지는 않는다.

## AOT와 trimming

ASP.NET Core의 gRPC client/server는 .NET 8부터 Native AOT를 지원하며, Microsoft의 .NET 10 지원 표에서도 gRPC는 완전 지원 항목이다. `.proto`에서 코드를 빌드 시점에 생성하는 공식 경로는 런타임 무제한 reflection 의존을 줄이는 데 유리하다. 다만 실제 서비스는 `dotnet publish -r <RID>`에서 모든 AOT/trimming 경고를 확인하고 publish 산출물로 통합 테스트해야 한다. [gRPC and Native AOT](https://learn.microsoft.com/en-us/aspnet/core/grpc/native-aot?view=aspnetcore-10.0), [ASP.NET Core Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)

`System.Text.Json`도 source generation을 사용하면 Native AOT에 적합하다. 기본 reflection 직렬화는 trimmed/AOT 앱에서 깨질 수 있으며, `JsonSerializerContext`를 등록하고 reflection default를 끄는 방식이 검증 가능하다. [System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)

MessagePack-CSharp 3.x는 `[MessagePackObject]` 타입에 formatter와 resolver를 생성하며 일반 시나리오를 AOT-safe로 설명한다. formatter를 찾지 못하면 `Reflection.Emit` fallback이 있으므로 AOT에서는 생성 resolver만 사용하도록 검증해야 한다. [MessagePack-CSharp AOT Code Generation](https://github.com/MessagePack-CSharp/MessagePack-CSharp#aot-code-generation)

protobuf-net 3.x는 runtime reflection 없는 `protobuf-net.Core` 분리와 AOT 방향을 제공하지만 공식 로드맵에는 build-time generator 작업이 계속 명시되어 있다. Native AOT가 필수인 신규 계약에서는 “동작할 것”으로 추정하지 말고 사용하는 기능 조합을 publish-test해야 하며, 검증 비용까지 고려하면 공식 Google 생성 경로가 더 보수적이다. [protobuf-net v3](https://protobuf-net.github.io/protobuf-net/3_0.html), [release notes](https://protobuf-net.github.io/protobuf-net/releasenotes.html)

## 브라우저, JSON transcoding과 API 호환성

브라우저에는 세 가지 경로가 있다.

| 경로 | 장점 | 제약 |
| --- | --- | --- |
| gRPC-Web + Protobuf | 생성된 양쪽 계약, 작은 binary payload, server streaming | 브라우저용 생성 client와 gRPC-Web 설정 필요. native gRPC와 다른 transport이며 client/bidirectional streaming에 제약 |
| gRPC JSON transcoding | 일반 `fetch`와 JSON으로 같은 gRPC 구현 호출, 생성 client 불필요, REST/OpenAPI 표면 제공 가능 | ProtoJSON 규칙과 HTTP annotation 관리 필요. transcoding streaming은 server streaming만 지원하고 line-delimited JSON 사용 |
| REST/OpenAPI + .NET 10 SSE | 기존 C# JSON DTO와 브라우저 HTTP 경로 재사용, 단방향 server push를 작은 표면으로 구현 | binary/IDL 이점 없음. 제출용 REST 요청과 이벤트 구독을 분리하고 재연결·event ID·누락 복구 정책을 직접 설계 |

Microsoft는 두 gRPC 경로의 차이를 [Use gRPC in browser apps](https://learn.microsoft.com/en-us/aspnet/core/grpc/browser?view=aspnetcore-10.0)와 [gRPC JSON transcoding](https://learn.microsoft.com/en-us/aspnet/core/grpc/json-transcoding?view=aspnetcore-10.0)에 설명한다. 여기에 .NET 10은 Minimal API와 controller 모두에서 `IAsyncEnumerable<SseItem<T>>`를 반환하는 `TypedResults.ServerSentEvents`를 정식 지원한다. [ASP.NET Core 10 SSE](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0#support-for-server-sent-events-sse)

Noxtend의 실행 흐름이 “REST로 실행 제출 → 브라우저가 진행 이벤트 구독”이고 역방향 실시간 메시지가 없다면 SSE가 가장 작은 기술 표면이다. 이미 `net10.0`, REST, `System.Text.Json`, OpenAPI를 사용하므로 gRPC-Web proxy와 TypeScript Protobuf client generation을 추가하지 않아도 된다. 반면 실행 계약을 C# 외 worker·CLI·다른 서비스가 동일 IDL로 소비하거나 binary payload·gRPC 생태계가 중요한 경우에는 기존 Protobuf 결정을 유지한다. 즉 최종 선택은 스트리밍 기능 자체보다 **계약의 소비 범위**로 결정한다.

## 대안 비교

| 선택 | 가장 잘 맞는 경우 | 장점 | 주요 비용 |
| --- | --- | --- | --- |
| Google.Protobuf + grpc-dotnet | 언어 간 계약, RPC, streaming, 장기 진화 | 명시적 IDL, 공식 generator, unknown-field 보존, AOT 지원, 도구 생태계 | 생성 단계와 field-number 규율, transport 타입 매핑, 브라우저 경로 결정 필요 |
| System.Text.Json | 일반 ASP.NET REST, 관리 API, 사람이 읽는 저장/로그, 단일 C# 앱 DTO | .NET 내장, C# record/required/NRT 활용, OpenAPI·브라우저 친화, source-gen AOT | 큰 text payload, unknown-field round-trip 없음, 진화 규칙을 별도로 설계 |
| protobuf-net | 기존 POCO를 C#답게 binary 직렬화, C# 중심 code-first gRPC | 속성 기반 도입이 쉽고 Protobuf wire 사용, contract-first도 가능 | Google runtime과 별도 구현, code-first 계약 거버넌스 필요, AOT 기능별 검증 필요 |
| MessagePack-CSharp | 폐쇄된 C#/Unity 경계, 캐시·게임 상태·고빈도 내부 메시지 | compact binary, int/string key 선택, source-generated formatter, LZ4 옵션 | 표준 gRPC/ProtoJSON 계약 아님, key/index 호환 규율을 직접 운영, 보안 옵션과 resolver 관리 |

protobuf-net은 [공식 소개](https://protobuf-net.github.io/protobuf-net/)대로 code-first가 중심이며, `[ProtoMember(n)]` 번호가 wire 계약이다. null은 Protobuf의 first-class 값이 아니므로 nullable과 collection 의미를 별도로 설계해야 한다. [protobuf-net null handling](https://protobuf-net.github.io/protobuf-net/nullwrappers.html)

MessagePack-CSharp도 index key를 재사용하지 않아야 하고, 없는 key는 멤버의 default로 초기화된다. 또한 신뢰하지 않는 입력에는 `MessagePackSecurity.UntrustedData`를 사용하고 typeless resolver를 피하라는 공식 경고가 있다. [MessagePack-CSharp README](https://github.com/MessagePack-CSharp/MessagePack-CSharp#object-serialization)

## 성능과 운영 trade-off

Binary Protobuf는 JSON보다 payload가 작고 특히 `bytes`를 base64로 만들지 않아 유리하다. Microsoft는 JSON의 base64가 binary 크기를 약 33% 늘린다고 설명한다. 그러나 gRPC는 메시지 전체를 메모리에 올려 송수신하므로 큰 payload는 LOH와 복사 비용을 만든다. 85,000바이트를 넘는 대형 binary는 chunked streaming이나 별도 blob HTTP 전송을 검토한다. [gRPC performance best practices](https://learn.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-10.0)

라이브러리 자체 benchmark 순위를 제품 결론으로 사용하지 않는다. MessagePack 공식 저장소의 공개 benchmark도 특정 데이터·하드웨어 결과다. Noxtend에서는 다음을 같은 조건으로 측정해야 한다.

- 실제 `SubmitRunRequest`와 `RunEvent`의 직렬화/역직렬화 처리량과 allocation
- JSON, gzip JSON, Protobuf의 payload 크기
- 브라우저에서 end-to-end latency와 generated client bundle 증가량
- 배포 시 `protoc`/plugin 버전 고정, breaking schema 검사, 다중 버전 client-server 호환 테스트 비용
- 장애 조사 시 binary payload 관찰성, 로그용 JSON 변환 비용, 보안상 민감 필드 노출 여부

## 최종 결정 기준

다음 중 둘 이상이 실제 요구라면 Protobuf가 설득력 있다.

- C#과 TypeScript 또는 다른 언어가 동일 계약을 생성해야 한다.
- 여러 종류의 RPC, 양방향 streaming 또는 고빈도 호출이 필요하다.
- payload 크기·CPU가 측정된 병목이다.
- 독립 배포되는 소비자 사이에서 field-number 기반 진화가 필요하다.

그렇지 않고 DTO가 ASP.NET Core 내부 계층 분리나 일반 REST 응답만을 위한 것이라면 `record`/class + `System.Text.Json`이 더 단순하다. Noxtend는 **전체 DTO를 Protobuf화하지 않는다**는 현재 원칙을 유지하되, 실행 이벤트 소비자가 브라우저뿐이면 REST/OpenAPI + SSE를 우선 spike하고, 강한 다언어 IDL이 실제 요구일 때 실행 전송 계약에 Protobuf를 적용하는 것이 타당하다.
