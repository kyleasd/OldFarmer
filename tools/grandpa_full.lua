-- grandpa_full.lua
-- 合并动画: "爷爷静态贴图 -> 旋风" 过渡 + 旋风循环。
-- 运行: aseprite.exe -b --script tools/grandpa_full.lua
--
-- 帧结构(共 27 帧):
--   [1..16]  tag "morph"   : 爷爷静止 -> 加速旋转/拉长 -> 旋风自下而上吞没 -> 完整旋风。
--   [16..27] tag "cyclone" : 完整旋风循环(12 帧)。第 16 帧为两段共用接缝帧。
-- 因为过渡末帧与旋风首帧逐像素相同, 所以 morph 播放一次后切到 cyclone 循环可无缝衔接。

local ROOT      = "D:\\Project\\OldFarmer\\"
local GRANDPA   = ROOT .. "assets\\origin\\grandpa_sprite.png"
local OUT_DIR   = ROOT .. "assets\\animations\\"
local OUT_FILE  = OUT_DIR .. "grandpa_full.aseprite"

local W, H      = 44, 52
local CX        = 22.0
local GY        = 27.0
local Y_TOP     = 7.0
local Y_BOT     = 47.0
local R_MAX     = 13.0
local RY_MAX    = 4.0
local BANDS     = 11
local MORPH_FRAMES   = 16
local CYCLONE_FRAMES = 12
local TOTAL          = MORPH_FRAMES + CYCLONE_FRAMES - 1   -- 27
local SPIN_TURN      = 4.0 * math.pi

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

local src = app.open(GRANDPA).cels[1].image

-- ── 爷爷: 旋转 + 收窄拉长 + 淡出 ────────────────────────────
local function buildGrandpa(theta, p, alpha)
    local img = Image(W, H)
    if alpha <= 0.01 then return img end

    local cosT    = math.cos(theta)
    local flip    = cosT < 0
    local squash  = 0.22 + 0.78 * math.abs(cosT)
    local narrow  = 1.0 - 0.50 * (p * p)
    local stretch = 1.0 + 0.70 * (p * math.sqrt(p))
    local width   = squash * narrow
    if width < 0.08 then width = 0.08 end

    local lift = p * 2.0

    for oy = 0, H - 1 do
        local sy = SCY + (oy - GY + lift) / stretch
        sy = math.floor(sy + 0.5)
        if sy >= 0 and sy < SH then
            for ox = 0, W - 1 do
                local off = (ox - CX) / width
                local sx = math.floor(SCX + (flip and -off or off) + 0.5)
                if sx >= 0 and sx < SW then
                    local cc = mulAlpha(src:getPixel(sx, sy), alpha)
                    if cc then img:drawPixel(ox, oy, cc) end
                end
            end
        end
    end
    return img
end

-- ── 旋风 ────────────────────────────────────────────────────
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

for i = 2, TOTAL do sprite:newFrame() end

-- 过渡段 1..16
for i = 1, MORPH_FRAMES do
    local p = (i - 1) / (MORPH_FRAMES - 1)
    local theta = SPIN_TURN * (p * math.sqrt(p))
    local alphaG = 1.0 - smoothstep(0.50, 0.80, p)
    local alphaC = smoothstep(0.04, 0.30, p)
    sprite:newCel(layerCyclone, i, buildCyclone(theta, p, alphaC), Point(0, 0))
    sprite:newCel(layerGrandpa, i, buildGrandpa(theta, p, alphaG), Point(0, 0))
end

-- 旋风循环段 17..27 (第 2..12 帧; 第 16 帧复用过渡末帧)
for f = 2, CYCLONE_FRAMES do
    local frame = MORPH_FRAMES + (f - 1)
    local theta = (f - 1) * (2.0 * math.pi / CYCLONE_FRAMES)
    sprite:newCel(layerCyclone, frame, buildCyclone(theta, 1.0, 1.0), Point(0, 0))
    sprite:newCel(layerGrandpa, frame, Image(W, H), Point(0, 0))
end

-- 帧时长: 过渡开头定格久一点, 旋风段 60ms
for i = 1, TOTAL do
    if i == 1 then
        sprite.frames[i].duration = 180
    elseif i == 2 then
        sprite.frames[i].duration = 120
    elseif i < MORPH_FRAMES then
        sprite.frames[i].duration = 70
    else
        sprite.frames[i].duration = 60
    end
end

local tagMorph = sprite:newTag(1, MORPH_FRAMES)
tagMorph.name = "morph"
local tagCyclone = sprite:newTag(MORPH_FRAMES, TOTAL)
tagCyclone.name = "cyclone"

app.fs.makeAllDirectories(OUT_DIR)
sprite:saveAs(OUT_FILE)

-- 同时导出带 tag / layer 信息的精灵表
app.command.ExportSpriteSheet{
    ui              = false,
    type            = "horizontal",
    texture         = true,
    textureFilename = OUT_DIR .. "grandpa_full_sheet.png",
    data            = true,
    dataFilename    = OUT_DIR .. "grandpa_full_sheet.json",
    dataFormat      = "json",
    listTags        = true,
    listLayers      = true,
}

print("Merged animation written to: " .. OUT_FILE .. "  (" .. TOTAL .. " frames)")
