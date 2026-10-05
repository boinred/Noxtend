using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 컨테이너 테스트가 함께 쓰는 SQL Server 한 대.
///
/// **전에는 테스트 클래스마다 한 대씩 띄웠다.** 클래스 넷이 병렬로 돌면 SQL Server 가 넷,
/// 거기에 개발용 k8s 클러스터의 것까지 다섯이 된다. 호스트 메모리가 모자라
/// `FAIL_PAGE_ALLOCATION` 이 나면서 테스트가 무작위로 실패했고, 돌릴 때마다 클러스터를
/// 껐다 켜야 했다.
///
/// 한 대를 공유하되 **테스트마다 데이터베이스를 나눈다.** 마이그레이션 테스트는 빈 DB 에서
/// 시작해 위아래로 오가므로 같은 DB 를 쓰면 서로를 밟는다.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private int databases;

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    /// <summary>
    /// 아무도 안 쓴 데이터베이스로 가는 연결 문자열.
    ///
    /// 미리 만들지 않는다 — EF 의 `MigrateAsync` 가 없으면 만든다. 이름에 호출자를 넣어
    /// 실패했을 때 어느 테스트의 DB 인지 알아볼 수 있게 한다.
    /// </summary>
    public string FreshDatabase(string owner)
        => new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"{owner}_{Interlocked.Increment(ref databases)}",
        }.ConnectionString;
}

/// <summary>
/// 이 컬렉션에 속한 테스트 클래스는 컨테이너 하나를 나눠 쓴다.
///
/// xUnit 은 같은 컬렉션의 클래스를 **병렬로 돌리지 않는다.** 컨테이너를 아끼는 것과 별개로
/// 그 편이 낫다 — SQL Server 한 대에 마이그레이션 여덟 개가 동시에 들어가면 그것대로 무겁다.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
