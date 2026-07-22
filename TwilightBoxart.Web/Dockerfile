# Multi-stage build: one `docker build -f TwilightBoxart.Web/Dockerfile -t twilightboxart:latest .`
# from a clean checkout restores, publishes and packages the app. The browser client under
# wwwroot/app is plain hand-written HTML/CSS/JS committed to the repo, so there is no Node step.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the project files + central package list, so the layer caches until a
# dependency actually changes.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY TwilightBoxart.Core/TwilightBoxart.Core.csproj TwilightBoxart.Core/
COPY TwilightBoxart.Data/TwilightBoxart.Data.csproj TwilightBoxart.Data/
COPY TwilightBoxart.Pipeline/TwilightBoxart.Pipeline.csproj TwilightBoxart.Pipeline/
COPY TwilightBoxart.Web/TwilightBoxart.Web.csproj TwilightBoxart.Web/
RUN dotnet restore TwilightBoxart.Web/TwilightBoxart.Web.csproj

# Then the sources, and publish.
COPY . .
RUN dotnet publish TwilightBoxart.Web/TwilightBoxart.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Retry apt: GitHub's arm64 runner intermittently times out reaching ports.ubuntu.com (Ubuntu's ARM
# mirror), so give it a few tries before failing rather than dying on the first blip.
RUN n=0; until apt-get -o Acquire::Retries=5 update \
      && apt-get -o Acquire::Retries=5 install -y --no-install-recommends gosu; do \
      n=$((n+1)); [ "$n" -ge 5 ] && echo "apt failed after $n attempts" && exit 1; \
      echo "apt failed, retry $n in 20s..."; sleep 20; \
    done \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -g 888 appgroup \
    && useradd -u 888 -g 888 -d /data -s /bin/false appuser

WORKDIR /app

COPY --from=build /app/publish ./
COPY TwilightBoxart.Web/entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

# The generated index and both cache layers live here. Nothing the server writes ever lands under
# /app/wwwroot.
VOLUME ["/data"]

ENV ASPNETCORE_HTTP_PORTS=8186

EXPOSE 8186

# entrypoint.sh remaps appuser to PUID/PGID and then drops privileges with gosu; the app never runs
# as root.
ENTRYPOINT ["./entrypoint.sh", "dotnet", "TwilightBoxart.Web.dll"]
