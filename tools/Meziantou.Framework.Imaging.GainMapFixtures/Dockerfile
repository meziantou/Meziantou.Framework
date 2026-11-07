# The reference tools of the gain map fixtures: libultrahdr 1.4.0 (ultrahdr_app, built from source: Ubuntu has no
# package) and libavif 1.3.0 (avifgainmaputil) from Ubuntu 26.04.
FROM ubuntu:26.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    build-essential cmake git ca-certificates pkg-config libjpeg-turbo8-dev \
    libavif-bin python3 python3-numpy python3-pil imagemagick \
    && rm -rf /var/lib/apt/lists/*
# GCC 15 no longer includes <cstdint> transitively, which libultrahdr 1.4.0 relies on
RUN git clone --depth 1 --branch v1.4.0 https://github.com/google/libultrahdr.git /src/libultrahdr \
    && cmake -S /src/libultrahdr -B /src/build -DCMAKE_BUILD_TYPE=Release "-DCMAKE_CXX_FLAGS=-include cstdint" -DUHDR_BUILD_TESTS=OFF -DUHDR_BUILD_EXAMPLES=ON \
    && cmake --build /src/build -j8 && cp /src/build/ultrahdr_app /usr/local/bin/
WORKDIR /work
