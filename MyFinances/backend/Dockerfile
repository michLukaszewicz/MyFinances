FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node is needed here because `dotnet publish` triggers the BuildAndCopyFrontend
# MSBuild target (see MyFinances.Api.csproj), which runs `npm ci && npm run build`
# in ../frontend and copies the output into the publish wwwroot. Debian's default
# `nodejs` package is too old for Vite 8/React Router 8 (need Node >=20.19), so we
# install a current Node from NodeSource instead.
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# react-router's SPA-mode prerender step fetches its own local server over
# "localhost"; Node's default DNS order resolves that to IPv6 first, which the
# server (IPv4-only) refuses inside this container. Force IPv4 resolution.
ENV NODE_OPTIONS=--dns-result-order=ipv4first

COPY MyFinances/backend/MyFinances.Api.csproj MyFinances/backend/
RUN dotnet restore MyFinances/backend/MyFinances.Api.csproj

COPY MyFinances/backend/ MyFinances/backend/
COPY MyFinances/frontend/ MyFinances/frontend/

RUN dotnet publish MyFinances/backend/MyFinances.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyFinances.Api.dll"]
