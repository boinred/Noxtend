# Build context is apps/backend (the folder holding Noxtend.slnx).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore before copying sources so a code-only change reuses the package layer.
#
# Every project needs a line here. Adding a project without touching this file fails
# with NETSDK1004 ("Assets file not found") at publish — loud, but only inside the
# container build, so `dotnet build` on the host still passes and the gap hides until
# deploy. Cycle #5 hit exactly that.
COPY Noxtend.slnx ./
COPY Noxtend.Domain/*.csproj Noxtend.Domain/
COPY Noxtend.Application/*.csproj Noxtend.Application/
COPY Noxtend.Tuning.Domain/*.csproj Noxtend.Tuning.Domain/
COPY Noxtend.Tuning.Application/*.csproj Noxtend.Tuning.Application/
COPY Noxtend.Infrastructure/*.csproj Noxtend.Infrastructure/
COPY Noxtend.Api/*.csproj Noxtend.Api/
COPY Noxtend.Tests/*.csproj Noxtend.Tests/
RUN dotnet restore Noxtend.Api/Noxtend.Api.csproj

COPY . .
RUN dotnet publish Noxtend.Api/Noxtend.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp needs fontconfig at load time. Without it libSkiaSharp.so fails to
# resolve and every mesh task dies with TypeInitializationException — the managed
# assembly and the native .so are both present, so the failure looks unrelated to
# packaging. The base aspnet image does not ship fontconfig.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Data Protection ring lives on a mounted volume so stored provider keys survive
# a pod restart (§7). The path matches deploy/k8s/api.yaml.
RUN mkdir -p /keys

EXPOSE 8080
ENTRYPOINT ["dotnet", "Noxtend.Api.dll"]
