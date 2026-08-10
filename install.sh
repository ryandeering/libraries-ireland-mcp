#!/bin/sh
# Installs the libraries-ireland-mcp server.
#
#   curl -fsSL https://raw.githubusercontent.com/ryandeering/libraries-ireland-mcp/main/install.sh | sh
#
# Downloads the release binary for this machine, checks it against the published SHA-256, and puts
# it on your PATH. Nothing else is installed: the binary is self-contained.
#
# Environment overrides:
#   REPO         owner/name of the GitHub repository to install from
#   VERSION      release tag to install, defaults to the latest
#   INSTALL_DIR  where to put the binary, defaults to ~/.local/bin

set -eu

REPO="${REPO:-ryandeering/libraries-ireland-mcp}"
VERSION="${VERSION:-latest}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
BIN="libraries-ireland-mcp"

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

os="$(uname -s)"
arch="$(uname -m)"

case "$os" in
    Darwin)
        asset="$BIN-macos-universal"
        ;;
    Linux)
        case "$arch" in
            x86_64 | amd64) asset="$BIN-linux-x64" ;;
            aarch64 | arm64) asset="$BIN-linux-arm64" ;;
            *) die "unsupported Linux architecture: $arch" ;;
        esac
        ;;
    MINGW* | MSYS* | CYGWIN*)
        die "On Windows, download $BIN-win-x64.exe from https://github.com/$REPO/releases"
        ;;
    *)
        die "unsupported operating system: $os"
        ;;
esac

if [ "$VERSION" = "latest" ]; then
    base="https://github.com/$REPO/releases/latest/download"
else
    base="https://github.com/$REPO/releases/download/$VERSION"
fi

command -v curl >/dev/null 2>&1 || die "curl is required"

tmp="$(mktemp -d)"
trap "rm -rf '$tmp'" EXIT INT TERM

say "Downloading $asset ($VERSION)..."
curl -fsSL "$base/$asset" -o "$tmp/$BIN" || die "could not download $base/$asset"

if command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "$tmp/$BIN" | cut -d' ' -f1)"
elif command -v shasum >/dev/null 2>&1; then
    actual="$(shasum -a 256 "$tmp/$BIN" | cut -d' ' -f1)"
elif [ "${ALLOW_UNVERIFIED:-0}" = "1" ]; then
    actual=""
    say "warning: no sha256 tool found, continuing because ALLOW_UNVERIFIED=1"
else
    die "no sha256sum or shasum available to verify the download. Install one, or re-run with ALLOW_UNVERIFIED=1 to skip verification."
fi

if [ -n "$actual" ]; then
    if curl -fsSL "$base/$asset.sha256" -o "$tmp/sum" 2>/dev/null; then
        expected="$(cut -d' ' -f1 < "$tmp/sum")"
        if [ -z "$expected" ]; then
            die "published checksum for $asset is empty"
        fi
        if [ "$actual" != "$expected" ]; then
            die "checksum mismatch: expected $expected, got $actual. Do not run this binary."
        fi
        say "Checksum verified."
    elif [ "${ALLOW_UNVERIFIED:-0}" = "1" ]; then
        say "warning: no published checksum for $asset, continuing because ALLOW_UNVERIFIED=1"
    else
        die "no published checksum for $asset, so the download cannot be verified. Re-run with ALLOW_UNVERIFIED=1 to install anyway."
    fi
fi

chmod +x "$tmp/$BIN"
mkdir -p "$INSTALL_DIR"

INSTALL_DIR=$(cd "$INSTALL_DIR" && pwd)
TARGET="$INSTALL_DIR/$BIN"

mv "$tmp/$BIN" "$TARGET"

# A binary downloaded with curl carries the quarantine attribute on macOS, which would otherwise
# make the first launch fail rather than merely pause.
if [ "$os" = "Darwin" ]; then
    xattr -d com.apple.quarantine "$TARGET" 2>/dev/null || true
fi

say ""
say "Installed to $TARGET"

case ":$PATH:" in
    *":$INSTALL_DIR:"*) ;;
    *)
        say ""
        say "$INSTALL_DIR is not on your PATH. Add this to your shell profile:"
        say "    export PATH=\"\$PATH:$INSTALL_DIR\""
        ;;
esac

say ""
say "Next, register it with your MCP client."
say ""
say "  Claude Code:"
say "    claude mcp add libraries-ireland $TARGET"
say ""
say "  Codex:"
say "    codex mcp add libraries-ireland -- $TARGET"
say ""
say "Then tell it which library you use, for example:"
say "    \"I'm with Dublin City libraries, I usually go to Ballyfermot.\""
