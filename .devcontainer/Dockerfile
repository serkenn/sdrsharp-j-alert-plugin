FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# --- Multiarch setup -----------------------------------------------------------
# Restrict the default archive.ubuntu.com / security.ubuntu.com sources to the
# host architecture (amd64), then add ports.ubuntu.com for arm64 / armhf so the
# cross-build sysroots can pull their dev packages.
RUN sed -i \
        -e '/^URIs: http:\/\/archive.ubuntu.com\/ubuntu\//a Architectures: amd64' \
        -e '/^URIs: http:\/\/security.ubuntu.com\/ubuntu\//a Architectures: amd64' \
        /etc/apt/sources.list.d/ubuntu.sources && \
    printf '%s\n' \
        'Types: deb' \
        'URIs: http://ports.ubuntu.com/ubuntu-ports/' \
        'Suites: noble noble-updates noble-backports noble-security' \
        'Components: main universe restricted multiverse' \
        'Architectures: arm64 armhf' \
        'Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg' \
        > /etc/apt/sources.list.d/ubuntu-ports.sources && \
    dpkg --add-architecture arm64 && \
    dpkg --add-architecture armhf

# --- Build tools + cross compilers --------------------------------------------
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        cmake \
        ninja-build \
        git \
        ca-certificates \
        pkg-config \
        curl \
        sudo \
        # Linux aarch64 cross
        gcc-aarch64-linux-gnu \
        g++-aarch64-linux-gnu \
        # Linux armhf cross
        gcc-arm-linux-gnueabihf \
        g++-arm-linux-gnueabihf \
    && rm -rf /var/lib/apt/lists/*

# --- Third-party headers used by SDR++ core + zlib (per architecture) ----------
# The plugin doesn't link the SDR++ deps — symbols come from sdrpp_core at
# runtime — but their headers are pulled in transitively by SDR++ core headers
# (fftw3, volk, OpenGL, glfw, zstd). zlib IS linked (NowcastPacket gunzip), so
# its dev package is needed in every cross sysroot.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libfftw3-dev libvolk-dev libgl-dev libglfw3-dev libzstd-dev zlib1g-dev \
        libfftw3-dev:arm64 libvolk-dev:arm64 libgl-dev:arm64 libglfw3-dev:arm64 libzstd-dev:arm64 zlib1g-dev:arm64 \
        libfftw3-dev:armhf libvolk-dev:armhf libgl-dev:armhf libglfw3-dev:armhf libzstd-dev:armhf zlib1g-dev:armhf \
    && rm -rf /var/lib/apt/lists/*

ARG USERNAME=dev
ARG USER_UID=1000
ARG USER_GID=1000

RUN if getent group ${USER_GID} >/dev/null; then \
        existing_group=$(getent group ${USER_GID} | cut -d: -f1); \
        groupmod -n ${USERNAME} "$existing_group"; \
    else \
        groupadd --gid ${USER_GID} ${USERNAME}; \
    fi && \
    if getent passwd ${USER_UID} >/dev/null; then \
        existing_user=$(getent passwd ${USER_UID} | cut -d: -f1); \
        usermod -l ${USERNAME} -d /home/${USERNAME} -m -g ${USER_GID} "$existing_user"; \
    else \
        useradd --uid ${USER_UID} --gid ${USER_GID} -m -s /bin/bash ${USERNAME}; \
    fi && \
    echo "${USERNAME} ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/${USERNAME} && \
    chmod 0440 /etc/sudoers.d/${USERNAME}

USER ${USERNAME}
WORKDIR /workspaces
