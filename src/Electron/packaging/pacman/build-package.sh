#!/usr/bin/env bash
# Builds the Arch Linux package from the zip that electron-forge makes on Linux.
# CI runs it as root in the archlinux:base-devel container. makepkg refuses to run as root, so the
# build itself runs as a separate builder user.
set -euo pipefail

if [ "$#" -ne 3 ] || [ -z "$1" ] || [ -z "$2" ] || [ -z "$3" ]; then
  echo "Usage: build-package.sh <path to ARCitISA-linux-x64-*.zip> <release version> <output dir>" >&2
  exit 1
fi

zip_path=$1
version=$2
out_dir=$3

if [ ! -f "$zip_path" ]; then
  echo "Zip not found: $zip_path" >&2
  exit 1
fi

zip_path=$(realpath "$zip_path")
mkdir -p "$out_dir"
out_dir=$(realpath "$out_dir")

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
icons_dir="$script_dir/../../assets/icons/png"
license_file="$script_dir/../../../../LICENSE"

# pacman forbids '-' in pkgver. Dropping it turns 3.1.0-alpha.1 into 3.1.0alpha.1, which vercmp sorts
# below 3.1.0. A '_' or '.' in its place would sort the prerelease above the release.
pkgver=${version//-/}

build_dir=$(mktemp -d)
trap 'rm -rf "$build_dir"' EXIT

cp "$script_dir/PKGBUILD" "$script_dir/arcitisa.desktop" "$license_file" "$build_dir/"
mkdir "$build_dir/icons"
cp "$icons_dir"/*.png "$build_dir/icons/"
bsdtar -xf "$zip_path" -C "$build_dir"

if ! id -u builder > /dev/null 2>&1; then
  useradd -m builder
fi
chown -R builder: "$build_dir"

cd "$build_dir"
runuser -u builder -- env ARCITISA_PKGVER="$pkgver" makepkg --nodeps --noconfirm

for package in "$build_dir"/*.pkg.tar.zst; do
  cp "$package" "$out_dir/"
  echo "$out_dir/$(basename "$package")"
done
