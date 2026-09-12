#!/usr/bin/env bash
set -euo pipefail

# 迁移文档约定的 Unix 入口，实际参数仍由根目录 build.ps1 统一维护。
config="${1:-Release}"
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
build_script="$root_dir/build.ps1"

if [[ ! -f "$build_script" ]]; then
  echo "找不到根目录打包脚本: $build_script" >&2
  exit 1
fi

if ! command -v pwsh >/dev/null 2>&1; then
  echo "需要安装 PowerShell 7（pwsh）后才能执行 Avalonia 打包" >&2
  exit 1
fi

pwsh -NoProfile -File "$build_script" -Config "$config" -Targets macos
