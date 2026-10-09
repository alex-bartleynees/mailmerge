# syntax=docker/dockerfile:1.7-labs
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache tzdata icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY --parents global.json nuget.config Directory.Build.props Directory.Packages.props src/**/*.csproj ./
# SharedKernel.* resolves from GitHub Packages (see nuget.config). The token is a BuildKit secret so it never
# lands in an image layer:  docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN .
RUN --mount=type=secret,id=github_packages_token \
    GITHUB_PACKAGES_TOKEN=$(cat /run/secrets/github_packages_token 2>/dev/null) \
    dotnet restore src/MailMerge.Api/MailMerge.Api.csproj
COPY src/ src/
RUN dotnet publish src/MailMerge.Api/MailMerge.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
USER app
ENTRYPOINT ["dotnet", "MailMerge.Api.dll"]
