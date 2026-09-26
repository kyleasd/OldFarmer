-- san_bar.lua
-- 复制原版体力条(stamina)的三段容器贴图，并把容器顶部表示体力(Energy)的
-- "E" 字母改成表示理智(Sanity)的 "S"，用于 OldFarmer 的 SAN 条 UI。
-- 运行: aseprite.exe -b --script tools/san_bar.lua
--
-- 原理:
--   原版体力条容器在 Cursors.png 中由三段 12x16 的 tile 纵向拼接而成：
--     顶部 cap(含字母) -> 可拉伸中段 -> 底部 cap
--   本脚本把三段原样拷贝成一张 12x48 的独立贴图，然后只改顶部 cap 上
--   字母的 2 个像素，把 "E" 变成 "S"。字母的字形、描边、凹槽底色全部
--   沿用原版像素，因此美术风格与原版完全一致。

local ROOT    = "D:\\Project\\OldFarmer\\"
local SRC     = "D:\\Program Files (x86)\\Steam\\steamapps\\common\\Stardew Valley\\Content (unpacked)\\LooseSprites\\Cursors.png"
local OUT_DIR = ROOT .. "assets\\sprites\\"
local OUT_PNG = OUT_DIR .. "san_bar.png"
local OUT_ASE = OUT_DIR .. "san_bar.aseprite"

local W, H = 12, 16                 -- 单段 tile 尺寸（与原版一致）

-- 原版体力条三段 tile 在 Cursors.png 中的左上角坐标
local TOP    = { x = 256, y = 408 }
local MIDDLE = { x = 256, y = 424 }
local BOTTOM = { x = 256, y = 448 }

local pc = app.pixelColor
local function rgba(r, g, b, a) return pc.rgba(r, g, b, a) end

-- ── 读取原版 Cursors.png ────────────────────────────────────
local srcSprite = app.open(SRC)
local src = srcSprite.cels[1].image

local function copyTile(img, dstY, tile)
    for y = 0, H - 1 do
        for x = 0, W - 1 do
            local c = src:getPixel(tile.x + x, tile.y + y)
            if pc.rgbaA(c) > 0 then
                img:drawPixel(x, dstY + y, c)
            end
        end
    end
end

-- ── 组装独立贴图 (12 x 48) ──────────────────────────────────
local sprite = Sprite(W, H * 3)
local img = sprite.cels[1].image
copyTile(img, 0,     TOP)
copyTile(img, H,     MIDDLE)
copyTile(img, H * 2, BOTTOM)

-- ── E -> S ─────────────────────────────────────────────────
-- 原版 "E" 字形（相对顶部 cap 左上角，坐标 (x, y)）:
--   y=3 : G H H G   顶横
--   y=4 : H . . .   左竖
--   y=5 : H H G .   中横
--   y=6 : H . . .   左竖
--   y=7 : G H H G   底横
-- 把 y=6 的左竖移到右端，即得到 "S":
--   y=3 : G H H G
--   y=4 : H . . .
--   y=5 : H H G .
--   y=6 : . . . H   右竖
--   y=7 : G H H G
-- 因此只需改动两个像素。
local TROUGH = rgba(0x5B, 0x2B, 0x2A, 255)  -- 原版凹槽底色 (5B2B2A)
local BRIGHT = rgba(0xFB, 0xF9, 0x00, 255)  -- 原版亮黄   (FBF900)
img:drawPixel(4, 6, TROUGH)                 -- 抹掉 E 的左竖
img:drawPixel(7, 6, BRIGHT)                 -- 画出 S 的右竖

-- ── 输出 ───────────────────────────────────────────────────
app.fs.makeAllDirectories(OUT_DIR)
sprite:saveAs(OUT_ASE)
sprite:saveCopyAs(OUT_PNG)
print("SAN bar sprite written to: " .. OUT_PNG)
