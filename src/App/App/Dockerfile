# ============================================================
# Stage 1: Build (Native AOT)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install prerequisites for Native AOT compilation
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        clang \
        zlib1g-dev && \
    rm -rf /var/lib/apt/lists/*

# Copy project file first for layer caching
COPY App.csproj ./
RUN dotnet restore

# Copy everything else and publish Native AOT binary
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# --- Install FFmpeg ---
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ffmpeg \
        wget \
        ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# --- Install Shaka Packager ---
# Pin to a specific release for reproducibility
ARG SHAKA_PACKAGER_VERSION=3.5.0
RUN ARCH=$(dpkg --print-architecture) && \
    case "$ARCH" in \
        amd64) SHAKA_ARCH="linux-x64" ;; \
        arm64) SHAKA_ARCH="linux-arm64" ;; \
        *) echo "Unsupported architecture: $ARCH" && exit 1 ;; \
    esac && \
    wget -q "https://github.com/shaka-project/shaka-packager/releases/download/v${SHAKA_PACKAGER_VERSION}/packager-${SHAKA_ARCH}" \
        -O /usr/local/bin/packager && \
    chmod +x /usr/local/bin/packager && \
    # Verify the binary runs
    /usr/local/bin/packager --version

# --- Cleanup wget (no longer needed at runtime) ---
RUN apt-get purge -y wget && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/*

# --- App setup ---
WORKDIR /app
COPY --from=build /app/publish .

# Ensure temp directory exists for workspaces
RUN mkdir -p /tmp/transcode

# Non-root user for security
RUN groupadd -r transcoder && useradd -r -g transcoder transcoder && \
    chown -R transcoder:transcoder /app /tmp/transcode
USER transcoder

# Verify tools are accessible
RUN ffmpeg -version && packager --version

ENTRYPOINT ["/app/App"]
