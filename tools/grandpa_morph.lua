-- grandpa_morph.lua
-- "爷爷静态贴图 -> 旋风" 的过渡(变身)动画。
-- 运行: aseprite.exe -b --script tools/grandpa_morph.lua
--
-- 过程(p 从 0 到 1):
--   1) p=0      : 爷爷原图静止站立。
--   2) 中段      : 爷爷原地加速旋转, 横向收窄、纵向拉长(高速旋转的模糊感)。
--   3) 后段      : 旋风自脚下生出, 一圈圈向上蔓延并吞没爷爷; 爷爷逐渐淡出。
--   4) p=1      : 只剩完整的旋风, 且与 grandpa_cyclone.aseprite 的第 1 帧一致(可无缝衔接)。
--
-- 两层: 底层 cyclone, 上层 grandpa; 两层都带各自的 alpha, 由 Aseprite 合成。

local ROOT      = "D:\\Project\\OldFarmer\\"
local GRANDPA   = ROOT .. "assets\\origin\\grandpa_sprite.png"
local OUT_DIR   = ROOT .. "assets\\animations\\"
local OUT_FILE  = OUT_DIR .. "grandpa_morph.aseprite"

local W, H      = 44, 52
local CX        = 22.0
local GY        = 27.0          -- 爷爷/旋风的共同中心
local Y_TOP     = 7.0
local Y_BOT     = 47.0
local R_MAX     = 13.0
local RY_MAX    = 4.0
local BANDS     = 11
local FRAMES    = 16
local SPIN_TURN = 4.0 * math.pi -- 整个过渡共转 2 圈(4π), 保证 p=1 时相位回到 0

-- ── 爷爷原图 ────────────────────────────────────────────────
local SW, SH    = 18, 35
local SCX, SCY  = 9.0, 17.0

-- ── 爷爷的调色板(取自 grandpa_sprite.png) ───────────────────
local pc = app.pixelColor
local function rgba(r, g, b, a) return pc.rgba(r, g, b, a) end

local DARK   = rgba(0, 51, 128, 255)
local DEEP   = rgba(0, 74, 164, 255)
local MID    = rgba(0, 125, 177, 255)
local BLUE   = rgba(0, 160, 224, 255)
local AZURE  = rgba(0, 178, 240, 255)
local CYAN   = rgba(0, 225, 240, 255)
local GREEN  = rgba(7, 253, 219, 255)
local LIGHT  = rgba(186, 253, 246, 255)
local GLOW   = rgba(6, 245, 252, 190)

local PALETTE = {
    { DARK,  DEEP  },
    { DEEP,  MID   },
    { MID,   BLUE  },
    { BLUE,  AZURE },
    { AZURE, CYAN  },
    { CYAN,  GREEN },
    { AZURE, CYAN  },
    { BLUE,  AZURE },
    { MID,   BLUE  },
    { DEEP,  MID   },
    { DARK,  DEEP  },
}

local function clamp01(x)
    if x < 0 then return 0 end
    if x > 1 then return 1 end
    return x
end

local function smoothstep(a, b, x)
    local t = clamp01((x - a) / (b - a))
    return t * t * (3.0 - 2.0 * t)
end

local function spindle(i)
    return math.sin(math.pi * (i / (BANDS - 1)))
end

local function mulAlpha(c, a)
    local al = pc.rgbaA(c)
    if al <= 0 or a <= 0 then return nil end
    return pc.rgba(pc.rgbaR(c), pc.rgbaG(c), pc.rgbaB(c), math.floor(al * a + 0.5))
end

-- ── 读取爷爷原图 ────────────────────────────────────────────
local src = app.open(GRANDPA).cels[1].image

-- ── 爷爷: 旋转加速 + 收窄拉长 + 淡出 ────────────────────────
local function buildGrandpa(theta, p, alpha)
    local img = Image(W, H)
    if alpha <= 0.01 then return img end

    local cosT    = math.cos(theta)
    local flip    = cosT < 0
    local squash  = 0.22 + 0.78 * math.abs(cosT)   -- 旋转造成的横向压缩
    local narrow  = 1.0 - 0.50 * (p * p)            -- 越到后面越细
    local stretch = 1.0 + 0.70 * (p * math.sqrt(p)) -- 越到后面越长
    local width   = squash * narrow
    if width < 0.08 then width = 0.08 end

    local lift = p * 2.0                            -- 被旋风稍稍托起

    for oy = 0, H - 1 do
        local sy = SCY + (oy - GY + lift) / stretch
        sy = math.floor(sy + 0.5)
        if sy >= 0 and sy < SH then
            for ox = 0, W - 1 do
                local off = (ox - CX) / width
                local sx = math.floor(SCX + (flip and -off or off) + 0.5)
                if sx >= 0 and sx < SW then
                    local c = src:getPixel(sx, sy)
                    local cc = mulAlpha(c, alpha)
                    if cc then img:drawPixel(ox, oy, cc) end
                end
            end
        end
    end
    return img
