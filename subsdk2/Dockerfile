FROM devkitpro/devkita64:latest

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update -y \
    && apt-get install -y --no-install-recommends cmake ninja-build \
    && command -v elf2nso \
    && command -v npdmtool \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

RUN cmake -S . -B build -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_TOOLCHAIN_FILE=cmake/toolchain.cmake \
    -DCMAKE_CXX_FLAGS="-fpermissive -fPIC" \
    && cmake --build build --target MegaExtension_Diamond_nso -j 2 \
    && test -f build/MegaExtension_Diamond_out/subsdk2

FROM scratch AS artifact
COPY --from=0 /src/build/MegaExtension_Diamond_out/subsdk2 /exefs/subsdk2
