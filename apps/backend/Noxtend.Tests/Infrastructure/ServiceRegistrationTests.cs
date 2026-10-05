using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure;
using Noxtend.Infrastructure.Persistence;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 등록이 실제로 조립되는가.
///
/// **테스트가 배선을 건드리지 않으면 첫 기동에서야 드러난다.** 유스케이스 테스트는
/// 저장소를 직접 만들거나 자기 컬렉션을 꾸리므로, 컨테이너가 실제로 이것들을 만들어
/// 낼 수 있는지는 아무도 확인하지 않는다.
///
/// 사이클 #10 에서 <c>AddDbContextFactory</c> 가 붙으며 이 확인이 필요해졌다.
/// `AddDbContext` 와 팩토리가 같은 <c>DbContextOptions</c> 를 서로 다른 수명으로
/// 등록해서, 스코프 검사를 켜면 기동이 죽는 조합이 만들어질 수 있다.
/// </summary>
public sealed class ServiceRegistrationTests
{
    [Fact]
    public void PersistenceResolves_WithScopeValidationOn()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IJobRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<NoxtendDbContext>());
    }

    /// <summary>
    /// 3D 실행 저장소는 팩토리로 짧은 컨텍스트를 연다 (§3.2).
    ///
    /// 이것이 스코프 컨텍스트를 잡으면 폴링이 공정 실행의 리스 갱신과 부딪힌다.
    /// </summary>
    [Fact]
    public void MeshRunRepositoryResolves_AndOpensItsOwnContext()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMeshRunRepository>());

        var factory = provider.GetRequiredService<IDbContextFactory<NoxtendDbContext>>();
        using var first = factory.CreateDbContext();
        using var second = factory.CreateDbContext();

        // 같은 인스턴스를 돌려주면 checkpoint 두 개가 한 컨텍스트를 나눠 쓴다
        Assert.NotSame(first, second);
    }

    /// <summary>
    /// **연결하지 않는다.** 문자열 모양만 맞으면 되고, 여기서 보는 것은 조립이다.
    /// </summary>
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        // API 가 넣어 주는 것 — 공급자 키 암호화가 이것에 기댄다
        services.AddDataProtection();

        services.AddNoxtendInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Db"] = "Server=localhost;Database=none;Trusted_Connection=True",
                ["ConnectionStrings:Blob"] = "UseDevelopmentStorage=true",
                ["ConnectionStrings:Redis"] = "localhost:6379",
            })
            .Build());

        // 수명이 어긋난 등록은 이 두 검사를 켜야 드러난다
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