end

-- ── 旋风: 由下往上生长, 最终与循环旋风一致 ──────────────────
local function buildCyclone(theta, p, alpha)
    local img = Image(W, H)
    if alpha <= 0.01 then return img end

    local function put(x, y, c)
        local cc = mulAlpha(c, alpha)
        if not cc then return end
        local xi, yi = math.floor(x + 0.5), math.floor(y + 0.5)
        if xi >= 0 and xi < W and yi >= 0 and yi < H then
            img:drawPixel(xi, yi, cc)
        end
    end

    local function ring(cx, cy, rx, ry, phase, colBack, colFront, gap, hiAngle)
        local a = 0.0
        while a < 2.0 * math.pi do
            local d = (a - phase) % (2.0 * math.pi)
            local dist = (d < math.pi) and d or (2.0 * math.pi - d)
            if dist > gap then
                local s = math.sin(a)
                local x = cx + math.cos(a) * rx
                local y = cy + s * ry
                if s >= 0.0 then
                    local hd = (a - hiAngle) % (2.0 * math.pi)
                    local hdist = (hd < math.pi) and hd or (2.0 * math.pi - hd)
                    local c = (rx > 8.0 and hdist < 0.28) and LIGHT or colFront
                    put(x, y, c)
                    if ry > 1.2 then put(x, y - 1.0, colBack) end
                    if ry > 3.2 then put(x, y - 2.0, colBack) end
                else
                    put(x, y, colBack)
                    if ry > 3.2 then put(x, y + 1.0, DARK) end
                end
            end
            a = a + 0.020
        end
    end

    local hiAngle = 2.55 + theta * 0.5

    for i = 0, BANDS - 1 do
        local t = i / (BANDS - 1)
        local k = spindle(i)
        -- 底部先长出, 再向上蔓延
        local start = 0.42 - 0.38 * t
        local g = smoothstep(start, start + 0.45, p)
        if g > 0.02 then
            local cy = Y_TOP + t * (Y_BOT - Y_TOP)
            local cx = CX + math.sin(theta + i * 0.40) * k * 2.2
            local rx = (0.5 + R_MAX * (k ^ 0.90)) * g
            local ry = (0.5 + RY_MAX * (k ^ 0.90)) * g
            local phase = theta + i * 1.05
            local gap = 1.00 - 0.40 * k
            local cols = PALETTE[i + 1]
            ring(cx, cy, rx, ry, phase, cols[1], cols[2], gap, hiAngle)
        end
    end

    for k = 0, 3 do
        local a = theta * 2.0 + k * (math.pi / 2.0)
        local r = R_MAX + 2.0 + 2.0 * math.sin(theta * 2.0 + k * 1.3)
        local y = 20.0 + (k % 3) * 8.0
        local x = CX + math.cos(a) * r
        put(x, y, ((k % 2) == 0) and GLOW or CYAN)
    end

    return img
end

-- ── 组装 ────────────────────────────────────────────────────
local sprite = Sprite(W, H)
local layerCyclone = sprite.layers[1]
layerCyclone.name = "cyclone"
local layerGrandpa = sprite:newLayer()
layerGrandpa.name = "grandpa"

for i = 2, FRAMES do sprite:newFrame() end

for i = 1, FRAMES do
    local p = (i - 1) / (FRAMES - 1)          -- 0 -> 1
    local theta = SPIN_TURN * (p * math.sqrt(p))

    local alphaG = 1.0 - smoothstep(0.50, 0.80, p)   -- 爷爷逐渐淡出(被旋风吞没)
    local alphaC = smoothstep(0.04, 0.30, p)         -- 旋风逐渐显现

    sprite:newCel(layerCyclone, i, buildCyclone(theta, p, alphaC), Point(0, 0))
    sprite:newCel(layerGrandpa, i, buildGrandpa(theta, p, alphaG), Point(0, 0))

    -- 开头两帧停留久一点, 让"静态爷爷"可辨识
    if i == 1 then
        sprite.frames[i].duration = 180
    elseif i == 2 then
        sprite.frames[i].duration = 120
    else
        sprite.frames[i].duration = 70
    end
end

local tag = sprite:newTag(1, FRAMES)
tag.name = "morph"

app.fs.makeAllDirectories(OUT_DIR)
sprite:saveAs(OUT_FILE)
print("Grandpa morph animation written to: " .. OUT_FILE)
