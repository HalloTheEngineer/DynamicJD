#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

: "${DYNAMICJD_1408_DIR:?Set DYNAMICJD_1408_DIR to the Beat Saber 1.40.8 directory}"
: "${DYNAMICJD_1420_DIR:?Set DYNAMICJD_1420_DIR to the Beat Saber 1.42.0 directory}"

DIST="$ROOT/dist"
rm -rf "$DIST"
mkdir -p "$DIST"

dotnet test Tests/DynamicJD.Tests/DynamicJD.Tests.csproj -c Release --nologo

build_one() {
    local id="$1"
    local range="$2"
    local game="$3"
    local family="$4"
    local directory="$5"

    echo "=== Building DynamicJD for Beat Saber $range"
    dotnet build -t:Rebuild -c Release \
        -p:GameVersion="$game" \
        -p:GameDirectory="${directory%/}/" \
        -p:VersionFamily="$family" \
        --nologo

    local dll="bin/Release/DynamicJD.dll"
    test -f "$dll"

    if ! strings -a "$dll" | grep -F "\"gameVersion\": \"$game\"" >/dev/null; then
        echo "Embedded manifest for $range has the wrong gameVersion" >&2
        exit 1
    fi

    local stage="$DIST/$id"
    mkdir -p "$stage/Plugins"
    cp "$dll" "$stage/Plugins/DynamicJD.dll"
    if [ -f bin/Release/DynamicJD.pdb ]; then
        cp bin/Release/DynamicJD.pdb "$stage/Plugins/DynamicJD.pdb"
    fi

    (cd "$stage" && zip -qr "$DIST/DynamicJD-0.1.0-bs${range}.zip" Plugins)
    rm -rf "$stage"
    echo "Created $DIST/DynamicJD-0.1.0-bs${range}.zip"
}

build_one legacy 1.40.8 1.40.8 legacy "$DYNAMICJD_1408_DIR"
build_one bs142 1.42.0 1.42.0 modern "$DYNAMICJD_1420_DIR"

echo
echo "Artifacts:"
sha256sum "$DIST"/*.zip
