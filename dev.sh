#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

target="${1:-web}"
if [ "$#" -gt 0 ]; then shift; fi

case "$target" in
    web)
        exec dotnet run --project StickerApp.Web --launch-profile http "$@"
        ;;
    android)
        exec dotnet build StickerApp/StickerApp.csproj -f net10.0-android "$@"
        ;;
    ios)
        # Apple Silicon simulator. Override RuntimeIdentifier for an Intel Mac or real iPhone.
        exec dotnet build StickerApp/StickerApp.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 "$@"
        ;;
    *)
        echo "Usage: ./dev.sh [web|android|ios] [additional dotnet arguments]" >&2
        exit 2
        ;;
esac
