-- grandpa_cyclone.lua
-- 使用爷爷贴图(grandpa_sprite.png)的调色板，从零逐像素绘制一段卡通旋风动画。
-- 运行: aseprite.exe -b --script tools/grandpa_cyclone.lua
--
-- 形状: 纺锤形 —— 上下两头收尖、中间最粗。
--   以 sin(pi*t) 作为每层的粗细系数, 顶部(t=0)与底部(t=1)趋近于 0, 中间最大。
--   每层是一圈带旋转开口的椭圆涡环(近亮远暗), 叠出旋转的立体感。

local ROOT     = "D:\\Project\\OldFarmer\\"
local OUT_DIR  = ROOT .. "assets\\animations\\"
local OUT_FILE = OUT_DIR .. "grandpa_cyclone.aseprite"

local W, H     = 44, 52
local CX       = 22.0
local Y_TOP    = 7.0           -- 最上层涡环中心
local Y_BOT    = 47.0          -- 最下层涡环中心
local R_MAX    = 13.0          -- 中间最粗处的水平半径
local RY_MAX   = 4.0           -- 中间最粗处的椭圆竖直半轴
local BANDS    = 11
local FRAMES   = 12
local FRAME_MS = 60

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

-- 每层的 [后缘色, 前缘色]: 中间最亮, 向上下两端逐渐变深
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

-- 纺锤形粗细系数: 0(顶) -> 1(中) -> 0(底)
local function spindle(i)
    local t = i / (BANDS - 1)
    return math.sin(math.pi * t)
end

local function bandRadius(i)
    local k = spindle(i)
    local rx = 0.5 + R_MAX * (k ^ 0.90)
    local ry = 0.5 + RY_MAX * (k ^ 0.90)
    return rx, ry, k
end

-- ── 绘制一帧旋风 ────────────────────────────────────────────
local function buildCyclone(theta)
    local img = Image(W, H)

    local function put(x, y, c)
        local xi, yi = math.floor(x + 0.5), math.floor(y + 0.5)
        if xi >= 0 and xi < W and yi >= 0 and yi < H then
            img:drawPixel(xi, yi, c)
        end
    end

    -- 一圈带旋转开口的椭圆涡环; 前缘有一段高光, 整体呈带状
    local function ring(cx, cy, rx, ry, phase, colBack, colFront, gap, hiAngle)
        local a = 0.0
        local step = 0.020
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
            a = a + step
        end
    end

    local hiAngle = 2.55 + theta * 0.5        -- 高光沿前缘缓慢移动(整数倍循环)

    -- 自下而上绘制; 中间层最宽, 两端收成尖
    for i = 0, BANDS - 1 do
        local t = i / (BANDS - 1)
        local cy = Y_TOP + t * (Y_BOT - Y_TOP)
        local rx, ry, k = bandRadius(i)
        local cx = CX + math.sin(theta + i * 0.40) * k * 2.2   -- 中间摆动最明显
        local phase = theta + i * 1.05
        local gap = 1.00 - 0.40 * k                            -- 中间环更完整
        local cols = PALETTE[i + 1]
        ring(cx, cy, rx, ry, phase, cols[1], cols[2], gap, hiAngle)
    end

    -- 被旋风甩出、向外飞散的碎屑(集中在最粗处附近)
    for k = 0, 3 do
        local a = theta * 2.0 + k * (math.pi / 2.0)
        local r = R_MAX + 2.0 + 2.0 * math.sin(theta * 2.0 + k * 1.3)
        local y = 20.0 + (k % 3) * 8.0
        local x = CX + math.cos(a) * r
        put(x, y, ((k % 2) == 0) and GLOW or CYAN)
    end

    return img
end

-- ── 组装并输出 ──────────────────────────────────────────────
local sprite = Sprite(W, H)
local layer = sprite.layers[1]
layer.name = "cyclone"

for i = 2, FRAMES do sprite:newFrame() end

for i = 1, FRAMES do
    local theta = (i - 1) * (2.0 * math.pi / FRAMES)
    sprite:newCel(layer, i, buildCyclone(theta), Point(0, 0))
    sprite.frames[i].duration = FRAME_MS
end

local tag = sprite:newTag(1, FRAMES)
tag.name = "cyclone"

app.fs.makeAllDirectories(OUT_DIR)
sprite:saveAs(OUT_FILE)
print("Grandpa cyclone animation written to: " .. OUT_FILE)
