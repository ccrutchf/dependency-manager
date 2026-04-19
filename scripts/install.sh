#!/bin/sh
# Downloads the latest `depend` release binary for this machine and installs it.
# Usage:  curl -fsSL https://raw.githubusercontent.com/clcrutch/dependency-manager/main/scripts/install.sh | sh
set -eu

REPO="clcrutch/dependency-manager"

case "$(uname -m)" in
    x86_64|amd64) RID="linux-x64" ;;
    aarch64|arm64) RID="linux-arm64" ;;
    *) echo "unsupported arch: $(uname -m)" >&2; exit 1 ;;
esac

echo "resolving latest release..."
TAG=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
    | grep '"tag_name"' \
    | head -n1 \
    | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')
VERSION="${TAG#v}"

URL="https://github.com/${REPO}/releases/download/${TAG}/depend-${RID}-${VERSION}.tar.gz"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "downloading $URL"
curl -fsSL "$URL" | tar -xz -C "$TMP"

if [ "$(id -u)" = 0 ]; then
    DEST="/usr/local/bin/depend"
else
    DEST="$HOME/.local/bin/depend"
    mkdir -p "$HOME/.local/bin"
fi

mv "$TMP/depend" "$DEST"
chmod +x "$DEST"
echo "installed: $DEST"
"$DEST" --version 2>/dev/null || "$DEST" list
