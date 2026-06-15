#!/usr/bin/env bash
# Drive cross-builds for every supported target. Each target's build tree lives
# under build/<target>/ and produces a single shared library that is collected
# into dist/<target>/.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# Supported targets are the three Linux ABIs that build cleanly from the
# Dev Container. Windows / macOS are intentionally out of scope — see README.md.
TARGETS="${TARGETS:-linux-x86_64 linux-aarch64 linux-armhf}"
BUILD_TYPE="${BUILD_TYPE:-Release}"
JOBS="${JOBS:-$(nproc)}"

# Prefer Ninja (the Dev Container ships it); fall back to Make so the script
# also works on a bare host.
if command -v ninja >/dev/null 2>&1; then
    GENERATOR="${GENERATOR:-Ninja}"
else
    GENERATOR="${GENERATOR:-Unix Makefiles}"
fi
echo "==> Using CMake generator: ${GENERATOR}"

# Ensure SDR++ source is present and up to date (skip if a symlink/checkout
# already provides it — handy for local dev next to a sibling clone).
if [ ! -e "${REPO_ROOT}/external/SDRPlusPlus/core/src/module.h" ]; then
    bash "${SCRIPT_DIR}/fetch-sdrpp.sh"
fi

mkdir -p "${REPO_ROOT}/build" "${REPO_ROOT}/dist"

build_one() {
    local target="$1"
    local toolchain_arg="$2"
    local build_dir="${REPO_ROOT}/build/${target}"
    local dist_dir="${REPO_ROOT}/dist/${target}"

    echo "==> [${target}] Configuring"
    cmake -S "${REPO_ROOT}" -B "${build_dir}" \
        -G "${GENERATOR}" \
        -DCMAKE_BUILD_TYPE="${BUILD_TYPE}" \
        ${toolchain_arg}

    echo "==> [${target}] Building"
    cmake --build "${build_dir}" -j "${JOBS}"

    echo "==> [${target}] Collecting artefacts into ${dist_dir}"
    mkdir -p "${dist_dir}"
    find "${build_dir}" -maxdepth 1 \( -name "*.so" -o -name "*.dll" -o -name "*.dylib" \) -exec cp {} "${dist_dir}/" \;
    ls -la "${dist_dir}"
}

for target in $TARGETS; do
    case "$target" in
        linux-x86_64)
            build_one "$target" ""
            ;;
        linux-aarch64)
            build_one "$target" "-DCMAKE_TOOLCHAIN_FILE=${REPO_ROOT}/cmake/toolchains/linux-aarch64.cmake"
            ;;
        linux-armhf)
            build_one "$target" "-DCMAKE_TOOLCHAIN_FILE=${REPO_ROOT}/cmake/toolchains/linux-armhf.cmake"
            ;;
        *)
            echo "Unknown target: $target" >&2
            exit 1
            ;;
    esac
done

echo "==> All builds complete. Artefacts:"
find "${REPO_ROOT}/dist" -mindepth 2 -maxdepth 2 -type f
