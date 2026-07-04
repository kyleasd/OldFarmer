#!/bin/bash
set -e

echo "========================================"
echo "  OldFarmer Mod 构建并启动"
echo "========================================"
echo ""

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$ROOT_DIR/OldFarmer"
CSPROJ="$PROJECT_DIR/OldFarmer.csproj"
MANIFEST="$PROJECT_DIR/manifest.json"

# 默认游戏路径；若存在 stardewvalley.targets.user 则优先使用其中的 GamePath
GAME_DIR="/d/Program Files (x86)/Steam/steamapps/common/Stardew Valley"
TARGETS_USER="$ROOT_DIR/stardewvalley.targets.user"
if [ -f "$TARGETS_USER" ]; then
  WIN_GAME="$(grep -m1 '<GamePath>' "$TARGETS_USER" | sed 's/.*<GamePath>\(.*\)<\/GamePath>.*/\1/' | tr -d '\r')"
  if [ -n "$WIN_GAME" ]; then
    GAME_DIR="$(echo "$WIN_GAME" | sed 's/\\/\//g' | sed 's/^\([A-Za-z]\):/\/\L\1/')"
  fi
fi

SMAPI_EXE="$GAME_DIR/StardewModdingAPI.exe"
MOD_DIR="$GAME_DIR/Mods/OldFarmer"
BUILD_DIR="$PROJECT_DIR/bin/Release/net6.0"

VERSION="$(grep -m1 '"Version"' "$MANIFEST" | sed 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')"
ZIP_PATH="$BUILD_DIR/OldFarmer $VERSION.zip"

echo "[1/3] 正在 Release 构建..."
dotnet build "$CSPROJ" -c Release
echo "编译成功！"
echo ""

echo "[2/3] 安装到游戏 Mods 目录..."
if tasklist 2>/dev/null | grep -qi "StardewModdingAPI"; then
  echo "游戏正在运行，跳过安装。请关闭游戏后重新运行本脚本。"
else
  mkdir -p "$MOD_DIR"
  cp -f "$BUILD_DIR/OldFarmer.dll" "$MOD_DIR/"
  cp -f "$MANIFEST" "$MOD_DIR/"
  echo "已安装到 $MOD_DIR"
fi
echo ""

echo "[3/3] 启动 SMAPI..."
if [ ! -f "$SMAPI_EXE" ]; then
  echo "找不到 SMAPI：$SMAPI_EXE"
  echo "请检查游戏路径，或在仓库根目录创建 stardewvalley.targets.user 指定 <GamePath>。"
  exit 1
fi
start "" "$SMAPI_EXE"
echo "游戏已启动！"
echo ""

echo "========================================"
echo "  完成！"
echo "  构建输出: $BUILD_DIR"
echo "  发布 zip: $ZIP_PATH"
echo "  游戏 Mod 目录: $MOD_DIR"
echo "========================================"
