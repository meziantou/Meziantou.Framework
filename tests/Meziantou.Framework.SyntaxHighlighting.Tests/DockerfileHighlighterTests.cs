namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DockerfileHighlighterTests
{

    [Fact]
    public void Instruction_From()
    {
        AssertHighlighter("dockerfile",
"""
FROM alpine
""",
"""
<span class="hljs-keyword">FROM</span> alpine
""");
    }

    [Fact]
    public void Instruction_FromVersioned()
    {
        AssertHighlighter("dockerfile",
"""
FROM node:20-alpine
""",
"""
<span class="hljs-keyword">FROM</span> node:<span class="hljs-number">20</span>-alpine
""");
    }

    [Fact]
    public void Instruction_FromDigest()
    {
        AssertHighlighter("dockerfile",
"""
FROM alpine@sha256:51b67269f354137895d43f3b3d810bfacd3945438e94dc5ac55fdac340352f48
""",
"""
<span class="hljs-keyword">FROM</span> alpine@sha256:<span class="hljs-number">51</span>b67269f354137895d43f3b3d810bfacd3945438e94dc5ac55fdac340352f48
""");
    }

    [Fact]
    public void Instruction_FromAs()
    {
        AssertHighlighter("dockerfile",
"""
FROM golang:1.22 AS build
""",
"""
<span class="hljs-keyword">FROM</span> golang:<span class="hljs-number">1.22</span> AS build
""");
    }

    [Fact]
    public void Instruction_FromScratch()
    {
        AssertHighlighter("dockerfile",
"""
FROM scratch
""",
"""
<span class="hljs-keyword">FROM</span> scratch
""");
    }

    [Fact]
    public void Instruction_FromPlatform()
    {
        AssertHighlighter("dockerfile",
"""
FROM --platform=linux/amd64 alpine
""",
"""
<span class="hljs-keyword">FROM</span> --platform=linux/amd64 alpine
""");
    }

    [Fact]
    public void Instruction_Maintainer()
    {
        AssertHighlighter("dockerfile",
"""
MAINTAINER alice@example.com
""",
"""
<span class="hljs-keyword">MAINTAINER</span> alice@example.com
""");
    }

    [Fact]
    public void Instruction_Label()
    {
        AssertHighlighter("dockerfile",
"""
LABEL maintainer="alice@example.com"
""",
"""
<span class="hljs-keyword">LABEL</span><span class="language-bash"> maintainer=<span class="hljs-string">&quot;alice@example.com&quot;</span></span>
""");
    }

    [Fact]
    public void Instruction_LabelMulti()
    {
        AssertHighlighter("dockerfile",
"""
LABEL maintainer="alice@example.com" \
      version="1.0" \
      description="My app"
""",
"""
<span class="hljs-keyword">LABEL</span><span class="language-bash"> maintainer=<span class="hljs-string">&quot;alice@example.com&quot;</span> \
      version=<span class="hljs-string">&quot;1.0&quot;</span> \
      description=<span class="hljs-string">&quot;My app&quot;</span></span>
""");
    }

    [Fact]
    public void Instruction_Run()
    {
        AssertHighlighter("dockerfile",
"""
RUN apt-get update && apt-get install -y curl
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> apt-get update &amp;&amp; apt-get install -y curl</span>
""");
    }

    [Fact]
    public void Instruction_RunExec()
    {
        AssertHighlighter("dockerfile",
"""
RUN ["apt-get", "install", "-y", "curl"]
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> [<span class="hljs-string">&quot;apt-get&quot;</span>, <span class="hljs-string">&quot;install&quot;</span>, <span class="hljs-string">&quot;-y&quot;</span>, <span class="hljs-string">&quot;curl&quot;</span>]</span>
""");
    }

    [Fact]
    public void Instruction_Cmd()
    {
        AssertHighlighter("dockerfile",
"""
CMD ["nginx", "-g", "daemon off;"]
""",
"""
<span class="hljs-keyword">CMD</span><span class="language-bash"> [<span class="hljs-string">&quot;nginx&quot;</span>, <span class="hljs-string">&quot;-g&quot;</span>, <span class="hljs-string">&quot;daemon off;&quot;</span>]</span>
""");
    }

    [Fact]
    public void Instruction_CmdShell()
    {
        AssertHighlighter("dockerfile",
"""
CMD echo "Hello, world!"
""",
"""
<span class="hljs-keyword">CMD</span><span class="language-bash"> <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Hello, world!&quot;</span></span>
""");
    }

    [Fact]
    public void Instruction_EntryPoint()
    {
        AssertHighlighter("dockerfile",
"""
ENTRYPOINT ["/usr/local/bin/app"]
""",
"""
<span class="hljs-keyword">ENTRYPOINT</span><span class="language-bash"> [<span class="hljs-string">&quot;/usr/local/bin/app&quot;</span>]</span>
""");
    }

    [Fact]
    public void Instruction_EntryPointShell()
    {
        AssertHighlighter("dockerfile",
"""
ENTRYPOINT /usr/local/bin/app
""",
"""
<span class="hljs-keyword">ENTRYPOINT</span><span class="language-bash"> /usr/local/bin/app</span>
""");
    }

    [Fact]
    public void Instruction_Expose()
    {
        AssertHighlighter("dockerfile",
"""
EXPOSE 80 443
""",
"""
<span class="hljs-keyword">EXPOSE</span> <span class="hljs-number">80</span> <span class="hljs-number">443</span>
""");
    }

    [Fact]
    public void Instruction_ExposeUdp()
    {
        AssertHighlighter("dockerfile",
"""
EXPOSE 53/udp
""",
"""
<span class="hljs-keyword">EXPOSE</span> <span class="hljs-number">53</span>/udp
""");
    }

    [Fact]
    public void Instruction_Env()
    {
        AssertHighlighter("dockerfile",
"""
ENV NODE_ENV=production
""",
"""
<span class="hljs-keyword">ENV</span> NODE_ENV=production
""");
    }

    [Fact]
    public void Instruction_EnvMulti()
    {
        AssertHighlighter("dockerfile",
"""
ENV PATH="/usr/local/bin:$PATH" \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8
""",
"""
<span class="hljs-keyword">ENV</span> PATH=<span class="hljs-string">&quot;/usr/local/bin:$PATH&quot;</span> \
    LANG=C.UTF-<span class="hljs-number">8</span> \
    LC_ALL=C.UTF-<span class="hljs-number">8</span>
""");
    }

    [Fact]
    public void Instruction_EnvSpaceForm()
    {
        AssertHighlighter("dockerfile",
"""
ENV NODE_ENV production
""",
"""
<span class="hljs-keyword">ENV</span> NODE_ENV production
""");
    }

    [Fact]
    public void Instruction_Arg()
    {
        AssertHighlighter("dockerfile",
"""
ARG VERSION=1.0
""",
"""
<span class="hljs-keyword">ARG</span> VERSION=<span class="hljs-number">1.0</span>
""");
    }

    [Fact]
    public void Instruction_ArgNoDefault()
    {
        AssertHighlighter("dockerfile",
"""
ARG BUILD_DATE
""",
"""
<span class="hljs-keyword">ARG</span> BUILD_DATE
""");
    }

    [Fact]
    public void Instruction_ArgGlobal()
    {
        AssertHighlighter("dockerfile",
"""
ARG TARGETPLATFORM
""",
"""
<span class="hljs-keyword">ARG</span> TARGETPLATFORM
""");
    }

    [Fact]
    public void Instruction_Add()
    {
        AssertHighlighter("dockerfile",
"""
ADD app.tar.gz /app/
""",
"""
<span class="hljs-keyword">ADD</span><span class="language-bash"> app.tar.gz /app/</span>
""");
    }

    [Fact]
    public void Instruction_AddUrl()
    {
        AssertHighlighter("dockerfile",
"""
ADD https://example.com/file.tar.gz /tmp/
""",
"""
<span class="hljs-keyword">ADD</span><span class="language-bash"> https://example.com/file.tar.gz /tmp/</span>
""");
    }

    [Fact]
    public void Instruction_AddChown()
    {
        AssertHighlighter("dockerfile",
"""
ADD --chown=1000:1000 src/ /app/src/
""",
"""
<span class="hljs-keyword">ADD</span><span class="language-bash"> --<span class="hljs-built_in">chown</span>=1000:1000 src/ /app/src/</span>
""");
    }

    [Fact]
    public void Instruction_Copy()
    {
        AssertHighlighter("dockerfile",
"""
COPY . /app
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> . /app</span>
""");
    }

    [Fact]
    public void Instruction_CopyMulti()
    {
        AssertHighlighter("dockerfile",
"""
COPY package.json package-lock.json ./
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> package.json package-lock.json ./</span>
""");
    }

    [Fact]
    public void Instruction_CopyChown()
    {
        AssertHighlighter("dockerfile",
"""
COPY --chown=node:node . /app
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> --<span class="hljs-built_in">chown</span>=node:node . /app</span>
""");
    }

    [Fact]
    public void Instruction_CopyChmod()
    {
        AssertHighlighter("dockerfile",
"""
COPY --chmod=755 ./entrypoint.sh /entrypoint.sh
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> --<span class="hljs-built_in">chmod</span>=755 ./entrypoint.sh /entrypoint.sh</span>
""");
    }

    [Fact]
    public void Instruction_CopyFromStage()
    {
        AssertHighlighter("dockerfile",
"""
COPY --from=build /workspace/bin/app /usr/local/bin/app
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=build /workspace/bin/app /usr/local/bin/app</span>
""");
    }

    [Fact]
    public void Instruction_CopyFromImage()
    {
        AssertHighlighter("dockerfile",
"""
COPY --from=alpine:3.20 /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=alpine:3.20 /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/</span>
""");
    }

    [Fact]
    public void Instruction_CopyLink()
    {
        AssertHighlighter("dockerfile",
"""
COPY --link --chown=1000:1000 src/ /app/
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> --<span class="hljs-built_in">link</span> --<span class="hljs-built_in">chown</span>=1000:1000 src/ /app/</span>
""");
    }

    [Fact]
    public void Instruction_Workdir()
    {
        AssertHighlighter("dockerfile",
"""
WORKDIR /app
""",
"""
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /app</span>
""");
    }

    [Fact]
    public void Instruction_WorkdirNested()
    {
        AssertHighlighter("dockerfile",
"""
WORKDIR /var/log/myapp
""",
"""
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /var/log/myapp</span>
""");
    }

    [Fact]
    public void Instruction_User()
    {
        AssertHighlighter("dockerfile",
"""
USER node
""",
"""
<span class="hljs-keyword">USER</span> node
""");
    }

    [Fact]
    public void Instruction_UserUid()
    {
        AssertHighlighter("dockerfile",
"""
USER 1000:1000
""",
"""
<span class="hljs-keyword">USER</span> <span class="hljs-number">1000</span>:<span class="hljs-number">1000</span>
""");
    }

    [Fact]
    public void Instruction_Volume()
    {
        AssertHighlighter("dockerfile",
"""
VOLUME /data
""",
"""
<span class="hljs-keyword">VOLUME</span><span class="language-bash"> /data</span>
""");
    }

    [Fact]
    public void Instruction_VolumeMulti()
    {
        AssertHighlighter("dockerfile",
"""
VOLUME ["/var/log", "/var/cache"]
""",
"""
<span class="hljs-keyword">VOLUME</span><span class="language-bash"> [<span class="hljs-string">&quot;/var/log&quot;</span>, <span class="hljs-string">&quot;/var/cache&quot;</span>]</span>
""");
    }

    [Fact]
    public void Instruction_Shell()
    {
        AssertHighlighter("dockerfile",
"""
SHELL ["powershell", "-Command"]
""",
"""
<span class="hljs-keyword">SHELL</span><span class="language-bash"> [<span class="hljs-string">&quot;powershell&quot;</span>, <span class="hljs-string">&quot;-Command&quot;</span>]</span>
""");
    }

    [Fact]
    public void Instruction_StopSignal()
    {
        AssertHighlighter("dockerfile",
"""
STOPSIGNAL SIGTERM
""",
"""
<span class="hljs-keyword">STOPSIGNAL</span> SIGTERM
""");
    }

    [Fact]
    public void Instruction_OnBuild()
    {
        AssertHighlighter("dockerfile",
"""
ONBUILD COPY . /app/src
""",
"""
<span class="hljs-keyword">ONBUILD</span> <span class="hljs-keyword">COPY</span><span class="language-bash"> . /app/src</span>
""");
    }

    [Fact]
    public void Instruction_HealthCheck()
    {
        AssertHighlighter("dockerfile",
"""
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1
""",
"""
<span class="hljs-keyword">HEALTHCHECK</span><span class="language-bash"> --interval=30s --<span class="hljs-built_in">timeout</span>=5s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || <span class="hljs-built_in">exit</span> 1</span>
""");
    }

    [Fact]
    public void Instruction_HealthCheckNone()
    {
        AssertHighlighter("dockerfile",
"""
HEALTHCHECK NONE
""",
"""
<span class="hljs-keyword">HEALTHCHECK</span><span class="language-bash"> NONE</span>
""");
    }

    [Fact]
    public void BuildKit_SyntaxDirective()
    {
        AssertHighlighter("dockerfile",
"""
# syntax=docker/dockerfile:1.6
FROM alpine
""",
"""
<span class="hljs-comment"># syntax=docker/dockerfile:1.6</span>
<span class="hljs-keyword">FROM</span> alpine
""");
    }

    [Fact]
    public void BuildKit_EscapeDirective()
    {
        AssertHighlighter("dockerfile",
"""
# escape=`
FROM mcr.microsoft.com/windows/servercore:ltsc2022
""",
"""
<span class="hljs-comment"># escape=`</span>
<span class="hljs-keyword">FROM</span> mcr.microsoft.com/windows/servercore:ltsc2022
""");
    }

    [Fact]
    public void BuildKit_CheckDirective()
    {
        AssertHighlighter("dockerfile",
"""
# check=error=true
FROM alpine
""",
"""
<span class="hljs-comment"># check=error=true</span>
<span class="hljs-keyword">FROM</span> alpine
""");
    }

    [Fact]
    public void BuildKit_MountCache()
    {
        AssertHighlighter("dockerfile",
"""
RUN --mount=type=cache,target=/root/.npm \
    npm ci
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=cache,target=/root/.npm \
    npm ci</span>
""");
    }

    [Fact]
    public void BuildKit_MountSecret()
    {
        AssertHighlighter("dockerfile",
"""
RUN --mount=type=secret,id=npmrc,target=/root/.npmrc \
    npm publish
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=secret,<span class="hljs-built_in">id</span>=npmrc,target=/root/.npmrc \
    npm publish</span>
""");
    }

    [Fact]
    public void BuildKit_MountBind()
    {
        AssertHighlighter("dockerfile",
"""
RUN --mount=type=bind,source=.,target=/src,readonly \
    cd /src && make build
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=<span class="hljs-built_in">bind</span>,<span class="hljs-built_in">source</span>=.,target=/src,<span class="hljs-built_in">readonly</span> \
    <span class="hljs-built_in">cd</span> /src &amp;&amp; make build</span>
""");
    }

    [Fact]
    public void BuildKit_MountSsh()
    {
        AssertHighlighter("dockerfile",
"""
RUN --mount=type=ssh \
    git clone git@github.com:org/private.git
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=ssh \
    git <span class="hljs-built_in">clone</span> git@github.com:org/private.git</span>
""");
    }

    [Fact]
    public void BuildKit_RunHeredoc()
    {
        AssertHighlighter("dockerfile",
"""
RUN <<EOF
apt-get update
apt-get install -y curl wget jq
EOF
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> &lt;&lt;<span class="hljs-string">EOF</span></span>
apt-get update
apt-get install -y curl wget jq
EOF
""");
    }

    [Fact]
    public void BuildKit_RunHeredocBash()
    {
        AssertHighlighter("dockerfile",
"""
RUN <<-"EOF" bash
set -euo pipefail
echo "building"
make all
EOF
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> &lt;&lt;-<span class="hljs-string">&quot;EOF&quot;</span> bash</span>
set -euo pipefail
echo <span class="hljs-string">&quot;building&quot;</span>
make all
EOF
""");
    }

    [Fact]
    public void BuildKit_CopyHeredoc()
    {
        AssertHighlighter("dockerfile",
"""
COPY <<EOF /app/entrypoint.sh
#!/bin/sh
set -e
exec "$@"
EOF
""",
"""
<span class="hljs-keyword">COPY</span><span class="language-bash"> &lt;&lt;<span class="hljs-string">EOF /app/entrypoint.sh</span></span>
<span class="hljs-comment">#!/bin/sh</span>
set -e
exec <span class="hljs-string">&quot;$@&quot;</span>
EOF
""");
    }

    [Fact]
    public void BuildKit_RunNetworkNone()
    {
        AssertHighlighter("dockerfile",
"""
RUN --network=none ./offline-build.sh
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> --network=none ./offline-build.sh</span>
""");
    }

    [Fact]
    public void BuildKit_AddGitContext()
    {
        AssertHighlighter("dockerfile",
"""
ADD --keep-git-dir=true https://github.com/org/repo.git /repo
""",
"""
<span class="hljs-keyword">ADD</span><span class="language-bash"> --keep-git-dir=<span class="hljs-literal">true</span> https://github.com/org/repo.git /repo</span>
""");
    }

    [Fact]
    public void Composite_NodeApp()
    {
        AssertHighlighter("dockerfile",
"""
# syntax=docker/dockerfile:1.6
FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --omit=dev

FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci
COPY . .
RUN npm run build

FROM node:20-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
RUN addgroup -S app && adduser -S app -G app
COPY --from=deps  /app/node_modules ./node_modules
COPY --from=build /app/dist          ./dist
USER app
EXPOSE 3000
CMD ["node", "dist/server.js"]
""",
"""
<span class="hljs-comment"># syntax=docker/dockerfile:1.6</span>
<span class="hljs-keyword">FROM</span> node:<span class="hljs-number">20</span>-alpine AS deps
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /app</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> package.json package-lock.json ./</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=cache,target=/root/.npm \
    npm ci --omit=dev</span>

<span class="hljs-keyword">FROM</span> node:<span class="hljs-number">20</span>-alpine AS build
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /app</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> package.json package-lock.json ./</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=cache,target=/root/.npm \
    npm ci</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> . .</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> npm run build</span>

<span class="hljs-keyword">FROM</span> node:<span class="hljs-number">20</span>-alpine AS runner
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /app</span>
<span class="hljs-keyword">ENV</span> NODE_ENV=production
<span class="hljs-keyword">RUN</span><span class="language-bash"> addgroup -S app &amp;&amp; adduser -S app -G app</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=deps  /app/node_modules ./node_modules</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=build /app/dist          ./dist</span>
<span class="hljs-keyword">USER</span> app
<span class="hljs-keyword">EXPOSE</span> <span class="hljs-number">3000</span>
<span class="hljs-keyword">CMD</span><span class="language-bash"> [<span class="hljs-string">&quot;node&quot;</span>, <span class="hljs-string">&quot;dist/server.js&quot;</span>]</span>
""");
    }

    [Fact]
    public void Composite_GoApp()
    {
        AssertHighlighter("dockerfile",
"""
FROM golang:1.22 AS build
WORKDIR /workspace
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -ldflags="-s -w" -o /out/app ./cmd/app

FROM gcr.io/distroless/static:nonroot
COPY --from=build /out/app /usr/local/bin/app
USER nonroot:nonroot
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/app"]
""",
"""
<span class="hljs-keyword">FROM</span> golang:<span class="hljs-number">1.22</span> AS build
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /workspace</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> go.mod go.sum ./</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> go mod download</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> . .</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> CGO_ENABLED=0 GOOS=linux go build -ldflags=<span class="hljs-string">&quot;-s -w&quot;</span> -o /out/app ./cmd/app</span>

<span class="hljs-keyword">FROM</span> gcr.io/distroless/static:nonroot
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=build /out/app /usr/local/bin/app</span>
<span class="hljs-keyword">USER</span> nonroot:nonroot
<span class="hljs-keyword">EXPOSE</span> <span class="hljs-number">8080</span>
<span class="hljs-keyword">ENTRYPOINT</span><span class="language-bash"> [<span class="hljs-string">&quot;/usr/local/bin/app&quot;</span>]</span>
""");
    }

    [Fact]
    public void Composite_RustApp()
    {
        AssertHighlighter("dockerfile",
"""
# syntax=docker/dockerfile:1.6
FROM rust:1.78 AS build
WORKDIR /src
COPY Cargo.toml Cargo.lock ./
RUN mkdir src && echo "fn main() {}" > src/main.rs && cargo build --release && rm -rf src
COPY . .
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/src/target \
    cargo build --release && \
    cp target/release/app /out/app

FROM debian:bookworm-slim
COPY --from=build /out/app /usr/local/bin/app
ENTRYPOINT ["app"]
""",
"""
<span class="hljs-comment"># syntax=docker/dockerfile:1.6</span>
<span class="hljs-keyword">FROM</span> rust:<span class="hljs-number">1.78</span> AS build
<span class="hljs-keyword">WORKDIR</span><span class="language-bash"> /src</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> Cargo.toml Cargo.lock ./</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> <span class="hljs-built_in">mkdir</span> src &amp;&amp; <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;fn main() {}&quot;</span> &gt; src/main.rs &amp;&amp; cargo build --release &amp;&amp; <span class="hljs-built_in">rm</span> -rf src</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> . .</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> --mount=<span class="hljs-built_in">type</span>=cache,target=/usr/local/cargo/registry \
    --mount=<span class="hljs-built_in">type</span>=cache,target=/src/target \
    cargo build --release &amp;&amp; \
    <span class="hljs-built_in">cp</span> target/release/app /out/app</span>

<span class="hljs-keyword">FROM</span> debian:bookworm-slim
<span class="hljs-keyword">COPY</span><span class="language-bash"> --from=build /out/app /usr/local/bin/app</span>
<span class="hljs-keyword">ENTRYPOINT</span><span class="language-bash"> [<span class="hljs-string">&quot;app&quot;</span>]</span>
""");
    }

    [Fact]
    public void Composite_WithHealth()
    {
        AssertHighlighter("dockerfile",
"""
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/nginx.conf
COPY ./html /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -qO- http://localhost/health || exit 1
CMD ["nginx", "-g", "daemon off;"]
""",
"""
<span class="hljs-keyword">FROM</span> nginx:<span class="hljs-number">1.27</span>-alpine
<span class="hljs-keyword">COPY</span><span class="language-bash"> nginx.conf /etc/nginx/nginx.conf</span>
<span class="hljs-keyword">COPY</span><span class="language-bash"> ./html /usr/share/nginx/html</span>
<span class="hljs-keyword">EXPOSE</span> <span class="hljs-number">80</span>
<span class="hljs-keyword">HEALTHCHECK</span><span class="language-bash"> --interval=30s --<span class="hljs-built_in">timeout</span>=5s --retries=3 \
  CMD wget -qO- http://localhost/health || <span class="hljs-built_in">exit</span> 1</span>
<span class="hljs-keyword">CMD</span><span class="language-bash"> [<span class="hljs-string">&quot;nginx&quot;</span>, <span class="hljs-string">&quot;-g&quot;</span>, <span class="hljs-string">&quot;daemon off;&quot;</span>]</span>
""");
    }

    [Fact]
    public void Comment_FullLine()
    {
        AssertHighlighter("dockerfile",
"""
# this is a comment
""",
"""
<span class="hljs-comment"># this is a comment</span>
""");
    }

    [Fact]
    public void Comment_AboveInstr()
    {
        AssertHighlighter("dockerfile",
"""
# install deps
RUN apt-get update && apt-get install -y curl
""",
"""
<span class="hljs-comment"># install deps</span>
<span class="hljs-keyword">RUN</span><span class="language-bash"> apt-get update &amp;&amp; apt-get install -y curl</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("dockerfile",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("dockerfile",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_BackslashContinue()
    {
        AssertHighlighter("dockerfile",
"""
RUN apt-get update && \
    apt-get install -y \
      curl \
      jq
""",
"""
<span class="hljs-keyword">RUN</span><span class="language-bash"> apt-get update &amp;&amp; \
    apt-get install -y \
      curl \
      jq</span>
""");
    }

    [Fact]
    public void SpecialEdge_LowercaseInstructions()
    {
        AssertHighlighter("dockerfile",
"""
from alpine
run apk add --no-cache curl
""",
"""
<span class="hljs-keyword">from</span> alpine
<span class="hljs-keyword">run</span><span class="language-bash"> apk add --no-cache curl</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyFrom()
    {
        AssertHighlighter("dockerfile",
"""
FROM scratch
""",
"""
<span class="hljs-keyword">FROM</span> scratch
""");
    }
}
