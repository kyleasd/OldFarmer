-- grandpa_spin.lua
-- 参考 assets/origin/grandpa_sprite.png，用 Aseprite 绘制一段"人物旋风旋转"动画。
-- 运行: aseprite.exe -b --script tools/grandpa_spin.lua
--
-- 原理:
--   1) 以原图(18x35)为素材，逐帧绕垂直轴旋转(横向 cos 压缩 + 背面镜像)。
--   2) 越靠近底部越收窄，叠成卡通龙卷风的锥形。
--   3) 在脚边画旋转的气流弧线，强化"旋风"感。
--   4) 12 帧无缝循环。

local ROOT       = "D:\\Project\\OldFarmer\\"
local SRC        = ROOT .. "assets\\origin\\grandpa_sprite.png"
local OUT_DIR    = ROOT .. "assets\\animations\\"
local OUT_FILE   = OUT_DIR .. "grandpa_spin.aseprite"

local SW, SH     = 18, 35          -- 原图尺寸
local SCX, SCY   = 9.0, 17.0       -- 原图身体中心(旋转轴)
local W, H       = 40, 44          -- 画布尺寸
local CX, CY     = 20.0, 20.0      -- 画布上的旋转轴位置

local FRAMES     = 12
local FRAME_MS   = 60

local MIN_W      = 0.32            -- 侧面时最小宽度比例(避免完全消失)
local TAPER      = 0.25            -- 底部收窄量(0=不收窄)
local STRETCH    = 0.14            -- 侧面时纵向拉伸量

-- ── 工具函数 ────────────────────────────────────────────────
local pc = app.pixelColor
local function rgba(r, g, b, a) return pc.rgba(r, g, b, a) end

local function clamp(v, lo, hi)
    if v < lo then return lo end
    if v > hi then return hi end
    return v
end

local function smoothstep(a, b, x)
    local t = clamp((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)
end

-- ── 读取原图 ────────────────────────────────────────────────
local srcSprite = app.open(SRC)
local src = srcSprite.cels[1].image

local function srcPixel(x, y)
    if x < 0 or y < 0 or x >= SW or y >= SH then return nil end
    return src:getPixel(x, y)
end

-- ── 生成角色帧 ──────────────────────────────────────────────
local function buildCharacter(theta)
    local img = Image(W, H)

    local cosT   = math.cos(theta)
    local flip   = cosT < 0
    local squash = MIN_W + (1.0 - MIN_W) * math.abs(cosT)
    local scaleY = 1.0 + STRETCH * (1.0 - math.abs(cosT))

    local sway     = 1.6 * math.sin(theta * 2.0)          -- 左右晃动
    local bounce   = -1.2 * math.abs(math.sin(theta))      -- 弹跳
    local cxDraw   = CX + sway

    local halfSrc  = (SH * scaleY) * 0.5
    local topSrc   = CY - halfSrc                          -- 角色顶端在画布上的 y

    for oy = 0, H - 1 do
        local ny = clamp((oy - topSrc) / (SH * scaleY), 0.0, 1.0)
        local taper = 1.0 - TAPER * smoothstep(0.55, 1.0, ny)
        local rowScale = squash * taper
        if rowScale < 0.12 then rowScale = 0.12 end

        local sy = SCY + (oy - CY - bounce) / scaleY
        sy = math.floor(sy + 0.5)

        if sy >= 0 and sy < SH then
            for ox = 0, W - 1 do
                local off = (ox - cxDraw) / rowScale
                local sx = SCX + (flip and -off or off)
                sx = math.floor(sx + 0.5)

                local c = srcPixel(sx, sy)
                if c and pc.rgbaA(c) > 0 then
                    img:drawPixel(ox, oy, c)
                end
            end
        end
    end

    return img
end

-- ── 生成气流/旋风帧 ────────────────────────────────────────
local function buildWind(theta)
    local img = Image(W, H)

    local bright = rgba(186, 253, 246, 235)
    local mid    = rgba(6, 245, 252, 190)
    local faint  = rgba(6, 200, 240, 105)

    local baseY = CY + 17.0
    local topY  = CY - 4.0
    local function funnelW(t) return 3.0 + 9.0 * (t ^ 1.2) end

    -- 漏斗轮廓: 底部收拢于脚下, 顶部张开
    for i = 0, 26 do
        local t = i / 26.0
        local y = math.floor(baseY - t * (baseY - topY) + 0.5)
        local w = funnelW(t)
        local xl = math.floor(CX - w + 0.5)
        local xr = math.floor(CX + w + 0.5)
        if y >= 0 and y < H then
            if xl >= 0 then img:drawPixel(xl, y, faint) end
            if xr < W then img:drawPixel(xr, y, faint) end
        end
    end

    -- 横向旋转的气流短线, 左右交替形成绕转感
    for b = 0, 4 do
        local t = 0.10 + b * 0.20
        local y = math.floor(baseY - t * (baseY - topY) + 0.5)
        local w = funnelW(t)
        local phase = theta + b * 1.55
        local col = (b % 2 == 0) and mid or faint
        local fromX, toX
        if math.cos(phase) >= 0 then
            fromX, toX = CX, CX + w
        else
            fromX, toX = CX - w, CX
        end
        local steps = math.floor(math.abs(toX - fromX)) + 1
        for k = 0, steps do
            local x = math.floor(fromX + (toX - fromX) * k / steps + 0.5)
            if x >= 0 and x < W and y >= 0 and y < H then
                img:drawPixel(x, y, col)
            end
        end
    end

    -- 脚下扬起的尘土
    for k = 0, 7 do
        local a = theta + k * (2.0 * math.pi / 8.0)
        local r = 8.0 + 4.0 * math.sin(theta * 2.0 + k)
        local x = CX + math.cos(a) * r
        local y = baseY + 1.0 + math.sin(a) * 1.6
        local xi, yi = math.floor(x + 0.5), math.floor(y + 0.5)
        if xi >= 0 and xi < W and yi >= 0 and yi < H and (k % 2 == 0) then
            img:drawPixel(xi, yi, mid)
        end
    end

    return img
end

-- ── 组装 Sprite ─────────────────────────────────────────────
local sprite = Sprite(W, H)
local layerWind = sprite.layers[1]        -- 底层: 旋风
layerWind.name = "wind"

local layerChar = sprite:newLayer()       -- 上层: 爷爷
layerChar.name = "grandpa"

for i = 2, FRAMES do sprite:newFrame() end

for i = 1, FRAMES do
    local theta = (i - 1) * (2.0 * math.pi / FRAMES)
    sprite:newCel(layerChar, i, buildCharacter(theta), Point(0, 0))
    sprite:newCel(layerWind, i, buildWind(theta), Point(0, 0))
    sprite.frames[i].duration = FRAME_MS
end

local tag = sprite:newTag(1, FRAMES)
tag.name = "spin"

app.fs.makeAllDirectories(OUT_DIR)
sprite:saveAs(OUT_FILE)
print("Grandpa spin animation written to: " .. OUT_FILE)
